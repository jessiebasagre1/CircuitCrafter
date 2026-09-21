using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// A learner's standing on one module's UNDERPINNING KNOWLEDGE - the quiz.
    ///
    /// Kept beside the outcome records rather than inside them because TESDA issues one written
    /// test per unit of competency, not one per learning outcome. Best-ever is stored, matching
    /// how OutcomeRecord keeps the best evidence position: a rushed retry must not make a learner
    /// look like they have gone backwards.
    /// </summary>
    [Serializable]
    public class KnowledgeRecord
    {
        public string moduleId = "";

        [Tooltip("Best percentage achieved so far. -1 means the quiz has never been taken.")]
        public int bestPercent = -1;

        [Tooltip("True once a GRADED run's quiz reached the knowledge threshold. Never goes back to false.")]
        public bool passed;

        public int attempts;

        [Tooltip("resultId of the run that produced bestPercent, so the evidence file can be found.")]
        public string evidenceResultId = "";

        public string achievedUtc = "";
    }

    /// <summary>One learner's standing on one learning outcome, accumulated
    /// across every run they've ever done.</summary>
    [Serializable]
    public class OutcomeRecord
    {
        public string moduleId = "";
        public string outcomeCode = "";
        public string outcomeTitle = "";

        [Tooltip("CompetencyStatus as an int.")]
        public int status = (int)CompetencyStatus.NotAttempted;

        public int attempts;
        public int bestScorePercent;

        [Tooltip("How much of this outcome's evidence was in place at the end of the best run so far. Stored on the record rather than recomputed, because the competency screen usually runs on the main menu where there is no TaskManager to ask.")]
        public int evidenceSatisfied;
        public int evidenceRequired;

        public string lastResultId = "";
        public string lastUpdatedUtc = "";

        public CompetencyStatus Status => (CompetencyStatus)status;
    }

    /// <summary>
    /// The learner's standing across all four modules, persisted per profile.
    /// This is the object that makes the four modules one qualification rather
    /// than four separate games.
    ///
    /// ATTACH THIS TO: the persistent Managers object, beside SaveManager,
    /// ScoreManager and ProfileManager. It is DontDestroyOnLoad for the same
    /// reason they are.
    ///
    /// WHY NOT PUT THIS IN THE SAVE FILE
    /// It could have been another ISaveable - and that was the first instinct -
    /// but the save file is scene state that gets reset when a mode is restarted
    /// or a save is deleted. A competency record must outlive all of that: a
    /// learner who replays COC1 from scratch has not stopped being competent at
    /// assembling a PC. Keeping it in its own file, keyed by profile, is what
    /// gives it the right lifetime.
    ///
    /// PROMOTION RULES
    ///   Any completed run                       -> at least Practised
    ///   Assessment mode, outcome fully evidenced,
    ///   run completed, score >= pass threshold  -> Competent
    ///   Never demotes.
    ///
    /// The Guided exclusion is deliberate and worth defending: a run where the
    /// game told the learner what to do at every step is evidence of exposure,
    /// not of independent competence. Practice runs count as Practised for the
    /// same reason. Only an unassisted graded run can produce a Competent
    /// judgement, which is also how the real assessment works.
    /// </summary>
    public class CompetencyLedger : MonoBehaviour
    {
        public static CompetencyLedger Instance { get; private set; }

        [Header("Competency Definitions")]
        [Tooltip("One CompetencyDefinition asset per module. Used to turn a run's task completions into outcome judgements, and to render the competency profile screen.")]
        [SerializeField] private List<CompetencyDefinition> definitions = new List<CompetencyDefinition>();

        [Header("Rules")]
        [Tooltip("Minimum score percentage an ASSESSMENT run must reach before any of its outcomes can be marked Competent. Below this the run still records as Practised.")]
        [Range(0, 100)]
        [SerializeField] private int passThresholdPercent = 80;

        [Header("Underpinning knowledge (the quiz)")]
        [Tooltip("Require the module's quiz to be passed before any outcome can be marked Competent.\n\n" +
                 "TESDA's Evidence Plan lists demonstration AND written test as separate requirements " +
                 "that must both be met - it does not average them. Leaving this ON is the behaviour " +
                 "that matches the real instrument.\n\n" +
                 "Untick it only if a module genuinely has no quiz; a module with no quiz and this " +
                 "ticked can never award Competent, and the Console will say so.")]
        [SerializeField] private bool requireKnowledgeEvidence = true;

        [Tooltip("Minimum quiz percentage. TESDA written tests are commonly set at 75%.")]
        [Range(0, 100)]
        [SerializeField] private int knowledgeThresholdPercent = 75;

        /// <summary>Fires whenever any record changes. A competency profile screen
        /// should subscribe rather than poll.</summary>
        public event Action OnLedgerChanged;

        [Serializable]
        private class LedgerFile
        {
            public string profileId = "";
            public List<OutcomeRecord> records = new List<OutcomeRecord>();

            // Added after the first ledgers were written. JsonUtility simply leaves a field it
            // cannot find at its default, so an existing ledger_*.json loads with an empty list
            // and fills in from the next run - no migration, nothing to delete.
            public List<KnowledgeRecord> knowledge = new List<KnowledgeRecord>();
        }

        private LedgerFile ledger = new LedgerFile();

        public static string LedgerPathFor(string profileId)
        {
            string safe = string.IsNullOrEmpty(profileId) ? "default" : profileId;
            return Path.Combine(Application.persistentDataPath, $"ledger_{safe}.json");
        }

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy(this), not Destroy(gameObject) - see the long comment on
                // ProfileManager.Awake. Scene-specific recorder/bridge components
                // live on the per-scene copy of this object, and destroying the
                // whole GameObject would take them down with it before they ever
                // ran, with no error to explain the silence.
                Debug.Log($"[CompetencyLedger] A second CompetencyLedger on '{name}' stood down - the one from the first scene is still active. Any other components on this object keep running.", this);
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Deliberately in Start, not Awake: ProfileManager also runs its Awake
            // this frame and there is no guaranteed order between two components,
            // even on the same GameObject. By Start it has definitely chosen an
            // active profile, so the right ledger gets loaded first time rather
            // than loading "default" and then being corrected.
            if (ProfileManager.Instance != null)
            {
                ProfileManager.Instance.OnActiveProfileChanged += HandleProfileChanged;
                Load(ProfileManager.Instance.ActiveProfileId);
            }
            else
            {
                Debug.LogWarning("[CompetencyLedger] No ProfileManager in the scene - falling back to a single shared ledger. Add ProfileManager to the same Managers object for per-learner records.");
                Load("default");
            }
        }

        private void OnDestroy()
        {
            if (ProfileManager.Instance != null)
                ProfileManager.Instance.OnActiveProfileChanged -= HandleProfileChanged;

            if (Instance == this) Instance = null;
        }

        private void HandleProfileChanged(LearnerProfile profile)
        {
            Load(profile != null ? profile.profileId : "default");
        }

        // ------------------------------------------------------------------
        // Recording
        // ------------------------------------------------------------------

        /// <summary>
        /// Folds a finished run into the ledger. Called by AssessmentRecorder;
        /// call it yourself if you build a result some other way.
        ///
        /// Also fills in result.criteria as a side effect, so the stored result
        /// file carries its own copy of the judgement and can be read back later
        /// without this ledger or the definition asset.
        /// </summary>
        public void RecordResult(AssessmentResult result)
        {
            if (result == null) return;

            CompetencyDefinition definition = GetDefinition(result.moduleId);
            if (definition == null)
            {
                Debug.LogWarning($"[CompetencyLedger] No CompetencyDefinition assigned for module '{result.moduleId}' - the run was saved, but no outcomes could be judged. Add the asset to the Definitions list.");
                return;
            }

            // Evidence comes from two places, and it needs both.
            //
            //   1. Live TaskManager state at the end of the run. Reading this
            //      rather than replaying the stage list means an outcome
            //      evidenced by work done outside the recorder's own stages - a
            //      checklist tool, a quiz, a station elsewhere in the scene -
            //      still counts.
            //
            //   2. Stages this run completed. COC3 doesn't use TaskManager at
            //      all, and COC4's repair tasks are named per ticket at runtime,
            //      so those modules close stages by manual id. Without this
            //      second source their outcomes could never be evidenced and
            //      every learner would sit at Practised forever.
            //
            // A CompetencyDefinition therefore accepts EITHER a TaskManager task
            // id or an AssessmentRecorder manual stage id in evidenceTaskIds,
            // and doesn't have to say which it is.
            Func<string, bool> isComplete = id =>
                (TaskManager.Instance != null && TaskManager.Instance.IsTaskComplete(
                    string.IsNullOrWhiteSpace(id) ? id : id.Trim()))
                || HasCompletedStage(result, id);

            // Knowledge evidence is gathered here rather than by whoever built the result, because
            // RecordResult runs BEFORE ResultsRepository.Save in both the recorder and the legacy
            // bridge - so the JSON evidence file and the CSV row carry these numbers too, without
            // either of those scripts having to change.
            KnowledgeCheck.FillInto(result);

            bool modeOk = result.Mode == SessionMode.Assessment;
            bool completedOk = result.completed;
            bool scoreOk = result.ScorePercent >= passThresholdPercent;

            // THE SECOND EVIDENCE REQUIREMENT, NOT A SECOND HALF OF THE FIRST.
            //
            // Performance and underpinning knowledge are separate requirements in TESDA's Evidence
            // Plan and both must be met; they are never averaged. So this is another AND, and the
            // quiz result is deliberately NOT mixed into result.score.
            bool knowledgeOk = !requireKnowledgeEvidence
                               || (result.KnowledgeAttempted &&
                                   result.KnowledgePercent >= knowledgeThresholdPercent);

            bool canAwardCompetent = modeOk && completedOk && scoreOk && knowledgeOk;

            // SAY WHY, WHEN THE ANSWER IS NO.
            //
            // Three separate conditions stand between a finished run and a Competent judgement,
            // and until now failing any of them looked identical from the outside: the run saves,
            // the profile screen shows Practised, and nothing anywhere says which one it was. A
            // trainer looking at a learner who plainly did the work has no way to tell whether the
            // score fell a point under the threshold, a stage never closed, or the run was not
            // recognised as an assessment at all.
            if (!canAwardCompetent)
            {
                string why = null;
                if (!modeOk)
                    why = Add(why, $"the run was recorded as {result.Mode}, not Assessment - it was " +
                                   "started from the campaign menu rather than the assessment menu");
                if (!completedOk)
                    why = Add(why, "not every stage was completed - an unfinished run can only ever " +
                                   "be Practised, however well it scored");
                if (!scoreOk)
                    why = Add(why, $"the score was {result.ScorePercent}%, below the {passThresholdPercent}% " +
                                   "pass threshold on this ledger");
                if (!knowledgeOk)
                    why = Add(why, result.KnowledgeAttempted
                        ? $"the quiz scored {result.KnowledgePercent}% ({result.knowledgeCorrect} of " +
                          $"{result.knowledgeQuestions}), below the {knowledgeThresholdPercent}% knowledge " +
                          "threshold - performance and knowledge are separate requirements and both must be met"
                        : "no quiz was answered during the run, so there is no underpinning-knowledge " +
                          "evidence. If this module has no quiz, untick Require Knowledge Evidence on " +
                          "this ledger - otherwise it can never award Competent");

                Debug.LogWarning($"[CompetencyLedger] '{result.moduleId}' run recorded, but NOTHING can " +
                                 $"be marked Competent from it: {why}.\n\n" +
                                 "The outcomes below will be stored as Practised instead.");
            }

            result.criteria.Clear();

            var evidenceReport = new System.Text.StringBuilder();

            foreach (var outcome in definition.outcomes)
            {
                if (outcome == null || string.IsNullOrEmpty(outcome.code)) continue;

                bool satisfied = definition.Evaluate(outcome, isComplete, out int have, out int need);

                evidenceReport.AppendLine($"  {outcome.code} '{outcome.title}': evidence {have}/{need}" +
                                          (satisfied ? " - satisfied" : " - NOT satisfied"));

                // NAME THE IDs THAT DID NOT MATCH, AND THE ONES THAT WERE AVAILABLE.
                //
                // "0 of 1 evidenced" is the single most common thing to go wrong here and the least
                // informative message possible: it says an outcome was not evidenced without saying
                // WHICH id was looked for or what the run actually closed. The two lists almost
                // always differ by a typo, or by an id the module names at runtime that the asset
                // could not have known - COC 4 names its repair stages per ticket.
                //
                // Printed only for outcomes that failed, so a clean run stays quiet.
                if (!satisfied && outcome.evidenceTaskIds != null)
                {
                    foreach (string id in outcome.evidenceTaskIds)
                    {
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        evidenceReport.AppendLine($"      looking for '{id.Trim()}' : " +
                                                  (isComplete(id) ? "found" : "NOT FOUND"));
                    }
                }

                CompetencyStatus earned;
                if (satisfied && canAwardCompetent) earned = CompetencyStatus.Competent;
                else if (have > 0 || result.completed) earned = CompetencyStatus.Practised;
                else earned = CompetencyStatus.NotAttempted;

                result.criteria.Add(new CriterionOutcome
                {
                    outcomeCode = outcome.code,
                    outcomeTitle = outcome.title,
                    status = (int)earned,
                    evidenceSatisfied = have,
                    evidenceRequired = need
                });

                Apply(definition.moduleId, outcome, earned, result, have, need);
            }

            // Printed whether or not anything was awarded. An outcome sitting at 2/3 names the one
            // piece of evidence that never arrived, which is usually an evidenceTaskIds entry that
            // does not match any task id the module actually reports.
            string knowledgeLine = result.KnowledgeAttempted
                ? $"{result.KnowledgePercent}% ({result.knowledgeCorrect}/{result.knowledgeQuestions} " +
                  $"across {result.knowledgeQuizzes} quiz(zes)), threshold {knowledgeThresholdPercent}%"
                : "not taken";

            Debug.Log($"[CompetencyLedger] '{result.moduleId}' outcomes judged\n" +
                      $"  Performance : {result.ScorePercent}%, threshold {passThresholdPercent}%\n" +
                      $"  Knowledge   : {knowledgeLine}" +
                      (requireKnowledgeEvidence ? "" : "   (not required on this ledger)") + "\n" +
                      $"  All stages complete: {result.completed}, mode {result.Mode}\n" +
                      evidenceReport);

            // What the run ACTUALLY produced, once, under the per-outcome lines. Copy the ids from
            // here into the CompetencyDefinition's Evidence Task Ids and the outcome starts
            // evidencing - no guessing at what a module calls its stages.
            if (result.stages != null && result.stages.Count > 0)
            {
                var closed = new System.Text.StringBuilder();
                foreach (var stage in result.stages)
                {
                    if (stage == null || !stage.completed) continue;
                    if (closed.Length > 0) closed.Append(", ");
                    closed.Append(stage.stageId);
                }

                Debug.Log($"[CompetencyLedger] Stage ids this '{result.moduleId}' run actually closed:\n  " +
                          (closed.Length > 0 ? closed.ToString() : "(none)") +
                          "\n\nAn outcome stuck at 0 of N is looking for an id that is not in this list. " +
                          "Paste the right ones into that outcome's Evidence Task Ids.");
            }

            RecordKnowledge(result);

            Save();
            OnLedgerChanged?.Invoke();
        }

        private static string Add(string all, string line)
        {
            return all == null ? line : all + "; and " + line;
        }

        // ------------------------------------------------------------------
        // Underpinning knowledge
        // ------------------------------------------------------------------

        /// <summary>
        /// Folds this run's quiz into the module's knowledge standing.
        ///
        /// BEST-EVER, NEVER WORSE - the same rule OutcomeRecord follows for evidence position. A
        /// learner who scored 90% and then replays the module and rushes the quiz has not become
        /// less knowledgeable, and a record that slid backwards would make the screen useless as a
        /// history.
        ///
        /// 'passed' is one-way and only a GRADED run can set it, for the same reason a Guided run
        /// cannot award Competent: answering questions with the game leading you is exposure.
        /// Practice attempts still update bestPercent, so progress is visible.
        /// </summary>
        private void RecordKnowledge(AssessmentResult result)
        {
            if (result == null || !result.KnowledgeAttempted) return;
            if (string.IsNullOrEmpty(result.moduleId)) return;

            KnowledgeRecord record = ledger.knowledge.Find(
                k => k != null && k.moduleId == result.moduleId);

            if (record == null)
            {
                record = new KnowledgeRecord { moduleId = result.moduleId };
                ledger.knowledge.Add(record);
            }

            record.attempts++;

            int percent = result.KnowledgePercent;
            if (percent > record.bestPercent)
            {
                record.bestPercent = percent;
                record.evidenceResultId = result.resultId;
                record.achievedUtc = result.finishedUtc;
            }

            if (result.Mode == SessionMode.Assessment && percent >= knowledgeThresholdPercent)
                record.passed = true;

            Debug.Log($"[CompetencyLedger] Knowledge for '{result.moduleId}': this run {percent}%, " +
                      $"best {record.bestPercent}%, passed: {record.passed} " +
                      $"(attempt {record.attempts}).");
        }

        /// <summary>This learner's knowledge standing for a module, or null if they have never
        /// answered a question in it. Safe to call on the main menu - it reads the ledger only.</summary>
        public KnowledgeRecord GetKnowledge(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId) || ledger.knowledge == null) return null;
            return ledger.knowledge.Find(k => k != null && k.moduleId == moduleId);
        }

        /// <summary>The knowledge threshold, so a screen can say "75% needed" without hard-coding
        /// a number that lives on this component.</summary>
        public int KnowledgeThresholdPercent => knowledgeThresholdPercent;

        /// <summary>Whether this ledger demands knowledge evidence at all, so a screen can hide the
        /// knowledge line entirely on a build that does not use quizzes.</summary>
        public bool RequiresKnowledgeEvidence => requireKnowledgeEvidence;

        /// <summary>The performance threshold, for the same reason.</summary>
        public int PassThresholdPercent => passThresholdPercent;

        /// <summary>True if this run closed a stage with the given id. Lets a
        /// manual stage id stand as evidence for modules that don't route their
        /// progress through TaskManager - see the comment in RecordResult.</summary>
        private static bool HasCompletedStage(AssessmentResult result, string stageId)
        {
            if (result == null || string.IsNullOrWhiteSpace(stageId)) return false;

            string wanted = stageId.Trim();

            // Trimmed, for the same reason as GetDefinition: a stage id comes from an Inspector
            // field and the evidence id it is being matched against comes from a different one.
            foreach (var stage in result.stages)
            {
                if (stage == null || !stage.completed || string.IsNullOrWhiteSpace(stage.stageId))
                    continue;

                if (string.Equals(stage.stageId.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void Apply(string moduleId, CompetencyDefinition.LearningOutcome outcome,
                           CompetencyStatus earned, AssessmentResult result,
                           int evidenceSatisfied, int evidenceRequired)
        {
            OutcomeRecord record = GetOrCreate(moduleId, outcome.code);
            record.outcomeTitle = outcome.title;

            if (earned != CompetencyStatus.NotAttempted)
                record.attempts++;

            // Keep the best evidence position ever reached, not the latest. A
            // learner who evidenced 4 of 4 last week and only got through 2 of 4
            // on a rushed retry has not un-done the earlier work, and the screen
            // showing them slipping backwards would be both wrong and dispiriting.
            record.evidenceRequired = evidenceRequired;
            if (evidenceSatisfied > record.evidenceSatisfied)
                record.evidenceSatisfied = evidenceSatisfied;

            // One-way promotion. See the class comment - a bad retry must not
            // erase a judgement the learner already earned.
            if ((int)earned > record.status)
                record.status = (int)earned;

            if (result.ScorePercent > record.bestScorePercent)
                record.bestScorePercent = result.ScorePercent;

            record.lastResultId = result.resultId;
            record.lastUpdatedUtc = DateTime.UtcNow.ToString("o");
        }

        // ------------------------------------------------------------------
        // Queries - for the competency profile screen and for unlock rules
        // ------------------------------------------------------------------

        public CompetencyStatus GetStatus(string moduleId, string outcomeCode)
        {
            var record = Find(moduleId, outcomeCode);
            return record != null ? record.Status : CompetencyStatus.NotAttempted;
        }

        public IReadOnlyList<OutcomeRecord> AllRecords => ledger.records;

        public List<OutcomeRecord> GetRecords(string moduleId)
        {
            var list = new List<OutcomeRecord>();
            foreach (var record in ledger.records)
            {
                if (record != null && record.moduleId == moduleId) list.Add(record);
            }
            return list;
        }

        public CompetencyDefinition GetDefinition(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId)) return null;

            string wanted = moduleId.Trim();

            // Trimmed and case-insensitive. Both sides are hand-typed strings in two different
            // Inspectors - the module id on the definition asset, and the one the assessment menu
            // starts the run with - and an exact compare means 'coc2' or 'COC2 ' silently finds
            // nothing. The run then saves with no outcomes judged at all.
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.moduleId)) continue;

                if (string.Equals(definition.moduleId.Trim(), wanted,
                                  StringComparison.OrdinalIgnoreCase))
                    return definition;
            }
            return null;
        }

        public IReadOnlyList<CompetencyDefinition> Definitions => definitions;

        /// <summary>How many outcomes in a module are Competent, and how many
        /// exist. Reads the count from the DEFINITION rather than from the stored
        /// records, so an outcome never attempted still counts in the denominator -
        /// otherwise a learner who has done one outcome shows as 100 percent.</summary>
        public void GetModuleProgress(string moduleId, out int competent, out int total)
        {
            competent = 0;
            total = 0;

            var definition = GetDefinition(moduleId);
            if (definition == null) return;

            foreach (var outcome in definition.outcomes)
            {
                if (outcome == null || string.IsNullOrEmpty(outcome.code)) continue;
                total++;
                if (GetStatus(moduleId, outcome.code) == CompetencyStatus.Competent) competent++;
            }
        }

        /// <summary>True when every outcome in a module has been judged Competent.
        /// This is the check to gate a COC4 ticket on - a cabling fault should
        /// require COC2 competence, not merely that COC2 was finished once.</summary>
        public bool IsModuleCompetent(string moduleId)
        {
            GetModuleProgress(moduleId, out int competent, out int total);
            return total > 0 && competent == total;
        }

        // ------------------------------------------------------------------
        // Persistence
        // ------------------------------------------------------------------

        private OutcomeRecord Find(string moduleId, string outcomeCode)
        {
            foreach (var record in ledger.records)
            {
                if (record != null && record.moduleId == moduleId && record.outcomeCode == outcomeCode)
                    return record;
            }
            return null;
        }

        private OutcomeRecord GetOrCreate(string moduleId, string outcomeCode)
        {
            var existing = Find(moduleId, outcomeCode);
            if (existing != null) return existing;

            var record = new OutcomeRecord
            {
                moduleId = moduleId,
                outcomeCode = outcomeCode,
                status = (int)CompetencyStatus.NotAttempted
            };
            ledger.records.Add(record);
            return record;
        }

        public void Load(string profileId)
        {
            string path = LedgerPathFor(profileId);

            if (!File.Exists(path))
            {
                ledger = new LedgerFile { profileId = profileId };
                OnLedgerChanged?.Invoke();
                return;
            }

            try
            {
                ledger = JsonUtility.FromJson<LedgerFile>(File.ReadAllText(path)) ?? new LedgerFile();
                if (ledger.records == null) ledger.records = new List<OutcomeRecord>();
                if (ledger.knowledge == null) ledger.knowledge = new List<KnowledgeRecord>();
                ledger.profileId = profileId;
                Debug.Log($"[CompetencyLedger] Loaded {ledger.records.Count} record(s) for profile '{profileId}'.");
            }
            catch (Exception e)
            {
                // Same reasoning as ProfileManager: start clean in memory, leave the
                // file alone so it can be recovered rather than overwritten.
                Debug.LogWarning($"[CompetencyLedger] Couldn't read '{path}' ({e.Message}). Starting an empty ledger; the existing file has been left in place.");
                ledger = new LedgerFile { profileId = profileId };
            }

            OnLedgerChanged?.Invoke();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(LedgerPathFor(ledger.profileId), JsonUtility.ToJson(ledger, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[CompetencyLedger] Couldn't write the ledger: {e.Message}");
            }
        }

        /// <summary>Wipes the active profile's ledger: every outcome AND the written-test (quiz)
        /// results, so the competency screen goes fully back to Not Attempted. For a reset
        /// button - confirm before calling, there is no undo.</summary>
        public void ResetActiveLedger()
        {
            ledger.records.Clear();

            // The Knowledge line on each module header reads from here. Clearing only the outcomes
            // would leave "Knowledge: 80% - met" showing on a module that says Not Attempted.
            ledger.knowledge.Clear();

            Save();
            OnLedgerChanged?.Invoke();
        }
    }
}