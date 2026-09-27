using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Skips the two steps that come before the OS install - acquiring Rufus in the browser, and
/// writing the bootable USB - so the install itself can be tested without doing both every time.
///
/// ATTACH THIS TO: any object in the COC1 scene. Sits happily beside OSInstallTestBypass; between
/// them the whole module can be reached from a standing start in a few seconds.
///
/// Right-click the component and pick one:
///   Bypass: Rufus acquired          marks 'install_rufus' done
///   Bypass: USB made bootable       marks 'create_bootable_usb' done and writes the image
///   Bypass: both                    the usual one
///
/// ---------------------------------------------------------------------------------------------
/// IT IS OFF IN A REAL BUILD, AND THAT IS THE POINT
///
/// Reading the job order, finding the right download and writing the RIGHT image to the drive is
/// the competency here - BootableUSBCreator refuses the wrong image and reports it as a mistake
/// precisely because picking it is part of the assessment. A build that let a learner skip that
/// would be recording evidence of something they never did.
///
/// So this does nothing outside the Editor and Development Builds, and every bypass logs that the
/// run is not valid evidence. Leave Allow In Builds off for anything anyone is assessed on.
/// ---------------------------------------------------------------------------------------------
///
/// WHY IT CALLS THE MANAGERS' OWN COMPLETION RATHER THAN JUST TICKING THE TASK
/// Reporting 'install_rufus' to TaskManager by hand would mark the task done and leave everything
/// else undone: RufusSetupManager.Complete also stops the timed step, raises OnSequenceComplete,
/// shows the completion dialog, closes the browser panel and re-shows the desktop - and it is the
/// desktop coming back that makes DesktopIconButton notice the Rufus icon has unlocked. A bypass
/// that skipped all that would leave the scene in a state no real playthrough ever produces, and
/// the next bug found would be a phantom.
/// </summary>
[DisallowMultipleComponent]
public class RufusTestBypass : MonoBehaviour
{
    [Header("When to fire")]
    [Tooltip("Runs \"both\" shortly after the scene starts.")]
    [SerializeField] private bool bypassOnPlay;

    [SerializeField] private float startDelay = 0.5f;

    [Tooltip("Press this to run \"both\" at any time. None disables the shortcut.")]
    [SerializeField] private KeyCode hotkey = KeyCode.F10;

    [Header("Bootable USB")]
    [Tooltip("Which drive from BootableUSBCreator's Available Drives to write. 0 is the first.")]
    [SerializeField] private int driveIndex;

    [Tooltip("ON: write the image the job order asks for, read from BootableUSBCreator's Required " +
             "Image. OFF: write the first image in its Available Images list.\n\n" +
             "Leave this ON unless you are deliberately testing what happens when the wrong image " +
             "was written - the install checks the image, so the wrong one here fails later in a " +
             "way that looks like a different bug.")]
    [SerializeField] private bool writeTheRequiredImage = true;

    [Header("Safety")]
    [Tooltip("OFF (correct): the bypass does nothing outside the Editor and Development Builds.")]
    [SerializeField] private bool allowInBuilds;

    // ==================================================================

    private void Start()
    {
        if (bypassOnPlay) Invoke(nameof(BypassBoth), Mathf.Max(0f, startDelay));
    }

    private void Update()
    {
        if (hotkey != KeyCode.None && Input.GetKeyDown(hotkey)) BypassBoth();
    }

    // ==================================================================

    [ContextMenu("Bypass: both")]
    public void BypassBoth()
    {
        // Rufus first, and the order matters: completing it re-shows the desktop, which is what
        // unlocks the Rufus icon. Writing the USB first would work, but the scene would pass
        // through a state the real sequence never produces.
        BypassRufusAcquisition();
        BypassBootableUsb();
    }

    /// <summary>Marks 'install_rufus' done, through the manager's own completion path.</summary>
    [ContextMenu("Bypass: Rufus acquired")]
    public void BypassRufusAcquisition()
    {
        if (!Allowed()) return;

        RufusSetupManager manager = RufusSetupManager.Instance;
        if (manager == null) manager = FindAnyObjectByType<RufusSetupManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            Debug.LogError("[RufusTestBypass] No RufusSetupManager in this scene.", this);
            return;
        }

        if (manager.IsComplete)
        {
            Debug.Log("[RufusTestBypass] 'install_rufus' is already complete - nothing to do.", this);
            return;
        }

        MethodInfo complete = typeof(RufusSetupManager).GetMethod(
            "Complete", BindingFlags.Instance | BindingFlags.NonPublic);

        if (complete == null)
        {
            Debug.LogError("[RufusTestBypass] RufusSetupManager has no private method called " +
                           "'Complete' any more - it has been renamed. Update this script.", this);
            return;
        }

        complete.Invoke(manager, null);

