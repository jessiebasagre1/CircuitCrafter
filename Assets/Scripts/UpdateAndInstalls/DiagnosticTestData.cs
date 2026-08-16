using UnityEngine;

/// <summary>
/// Data-driven definition of one diagnostic/test step (step 4, "Test the
/// computer"). A test PASSES only if every listed prerequisite TaskManager
/// task is already complete -- e.g. a "Network Connectivity Test" that
/// requires SystemConfigTaskIds.InstallDriverNetwork, so skipping the network
/// driver earlier genuinely fails this test later instead of the two steps
/// being disconnected.
/// </summary>
[CreateAssetMenu(fileName = "NewDiagnosticTest", menuName = "PCSim/System Config/Diagnostic Test")]
public class DiagnosticTestData : ScriptableObject
{
    [Tooltip("Shown on the diagnostics screen while this test runs, e.g. \"Network Connectivity Test\".")]
    public string testName;

    [TextArea(2, 4)]
    [Tooltip("Optional flavor/help text describing what this test checks.")]
    public string description;

    [Tooltip("How long this test takes to run, in seconds.")]
    public float duration = 2f;

    [Tooltip("TaskManager taskId(s) that must ALL already be complete for this test to pass, e.g. SystemConfigTaskIds.InstallDriverNetwork. Leave empty for a test that always passes once run (e.g. a simple POST check).")]
    public string[] prerequisiteTaskIds;

    [TextArea(1, 3)]
    [Tooltip("Shown to the player when this test fails, explaining what to go fix, e.g. \"Network adapter not responding -- install the network driver in Device Manager.\"")]
    public string failureHint;
}
