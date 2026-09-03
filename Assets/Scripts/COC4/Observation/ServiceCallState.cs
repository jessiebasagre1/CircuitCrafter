using System.Collections.Generic;
using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// What the learner has done on this service call so far: what they looked
    /// at, how long it took, and how many times they guessed.
    ///
    /// A plain static, for the same reason RepairTicketMode is one - a service
    /// call is a property of this trip out to the machine, not of saved
    /// progress. It must survive the hub-to-site scene load and die with the
    /// application.
    ///
    /// IT RESETS ITSELF WHEN THE TICKET CHANGES. Nothing has to remember to
    /// clear it: every entry point calls EnsureTicket first, which wipes
    /// everything the moment it sees a different ticket id. That avoids the
    /// whole class of bug where a second ticket inherits the first one's
    /// observations and looks already half-solved.
    /// </summary>
    public static class ServiceCallState
    {
        private static string currentTicketId;
        private static readonly HashSet<string> observed = new HashSet<string>();

        /// <summary>Seconds added by observations and wrong guesses. Not a clock -
        /// this is the cost the learner chose to spend, which is the number worth
        /// reporting.</summary>
        public static int TimeSpentSeconds { get; private set; }

        public static int ObservationCount => observed.Count;

        /// <summary>How many diagnoses have been submitted, right or wrong.</summary>
        public static int DiagnosisAttempts { get; private set; }

        /// <summary>How many observations had been made when the FIRST diagnosis was
        /// submitted. Zero means they guessed without looking, which is the single
        /// clearest signal of method in the whole module.</summary>
        public static int ObservationsBeforeFirstDiagnosis { get; private set; } = -1;

        /// <summary>True once the learner has run the power-on test at least once.
        /// Closing a ticket without this is a callback waiting to happen, and
        /// callbacks are what the industry actually measures.</summary>
        public static bool VerificationRun { get; private set; }

        /// <summary>True if the most recent test passed.</summary>
        public static bool VerificationPassed { get; private set; }

        // ------------------------------------------------------------------

        /// <summary>Call at the top of anything that reads or writes this. Wipes
        /// everything when the ticket has changed.</summary>
        public static void EnsureTicket(string ticketId)
        {
            if (currentTicketId == ticketId) return;

            currentTicketId = ticketId;
            observed.Clear();
            TimeSpentSeconds = 0;
            DiagnosisAttempts = 0;
            ObservationsBeforeFirstDiagnosis = -1;
            VerificationRun = false;
            VerificationPassed = false;

            Debug.Log($"[ServiceCallState] Started a fresh service call for '{ticketId}'.");
        }

        public static bool HasObserved(string observationId)
        {
            return !string.IsNullOrEmpty(observationId) && observed.Contains(observationId);
        }

        /// <summary>Records an observation. Returns false if it had already been
        /// made - repeating one costs nothing, because re-reading something you
        /// already saw is not a decision.</summary>
        public static bool RecordObservation(string observationId, int timeCostSeconds)
        {
            if (string.IsNullOrEmpty(observationId)) return false;
            if (!observed.Add(observationId)) return false;

            TimeSpentSeconds += Mathf.Max(0, timeCostSeconds);
            return true;
        }

        public static void RecordDiagnosisAttempt(int timePenaltySeconds)
        {
            if (ObservationsBeforeFirstDiagnosis < 0)
                ObservationsBeforeFirstDiagnosis = observed.Count;

            DiagnosisAttempts++;
            TimeSpentSeconds += Mathf.Max(0, timePenaltySeconds);
        }

        public static void RecordVerification(bool passed, int timeCostSeconds)
        {
            VerificationRun = true;
            VerificationPassed = passed;
            TimeSpentSeconds += Mathf.Max(0, timeCostSeconds);
        }

        public static string FormatTime()
        {
            return $"{TimeSpentSeconds / 60:00}:{TimeSpentSeconds % 60:00}";
        }

        /// <summary>One line for a report or a debrief.</summary>
        public static string Summary()
        {
            string beforeFirst = ObservationsBeforeFirstDiagnosis < 0
                ? "no diagnosis submitted"
                : $"{ObservationsBeforeFirstDiagnosis} before first diagnosis";

            string verify = !VerificationRun ? "not verified"
                          : VerificationPassed ? "verified working" : "verification failed";

            return $"{observed.Count} observation(s) made ({beforeFirst}), " +
                   $"{DiagnosisAttempts} diagnosis attempt(s), {verify}, {FormatTime()} spent.";
        }

        /// <summary>Wipes everything. Call from the hub when a COC4 session ends -
        /// beside TicketSessionState.Clear().</summary>
        public static void Clear()
        {
            currentTicketId = null;
            observed.Clear();
            TimeSpentSeconds = 0;
            DiagnosisAttempts = 0;
            ObservationsBeforeFirstDiagnosis = -1;
            VerificationRun = false;
            VerificationPassed = false;
        }
    }
}