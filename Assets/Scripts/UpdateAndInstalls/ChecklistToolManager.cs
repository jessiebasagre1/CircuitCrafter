using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One reusable "installer app" for the post-install desktop: Device Manager
/// (drivers), Software Center (application software) and Windows Update
/// (updates/patches) are all this same component, just pointed at a
/// different list of ChecklistItemData and a different groupTaskId  same
/// idea as DesktopIconButton being reused for every desktop icon.
///
/// No real installation happens; this is cosmetic progress only, same
/// pattern as OSInstallManager.RunTimedStage / BootableUSBCreator.WriteRoutine.
///
/// Each item reports to TWO TaskManager tasks:
///   - its own itemTaskId (1 step)   lets TaskDialogTrigger/QuizSetData
///     react to that specific item, and lets UI show a per-item checkmark
///     via IsItemComplete().
///   - the shared groupTaskId (1 step per item, required = item count)  a
///     single task other systems (COC1CompletionManager, DiagnosticTestData
///     prerequisites, ServiceReportPanel) can check without knowing how many
///     items this particular tool has.
///
/// UI (buttons, progress bar) is expected to call BeginInstall(item); this
/// class holds no Canvas references itself, same separation as
/// OSInstallManager/OSInstallView.
/// </summary>
public class ChecklistToolManager : MonoBehaviour
{
    [Tooltip("Shared task all items in this tool report a step to once installed, e.g. SystemConfigTaskIds.InstallDrivers. Registered with required steps = items.Count.")]
    [SerializeField] private string groupTaskId = SystemConfigTaskIds.InstallDrivers;

    [Tooltip("Every item this tool can install, e.g. the 4 driver categories for Device Manager.")]
    [SerializeField] private List<ChecklistItemData> items = new List<ChecklistItemData>();

    [Tooltip("Shown when an install is refused because the concurrency limit below has been " +
             "reached. No longer shown merely because something else is installing.")]
    [SerializeField] private DialogSequence alreadyInstallingWarning;

    [Tooltip("How many items may install at the same time. 0 means no limit, which is the " +
             "realistic setting for all three tools. Set it to 1 to go back to strictly one at a " +
             "time.")]
    [SerializeField] private int maxConcurrentInstalls = 0;

    public IReadOnlyList<ChecklistItemData> Items => items;

    // ------------------------------------------------------------------
    // SEVERAL ITEMS CAN INSTALL AT ONCE.
    //
    // It used to be one bool: anything already installing refused the next click. That is not how
    // any of these three tools behaves in reality - Windows Update downloads and installs a whole
    // list at once, Device Manager will happily work through several drivers, and a technician
    // kicks off the long installs and gets on with the next one rather than watching a bar. The
    // single flag also made the tool tedious to use and taught a habit nobody wants: wait, click,
    // wait, click.
    //
    // So the state is now a SET of items in progress. Order is still enforced per item through
    // prerequisiteTaskIds, which is the part that carries the competency - starting the display
    // driver while the chipset driver is still installing is still refused, because the chipset
    // task is not complete until its routine finishes.
    // ------------------------------------------------------------------

    private readonly HashSet<ChecklistItemData> installing = new HashSet<ChecklistItemData>();

    /// <summary>True while ANY item is installing. Kept for existing UI.</summary>
    public bool IsInstalling => installing.Count > 0;

    /// <summary>How many are going at once.</summary>
    public int InstallsInProgress => installing.Count;

    /// <summary>The most recently started item. Kept for existing UI; prefer IsItemInstalling,
    /// which is the question a row actually wants answered now that there can be several.</summary>
    public ChecklistItemData CurrentItem { get; private set; }

    /// <summary>True while this particular item is installing.</summary>
    public bool IsItemInstalling(ChecklistItemData item)
    {
        return item != null && installing.Contains(item);
    }

    // UI hooks -- mirrors OSInstallManager/BootableUSBCreator's event pattern
    public event Action<ChecklistItemData> OnItemInstallStarted;
    public event Action<ChecklistItemData, float> OnItemProgress; // 0-1
    public event Action<ChecklistItemData> OnItemInstallComplete;
    public event Action OnAllItemsComplete; // fires once, the run that finishes the last remaining item

    /// <summary>Fired when BeginInstall is refused because the item's prerequisites are not
    /// installed yet. string = the item's blockedHint, for UI that wants to show it inline.</summary>
    public event Action<ChecklistItemData, string> OnItemBlocked;

    private bool _allCompleteFired;

