using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

/// <summary>
/// Drives the simulated OS installation sequence after PC assembly.
/// Does NOT emulate a real OS install Â it's a scripted, data-driven
/// state machine (POST -> boot check -> installer -> progress -> reboot -> desktop)
/// that reuses the same event/listener pattern as DialogManager.
///
/// Wired to the real project API:
///   - PSUPartItem/SSDPartItem inherit CableGatedPartItem.cables[]; each
///     CableConnector exposes a public bool isPlugged
///   - RAMPartItem/PCPartItem.HomeSocket is null when picked up, non-null
///     when seated (RAMPartItem already gates the latch-closed check itself)
///   - DialogManager.Instance.ShowDialog(DialogSequence) Â no DialogType enum
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
        Complete,

        // APPENDED, NEVER INSERTED.
        // These values are serialised by ORDINAL on every OSInstallStageData asset,
        // so inserting one in the middle would silently re-point every existing asset
        // at the wrong stage. Appending keeps 0-9 meaning exactly what they always did.
        //
        // The order the install actually RUNS in comes from the stages LIST on this
        // component, not from this enum - RunSequence walks the list by index. So a new
        // stage slots anywhere in the sequence just by where you place it in that list.
        BiosSetup,
        ProductKey,
        EditionSelect,
        LicenseTerms,
        InstallType,
        RegionKeyboard,
        AccountSetup
    }

    [Header("Stage Data (in order)")]
    [SerializeField] private List<OSInstallStageData> stages = new List<OSInstallStageData>();

    [Header("Scene References")]
    [SerializeField] private PSUPartItem psu;
    [SerializeField] private SSDPartItem bootDrive;
    [SerializeField] private RAMPartItem[] ramSticks;
    [SerializeField] private USBDriveItem usbDrive;

    [Tooltip("This target PC's own USB port. usbDrive must be plugged into THIS specific port  not just plugged in somewhere  for install to be allowed. Prevents a drive left plugged into the reference PC's port from being mistaken for being plugged in here.")]
    [SerializeField] private USBPort targetPort;

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

    // UI listens to these Â mirrors DialogView's listener pattern
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
    /// replaying earlier stages or their timers Â used when restoring a save.
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
            Debug.LogWarning($"OSInstallManager: {bootDrive.name}.cables is empty Â assign its CableConnector(s) in the Inspector.");
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
        if (IsRunning)
        {
            Debug.LogWarning($"[OSInstallManager] BeginInstall ignored - already running. " +
                $"CurrentStage={CurrentStage}, waitingForUserInput={_waitingForUserInput}");
            return;
        }

        DialogSequence failDialog;
        string failReason;
        if (!CanStartInstall(out failReason, out failDialog))
        {
            Debug.LogWarning($"[OSInstallManager] BeginInstall blocked: {failReason}");

            OnInstallFailed?.Invoke(failReason);

            if (failDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(failDialog);
            else if (failDialog == null)
                Debug.LogWarning("[OSInstallManager] No warning DialogSequence assigned for this failure - player saw nothing on screen.");

            return;
        }

        IsRunning = true;

        // Resume from wherever a loaded save left off (RestoreToStage sets
        // CurrentStageIndex) rather than always restarting at stage 0 â
        // otherwise reloading mid-install and pressing Power On again would
        // silently discard progress and replay the whole sequence.
        int startIndex = CurrentStageIndex >= 0 ? CurrentStageIndex : 0;
        _runningCoroutine = StartCoroutine(RunSequence(startIndex));
    }

    public void StopInstall()
    {
        if (_runningCoroutine != null)
            StopCoroutine(_runningCoroutine);

        IsRunning = false;
    }

    /// <summary>The dropdown option (from OSInstallStageData.userChoices) the
    /// player picked on the current/last requiresUserInput stage, e.g.
    /// "Filipino" or "Erase disk". Null if that stage had no dropdown.</summary>
    public string SelectedChoice { get; private set; }

    /// <summary>
    /// Call this from UI when a stage has requiresUserInput = true
    /// (e.g. player picked a language or partition layout) to unblock the sequence.
    /// Pass the chosen dropdown option's text, or leave null for stages with
    /// no dropdown (plain Continue button).
    /// </summary>
    public void SubmitUserChoice(string choice = null)
    {
        SelectedChoice = choice;
        _waitingForUserInput = false;
    }

    // ---------------------------------------------------------------
    // Validation Â reuses existing part-state checks
    // ---------------------------------------------------------------

    private bool CanStartInstall(out string failReason, out DialogSequence failDialog)
    {
        // PSU is "powered" once every cable assigned to it (cables array on
        // CableGatedPartItem) is plugged in Â same check the PSU itself uses
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
            // A RAM stick is "installed" when it currently has a HomeSocket Â
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
        // gets installed ONTO. usbDrive is the install MEDIA Â it must have
        // been through BootableUSBCreator and be plugged into the PC.
        // Must be plugged into THIS PC's port specifically  IsInsertedInPort
        // alone would also be true if it's still sitting in the reference
        // PC's port and was never explicitly removed from there.
        if (usbDrive == null || !usbDrive.IsBootable || usbDrive.CurrentPort != targetPort)
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

    private IEnumerator RunSequence(int startIndex)
    {
        for (int i = startIndex; i < stages.Count; i++)
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
                // Choices used to be accepted unconditionally: SubmitUserChoice stored the
                // text and the sequence moved on, so picking "Upgrade" instead of "Custom",
                // or booting from the hard disk instead of the USB, passed silently. That is
                // fine for a free choice like language, but wrong for the ones with a
                // defensible answer - and those are exactly what an assessor watches.
                //
                // A stage now only advances when its correctChoice matches (or when it is
                // left empty, which keeps every existing asset behaving as before). A wrong
                // pick is recorded as a mistake and the learner chooses again, the same way
                // a wrong answer works in COC 3.
                while (true)
                {
                    _waitingForUserInput = true;
                    while (_waitingForUserInput)
                        yield return null;

                    if (string.IsNullOrEmpty(stageData.correctChoice)) break;

                    if (string.Equals(SelectedChoice, stageData.correctChoice,
                                      StringComparison.OrdinalIgnoreCase)) break;

                    COC1AssessmentManager.ReportMistake(
                        $"OS setup - {stageData.stage}: chose '{SelectedChoice}' " +
                        $"instead of '{stageData.correctChoice}'.", stageData);

                    // ignoreSuppression: this is corrective feedback, not a tutorial, so it
                    // must still appear while an assessment has tutorials switched off.
                    if (stageData.wrongChoiceDialog != null && DialogManager.Instance != null)
                        DialogManager.Instance.ShowDialog(stageData.wrongChoiceDialog, null, true);
                }
            }
            else
            {
                yield return RunTimedStage(stageData.duration);
            }
        }

        IsRunning = false;
        OnInstallComplete?.Invoke();

        if (SaveManager.Instance != null)
            SaveManager.Instance.SaveCheckpoint(showProgressToast: true); // OS install milestone
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