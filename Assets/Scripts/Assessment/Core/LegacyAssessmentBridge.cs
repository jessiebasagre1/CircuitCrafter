using System;
using UnityEngine;
using CSSSimulation.Networking;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Makes COC1 and COC2 produce persistent results WITHOUT editing
    /// COC1AssessmentManager or COC2AssessmentManager.
    ///
    /// ATTACH THIS TO: the Managers object in the COC1 scene, and again in the
    /// COC2 scene. Assign the one manager that scene has and leave the other
    /// field empty.
    ///
    /// HOW IT WORKS
    /// Both existing managers already expose everything a result needs as public
    /// state - Score, Mistakes, ElapsedSeconds, IsFinished, and a public stages
    /// list whose entries carry displayName, points and a completed flag. Neither
    /// has a "finished" event, so this component watches IsFinished flip from
    /// false to true, exactly the way COC3CompletionNotifier uses OnEnable as its
    /// signal instead of an event that doesn't exist.
    ///
    /// The result is that two finished, working, wired-up scenes start writing
    /// records with no risk of breaking them. When you later refactor those two
    /// managers onto AssessmentManagerBase, delete this component - the base class
    /// records directly and this would double up.
    ///
    /// DO NOT also put an AssessmentRecorder in these two scenes. Both would
    /// listen to the same TaskManager and write two results per run.
    ///
    /// WHAT IT CANNOT RECOVER
    /// The legacy managers keep their mistake log as a private list of strings and
    /// track no per-attempt data, so a bridged result carries the mistake COUNT
    /// but not the individual entries, and reports first-attempt accuracy as "not
    /// available" rather than inventing a number. That is the honest reading:
    /// those runs genuinely didn't record it. New modules built on
    /// AssessmentRecorder get the full picture.
    /// </summary>
    public class LegacyAssessmentBridge : MonoBehaviour
    {
        [Header("Assign the ONE manager this scene has")]
        [Tooltip("The COC1AssessmentManager in this scene. Leave empty in the COC2 scene.")]
        [SerializeField] private COC1AssessmentManager coc1Manager;

        [Tooltip("The COC2AssessmentManager in this scene. Leave empty in the COC1 scene.")]
        [SerializeField] private COC2AssessmentManager coc2Manager;

        [Header("Options")]
        [Tooltip("Also record campaign (Guided) runs. The legacy managers switch themselves off outside assessment mode, so in practice only assessment runs ever reach this bridge - this flag only matters if you later let them run in campaign too.")]
        [SerializeField] private bool recordGuidedRuns = true;

        [Tooltip("Add the manager's final score to the global ScoreManager as well. Leave OFF unless you know the legacy manager isn't already doing it - COC1 and COC2 currently keep their assessment score to themselves, so ON is usually correct.")]
        [SerializeField] private bool contributeToGlobalScore = false;

        /// <summary>Fires once, after the bridged result has been saved.</summary>
        public event Action<AssessmentResult> OnRunFinished;

        private bool alreadyRecorded;
        private bool lastFinished;

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (coc1Manager == null && coc2Manager == null)
            {
                // Find them rather than demanding the reference - one fewer thing to
                // forget when adding this to a scene that's otherwise fully wired.
                coc1Manager = FindAnyObjectByType<COC1AssessmentManager>();
                coc2Manager = FindAnyObjectByType<COC2AssessmentManager>();
            }

            if (coc1Manager != null && coc2Manager != null)
            {
                Debug.LogWarning("[LegacyAssessmentBridge] Both a COC1 and a COC2 assessment manager were found in this scene. Only the COC1 one will be bridged - assign the correct manager explicitly.", this);
                coc2Manager = null;
            }

            if (coc1Manager == null && coc2Manager == null)
            {
                Debug.LogWarning("[LegacyAssessmentBridge] No legacy assessment manager found in this scene - this component will do nothing.", this);
                return;
            }

            // Says so at startup, on purpose. This component's only other output
            // happens at the END of a run, so without this line a bridge that was
            // never wired, was destroyed alongside a duplicate manager object, or
            // sat in a scene with no assessment manager is indistinguishable from a
            // run that simply hasn't finished yet. One log line at Awake turns
            // "nothing happened" into an answerable question.
            Debug.Log($"[LegacyAssessmentBridge] Watching {(coc1Manager != null ? "COC1AssessmentManager" : "COC2AssessmentManager")} " +
                      $"- a result will be recorded when its run finishes.", this);
        }

        private void Update()
        {
            if (alreadyRecorded) return;

            bool finished = coc1Manager != null ? coc1Manager.IsFinished
                          : coc2Manager != null && coc2Manager.IsFinished;

            // Edge, not level: the managers leave IsFinished true for the rest of
            // the scene's life, so acting on the level would re-record every frame.
            if (finished && !lastFinished) Record();

            lastFinished = finished;
        }

        // ------------------------------------------------------------------

        private void Record()
        {
            if (alreadyRecorded) return;
            if (!recordGuidedRuns && TrainingMode.Current == SessionMode.Guided) return;

            AssessmentResult result = coc1Manager != null ? BuildFromCoc1() : BuildFromCoc2();
            if (result == null) return;

            alreadyRecorded = true;

            if (CompetencyLedger.Instance != null) CompetencyLedger.Instance.RecordResult(result);
            ResultsRepository.Save(result);

            if (contributeToGlobalScore && ScoreManager.Instance != null)
                ScoreManager.Instance.AddScore(result.score, $"module:{result.moduleId}");

            Debug.Log($"[LegacyAssessmentBridge] Recorded a {result.moduleId} run: " +
                      $"score {result.score}/{result.maxScore}, mistakes {result.mistakeCount}, " +
                      $"time {AssessmentResult.FormatTime(result.durationSeconds)}.");

            OnRunFinished?.Invoke(result);
        }

        private AssessmentResult BuildFromCoc1()
        {
            var result = NewResult("COC1", coc1Manager.Score, coc1Manager.Mistakes, coc1Manager.ElapsedSeconds);

            int maxScore = 0;
            bool allDone = coc1Manager.stages.Count > 0;

            foreach (var stage in coc1Manager.stages)
            {
                if (stage == null) continue;

                maxScore += stage.points;
                if (!stage.completed) allDone = false;

                result.stages.Add(new StageOutcome
                {
                    stageId = stage.taskId,
                    displayName = stage.displayName,
                    completed = stage.completed,
                    points = stage.completed ? stage.points : 0,
                    completedAtSeconds = -1f,   // the legacy manager doesn't keep per-stage timings
                    attempts = 0,
                    firstAttemptCorrect = false
                });
            }

            // The perfect-run bonus is part of what was AVAILABLE, so it belongs in
            // maxScore whether or not this learner earned it. Leaving it out is how
            // a flawless run reports "410 of 360" - a score above its own maximum,
            // and a ScorePercent that only looks sane because it gets clamped.
            result.maxScore = maxScore + Mathf.Max(0, coc1Manager.perfectRunBonus);
            result.completed = allDone;
            return result;
        }

        private AssessmentResult BuildFromCoc2()
        {
            var result = NewResult("COC2", coc2Manager.Score, coc2Manager.Mistakes, coc2Manager.ElapsedSeconds);

            int maxScore = 0;
            bool allDone = coc2Manager.stages.Count > 0;

            foreach (var stage in coc2Manager.stages)
            {
                if (stage == null) continue;

                maxScore += stage.points;
                if (!stage.completed) allDone = false;

                result.stages.Add(new StageOutcome
                {
                    stageId = !string.IsNullOrEmpty(stage.taskId) ? stage.taskId : stage.manualId,
                    displayName = stage.displayName,
                    completed = stage.completed,
                    points = stage.completed ? stage.points : 0,
                    completedAtSeconds = -1f,
                    attempts = 0,
                    firstAttemptCorrect = false
                });
            }

            // COC2 is the one legacy manager that records a genuinely diagnostic
            // number - how many cables were ruined - and it would be a shame to
            // throw it away, so it goes in as a categorised mistake entry even
            // though the individual failures aren't recoverable.
            if (coc2Manager.FailedCables > 0)
            {
                result.mistakes.Add(new MistakeEntry
                {
                    atSeconds = coc2Manager.ElapsedSeconds,
                    category = "cable",
                    description = $"{coc2Manager.FailedCables} cable(s) failed the tester during this run."
                });
            }

            // Same reasoning as BuildFromCoc1 - the bonus was on offer, so it counts
            // toward the maximum.
            result.maxScore = maxScore + Mathf.Max(0, coc2Manager.perfectRunBonus);
            result.completed = allDone;
            return result;
        }

        private AssessmentResult NewResult(string moduleId, int score, int mistakes, float elapsed)
        {
            var profile = ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfile : null;

            return new AssessmentResult
            {
                resultId = Guid.NewGuid().ToString("N").Substring(0, 12),
                profileId = profile != null ? profile.profileId : "default",
                learnerName = profile != null ? profile.displayName : "",
                learnerSection = profile != null ? profile.section : "",
                moduleId = moduleId,
                mode = (int)TrainingMode.Current,

                // The legacy managers don't record a start time, so it's derived
                // from the elapsed seconds they do expose. Accurate to the frame,
                // which is far more precision than a report needs.
                startedUtc = DateTime.UtcNow.AddSeconds(-elapsed).ToString("o"),
                finishedUtc = DateTime.UtcNow.ToString("o"),
                durationSeconds = elapsed,

                score = score,
                mistakeCount = mistakes,

                // Left at zero on purpose: these managers track no per-attempt data,
                // so AssessmentResult.FirstAttemptPercent returns -1 and a report can
                // print "n/a" rather than a fabricated 100%.
                gradedAttempts = 0,
                firstAttemptCorrect = 0
            };
        }
    }
}