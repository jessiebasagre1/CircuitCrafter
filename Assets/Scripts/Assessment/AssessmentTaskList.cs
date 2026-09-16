using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// The checklist a learner works down during an assessment: every task, in order, ticked off
    /// as it is finished.
    ///
    /// ATTACH THIS TO: the task list panel in an assessment scene. Build it with
    /// CircuitCrafter > Build Assessment Task List, which creates the panel, fills in the rows
    /// from whichever assessment manager is in the scene, and wires every field below.
    ///
    /// WHY IT READS TaskManager AND NOT THE ASSESSMENT MANAGER
    /// COC 1, 2, 3 and 4 have four different managers with four different shapes - stages,
    /// steps, tickets - and no common base class. A list that knew about all four would need
    /// changing every time one of them did, and would be the reason a new module's checklist
    /// silently showed nothing.
    ///
    /// All four report progress through the same TaskManager, and TaskManager already answers
    /// the only question this panel has: is this task id finished. So this component knows
    /// about TaskManager and nothing else, and works in any module without being told which
    /// one it is in. The module-specific part - what the rows are called - is filled in once,
    /// at build time, by the editor tool. That is the right place for knowledge that only
    /// matters once.
    ///
    /// WHY IT IS COLLAPSIBLE
    /// A checklist earns its place by being glanceable, and loses it the moment it covers the
    /// machine. Collapsed, it is a single bar reading "3 / 5 done" - still an answer to "how
    /// far am I", in about a centimetre of screen. That matters most on a phone, where the
    /// expanded list is a real fraction of the view.
    /// </summary>
    [DisallowMultipleComponent]
    public class AssessmentTaskList : MonoBehaviour
    {
        [System.Serializable]
        public class Row
        {
            [Tooltip("The TaskManager task id this row watches, e.g. \"assemble_pc\". It must match " +
                     "EXACTLY - a trailing space is a row that can never tick.")]
            public string taskId = "";

            [Tooltip("What the learner reads. Plain words, not the task id.")]
            public string label = "Task";

            [Tooltip("The row object in the panel. The builder fills this in.")]
            public AssessmentTaskRow view;
        }

        // ------------------------------------------------------------------
        [Header("What this list is for")]
        [Tooltip("The assessment mode id this belongs to - \"COC1\", \"COC2\", \"COC3\", \"COC4\".\n\n" +
                 "Used only to decide whether to appear. Leave blank to always appear.")]
        public string moduleId = "COC1";

        [Tooltip("Hide the whole panel outside assessment mode.\n\n" +
                 "Leave this ON. Story mode has its own guidance - dialogs, the waypoint arrow, the " +
                 "tutor - and a second checklist on top of it is noise. The assessment is the mode " +
                 "with no hand-holding, which is exactly why a plain list of what is being marked " +
                 "belongs there.")]
        public bool hideOutsideAssessment = true;

        // ------------------------------------------------------------------
        [Header("Tasks, in the order they must be done")]
        public List<Row> rows = new List<Row>();

        // ------------------------------------------------------------------
        [Header("Panel")]
        [Tooltip("The whole panel, header included.")]
        public GameObject panelRoot;

        [Tooltip("The part that collapses - the rows. The header stays visible.")]
        public GameObject bodyRoot;

        [Tooltip("The header button that collapses and expands the list. Wired automatically.")]
        public Button toggleButton;

        [Tooltip("Optional - a chevron that flips when the list collapses.")]
        public RectTransform toggleArrow;

        [Tooltip("Optional - reads \"3 / 5\". Visible collapsed as well as expanded, which is the " +
                 "point of collapsing it.")]
        public TMP_Text summaryText;

        [Tooltip("Optional - the title, e.g. \"Assessment tasks\".")]
        public TMP_Text titleText;

        [Tooltip("Start with the rows hidden. Off is usually right: a learner who never expands it " +
                 "never learns it is there.")]
        public bool startCollapsed;

        // ------------------------------------------------------------------
        [Header("Sound (optional)")]
        [Tooltip("Played when a row ticks. Leave empty for silence.\n\n" +
                 "Keep it small. This fires at the same moment as the stage completion sound and " +
                 "its dialog, and three things at once is a mess - a short tick UNDER the stage " +
                 "sound, not another fanfare beside it.")]
        public SfxEntry tickSound = new SfxEntry();

        // ------------------------------------------------------------------
        [Header("Diagnostics")]
        [Tooltip("Log each tick, and warn about task ids TaskManager has never heard of.")]
        public bool verbose = true;

        private bool collapsed;
        private int lastDoneCount = -1;
        private bool subscribed;

        // ==================================================================

        private void Awake()
        {
            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(ToggleCollapsed);
                toggleButton.onClick.AddListener(ToggleCollapsed);
            }
        }

        private void Start()
        {
            if (hideOutsideAssessment && !string.IsNullOrEmpty(moduleId) &&
                !AssessmentMode.IsAssessing(moduleId))
            {
                if (panelRoot != null) panelRoot.SetActive(false);
                enabled = false;
                return;
            }

            if (panelRoot != null) panelRoot.SetActive(true);

            SetCollapsed(startCollapsed);
            StartCoroutine(SubscribeWhenReady());
        }

        /// <summary>
        /// TaskManager may not have claimed its Instance yet - it lives on another object and
        /// script execution order between two Awakes is not something to rely on. Waiting a few
        /// frames costs nothing and is the same pattern StoryCompleteTrigger uses.
        /// </summary>
        private IEnumerator SubscribeWhenReady()
        {
            // Waits for EITHER source. COC 1 and COC 2 have a TaskManager and no recorder; COC 3
            // has a recorder and closes almost nothing through TaskManager; COC 4 has both. So
            // the wait ends as soon as one of them is up rather than insisting on a particular
            // one - insisting is how this would work in three modules and quietly not the fourth.
            for (int i = 0; i < 120 && TaskManager.Instance == null && Recorder == null; i++)
                yield return null;

            if (TaskManager.Instance == null && Recorder == null)
            {
                Debug.LogWarning("[AssessmentTaskList] Neither a TaskManager nor an AssessmentRecorder " +
                                 "appeared in this scene, so nothing can ever tick. The list will show " +
                                 "every task as pending.", this);
                Refresh();
                yield break;
            }

            if (TaskManager.Instance != null)
            {
                TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
                TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
            }

            if (Recorder != null)
            {
                Recorder.OnStageCompleted -= HandleTaskCompleted;
                Recorder.OnStageCompleted += HandleTaskCompleted;
            }

            subscribed = true;

            // Refresh AFTER subscribing, not before. A task finished in the gap would otherwise
            // be missed by both - too late for the refresh, too early for the event.
            Refresh();
            ValidateIds();
        }

        /// <summary>The recorder running this scene, if there is one. A property rather than a
        /// cached field because AssessmentRecorder.Active is claimed in its own Start, which can
        /// land after this component's.</summary>
        private static AssessmentRecorder Recorder => AssessmentRecorder.Active;

        private void OnDestroy()
        {
            if (!subscribed) return;

            if (TaskManager.Instance != null)
                TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;

            if (Recorder != null)
                Recorder.OnStageCompleted -= HandleTaskCompleted;
        }

        private void HandleTaskCompleted(string taskId)
        {
            Refresh();
        }

        // ==================================================================

        /// <summary>Re-reads every row from TaskManager. Safe to call as often as you like -
        /// it works out the whole state each time rather than tracking changes.</summary>
        public void Refresh()
        {
            int done = 0;
            int currentIndex = -1;

            // Two passes. The first counts what is finished, because "the current task" is the
            // first UNFINISHED one and that cannot be known until every row has been read -
            // tasks do not always complete in list order.
            var isDone = new bool[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row == null) continue;

                isDone[i] = IsComplete(row.taskId);
                if (isDone[i]) done++;
                else if (currentIndex < 0) currentIndex = i;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row == null || row.view == null) continue;

                row.view.SetLabel(row.label);
                row.view.SetState(isDone[i] ? AssessmentTaskRow.State.Done
                                : i == currentIndex ? AssessmentTaskRow.State.Current
                                : AssessmentTaskRow.State.Pending);
            }

            if (summaryText != null) summaryText.text = $"{done} / {rows.Count}";

            if (done != lastDoneCount)
            {
                // Only on an increase, and never on the first pass. lastDoneCount starts at -1,
                // so a scene entered with tasks already complete - a restored save - does not
                // fire a tick for each of them at once.
                if (lastDoneCount >= 0 && done > lastDoneCount)
                {
                    if (tickSound != null && tickSound.HasSound) SfxPlayer.PlayCustom(tickSound);
                    if (verbose) Debug.Log($"[AssessmentTaskList] {done} of {rows.Count} done.", this);
                }

                lastDoneCount = done;
            }
        }

        /// <summary>
        /// Done according to EITHER source, and that is what makes one panel work in all four
        /// modules with nothing to configure.
        ///
        /// COC 1 and COC 2 close their stages through TaskManager. COC 3 grades per step through
        /// AssessmentRecorder and reports almost nothing to TaskManager. COC 4 does both - its
        /// recorder stages carry TaskManager task ids. A setting to pick one would be a setting
        /// somebody sets wrong, and the failure would be a checklist that never ticks with
        /// nothing on screen or in the log to say why. Asking both costs two dictionary lookups.
        /// </summary>
        private static bool IsComplete(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return false;

            // Trimmed, for the same reason StoryCompleteTrigger trims: a task id typed into the
            // Inspector with a trailing space looks identical to the correct one and matches
            // nothing. Trimming here means a stray space costs a warning rather than a row that
            // never ticks for reasons nobody can see.
            string id = taskId.Trim();

            if (TaskManager.Instance != null && TaskManager.Instance.IsTaskComplete(id)) return true;
            if (Recorder != null && Recorder.IsStageComplete(id)) return true;

            return false;
        }

        // ==================================================================
        // Collapsing
        // ==================================================================

        public void ToggleCollapsed() => SetCollapsed(!collapsed);

        public void SetCollapsed(bool value)
        {
            collapsed = value;

            if (bodyRoot != null) bodyRoot.SetActive(!collapsed);

            // Flipped, not rotated over time. An animation here would need a coroutine that
            // survives the panel being switched off mid-flip, which is a lot of machinery for a
            // chevron.
            if (toggleArrow != null)
                toggleArrow.localRotation = Quaternion.Euler(0f, 0f, collapsed ? -90f : 0f);
        }

        // ==================================================================
        // Diagnostics
        // ==================================================================

        /// <summary>
        /// Warns about a row whose task id TaskManager has never been told about.
        ///
        /// This is the failure this panel is most likely to have, and the least visible: the
        /// row appears, looks completely normal, and simply never ticks. The learner assumes
        /// they have not finished the task and does it again. Nothing errors, because asking
        /// TaskManager about an unknown id is a perfectly legal question with the answer "no".
        /// </summary>
        private void ValidateIds()
        {
            if (!verbose) return;
            if (TaskManager.Instance == null && Recorder == null) return;

            var sb = new System.Text.StringBuilder();
            var seen = new HashSet<string>();

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row == null) { sb.AppendLine($"  Row {i + 1}: EMPTY ENTRY - remove it."); continue; }

                if (string.IsNullOrWhiteSpace(row.taskId))
                {
                    sb.AppendLine($"  Row {i + 1} '{row.label}': TASK ID IS BLANK - it can never tick.");
                    continue;
                }

                if (row.taskId != row.taskId.Trim())
                    sb.AppendLine($"  Row {i + 1} '{row.label}': task id \"{row.taskId}\" has a space " +
                                  "around it. Matched trimmed, so it still works - but fix the field.");

                if (!seen.Add(row.taskId.Trim()))
                    sb.AppendLine($"  Row {i + 1} '{row.label}': task id \"{row.taskId.Trim()}\" is " +
                                  "used by an earlier row too, so both tick together.");

                if (row.view == null)
                    sb.AppendLine($"  Row {i + 1} '{row.label}': has no row object assigned, so it " +
                                  "is not on screen at all.");
            }

            if (sb.Length > 0)
                Debug.LogWarning($"[AssessmentTaskList] Problems with the task list on '{name}':\n" +
                                 sb.ToString(), this);
        }

        [ContextMenu("Report task list")]
        private void ReportTaskList()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[AssessmentTaskList] on '{name}', module '{moduleId}':");
            sb.AppendLine($"  Assessment mode active for this module: " +
                          $"{(string.IsNullOrEmpty(moduleId) || AssessmentMode.IsAssessing(moduleId))}");
            sb.AppendLine($"  TaskManager present: {TaskManager.Instance != null}");
            sb.AppendLine($"  AssessmentRecorder present: {Recorder != null}" +
                          (Recorder != null ? $" (module '{Recorder.ModuleId}', running={Recorder.IsRunning})" : ""));
            sb.AppendLine($"  Panel Root: {(panelRoot == null ? "NOT ASSIGNED" : panelRoot.name)}");
            sb.AppendLine($"  Body Root: {(bodyRoot == null ? "NOT ASSIGNED - it cannot collapse" : bodyRoot.name)}");
            sb.AppendLine($"  {rows.Count} row(s):");

            foreach (Row row in rows)
            {
                if (row == null) { sb.AppendLine("    (empty entry)"); continue; }

                string state = (TaskManager.Instance == null && Recorder == null) ? "?"
                             : IsComplete(row.taskId) ? "DONE" : "pending";

                sb.AppendLine($"    [{state}] \"{row.label}\"   taskId=\"{row.taskId}\"" +
                              (row.view == null ? "   <-- NO ROW OBJECT" : ""));
            }

            Debug.Log(sb.ToString(), this);
        }
    }
}