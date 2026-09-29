using System;
using System.Collections.Generic;
using System.Globalization;
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

        // ---- underpinning knowledge (the quiz) -----------------------
        //
        // SEPARATE FROM score AND maxScore, DELIBERATELY. TESDA's Evidence Plan lists
        // "Demonstration with oral questioning" and "Written test" as two requirements that must
        // BOTH be met, not one blended figure. Quiz points used to be added into ScoreManager,
        // which meant good answers could carry a weak performance over the pass threshold and bad
        // ones could sink a good performance. An assessor has to be able to see each on its own.
        [Tooltip("Quiz questions answered during this run. 0 means no quiz was taken.")]
        public int knowledgeQuestions;

        [Tooltip("How many of those were answered correctly.")]
        public int knowledgeCorrect;

        [Tooltip("How many separate quizzes contributed. For a report that wants to say 'across 3 quizzes'.")]
        public int knowledgeQuizzes;

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

        /// <summary>Percentage of quiz questions answered correctly, or -1 when no quiz was taken -
        /// so a report prints "not taken" rather than a misleading 0%.</summary>
        public int KnowledgePercent
        {
            get
            {
                if (knowledgeQuestions <= 0) return -1;
                return Mathf.Clamp(Mathf.RoundToInt(100f * knowledgeCorrect / knowledgeQuestions), 0, 100);
            }
        }

        /// <summary>True when this run has any knowledge evidence at all.</summary>
        public bool KnowledgeAttempted => knowledgeQuestions > 0;

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

        /// <summary>Parses a stored UTC timestamp (written as DateTime.UtcNow.ToString("o"),
        /// everywhere this project stamps a result) into a short LOCAL-time string for display -
        /// a history row or a "Best: 92% (Sep 3)" line. Falls back to printing the raw stored
        /// value rather than nothing, so a malformed timestamp is visible instead of silently
        /// blank.</summary>
        public static string FormatWhen(string utcIso, string pattern = "MMM d, yyyy")
        {
            if (string.IsNullOrEmpty(utcIso)) return "n/a";

            if (DateTime.TryParse(utcIso, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out DateTime dt))
                return dt.ToLocalTime().ToString(pattern, CultureInfo.InvariantCulture);

            return utcIso;
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

        // ---- added for the assessment review (appended, so older result files still load) ----

        [Tooltip("The correct procedure or expected values, withheld during the run and saved with the result. Optional.")]
        public string correction = "";

        [Tooltip("Where it happened, e.g. the COC4 ticket title. Optional.")]
        public string context = "";
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