using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Simple bootable-USB creation tool: player picks an ISO and a target USB
/// drive from dropdowns, clicks Create, watches a progress bar, then the
/// drive is marked bootable. No real disk writing  cosmetic progress only,
/// same pattern as OSInstallManager's RunTimedStage.
///
/// UI (dropdowns, Create button) is expected to call the public methods below;
/// this class holds no Canvas references itself, same separation as
/// OSInstallManager/OSInstallView.
/// </summary>
public class BootableUSBCreator : MonoBehaviour
{
    public static BootableUSBCreator Instance { get; private set; }

    [Tooltip("Registered with TaskManager as a 1-step task the moment the drive finishes writing. " +
             "Add a QuizSetData with this exact taskId to QuizManager's Quiz Sets list to have a " +
             "quiz auto-fire right after USB creation completes -- no extra trigger script needed.")]
    public const string TaskId = "create_bootable_usb";

    [Header("Available options (populate dropdowns from these)")]
    [SerializeField] private ISOImageData[] availableImages;
    [SerializeField] private USBDriveItem[] availableDrives;

    [Header("Timing")]
    [SerializeField] private float writeDuration = 4f;

    [Tooltip("This reference PC's own USB port. The selected drive must be plugged into THIS specific port  not just plugged in somewhere  to write an image. Prevents a drive plugged into the TARGET PC's port from being mistaken for being plugged in here.")]
    [SerializeField] private USBPort referencePort;

    [Header("Warnings")]
    [Tooltip("Shown if the player clicks Create without picking both an ISO and a drive.")]
    [SerializeField] private DialogSequence noSelectionWarning;
    [Tooltip("Shown if the selected drive isn't currently plugged into a USB port.")]
    [SerializeField] private DialogSequence driveNotPluggedWarning;

    public ISOImageData[] AvailableImages => availableImages;
    public USBDriveItem[] AvailableDrives => availableDrives;

    private ISOImageData _selectedImage;
    private USBDriveItem _selectedDrive;
    public bool IsWriting { get; private set; }

    // UI hooks  mirrors OSInstallManager's event pattern
    public event Action<ISOImageData> OnImageSelected;
    public event Action<USBDriveItem> OnDriveSelected;
    public event Action<float> OnWriteProgress; // 0-1
    public event Action<USBDriveItem> OnWriteComplete;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.RegisterTask(TaskId, 1);
    }

    // Call from the ISO dropdown's OnValueChanged
    public void SelectImage(ISOImageData image)
    {
        _selectedImage = image;
        OnImageSelected?.Invoke(image);
    }

    // Call from the USB dropdown's OnValueChanged
    public void SelectDrive(USBDriveItem drive)
    {
        _selectedDrive = drive;
        OnDriveSelected?.Invoke(drive);
    }

    // Call from the "Create" button. Returns true if writing actually
    // started, false if it was blocked (no selection, drive not plugged in)
    //  the UI should only show "Writing..." state if this returns true.
    public bool BeginCreate()
    {
        if (IsWriting) return false;

        if (_selectedImage == null || _selectedDrive == null)
        {
            if (noSelectionWarning != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(noSelectionWarning);
            return false;
        }

        if (_selectedDrive.CurrentPort != referencePort)
        {
            if (driveNotPluggedWarning != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(driveNotPluggedWarning);
            return false;
        }

        StartCoroutine(WriteRoutine(_selectedImage, _selectedDrive));
        return true;
    }

    private IEnumerator WriteRoutine(ISOImageData image, USBDriveItem drive)
    {
        IsWriting = true;

        float t = 0f;
        while (t < writeDuration)
        {
            t += Time.deltaTime;
            OnWriteProgress?.Invoke(Mathf.Clamp01(t / writeDuration));
            yield return null;
        }
        OnWriteProgress?.Invoke(1f);

        drive.MarkAsBootable(image);

        IsWriting = false;
        OnWriteComplete?.Invoke(drive);

        // Reporting here (instead of calling SaveManager.SaveCheckpoint()
        // directly) lets TaskCheckpointAutosaver handle the checkpoint
        // generically once this task completes -- same pattern as
        // PCPartItem's assemble_pc/disassemble_pc reporting -- and lets
        // QuizManager auto-fire a quiz for this task if one is configured.
        if (TaskManager.Instance != null)
            TaskManager.Instance.ReportProgress(TaskId);
    }
}