        Debug.LogWarning("[RufusTestBypass] Rufus marked as acquired WITHOUT doing the browser " +
                         "steps. For testing only - this run is not valid evidence.", this);
    }

    /// <summary>Writes the image to a drive and marks 'create_bootable_usb' done.</summary>
    [ContextMenu("Bypass: USB made bootable")]
    public void BypassBootableUsb()
    {
        if (!Allowed()) return;

        BootableUSBCreator creator = BootableUSBCreator.Instance;
        if (creator == null) creator = FindAnyObjectByType<BootableUSBCreator>(FindObjectsInactive.Include);

        if (creator == null)
        {
            Debug.LogError("[RufusTestBypass] No BootableUSBCreator in this scene.", this);
            return;
        }

        USBDriveItem drive = PickDrive(creator);
        if (drive == null) return;

        ISOImageData image = PickImage(creator);
        if (image == null) return;

        if (drive.IsBootable)
        {
            Debug.Log($"[RufusTestBypass] '{drive.name}' is already bootable - writing it again " +
                      "anyway, which is harmless.", this);
        }

        // The same two lines the real WriteRoutine ends with, minus the progress bar. Marking the
        // drive is what OSInstallManager later checks; reporting the task is what TaskManager,
        // the autosaver and any quiz attached to this task are waiting for. Doing one without the
        // other leaves a half-finished state that is worse than not bypassing at all.
        drive.MarkAsBootable(image);

        if (TaskManager.Instance != null)
            TaskManager.Instance.ReportProgress(BootableUSBCreator.TaskId);
        else
            Debug.LogWarning("[RufusTestBypass] No TaskManager in this scene, so the drive is " +
                             "bootable but the task was not reported.", this);

        Debug.LogWarning($"[RufusTestBypass] '{drive.name}' written with '{image.displayName}' " +
                         "WITHOUT going through Rufus. For testing only - this run is not valid " +
                         "evidence.", this);
    }

    // ==================================================================

    private USBDriveItem PickDrive(BootableUSBCreator creator)
    {
        USBDriveItem[] drives = creator.AvailableDrives;

        if (drives == null || drives.Length == 0)
        {
            Debug.LogError("[RufusTestBypass] BootableUSBCreator has no Available Drives assigned, " +
                           "so there is nothing to write.", this);
            return null;
        }

        int index = Mathf.Clamp(driveIndex, 0, drives.Length - 1);

        if (index != driveIndex)
        {
            Debug.LogWarning($"[RufusTestBypass] Drive Index {driveIndex} is out of range - there " +
                             $"are {drives.Length} drive(s). Used {index} instead.", this);
        }

        if (drives[index] == null)
        {
            Debug.LogError($"[RufusTestBypass] Available Drives[{index}] is an empty slot.", this);
            return null;
        }

        return drives[index];
    }

    /// <summary>
    /// The image to write: the one the job order asks for, read from the private requiredImage
    /// field, falling back to the first available.
    ///
    /// Reflection because requiredImage is [SerializeField] private and belongs to the learner's
    /// own script - a testing convenience is not worth editing the file that grades the choice.
    /// </summary>
    private ISOImageData PickImage(BootableUSBCreator creator)
    {
        if (writeTheRequiredImage)
        {
            FieldInfo field = typeof(BootableUSBCreator).GetField(
                "requiredImage", BindingFlags.Instance | BindingFlags.NonPublic);

            var required = field != null ? field.GetValue(creator) as ISOImageData : null;

            if (required != null) return required;

            Debug.LogWarning("[RufusTestBypass] No Required Image is set on BootableUSBCreator " +
                             "(or the field has been renamed), so the first available image is " +
                             "being used. If the install later rejects it, that is why.", this);
        }

        ISOImageData[] images = creator.AvailableImages;

        if (images == null || images.Length == 0 || images[0] == null)
        {
            Debug.LogError("[RufusTestBypass] BootableUSBCreator has no Available Images assigned, " +
                           "so there is nothing to write.", this);
            return null;
        }

        return images[0];
    }

    private bool Allowed()
    {
        if (Application.isEditor || Debug.isDebugBuild || allowInBuilds) return true;

        Debug.LogWarning("[RufusTestBypass] Ignored - this is a normal build. Choosing and writing " +
                         "the right image is part of what is being assessed, so skipping it here " +
                         "would record evidence of something the learner never did.", this);
        return false;
    }

    // ==================================================================

    [ContextMenu("Report state")]
    public void Report()
    {
        var sb = new StringBuilder("[RufusTestBypass] REPORT\n");

        bool rufusDone = TaskManager.Instance != null &&
                         TaskManager.Instance.IsTaskComplete(RufusSetupManager.TaskId);

        bool usbDone = TaskManager.Instance != null &&
                       TaskManager.Instance.IsTaskComplete(BootableUSBCreator.TaskId);

        sb.AppendLine($"  {RufusSetupManager.TaskId}   : {(rufusDone ? "complete" : "not complete")}");
        sb.AppendLine($"  {BootableUSBCreator.TaskId}: {(usbDone ? "complete" : "not complete")}");

        BootableUSBCreator creator = BootableUSBCreator.Instance;
        if (creator == null) creator = FindAnyObjectByType<BootableUSBCreator>(FindObjectsInactive.Include);

        if (creator?.AvailableDrives != null)
        {
            sb.AppendLine("  Drives:");

            for (int i = 0; i < creator.AvailableDrives.Length; i++)
            {
                USBDriveItem drive = creator.AvailableDrives[i];

                sb.AppendLine(drive == null
                    ? $"    {i}  (empty slot)"
                    : $"    {i}  {drive.name} - " +
                      (drive.IsBootable
                          ? $"bootable with '{(drive.WrittenImage != null ? drive.WrittenImage.displayName : "?")}'"
                          : "not bootable"));
            }
        }

        Debug.Log(sb.ToString(), this);
    }
}
