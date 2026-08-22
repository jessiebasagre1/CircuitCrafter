using UnityEngine;

/// <summary>
/// Shows and hides desktop shortcuts for programs that are not installed yet.
///
/// ATTACH THIS TO: the desktop panel - the object the icons are children of.
///
/// WHY IT LIVES ON THE PANEL AND NOT ON THE ICONS
/// A GameObject that switches itself off can never switch itself back on. Nothing on an
/// inactive object runs: no Update, no OnEnable, no coroutine. So an icon that hid itself when
/// locked would stay hidden forever, however many times the learner completed the task that
/// was supposed to reveal it. The decision has to be made by something that stays active, and
/// the desktop panel is the obvious candidate - it is switched on every time the learner comes
/// back to the desktop, which is exactly when the icons need re-checking.
///
/// NO WIRING NEEDED
/// It finds every DesktopIconButton beneath it with GetComponentsInChildren(true) - the `true`
/// matters, because the icons it most needs to find are the hidden ones. Drop it on the
/// desktop panel and it handles every icon there, including ones you add later.
/// </summary>
public class DesktopIconVisibility : MonoBehaviour
{
    [Tooltip("Also re-check while the desktop is already open, the moment any task completes. " +
             "Leave on: it costs nothing and it covers an icon that unlocks without the learner " +
             "leaving the desktop.")]
    [SerializeField] private bool refreshOnTaskCompleted = true;

    [Tooltip("Log each show/hide decision. Useful while setting the gates up, noisy afterwards.")]
    [SerializeField] private bool verbose;

    private void OnEnable()
    {
        if (refreshOnTaskCompleted && TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

        Refresh();
    }

    private void OnDisable()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
    }

    private void Start()
    {
        // OnEnable runs before every Start in the scene, so on the very first frame the tasks
        // these gates depend on may not be registered yet. One more pass after everything has
        // had its Start covers that without any ordering assumptions.
        Refresh();
    }

    private void HandleTaskCompleted(string taskId)
    {
        Refresh();
    }

    /// <summary>Re-checks every icon below this object. Safe to call as often as you like -
    /// it reads TaskManager live and caches nothing.</summary>
    public void Refresh()
    {
        // includeInactive: true. The icons that matter most here are the hidden ones, and a
        // search that skipped them could reveal an icon exactly once and never again.
        var icons = GetComponentsInChildren<DesktopIconButton>(true);

        foreach (var icon in icons)
        {
            if (icon == null) continue;

            if (!icon.HideWhenLocked)
            {
                // Not a hide-when-locked icon: leave its active state alone entirely. Something
                // else in the scene may own it, and stomping that would be a surprise.
                if (icon.gameObject.activeSelf) icon.Refresh();
                continue;
            }

            bool unlocked = icon.IsUnlocked;

            if (icon.gameObject.activeSelf != unlocked)
            {
                icon.gameObject.SetActive(unlocked);

                if (verbose)
                    Debug.Log($"[DesktopIconVisibility] '{icon.name}' " +
                              (unlocked ? "shown - its gate is now open." : "hidden - gate is locked."),
                              icon);
            }

            // Refresh() on an inactive icon would do nothing useful, and its own Awake has not
            // necessarily run yet either.
            if (unlocked) icon.Refresh();
        }
    }
}
