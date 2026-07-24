using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the simulated OS installation sequence after PC assembly.
/// Does NOT emulate a real OS install — it's a scripted, data-driven
/// state machine (POST -> boot check -> installer -> progress -> reboot -> desktop)
/// that reuses the same event/listener pattern as DialogManager.
///
/// Wired to the real project API:
///   - PSUPartItem/SSDPartItem inherit CableGatedPartItem.cables[]; each
///     CableConnector exposes a public bool isPlugged
///   - RAMPartItem/PCPartItem.HomeSocket is null when picked up, non-null
///     when seated (RAMPartItem already gates the latch-closed check itself)
///   - DialogManager.Instance.ShowDialog(DialogSequence) — no DialogType enum
/// </summary>
public class OSInstallManager : MonoBehaviour
{
    public static OSInstallManager Instance { get; private set; }

    public enum InstallStage
    {
        PowerOn,
        POST,
        BootMediaCheck,
        LanguageSelect,
        PartitionSelect,
        Copying,
        Installing,
        Reboot,
        FirstBoot,
        Complete
    }

    [Header("Stage Data (in order)")]
    [SerializeField] private List<OSInstallStageData> stages = new List<OSInstallStageData>();

    [Header("Scene References")]
    [SerializeField] private PSUPartItem psu;
    [SerializeField] private SSDPartItem bootDrive;
    [SerializeField] private RAMPartItem[] ramSticks;
    [SerializeField] private USBDriveItem usbDrive;

    [Header("Warning Dialogs (assign DialogSequence assets)")]
    [SerializeField] private DialogSequence noPowerWarning;
    [SerializeField] private DialogSequence noRamWarning;
    [SerializeField] private DialogSequence noBootDeviceWarning;
    [SerializeField] private DialogSequence noBootableUSBWarning;

    public InstallStage CurrentStage { get; private set; } = InstallStage.PowerOn;
    public bool IsRunning { get; private set; }

    /// <summary>Index of CurrentStage within the stages list, or -1 if the
    /// sequence hasn't started yet. Used by SaveManager to persist/restore
    /// exactly where the player left off.</summary>
    public int CurrentStageIndex { get; private set; } = -1;

    // UI listens to these — mirrors DialogView's listener pattern
    public event Action<OSInstallStageData> OnStageChanged;
    public event Action<float> OnStageProgress; // 0-1 within current stage
    public event Action OnInstallComplete;
    public event Action<string> OnInstallFailed; // reason string for UI/dialog

    private Coroutine _runningCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Jumps the monitor UI straight to a previously-reached stage without
    /// replaying earlier stages or their timers — used when restoring a save.
    /// Does NOT resume the coroutine sequence (the player will need to
    /// re-trigger BeginInstall to continue from a live stage if you want
    /// that; this just gets the visuals showing the right screen).
    /// </summary>
    public void RestoreToStage(int index)
    {
        if (stages == null || index < 0 || index >= stages.Count) return;

        CurrentStageIndex = index;
        CurrentStage = stages[index].stage;
        OnStageChanged?.Invoke(stages[index]);
    }

    [ContextMenu("Debug: Check Boot Drive Cables")]
    private void DebugCheckBootDriveCables()
    {
        if (bootDrive == null)
        {
            Debug.LogWarning("OSInstallManager: bootDrive is not assigned in the Inspector.");
            return;
        }

        if (bootDrive.cables == null || bootDrive.cables.Length == 0)
        {
            Debug.LogWarning($"OSInstallManager: {bootDrive.name}.cables is empty — assign its CableConnector(s) in the Inspector.");
            return;
        }

        for (int i = 0; i < bootDrive.cables.Length; i++)
        {
            var c = bootDrive.cables[i];
            if (c == null)
            {
                Debug.LogWarning($"OSInstallManager: {bootDrive.name}.cables[{i}] is an empty slot.");
                continue;
            }
            Debug.Log($"OSInstallManager: {bootDrive.name}.cables[{i}] = {c.name}, isPlugged = {c.isPlugged}");
        }
    }

    // ---------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------

