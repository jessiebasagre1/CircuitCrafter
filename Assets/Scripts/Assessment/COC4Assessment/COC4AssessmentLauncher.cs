using System.Collections.Generic;
using UnityEngine;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Starts the COC4 assessment. Wire a menu button's OnClick to StartAssessment().
    ///
    /// ATTACH THIS TO: an object on the Main Menu canvas, beside whatever launches the other COCs'
    /// assessments. It never enters a scene itself - COC4AssessmentRun loads the first ticket's
    /// site directly, which is what "no hub in the assessment" means in practice.
    ///
    /// THE POOL SHOULD BE BIGGER THAN THE DRAW. Five drawn from exactly five is not a random
    /// assessment, it is a fixed one in a shuffled order - every learner meets every fault and they
    /// compare notes at break. Eight to twelve tickets for a draw of five means two learners sitting
    /// side by side get genuinely different papers.
    /// </summary>
    public class COC4AssessmentLauncher : MonoBehaviour
    {
        [Header("Tickets")]
        [Tooltip("Every ticket this assessment may draw from. Put MORE here than Tickets To Draw - " +
                 "see the class note. A ticket with no Ticket Id or no Scene Name is skipped and " +
                 "warned about rather than crashing the run.")]
        [SerializeField] private List<TicketData> pool = new List<TicketData>();

        [Tooltip("How many to set. Five is the brief.")]
        [Min(1)]
        [SerializeField] private int ticketsToDraw = 5;

        [Header("Scenes")]
        [Tooltip("Exact scene name of the results screen shown after the last ticket. It must be in " +
                 "Build Settings, and it is the ONLY scene in this flow the learner has not already " +
                 "visited - which makes it the one most likely to be missing from that list.")]
        [SerializeField] private string resultsSceneName = "COC4-Results";

        [Header("Time limit")]
        [Tooltip("Minutes allowed for ALL FIVE tickets together, not each. 0 means no limit.\n\n" +
                 "One limit for the whole run rather than one per ticket is what makes it a fair " +
                 "test of triage: a learner who clears an easy fault in three minutes has bought " +
                 "themselves time for a hard one, which is exactly the judgement a technician makes " +
                 "on a call-out list.")]
        [Min(0f)]
        [SerializeField] private float timeLimitMinutes = 40f;

        [Header("Scoring")]
        [Tooltip("Points for a ticket both diagnosed AND repaired. Neither half alone scores full: " +
                 "a machine fixed by swapping parts until it works is not a diagnosis, and a correct " +
                 "diagnosis nobody acted on is not a repair.")]
        [Min(0)]
        [SerializeField] private int pointsPerTicket = 100;

        [Tooltip("Part credit for naming the cause correctly but not completing the repair. Worth " +
                 "having: identifying the fault is the harder half and the one the unit is really " +
                 "about. Clamped to Points Per Ticket.")]
        [Min(0)]
        [SerializeField] private int pointsForDiagnosisOnly = 40;

        [Min(0)]
        [SerializeField] private int mistakePenalty = 10;

        [Header("Assessment")]
        [Tooltip("Module id recorded against the result, matching the COC4 CompetencyDefinition asset.")]
        [SerializeField] private string moduleId = "COC4";

        // ==================================================================

        /// <summary>Wire your menu button here.</summary>
        public void StartAssessment()
        {
            if (pool == null || pool.Count == 0)
            {
                Debug.LogError("[COC4AssessmentLauncher] The ticket pool is empty - nothing to set. " +
                               "Drag your TicketData assets into Pool.", this);
                return;
            }

            // Set BEFORE the run begins, because the first site scene reads it during its own Awake.
            // Begin() loads that scene immediately, so anything set after this call arrives too late
            // for the scene it was meant to configure - the waypoint arrow stays up, the story
            // dialogs play, and the first ticket is quietly not an assessment at all.
            AssessmentMode.Begin(moduleId);

            COC4AssessmentRun.Begin(pool, ticketsToDraw, resultsSceneName, timeLimitMinutes * 60f,
                                    pointsPerTicket, pointsForDiagnosisOnly, mistakePenalty);
        }

        /// <summary>For a Cancel button while the learner is still on the menu.</summary>
        public void CancelAssessment()
        {
            COC4AssessmentRun.Abandon();
            AssessmentMode.End();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (pool != null && ticketsToDraw > pool.Count && pool.Count > 0)
                Debug.LogWarning($"[COC4AssessmentLauncher] Tickets To Draw is {ticketsToDraw} but the " +
                                 $"pool holds {pool.Count}. The run will be {pool.Count} long, and with " +
                                 "the draw equal to the pool every learner gets the same tickets in a " +
                                 "different order.", this);
        }
#endif
    }
}