using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The COC4 assessment report, shown in the results scene after the fifth ticket.
    ///
    /// ATTACH THIS TO: the report panel in your COC4-Results scene. That scene needs the
    /// AssessmentManagers prefab in it as well, or there is no ledger to record against and the
    /// result cannot be judged.
    ///
    /// IT BUILDS THE AssessmentResult ITSELF rather than going through AssessmentRecorder, because
    /// the recorder is a scene component whose Begin() resets the score - correct for a module that
    /// happens in one scene, and unusable across six. The result it produces is the ordinary shape
    /// though: one StageOutcome per ticket, saved through ResultsRepository and judged by
    /// CompetencyLedger exactly like COC1, 2 and 3, so the competency screen and the CSV export
    /// need no special case for COC4.
    ///
    /// ONE TICKET IS ONE STAGE. That mapping is what makes a five-scene run legible next to the
    /// others: a learner who resolved three of five reads as three stages of five complete, and an
    /// instructor comparing COC4 against COC2 is comparing the same units.
    /// </summary>
    public class COC4AssessmentReportView : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text headlineText;
        [SerializeField] private TMP_Text bodyText;

        [Tooltip("Optional. The score on its own, for a layout that separates it from the headline. " +
                 "The headline already carries it, so leaving this empty loses nothing.")]
        [SerializeField] private TMP_Text scoreText;

        [Header("Buttons")]
        [Tooltip("Back to the Main Menu.")]
        [SerializeField] private Button doneButton;

        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Header("Assessment")]
        [SerializeField] private string moduleId = "COC4";

        [Tooltip("How many of the drawn tickets must be FULLY resolved - diagnosed, repaired AND " +
                 "tested - for this run to count as completed.\n\n" +
                 "THIS IS THE COC 4 PASS STANDARD, and this is the only place it is written. Set it " +
                 "to the number drawn (5) to demand a perfect run; 4 allows one failure. It is " +
                 "clamped to the number actually drawn, so it can never ask for more tickets than " +
                 "the learner was given.")]
        [SerializeField] private int ticketsRequiredToPass = 4;

        [Header("Mistakes list")]
        [Tooltip("Print the correct procedure under each mistake. The run is over by the time this " +
                 "screen shows, and the Competency screen shows the same lines.")]
        [SerializeField] private bool showCorrections = true;

        [Tooltip("How many mistakes to print before \"...and N more\". 0 = all of them. Keeps a long " +
                 "list from running off the bottom of the panel; the Competency screen always has " +
                 "the full list.")]
        [Min(0)]
        [SerializeField] private int maxMistakesListed = 8;

        // ==================================================================

        private void Start()
        {
            if (COC4AssessmentRun.Count == 0)
            {
                // Reached with nothing to report - usually the results scene opened directly from
                // the Editor. Said plainly rather than showing an empty report that looks like a
                // learner who scored zero.
                if (headlineText != null) headlineText.text = "No assessment to report";
                if (bodyText != null)
                    bodyText.text = "This scene was opened without a run. Start the COC4 assessment " +
                                    "from the main menu.";

                // Cleared, not left showing the placeholder the builder put there - a score beside
                // "No assessment to report" is the one thing on this screen that could be mistaken
                // for a real result.
                if (scoreText != null) scoreText.text = "";

                if (panelRoot != null) panelRoot.SetActive(true);
                Debug.LogWarning("[COC4AssessmentReportView] No run data. Nothing was recorded.", this);
                return;
            }

            if (COC4AssessmentRun.IsActive)
            {
                // The results scene was opened while tickets are still being worked - not by the
                // run finishing. Recording now would save a half-run as the learner's result.
                if (headlineText != null) headlineText.text = "Assessment still in progress";
                if (bodyText != null) bodyText.text = "Finish the remaining tickets first.";
                if (scoreText != null) scoreText.text = "";
                if (panelRoot != null) panelRoot.SetActive(true);
                Debug.LogWarning("[COC4AssessmentReportView] The run is still active - nothing was recorded.", this);
                return;
            }

            // ONE RECORD PER RUN. A second report component in the scene, or this scene loading a
            // second time, shows the saved result instead of writing a duplicate to the competency
            // screen (a duplicate would also count as another attempt in the ledger).
            AssessmentResult result;
            if (COC4AssessmentRun.ResultRecorded && COC4AssessmentRun.LastResult != null)
            {
                result = COC4AssessmentRun.LastResult;
                Debug.Log("[COC4AssessmentReportView] This run is already recorded - showing it, not saving it again.", this);
            }
            else
            {
                result = BuildAndRecord();
            }

            if (panelRoot != null) panelRoot.SetActive(true);
            if (headlineText != null) headlineText.text = Headline(result);
            if (bodyText != null) bodyText.text = BuildBody(result);
            if (scoreText != null) scoreText.text = $"{result.score} / {result.maxScore}  ({result.ScorePercent}%)";

            if (doneButton != null) doneButton.onClick.AddListener(Done);

            // Ends here, not when the report is dismissed. The run is over the moment it is
            // recorded, and leaving the flag up would make a Story Mode launch afterwards think it
            // was still being graded.
            AssessmentMode.End();
        }

        // ==================================================================

        private AssessmentResult BuildAndRecord()
        {
            var profile = ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfile : null;

            var result = new AssessmentResult
            {
                resultId = Guid.NewGuid().ToString("N"),
                // Read exactly the way AssessmentRecorder reads them, so a COC4 result and a COC2
                // result carry the learner's name in the same shape. displayName, not learnerName -
                // I guessed the field name here once and it cost a compile.
                profileId = profile != null ? profile.profileId : "default",
                learnerName = profile != null ? profile.displayName : "",
                learnerSection = profile != null ? profile.section : "",
                moduleId = moduleId,
                mode = (int)SessionMode.Assessment,
                startedUtc = DateTime.UtcNow.AddSeconds(-COC4AssessmentRun.ElapsedSeconds).ToString("o"),
                finishedUtc = DateTime.UtcNow.ToString("o"),
                durationSeconds = COC4AssessmentRun.ElapsedSeconds,
            };

            // THE MISTAKES, ONE ENTRY PER -10.
            //
            // Copied from the run's log, so the competency screen's "Mistakes: N", the list under
            // it and the points deducted always agree. It used to hold only notes about unresolved
            // tickets (which cost no penalty) while the count came from elsewhere, so the screen
            // could say "Mistakes: 3" above one unrelated line. Ticket outcomes now go on the
            // ticket's own stage line instead.
            foreach (MistakeEntry logged in COC4AssessmentRun.Mistakes)
            {
                if (logged == null) continue;
                result.mistakes.Add(new MistakeEntry
                {
                    atSeconds = logged.atSeconds,
                    category = logged.category,
                    description = logged.description,
                    correction = logged.correction,
                    context = logged.context
                });
            }
            result.mistakeCount = result.mistakes.Count;

            int score = 0;
            int maxScore = 0;
            int gradedAttempts = 0;
            int firstAttemptCorrect = 0;
            float atSeconds = 0f;

            foreach (var attempt in COC4AssessmentRun.Attempts)
            {
                if (attempt == null || attempt.ticket == null) continue;

                atSeconds += attempt.seconds;
                maxScore += COC4AssessmentRun.PointsPerTicket;

                // One scoring rule, owned by COC4AssessmentRun, so the HUD's running total and this
                // report can never disagree about what a ticket was worth.
                int points = COC4AssessmentRun.PointsFor(attempt);

                score += points;
                gradedAttempts++;
                if (attempt.Passed && attempt.mistakes == 0) firstAttemptCorrect++;

                result.stages.Add(new StageOutcome
                {
                    // The ticket id, so a stage in the report can be traced straight back to the
                    // TicketData asset that set it.
                    stageId = attempt.ticket.ticketId,

                    // The outcome rides on the ticket's own line ("... (gave up)") instead of as a
                    // mistake entry. Not resolving a ticket already costs its points; listing it as
                    // a mistake too made the list longer than the penalty count.
                    displayName = attempt.Passed
                        ? attempt.ticket.title
                        : $"{attempt.ticket.title} ({Verdict(attempt)})",
                    completed = attempt.Passed,
                    points = points,
                    completedAtSeconds = attempt.Passed ? atSeconds : -1f,
                    attempts = 1,
                    firstAttemptCorrect = attempt.Passed && attempt.mistakes == 0
                });
            }

            int required = TicketsRequiredToPass();
            AddCompetencyStages(result, required);

            // Minus MistakePenalty (10) for every mistake in the list above - the same rule as the
            // HUD's running score, applied once, here. Never below zero.
            score = Mathf.Max(0, score - result.mistakeCount * COC4AssessmentRun.MistakePenalty);

            result.score = score;
            result.maxScore = Mathf.Max(1, maxScore);
            result.gradedAttempts = gradedAttempts;
            result.firstAttemptCorrect = firstAttemptCorrect;

            // Completed means the PASS STANDARD was met, not that every ticket was perfect.
            //
            // One number decides this and the three outcome stages below it. Before, completion
            // demanded 5 of 5 while the ledger's score threshold asked for 75% - two rules of
            // different strictness, so a learner at 80% was failed by the one the report never
            // showed them, and only the Console said which.
            result.completed = COC4AssessmentRun.PassedCount >= required;

            // Ledger first: RecordResult fills in result.criteria, and the stored JSON should carry
            // that judgement rather than an empty list. Same order AssessmentRecorder uses.
            if (CompetencyLedger.Instance != null) CompetencyLedger.Instance.RecordResult(result);
            else Debug.LogWarning("[COC4AssessmentReportView] No CompetencyLedger in this scene, so " +
                                  "nothing was judged. Add the AssessmentManagers prefab to " +
                                  "COC4-Results.", this);

            ResultsRepository.Save(result);
            COC4AssessmentRun.MarkRecorded(result);

            if (ScoreManager.Instance != null) ScoreManager.Instance.AddScore(score, "module:" + moduleId);

            Debug.Log($"[COC4AssessmentReportView] Recorded: {score}/{result.maxScore} " +
                      $"({result.ScorePercent}%), {COC4AssessmentRun.PassedCount} of " +
                      $"{COC4AssessmentRun.Count} tickets, {result.mistakeCount} mistake(s).");

            return result;
        }

        /// <summary>
        /// Adds the stages a CompetencyDefinition can actually name.
        ///
        /// THIS IS THE DIFFERENCE BETWEEN A RUN THAT IS RECORDED AND ONE THAT IS JUDGED.
        /// CompetencyLedger evidences an outcome by matching evidenceTaskIds against the stage ids
        /// this run completed. The per-ticket stages above use the TICKET id - right for the report,
        /// where a line has to be traceable to the asset that set it, and useless to the ledger,
        /// because those ids are drawn at random from the pool and change every run. No definition
        /// can list an id it cannot predict, so every COC4 outcome sat unevidenced and every learner
        /// stayed at Practised no matter how well they did.
        ///
        /// So the run also closes stages with FIXED names. Put these in evidenceTaskIds:
        ///
        ///   coc4_ticket_1 .. coc4_ticket_5   one per position in the run, completed when that
        ///                                    ticket was resolved. Pair with Required Evidence
        ///                                    Count to say "any 4 of 5" without naming which.
        ///   coc4_identify                    at least Tickets Required To Pass were diagnosed
        ///   coc4_repair                      at least that many were repaired
        ///   coc4_verify                      at least that many were diagnosed AND repaired
        ///   coc4_all_resolved                every ticket DRAWN was resolved
        ///
        /// They carry no points. Scoring is the per-ticket stages' job, and counting a summary of
        /// the run as if it were more work would inflate every learner's percentage.
        /// </summary>
        private static void AddCompetencyStages(AssessmentResult result, int required)
        {
            var attempts = COC4AssessmentRun.Attempts;

            int started = 0, diagnosed = 0, repaired = 0, passed = 0;

            for (int i = 0; i < attempts.Count; i++)
            {
                var attempt = attempts[i];
                if (attempt == null) continue;

                AddStage(result, $"coc4_ticket_{i + 1}",
                         $"Ticket {i + 1} resolved", attempt.Passed);

                if (!attempt.started) continue;

                started++;
                if (attempt.diagnosed && !attempt.gaveUp) diagnosed++;
                if (attempt.repaired && !attempt.gaveUp) repaired++;
                if (attempt.Passed) passed++;
            }

            // AT LEAST `required`, NOT "every ticket attempted".
            //
            // The old rule was == started, which meant ONE give-up closed none of these three: a
            // learner who diagnosed, repaired and tested four machines out of five evidenced no
            // outcome whatsoever. Counting what they did demonstrate is the entire point of
            // sampling five tickets rather than marking one.
            bool anyStarted = started > 0;

            AddStage(result, "coc4_identify", $"Identified the fault on at least {required} ticket(s)",
                     anyStarted && diagnosed >= required);

            AddStage(result, "coc4_repair", $"Repaired at least {required} ticket(s)",
                     anyStarted && repaired >= required);

            AddStage(result, "coc4_verify", $"Diagnosed, repaired and tested at least {required} ticket(s)",
                     anyStarted && passed >= required);

            AddStage(result, "coc4_all_resolved", "Resolved all five tickets",
                     attempts.Count > 0 && passed == attempts.Count);

            Debug.Log("[COC4AssessmentReportView] Competency stages closed this run: " +
                      DescribeClosed(result) + "\n\n" +
                      "Put these ids in the COC4 CompetencyDefinition's Evidence Task Ids. Anything " +
                      "an outcome asks for that is NOT in that list can never be satisfied.");
        }

        /// <summary>
        /// The pass standard, clamped to what was actually drawn.
        ///
        /// Clamped because Tickets To Draw lives on COC4AssessmentLauncher and this number lives
        /// here: drop the draw to 3 for a quick test and an unclamped 5 would make every run
        /// unpassable, with nothing on screen to say why.
        /// </summary>
        private int TicketsRequiredToPass()
        {
            int drawn = Mathf.Max(1, COC4AssessmentRun.Count);
            return Mathf.Clamp(ticketsRequiredToPass, 1, drawn);
        }

        private static void AddStage(AssessmentResult result, string id, string name, bool completed)
        {
            result.stages.Add(new StageOutcome
            {
                stageId = id,
                displayName = name,
                completed = completed,
                points = 0,                 // scoring belongs to the per-ticket stages alone
                completedAtSeconds = completed ? COC4AssessmentRun.ElapsedSeconds : -1f,
                attempts = 1,
                firstAttemptCorrect = completed
            });
        }

        private static string DescribeClosed(AssessmentResult result)
        {
            var sb = new StringBuilder();
            foreach (var stage in result.stages)
            {
                if (stage == null || !stage.stageId.StartsWith("coc4_")) continue;
                sb.Append("\n    ").Append(stage.completed ? "[x] " : "[ ] ").Append(stage.stageId);
            }
            return sb.ToString();
        }

        // ==================================================================

        private static string Headline(AssessmentResult result)
        {
            return (COC4AssessmentRun.TimedOut ? "Time up.  " : "") +
                   $"{COC4AssessmentRun.PassedCount} of {COC4AssessmentRun.Count} tickets resolved" +
                   $"   -   {result.score} / {result.maxScore} ({result.ScorePercent}%)";
        }

        private string BuildBody(AssessmentResult result)
        {
            var sb = new StringBuilder();

            int penalty = result.mistakeCount * COC4AssessmentRun.MistakePenalty;

            sb.AppendLine($"Time: {AssessmentResult.FormatTime(result.durationSeconds)}" +
                          (COC4AssessmentRun.TimedOut ? "   -   TIME LIMIT REACHED" : ""));
            sb.AppendLine($"Mistakes: {result.mistakeCount}" +
                          (penalty > 0 ? $"   (-{penalty} points)" : ""));
            sb.AppendLine();
            sb.AppendLine("TICKETS");

            int n = 1;
            foreach (var attempt in COC4AssessmentRun.Attempts)
            {
                if (attempt == null || attempt.ticket == null) continue;

                sb.AppendLine($"  {n}. {attempt.ticket.title}");
                sb.AppendLine($"       {Verdict(attempt)}   -   {AssessmentResult.FormatTime(attempt.seconds)}" +
                              (attempt.mistakes > 0 ? $", {attempt.mistakes} mistake(s)" : ""));
                n++;
            }

            if (result.mistakes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"MISTAKES   (-{COC4AssessmentRun.MistakePenalty} points each)");

                int shown = 0;
                foreach (MistakeEntry mistake in result.mistakes)
                {
                    if (mistake == null) continue;
                    if (maxMistakesListed > 0 && shown >= maxMistakesListed)
                    {
                        sb.AppendLine($"  ...and {result.mistakes.Count - shown} more - see the Competency screen.");
                        break;
                    }

                    sb.AppendLine($"  - {mistake.description}" +
                                  (string.IsNullOrEmpty(mistake.context) ? "" : $"   ({mistake.context})"));

                    if (showCorrections && !string.IsNullOrEmpty(mistake.correction))
                        sb.AppendLine($"       Correct procedure: {mistake.correction}");

                    shown++;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// What happened on one ticket, in words. Says WHICH half was missing, not just pass or
        /// fail: "repaired, but the cause was never recorded" tells a learner what to do
        /// differently; "failed" tells them nothing they can act on.
        /// </summary>
        private static string Verdict(COC4AssessmentRun.TicketAttempt attempt)
        {
            if (!attempt.started) return "not attempted - the time ran out first";
            if (attempt.gaveUp) return "gave up";
            if (attempt.Passed) return "resolved";
            if (attempt.repaired) return "repaired, but the cause was never recorded";
            if (attempt.diagnosed) return "cause identified, but not repaired";
            return "not resolved";
        }

        /// <summary>Wired to the Done button.</summary>
        public void Done()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}