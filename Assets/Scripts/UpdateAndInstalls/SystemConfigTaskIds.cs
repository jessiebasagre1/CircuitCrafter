/// <summary>
/// Single source of truth for every TaskManager taskId used by the
/// post-install "Configure Computer Systems" phase (drivers, application
/// software, updates/patches, testing, documentation).
///
/// Mirrors PCDisassemblySetup.TaskId / AssembleTaskId  put the id in one
/// place so ChecklistToolManager instances, DiagnosticTestData prerequisites,
/// ServiceReportPanel, COC1CompletionManager, TaskDialogTrigger and
/// QuizSetData assets all agree on spelling. Inspector fields (ScriptableObject
/// assets, TaskDialogTrigger.taskId, QuizSetData.taskId) can't reference C#
/// consts directly  when wiring those in the Editor, type the exact string
/// from here.
/// </summary>
public static class SystemConfigTaskIds
{
    // Group tasks  one ChecklistToolManager per "app", each item inside it
    // reports to both its own item id below AND to this shared group id, so
    // ReportedProgress on the group reaches (required = item count) once
    // every item in that app has been completed at least once.
    public const string InstallDrivers = "install_drivers";
    public const string InstallAppSoftware = "install_app_software";
    public const string InstallUpdates = "install_updates";

    // Individual driver categories from the checklist (Motherboard/chipset,
    // Network, Audio, Display and other peripherals). Give each
    // ChecklistItemData asset one of these as its itemTaskId.
    public const string InstallDriverMotherboard = "install_driver_motherboard";
    public const string InstallDriverNetwork = "install_driver_network";
    public const string InstallDriverAudio = "install_driver_audio";
    public const string InstallDriverDisplay = "install_driver_display";

    // Individual application-software items. Same pattern as the driver ids
    // above: each ChecklistItemData asset gets one of these as its itemTaskId,
    // and also reports a step to the shared InstallAppSoftware group id.
    public const string InstallAppBrowser = "install_app_browser";
    public const string InstallAppOffice = "install_app_office";
    public const string InstallAppAntivirus = "install_app_antivirus";
    public const string InstallAppUtilities = "install_app_utilities";

    // Individual update/patch items.
    public const string InstallUpdateCumulative = "install_update_cumulative";
    public const string InstallUpdateSecurity = "install_update_security";
    public const string InstallUpdateDefender = "install_update_defender";

    // Step 4: Test the computer.
    public const string TestComputer = "test_computer";

    // Step 5: Check and document the completed work.
    public const string DocumentWork = "document_work";
}