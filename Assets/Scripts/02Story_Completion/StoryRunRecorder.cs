using System;
using System.Collections.Generic;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Records a STORY run into the ledger, so finishing a module in story mode appears on the
    /// competency screen as Practised instead of leaving no trace at all.
    ///
    /// ATTACH THIS TO: the same object as StoryCompleteTrigger, in each story scene.
    ///
    /// WHY NOTHING WAS BEING RECORDED
    /// LegacyAssessmentBridge watches COC1AssessmentManager.IsFinished. In story mode that manager
    /// is in the scene but never runs, so IsFinished never turns true and the bridge waits forever -
    /// which is why the Console says "Watching COC1AssessmentManager" and then never says
    /// "Recorded a run". Story mode ends through StoryCompleteTrigger instead, and nothing was
    /// listening to that. The quiz answers were being collected correctly the whole time; there was
    /// simply no result for them to be attached to.
    ///
    /// WHAT IT PRODUCES, AND WHAT IT CANNOT
    /// A Guided AssessmentResult. CompetencyLedger's first gate is mode == Assessment, so a run from
    /// here can NEVER award Competent however well it went - every outcome it evidences lands on
    /// Practised. That is the intended meaning of a story run: exposure, not evidence, and the
    /// ledger's own comment defends the rule. Nothing here weakens it.
    ///
    /// The quiz still counts on its own terms: KnowledgeCheck's tally rides along, so the knowledge
    /// line on the competency screen shows "82% in practice - needs a graded run" rather than
    /// "not taken". Progress the learner can see, evidence it still is not.
    ///
    /// IT WILL NOT DOUBLE-RECORD - LATCHED, NOT JUST CHECKED LIVE. Standing down only WHILE
    /// AssessmentMode.IsActive is true is not enough: the moment a real assessment run finishes
    /// and the player returns to the menu, AssessmentMode.End() fires and THIS component wakes up
    /// on its very next Update - and finds its required task already complete, because that
    /// completion is leftover state from the assessment run that just ended, not from an actual
    /// story playthrough. Without a permanent latch, that reads as "the module just finished in
    /// story mode" and writes a second, bogus Guided record with a fabricated 0% score right
    /// after the real Assessment one. So once AssessmentMode is ever seen active, this component
    /// disqualifies itself from recording for the rest of its lifetime, even after AssessmentMode
    /// goes back to false.
    /// </summary>
    [DisallowMultipleComponent]
    public class StoryRunRecorder : MonoBehaviour
    {
        [Header("What counts as finished")]
        [Tooltip("The task IDs that mean the module is over. Leave empty to copy them from the " +
                 "StoryCompleteTrigger on this same object, which is what you normally want - one " +
                 "list, no chance of the two drifting apart.")]
        [SerializeField] private string[] requiredTaskIds = new string[0];

        [Header("Identity")]
        [Tooltip("\"COC1\" ... \"COC4\". Must match the Module Id on the CompetencyDefinition asset, " +
                 "or the ledger has nothing to judge the run against and says so.")]
        [SerializeField] private string moduleId = "COC1";

        [Tooltip("Extra task IDs to write into the saved evidence file, beyond the required ones.\n\n" +
                 "OPTIONAL, and it does not affect the judgement: the ledger reads TaskManager live, " +
                 "so every outcome is evidenced whether or not its task is listed here. This only " +
                 "makes the stored JSON more complete for someone reading it later without the scene.\n\n" +
                 "For COC1 the useful set is: disassemble_pc, assemble_pc, install_rufus, " +
                 "create_bootable_usb, install_os, install_drivers, install_app_software, " +
                 "install_updates, test_computer, document_work.")]
        [SerializeField] private string[] evidenceTaskIds = new string[0];

        [Header("Diagnostics")]
        [SerializeField] private bool logDecision = true;

        private bool recorded;

        // Latched true the FIRST time AssessmentMode.IsActive is ever seen true, and never
        // cleared back to false. See the class comment above for exactly why a live check alone
        // isn't enough.
        private bool assessmentRanThisSession;

        // ==================================================================

        private void Start()
        {
            if (requiredTaskIds == null || requiredTaskIds.Length == 0)
                requiredTaskIds = ReadTriggerTaskIds();

            // A story run is one run: the tally has to start clean, or a second playthrough in the
            // same session counts the first playthrough's answers too.
            KnowledgeCheck.Begin(moduleId);

            if (logDecision)
                Debug.Log($"[StoryRunRecorder] Watching {Describe()} for module '{moduleId}'. " +
                          "A Practised record will be written when they are all complete.", this);
        }

        private void Update()
        {
            if (recorded) return;

            // Checking every frame rather than once at Start because AssessmentMode is a static
            // that a scene can enter after this component has woken. LATCHED rather than a plain
            // early-return: see the class comment for the race this specifically closes.
            if (AssessmentMode.IsActive)
            {
                if (!assessmentRanThisSession && logDecision)
                    Debug.Log($"[StoryRunRecorder] Assessment mode started for '{moduleId}' - " +
                              "standing down for the rest of this scene, even after it ends.", this);

                assessmentRanThisSession = true;
                return;
            }

            if (assessmentRanThisSession) return;

            if (!AllRequiredComplete()) return;

            recorded = true;
            Record();
        }

        // ==================================================================

        private void Record()
        {
            if (TaskManager.Instance == null)
            {
                Debug.LogWarning("[StoryRunRecorder] No TaskManager in this scene, so there is no " +
                                 "task state to record. Nothing was written.", this);
                return;
            }

            var result = new AssessmentResult
            {
                resultId = Guid.NewGuid().ToString("N").Substring(0, 12),
                moduleId = moduleId,

                // GUIDED, ALWAYS AND DELIBERATELY. This is the field that stops a story run ever
                // reading as competence, and it is set here rather than read from TrainingMode so
                // it cannot drift if a scene forgets to call Begin.
                mode = (int)SessionMode.Guided,

                startedUtc = "",
                finishedUtc = DateTime.UtcNow.ToString("o"),
                durationSeconds = Time.timeSinceLevelLoad,

                completed = true,

                // NO SCORE. A story run is not marked, and inventing a percentage here would put a
                // number on the one screen whose whole point is that competency is met or not met.
                // The ledger only reads ScorePercent when mode is Assessment, so leaving these at
                // zero changes no judgement.
                score = 0,
                maxScore = 0,

                mistakeCount = 0,

                // -1 via gradedAttempts == 0, which FirstAttemptPercent reports as "n/a". The same
                // choice LegacyAssessmentBridge makes rather than fabricating a number.
                firstAttemptCorrect = 0,
                gradedAttempts = 0
            };

            if (ProfileManager.Instance != null && ProfileManager.Instance.ActiveProfile != null)
            {
                result.profileId = ProfileManager.Instance.ActiveProfileId;
                result.learnerName = ProfileManager.Instance.ActiveProfile.displayName;
                result.learnerSection = ProfileManager.Instance.ActiveProfile.section;
            }

            AddStages(result);

            // Ledger first, exactly as the bridge and the recorder do it: RecordResult fills in
            // result.criteria AND pulls in the KnowledgeCheck tally, so the file saved next carries
            // both.
            if (CompetencyLedger.Instance != null)
            {
                CompetencyLedger.Instance.RecordResult(result);
            }
            else
            {
                Debug.LogWarning("[StoryRunRecorder] No CompetencyLedger in this scene - the run " +
                                 "cannot be judged. Add the AssessmentManagers prefab.", this);
            }

            ResultsRepository.Save(result);

            Debug.Log($"[StoryRunRecorder] Recorded a STORY run of '{moduleId}': " +
                      $"{result.stages.Count} task(s) complete, " +
                      (result.KnowledgeAttempted
                          ? $"quiz {result.knowledgeCorrect}/{result.knowledgeQuestions} ({result.KnowledgePercent}%)."
                          : "no quiz answered.") +
                      "\nEvery outcome it evidences becomes Practised - a guided run cannot award " +
                      "Competent, by design.", this);
        }

        /// <summary>
        /// Writes the completed tasks onto the result.
        ///
        /// THE JUDGEMENT DOES NOT DEPEND ON THIS. CompetencyLedger.RecordResult asks TaskManager
        /// directly - "(TaskManager.Instance != null &amp;&amp; IsTaskComplete(id)) || HasCompletedStage(...)"
        /// - and TaskManager is still alive in the scene at the moment this runs, so every outcome
        /// is evidenced from live state whether or not a stage appears here.
        ///
        /// The list is written anyway so the saved JSON is self-contained evidence that can be read
        /// years later without the scene, which is the reason StageOutcome exists at all. TaskManager
        /// exposes no way to enumerate its tasks, so this records the IDs it has been given rather
        /// than inventing a reflection hack for something the ledger does not need.
        /// </summary>
        private void AddStages(AssessmentResult result)
        {
            var seen = new HashSet<string>();

            AddIds(result, requiredTaskIds, seen);
            AddIds(result, evidenceTaskIds, seen);
        }

        private void AddIds(AssessmentResult result, string[] ids, HashSet<string> seen)
        {
            if (ids == null) return;

            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;

                string trimmed = id.Trim();
                if (!seen.Add(trimmed)) continue;
                if (!TaskManager.Instance.IsTaskComplete(trimmed)) continue;

                result.stages.Add(new StageOutcome
                {
                    stageId = trimmed,
                    displayName = trimmed,
                    completed = true,
                    points = 0,
                    completedAtSeconds = -1f,
                    attempts = 0,
                    firstAttemptCorrect = false
                });
            }
        }

        private bool AllRequiredComplete()
        {
            if (requiredTaskIds == null || requiredTaskIds.Length == 0) return false;
            if (TaskManager.Instance == null) return false;

            foreach (string id in requiredTaskIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!TaskManager.Instance.IsTaskComplete(id.Trim())) return false;
            }
            return true;
        }

        /// <summary>
        /// Borrows StoryCompleteTrigger's own list through SerializedObject-free reflection on the
        /// private field.
        ///
        /// Reflection rather than a public property because StoryCompleteTrigger is the learner's
        /// file and this must not require editing it. One list means the recorder and the trigger
        /// can never disagree about when the module ended - and disagreeing would mean a record
        /// written at a different moment from the completion panel, which is exactly the kind of
        /// almost-right that is hard to notice.
        /// </summary>
        private string[] ReadTriggerTaskIds()
        {
            var trigger = GetComponent<StoryCompleteTrigger>();
            if (trigger == null) return new string[0];

            var field = typeof(StoryCompleteTrigger).GetField("requiredTaskIds",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            return field != null ? field.GetValue(trigger) as string[] ?? new string[0] : new string[0];
        }

        private string Describe()
        {
            if (requiredTaskIds == null || requiredTaskIds.Length == 0)
                return "NO TASK IDS - nothing will ever be recorded. Put this on the same object as " +
                       "StoryCompleteTrigger, or fill Required Task Ids yourself";

            return requiredTaskIds.Length == 1
                ? $"'{requiredTaskIds[0]}'"
                : $"{requiredTaskIds.Length} tasks: {string.Join(", ", requiredTaskIds)}";
        }
    }
}