    private void Start()
    {
        if (TaskManager.Instance == null) return;

        TaskManager.Instance.RegisterTask(groupTaskId, Mathf.Max(items.Count, 1));
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.itemTaskId)) continue;
            TaskManager.Instance.RegisterTask(item.itemTaskId, 1);
        }

        // Resuming a save where every item was already done in an earlier
        // session -- catch up _allCompleteFired so a fresh completion later
        // (e.g. a 6th item added by a designer after ship) still fires once,
        // while not re-firing OnAllItemsComplete for something already done.
        _allCompleteFired = AllItemsComplete;
    }

    /// <summary>True once every item in this tool has been installed (checked
    /// live against TaskManager, so it's correct after a save reload with no
    /// extra persistence code needed here).</summary>
    public bool AllItemsComplete
    {
        get
        {
            if (items.Count == 0) return true;
            foreach (var item in items)
            {
                if (!IsItemComplete(item)) return false;
            }
            return true;
        }
    }

    public bool IsItemComplete(ChecklistItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemTaskId)) return false;
        return TaskManager.Instance != null && TaskManager.Instance.IsTaskComplete(item.itemTaskId);
    }

    /// <summary>Call from the item's "Install" button. Returns true if
    /// installation actually started, false if it was blocked (already
    /// installed, or something else is currently installing).</summary>
    public bool BeginInstall(ChecklistItemData item)
    {
        if (item == null) return false;

        if (IsItemComplete(item))
            return false; // nothing to do, already installed -- not an error

        // Double-clicking one row is the only "already installing" case left, and it is not worth
        // a popup - the learner can see that row's own progress bar moving.
        if (IsItemInstalling(item)) return false;

        if (maxConcurrentInstalls > 0 && installing.Count >= maxConcurrentInstalls)
        {
            if (alreadyInstallingWarning != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(alreadyInstallingWarning);
            else
                Debug.Log($"[ChecklistToolManager] '{item.displayName}' has to wait - " +
                          $"{installing.Count} of {maxConcurrentInstalls} installs already running.",
                          this);

            return false;
        }

        // Order used to be unenforced: any item in the list could be installed at any time, so
        // installing the display driver before the chipset driver passed silently. On a real
        // bench that is the classic mistake -- Windows names the device wrongly until the
        // chipset INF is in, so the rest go on top of a guess. An item now refuses to install
        // until every id in its prerequisiteTaskIds is complete.
        string missing;
        if (!PrerequisitesMet(item, out missing))
        {
            string reason = string.IsNullOrEmpty(item.blockedHint)
                ? $"'{missing}' must be installed first."
                : item.blockedHint;

            COC1AssessmentManager.ReportMistake(
                $"{item.displayName}: installed out of order - {reason}", item);

            // Corrective feedback: shown at once in Story Mode; in an assessment its text is kept
            // for the review screen instead of revealing the install order mid-run.
            if (item.blockedDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowCorrectiveDialog(item.blockedDialog);
            else
                Debug.LogWarning($"[ChecklistToolManager] {item.displayName} blocked: {reason}", item);

            OnItemBlocked?.Invoke(item, reason);
            return false;
        }

        StartCoroutine(InstallRoutine(item));
        return true;
    }

    /// <summary>True when every prerequisite task on this item is already complete. Also true
    /// when the item lists no prerequisites at all, which is how every existing asset behaves.</summary>
    public bool PrerequisitesMet(ChecklistItemData item, out string missingTaskId)
    {
        missingTaskId = null;
        if (item == null || item.prerequisiteTaskIds == null || item.prerequisiteTaskIds.Length == 0)
            return true;

        foreach (var id in item.prerequisiteTaskIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (TaskManager.Instance == null || !TaskManager.Instance.IsTaskComplete(id))
            {
                missingTaskId = id;
                return false;
            }
        }
        return true;
    }

    /// <summary>Convenience overload for UI that only needs the yes/no.</summary>
    public bool PrerequisitesMet(ChecklistItemData item)
    {
        string _;
        return PrerequisitesMet(item, out _);
    }

    private IEnumerator InstallRoutine(ChecklistItemData item)
    {
        installing.Add(item);
        CurrentItem = item;
        OnItemInstallStarted?.Invoke(item);

        float t = 0f;
        float duration = Mathf.Max(item.duration, 0.01f);
        while (t < duration)
        {
            t += Time.deltaTime;
            OnItemProgress?.Invoke(item, Mathf.Clamp01(t / duration));
            yield return null;
        }
        OnItemProgress?.Invoke(item, 1f);

        // Reporting here (rather than calling SaveManager directly) lets
        // TaskCheckpointAutosaver checkpoint and QuizManager auto-fire a quiz
        // generically, same pattern as BootableUSBCreator.WriteRoutine.
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.ReportProgress(item.itemTaskId);
            TaskManager.Instance.ReportProgress(groupTaskId);
        }

        if (item.educationalDialog != null && DialogManager.Instance != null)
            DialogManager.Instance.ShowDialog(item.educationalDialog);

        installing.Remove(item);

        // Only clear CurrentItem if this was the one it pointed at - with several running, the
        // last to finish must not blank out a reference to one still going.
        if (CurrentItem == item) CurrentItem = null;

        OnItemInstallComplete?.Invoke(item);

        if (!_allCompleteFired && AllItemsComplete)
        {
            _allCompleteFired = true;
            OnAllItemsComplete?.Invoke();
        }
    }
}