    /// <summary>
    /// Call this when the player hits the "Power On" button on the assembled rig.
    /// </summary>
    public void BeginInstall()
    {
        if (IsRunning) return;

        DialogSequence failDialog;
        string failReason;
        if (!CanStartInstall(out failReason, out failDialog))
        {
            OnInstallFailed?.Invoke(failReason);

            if (failDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(failDialog);

            return;
        }

        IsRunning = true;
        _runningCoroutine = StartCoroutine(RunSequence());
    }

    public void StopInstall()
    {
        if (_runningCoroutine != null)
            StopCoroutine(_runningCoroutine);

        IsRunning = false;
    }

    /// <summary>
    /// Call this from UI when a stage has requiresUserInput = true
    /// (e.g. player picked a language or partition layout) to unblock the sequence.
    /// </summary>
    public void SubmitUserChoice()
    {
        _waitingForUserInput = false;
    }

    // ---------------------------------------------------------------
    // Validation — reuses existing part-state checks
    // ---------------------------------------------------------------

    private bool CanStartInstall(out string failReason, out DialogSequence failDialog)
    {
        // PSU is "powered" once every cable assigned to it (cables array on
        // CableGatedPartItem) is plugged in — same check the PSU itself uses
        // to decide whether it can be picked up, just inverted.
        if (psu == null || !AllCablesPlugged(psu.cables))
        {
            failReason = "No power detected. Check the PSU connections.";
            failDialog = noPowerWarning;
            return false;
        }

        if (ramSticks == null || ramSticks.Length == 0)
        {
            failReason = "No memory detected. Install at least one RAM stick.";
            failDialog = noRamWarning;
            return false;
        }

        foreach (var ram in ramSticks)
        {
            // A RAM stick is "installed" when it currently has a HomeSocket —
            // Pickup() nulls this out, ReturnToSocket() sets it. Clip-closed
            // state is already enforced by RAMPartItem.PickupBlockedReason,
            // so if it's seated here the latch is guaranteed closed.
            if (ram == null || ram.HomeSocket == null)
            {
                failReason = "RAM not fully seated in its socket.";
                failDialog = noRamWarning;
                return false;
            }
        }

        // Boot drive needs to be seated AND its cable(s) plugged in.
        if (bootDrive == null || bootDrive.HomeSocket == null || !AllCablesPlugged(bootDrive.cables))
        {
            failReason = "No boot device detected. Connect the storage cable.";
            failDialog = noBootDeviceWarning;
            return false;
        }

        // Separate from bootDrive above: bootDrive is the target disk the OS
        // gets installed ONTO. usbDrive is the install MEDIA — it must have
        // been through BootableUSBCreator and be plugged into the PC.
        if (usbDrive == null || !usbDrive.IsBootable || !usbDrive.IsInsertedInPort)
        {
            failReason = "No bootable USB drive detected. Create one and plug it in.";
            failDialog = noBootableUSBWarning;
            return false;
        }

        failReason = null;
        failDialog = null;
        return true;
    }

    private bool AllCablesPlugged(CableConnector[] connectors)
    {
        if (connectors == null || connectors.Length == 0) return false;
        foreach (var c in connectors)
        {
            if (c == null || !c.isPlugged) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------
    // Sequence driver
    // ---------------------------------------------------------------

    private bool _waitingForUserInput;

    private IEnumerator RunSequence()
    {
        for (int i = 0; i < stages.Count; i++)
        {
            var stageData = stages[i];
            CurrentStageIndex = i;
            CurrentStage = stageData.stage;
            OnStageChanged?.Invoke(stageData);

            // Auto-save checkpoint every time the install advances a stage.
            if (SaveManager.Instance != null)
                SaveManager.Instance.SaveCheckpoint();

            if (stageData.requiresRebootAnimation)
                yield return PlayRebootAnimation();

            if (stageData.educationalDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(stageData.educationalDialog);

            if (stageData.requiresUserInput)
            {
                _waitingForUserInput = true;
                while (_waitingForUserInput)
                    yield return null;
            }
            else
            {
                yield return RunTimedStage(stageData.duration);
            }
        }

        IsRunning = false;
        OnInstallComplete?.Invoke();

        if (SaveManager.Instance != null)
            SaveManager.Instance.SaveCheckpoint();
    }

    private IEnumerator RunTimedStage(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            OnStageProgress?.Invoke(Mathf.Clamp01(t / duration));
            yield return null;
        }
        OnStageProgress?.Invoke(1f);
    }

    private IEnumerator PlayRebootAnimation()
    {
        // Hook screen-off/on visual here (CRT flicker, fans spin down, etc.)
        // Kept as a short fixed pause as a placeholder.
        yield return new WaitForSeconds(1.2f);
    }
}