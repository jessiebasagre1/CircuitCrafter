using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives step 5, "Check and document the completed work": a checklist
/// screen that pulls its check/cross state straight from TaskManager and
/// DiagnosticsTestManager (no state of its own to get out of sync), and a
/// Submit button that only succeeds once everything is genuinely finished --
/// same idea as a real technician's sign-off sheet.
///
/// UI (checklist rows, Submit button) is expected to call Refresh() when the
/// panel opens and SubmitReport() on click; this class holds no Canvas
/// references itself, same separation as OSInstallManager/OSInstallView.
/// </summary>
public class ServiceReportPanel : MonoBehaviour
{
    [Tooltip("Every TaskManager taskId that must be complete before the report can be submitted, e.g. InstallDrivers/InstallAppSoftware/InstallUpdates from SystemConfigTaskIds.")]
    [SerializeField] private string[] requiredTaskIds = new string[]
    {
        SystemConfigTaskIds.InstallDrivers,
        SystemConfigTaskIds.InstallAppSoftware,
        SystemConfigTaskIds.InstallUpdates,
    };

    [Tooltip("Optional -- if assigned, the report also requires every diagnostic test to have PASSED (not just been run) before it can be submitted.")]
    [SerializeField] private DiagnosticsTestManager diagnostics;

    [Tooltip("Reported to TaskManager (1 step) once the player successfully submits the report.")]
    [SerializeField] private string documentTaskId = SystemConfigTaskIds.DocumentWork;

    [Tooltip("Shown if the player clicks Submit while something on the checklist is still incomplete.")]
    [SerializeField] private DialogSequence incompleteWarning;

    public bool IsReadyToSubmit { get; private set; }

    /// <summary>Fires whenever Refresh() runs, with the current per-item
    /// complete/incomplete state (same order as requiredTaskIds) so UI can
    /// draw checkmarks without querying TaskManager itself.</summary>
    public event Action<IReadOnlyList<(string taskId, bool complete)>> OnChecklistRefreshed;

    public event Action OnReportSubmitted;

    private readonly List<(string taskId, bool complete)> _checklist = new List<(string, bool)>();

    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.RegisterTask(documentTaskId, 1);
    }

    private void OnEnable()
    {
        Refresh();
    }

    /// <summary>Recomputes the checklist against live TaskManager/diagnostics
    /// state. Call this whenever the panel opens, and it's cheap enough to
    /// also call after any relevant OnTaskCompleted event if you want the
    /// checklist to update live while the panel is already open.</summary>
    public void Refresh()
    {
        _checklist.Clear();
        bool ready = true;

        if (TaskManager.Instance != null)
        {
            foreach (var id in requiredTaskIds)
            {
                bool complete = TaskManager.Instance.IsTaskComplete(id);
                _checklist.Add((id, complete));
                if (!complete) ready = false;
            }
        }
        else
        {
            ready = false;
        }

        if (diagnostics != null && !diagnostics.AllTestsPassed)
            ready = false;

        IsReadyToSubmit = ready;
        OnChecklistRefreshed?.Invoke(_checklist);
    }

    /// <summary>Call from the Submit button. Returns true if the report was
    /// actually submitted, false if the checklist wasn't ready yet (an
    /// incompleteWarning dialog is shown in that case, if assigned).</summary>
    public bool SubmitReport()
    {
        Refresh(); // guard against stale state if the panel's been open a while

        if (!IsReadyToSubmit)
        {
            if (incompleteWarning != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(incompleteWarning);
            return false;
        }

        // Reporting here (rather than calling SaveManager directly) lets
        // TaskCheckpointAutosaver checkpoint and QuizManager auto-fire a
        // closing quiz generically, same pattern used everywhere else in
        // this module.
        if (TaskManager.Instance != null)
            TaskManager.Instance.ReportProgress(documentTaskId);

        OnReportSubmitted?.Invoke();
        return true;
    }
}
