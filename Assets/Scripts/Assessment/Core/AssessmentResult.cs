using System;
using System.Collections.Generic;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// One complete record of one learner's run through one module. This is the
    /// object that replaces the StringBuilder reports - it holds everything the
    /// existing reports print, plus the things they currently throw away: who
    /// did it, when, which mode, per-stage timings, and first-attempt accuracy.
    ///
    /// SHAPED FOR JsonUtility, DELIBERATELY
    /// Same constraint GameSaveData already works around: no Dictionary, no
    /// nested generics, no properties, no nullables. Flat [Serializable] classes
    /// with public fields and List<T> only. Keeping to that means results can be
    /// written and read back with the same one-line JsonUtility calls used
    /// everywhere else in the project, with no third-party JSON dependency.
    ///
    /// WHY firstAttemptCorrect IS TRACKED SEPARATELY FROM score
    /// A retry loop with a small penalty eventually lets everyone finish, so a
    /// final score mostly measures persistence. Whether the learner got it right
    /// the FIRST time is the number that measures whether they knew it. Both are
    /// recorded; only one is worth graphing.
    /// </summary>
    [Serializable]
    public class AssessmentResult
    {
        // ---- identity ------------------------------------------------
        [Tooltip("Unique per run. Used as the results filename and as the ledger's evidence pointer.")]
        public string resultId = "";

        public string profileId = "";
        public string learnerName = "";
        public string learnerSection = "";

        [Tooltip("\"COC1\" ... \"COC4\".")]
        public string moduleId = "";

        [Tooltip("SessionMode as an int, because JsonUtility writes enums as ints anyway and this makes the file readable without the enum to hand.")]
        public int mode = (int)SessionMode.Guided;

        // ---- timing --------------------------------------------------
        public string startedUtc = "";
        public string finishedUtc = "";
        public float durationSeconds;

        // ---- outcome -------------------------------------------------
        [Tooltip("False when the run was abandoned or the time limit ran out before every stage was done.")]
        public bool completed;

        public int score;
        public int maxScore;

        public int mistakeCount;

        [Tooltip("Graded attempts where the learner was right first time.")]
        public int firstAttemptCorrect;

        [Tooltip("Total graded attempts across the run. firstAttemptCorrect / gradedAttempts is the accuracy figure worth reporting.")]
        public int gradedAttempts;

        public List<StageOutcome> stages = new List<StageOutcome>();
        public List<MistakeEntry> mistakes = new List<MistakeEntry>();
        public List<CriterionOutcome> criteria = new List<CriterionOutcome>();

        // ---- derived -------------------------------------------------

        /// <summary>Score as a percentage of what was available, clamped to 0-100.
        /// Returns 0 rather than dividing by zero when a module awards no points.</summary>
        public int ScorePercent
        {
            get
            {
                if (maxScore <= 0) return 0;
                return Mathf.Clamp(Mathf.RoundToInt(100f * score / maxScore), 0, 100);
            }
        }

        /// <summary>Percentage of graded attempts answered correctly first time.
        /// -1 when the run recorded no graded attempts at all, so a report can
        /// print "n/a" rather than a misleading 0.</summary>
        public int FirstAttemptPercent
        {
            get
            {
                if (gradedAttempts <= 0) return -1;
                return Mathf.Clamp(Mathf.RoundToInt(100f * firstAttemptCorrect / gradedAttempts), 0, 100);
            }
        }

        public SessionMode Mode => (SessionMode)mode;

        public int StagesCompleted
        {
            get
            {
                int n = 0;
                foreach (var stage in stages) if (stage != null && stage.completed) n++;
                return n;
            }
        }

        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        public string ToJson() => JsonUtility.ToJson(this, prettyPrint: true);

        public static AssessmentResult FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<AssessmentResult>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[AssessmentResult] Couldn't parse a stored result: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>One stage of a run - a station in COC1, a cable in COC2, a step
    /// group in COC3, a ticket in COC4.</summary>
    [Serializable]
    public class StageOutcome
    {
        [Tooltip("The TaskManager task id where there is one, otherwise a manual id.")]
        public string stageId = "";

        public string displayName = "";
        public bool completed;
        public int points;

        [Tooltip("Seconds from the start of the run to the moment this stage completed. -1 if it never did.")]
        public float completedAtSeconds = -1f;

        [Tooltip("How many graded attempts this stage took. 1 means right first time.")]
        public int attempts;

        public bool firstAttemptCorrect;
    }

    /// <summary>One recorded mistake, with enough context to be useful in a
    /// report rather than just a count. Category lets an instructor see PATTERNS -
    /// a learner who keeps making sequence errors needs different help from one
    /// who keeps making identification errors.</summary>
    [Serializable]
    public class MistakeEntry
    {
        public float atSeconds;

        [Tooltip("Short bucket, e.g. \"sequence\", \"identification\", \"safety\", \"configuration\", \"cable\". Keep the set small so it's countable.")]
        public string category = "general";

        public string description = "";
    }

    /// <summary>How one learning outcome came out of this run. Stored ON the
    /// result as well as in the ledger, so a result file is self-contained
    /// evidence that can be read years later without the ledger or the
    /// CompetencyDefinition asset that produced it.</summary>
    [Serializable]
    public class CriterionOutcome
    {
        public string outcomeCode = "";
        public string outcomeTitle = "";

        [Tooltip("CompetencyStatus as an int.")]
        public int status = (int)CompetencyStatus.NotAttempted;

        public int evidenceSatisfied;
        public int evidenceRequired;

        public CompetencyStatus Status => (CompetencyStatus)status;
    }
}
