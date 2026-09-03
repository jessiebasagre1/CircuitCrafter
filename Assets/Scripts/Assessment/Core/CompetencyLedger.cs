using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
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

        /// <summary>Fires whenever any record changes. A competency profile screen
        /// should subscribe rather than poll.</summary>
        public event Action OnLedgerChanged;

        [Serializable]
        private class LedgerFile
        {
            public string profileId = "";
            public List<OutcomeRecord> records = new List<OutcomeRecord>();
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
                (TaskManager.Instance != null && TaskManager.Instance.IsTaskComplete(id))
                || HasCompletedStage(result, id);

            bool canAwardCompetent =
                result.Mode == SessionMode.Assessment &&
                result.completed &&
                result.ScorePercent >= passThresholdPercent;

            result.criteria.Clear();

            foreach (var outcome in definition.outcomes)
            {
                if (outcome == null || string.IsNullOrEmpty(outcome.code)) continue;

                bool satisfied = definition.Evaluate(outcome, isComplete, out int have, out int need);

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

            Save();
            OnLedgerChanged?.Invoke();
        }

        /// <summary>True if this run closed a stage with the given id. Lets a
        /// manual stage id stand as evidence for modules that don't route their
        /// progress through TaskManager - see the comment in RecordResult.</summary>
        private static bool HasCompletedStage(AssessmentResult result, string stageId)
        {
            if (result == null || string.IsNullOrEmpty(stageId)) return false;

            foreach (var stage in result.stages)
            {
                if (stage != null && stage.completed && stage.stageId == stageId) return true;
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
            foreach (var definition in definitions)
            {
                if (definition != null && definition.moduleId == moduleId) return definition;
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

        /// <summary>Wipes the active profile's ledger. For an instructor reset
        /// button - confirm before calling, there is no undo.</summary>
        public void ResetActiveLedger()
        {
            ledger.records.Clear();
            Save();
            OnLedgerChanged?.Invoke();
        }
    }
}