using System.Collections.Generic;
using UnityEngine;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Every mistake made during an assessment, WITH the explanation that was withheld at the time.
    ///
    /// NOTHING TO ATTACH. The existing mistake paths feed it with one line each:
    ///   COC1AssessmentManager.AddMistake, COC2AssessmentManager.AddMistake / NotifyTestFailed /
    ///   ReportTerminationFailure, AssessmentRecorder.ReportMistake (COC3, COC4 and anything new).
    /// ResultsRepository.Save then folds it into the saved AssessmentResult, so the review screen and
    /// the history screen both see every mistake and its correct procedure.
    ///
    /// WHY IT EXISTS
    /// 1. "Don't reveal the correct procedure after a mistake" means an explanation has to be KEPT
    ///    somewhere until the run is over. The wrong-choice dialogs that used to show it immediately
    ///    now hand their text here instead (DialogManager.ShowCorrectiveDialog).
    /// 2. COC1 and COC2's managers keep their mistake log as private strings, so LegacyAssessmentBridge
    ///    could only save the COUNT. The journal gives the saved result the individual entries.
    /// 3. A COC4 run spans six scenes; a static survives all of them.
    ///
    /// It records nothing outside an assessment. Story mode's mistakes are explained on the spot.
    /// </summary>
    public static class MistakeJournal
    {
        public class Entry
        {
            public float atSeconds;
            public string moduleId = "";
            public string category = "general";
            public string description = "";
            public string correction = "";

            /// <summary>Where it happened - the COC4 ticket title, for instance. Optional.</summary>
            public string context = "";

            public int frame;
        }

        private static readonly List<Entry> entries = new List<Entry>();
        private static float startedAt;
        private static string runModule = "";

        public static IReadOnlyList<Entry> Entries => entries;
        public static string RunModule => runModule;
        public static int Count => entries.Count;

        /// <summary>
        /// Starts a fresh journal. Called at the start of every graded run - the COC1/2/3 managers'
        /// BeginAssessment, AssessmentRecorder.Begin when graded, and COC4AssessmentRun.Begin. A Retry
        /// reloads the scene, which runs those again, so a retried run never inherits old mistakes.
        /// </summary>
        public static void BeginRun(string moduleId, bool force = false)
        {
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

            if (!force)
            {
                // A COC4 run owns the journal for all six of its scenes. A recorder beginning in one
                // of the repair sites must not wipe the tickets already done.
                if (CircuitCrafter.Repair.COC4AssessmentRun.IsActive) return;

                // Two components starting the SAME run in the same scene (a manager and a recorder)
                // is one run. A Retry reloads the scene, which gives it a new handle, so that still
                // starts clean.
                if (runModule == (moduleId ?? "") && runScene == scene) return;
            }

            entries.Clear();
            startedAt = Time.realtimeSinceStartup;
            runModule = moduleId ?? "";
            runScene = scene;
            GuidancePolicy.ResetReview();
        }

        private static int runScene = -1;

        /// <summary>Records one mistake. Does nothing outside an assessment.</summary>
        public static void Record(string category, string description, string correction = null, string context = null)
        {
            if (!GuidancePolicy.IsAssessment) return;
            if (string.IsNullOrEmpty(description)) return;

            // The same event reported twice in one frame (a manager AND a recorder in one scene) is
            // one mistake, not two.
            if (entries.Count > 0)
            {
                Entry last = entries[entries.Count - 1];
                if (last.frame == Time.frameCount && last.description == description)
                {
                    if (string.IsNullOrEmpty(last.correction) && !string.IsNullOrEmpty(correction))
                        last.correction = correction;
                    return;
                }
            }

            if (string.IsNullOrEmpty(context) && RepairTicketMode.ActiveTicket != null)
                context = RepairTicketMode.ActiveTicket.title;

            entries.Add(new Entry
            {
                atSeconds = Time.realtimeSinceStartup - startedAt,
                moduleId = string.IsNullOrEmpty(runModule) ? (AssessmentMode.ModeId ?? "") : runModule,
                category = string.IsNullOrEmpty(category) ? "general" : category,
                description = description,
                correction = correction ?? "",
                context = context ?? "",
                frame = Time.frameCount
            });
        }

        /// <summary>
        /// Attaches an explanation to the mistake recorded THIS frame. Used by
        /// DialogManager.ShowCorrectiveDialog: the gameplay code reports the mistake and then asks
        /// for its explanation dialog on the next line; in an assessment the dialog's text is kept
        /// here instead of being shown. Returns false when there was no mistake this frame.
        /// </summary>
        public static bool AttachCorrectionToLast(string correction)
        {
            if (string.IsNullOrEmpty(correction) || entries.Count == 0) return false;

            Entry last = entries[entries.Count - 1];
            if (last.frame != Time.frameCount) return false;

            last.correction = string.IsNullOrEmpty(last.correction) ? correction : last.correction + "\n" + correction;
            return true;
        }

        /// <summary>
        /// Copies the journal into a result before it is saved. A mistake the result already lists
        /// (same description) gets its correction filled in; one it does not list is appended. The
        /// result's mistakeCount is NOT changed - the managers already counted these.
        ///
        /// Only for the run the journal belongs to: a result for another module leaves it untouched.
        /// </summary>
        public static void MergeInto(AssessmentResult result)
        {
            if (result == null || entries.Count == 0) return;
            if (!string.IsNullOrEmpty(runModule) && !string.IsNullOrEmpty(result.moduleId) &&
                runModule != result.moduleId) return;

            // Each result entry can stand for ONE journal entry. The same mistake made twice is two
            // entries in both lists, and must stay two - matching by text alone would fold them into one.
            var claimed = new HashSet<MistakeEntry>();

            foreach (Entry entry in entries)
            {
                MistakeEntry existing = null;
                foreach (MistakeEntry m in result.mistakes)
                {
                    if (m != null && !claimed.Contains(m) && m.description == entry.description) { existing = m; break; }
                }

                if (existing != null)
                {
                    claimed.Add(existing);
                    if (string.IsNullOrEmpty(existing.correction)) existing.correction = entry.correction;
                    if (string.IsNullOrEmpty(existing.context)) existing.context = entry.context;
                    continue;
                }

                result.mistakes.Add(new MistakeEntry
                {
                    atSeconds = entry.atSeconds,
                    category = entry.category,
                    description = entry.description,
                    correction = entry.correction,
                    context = entry.context
                });
            }
        }
    }
}
