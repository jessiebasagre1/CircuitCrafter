using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The COC4 assessment: five randomly drawn tickets, one repair-site scene at a time, no hub,
    /// and one report at the end.
    ///
    /// A STATIC, because it has to outlive six scene loads. AssessmentRecorder is a MonoBehaviour
    /// and its Begin() resets the score - which is right for a module that happens in one scene and
    /// completely wrong here, where the learner crosses five. A per-scene recorder would hand back
    /// the fifth ticket's score as the whole assessment's. So the run lives where AssessmentMode
    /// and RepairTicketMode already live: a plain static, which survives LoadScene and nothing else.
    ///
    /// WHY THE TICKETS ARE DRAWN ONCE, AT THE START, AND KEPT
    /// Drawing the next ticket as each one finishes would be simpler and is wrong twice over: a
    /// learner could see the same fault twice in one assessment, and an assessor could never say
    /// what was actually set. Five ids chosen up front are a paper you can reprint - the same run
    /// can be described, argued with, and marked.
    ///
    /// NOTHING HERE IS WRITTEN TO DISK until the run finishes. An assessment abandoned by closing
    /// the game is not a result, and half of one on disk is worse than none.
    /// </summary>
    public static class COC4AssessmentRun
    {
        /// <summary>What happened on one ticket.</summary>
        public class TicketAttempt
        {
            public TicketData ticket;
            public bool diagnosed;
            public bool repaired;
            public bool gaveUp;
            public float seconds;
            public int mistakes;

            /// <summary>False for a ticket the clock ran out before the learner ever saw. Kept
            /// apart from a failed one deliberately: "not attempted" and "attempted and not
            /// resolved" are different facts about a learner, and an assessment that reports them
            /// as the same thing is lying about four of them to be tidy about one.</summary>
            public bool started;

            /// <summary>Competent on this ticket: named the cause AND fixed it. Either alone is
            /// half a job - a machine fixed by swapping parts until it works is not a diagnosis,
            /// and a correct diagnosis nobody acted on is not a repair.</summary>
            public bool Passed => diagnosed && repaired && !gaveUp;

            /// <summary>"category|description" of every mistake already counted on this ticket,
            /// so the same slip repeated on the same ticket is counted once. See ReportMistake.</summary>
            internal readonly HashSet<string> mistakeKeys = new HashSet<string>();
        }

        // ---- shared mistake wording ------------------------------------
        //
        // RepairWorkGate (reaching for a part) and TicketFaultCoordinator (a repair landing) can both
        // catch the same wrong habit on the same ticket - starting the repair before the diagnosis.
        // They report it with this ONE wording, so the same-ticket rule in ReportMistake counts it
        // once instead of charging the learner twice for one decision.

        public const string RepairBeforeDiagnosisText = "Started the repair before diagnosing the fault.";

        public const string RepairBeforeDiagnosisCorrection =
            "Diagnose first: observe the symptoms, run your checks, and submit the cause (Diagnose) " +
            "before opening the case or touching any part.";

        public const string FailedTestCorrection =
            "Repair every fault you found before testing. Then power on and confirm the machine " +
            "passes every check before closing the job.";

        // ==================================================================

        public static bool IsActive { get; private set; }

        /// <summary>The tickets drawn for this run, in the order they will be given.</summary>
        public static IReadOnlyList<TicketAttempt> Attempts => attempts;

        /// <summary>0-based index of the ticket being worked on now.</summary>
        public static int Index { get; private set; }

        public static int Count => attempts.Count;

        /// <summary>Human-facing "Ticket 2 of 5".</summary>
        public static string Progress => $"Ticket {Mathf.Min(Index + 1, Count)} of {Count}";

        public static TicketAttempt Current =>
            Index >= 0 && Index < attempts.Count ? attempts[Index] : null;

        /// <summary>Total seconds across every ticket so far, including the one in progress.</summary>
        public static float ElapsedSeconds =>
            IsActive ? bankedSeconds + (Time.realtimeSinceStartup - ticketStartedAt) : bankedSeconds;

        public static int TotalMistakes { get; private set; }

        /// <summary>Every mistake counted in this run, oldest first - one entry per -MistakePenalty.
        /// Its Count always equals TotalMistakes, which is what keeps the report's "Mistakes: N",
        /// the list under it and the points taken off in agreement.</summary>
        public static IReadOnlyList<MistakeEntry> Mistakes => mistakeLog;

        /// <summary>Points taken off for mistakes so far (TotalMistakes x MistakePenalty).</summary>
        public static int PenaltyPoints => TotalMistakes * MistakePenalty;

        /// <summary>True when the same mistake on the same ticket counts every time. Off by default.
        /// Set by the launcher.</summary>
        public static bool CountRepeatedMistakes { get; private set; }

        /// <summary>True once the results screen has saved this run. A second report component, or
        /// the results scene loading twice, then shows LastResult instead of saving a duplicate
        /// record to the competency screen.</summary>
        public static bool ResultRecorded { get; private set; }

        /// <summary>The result that was saved for this run, or null.</summary>
        public static AssessmentResult LastResult { get; private set; }

        public static void MarkRecorded(AssessmentResult result)
        {
            ResultRecorded = true;
            LastResult = result;
        }

        public static int PassedCount
        {
            get
            {
                int n = 0;
                foreach (var attempt in attempts) if (attempt.Passed) n++;
                return n;
            }
        }

        // ---- scoring ---------------------------------------------------
        //
        // The point values live HERE, not on the report screen, and that is the fix for a real
        // flaw: with them on the report, the report was the only thing in the project that could
        // work out a score. Anything else wanting the number - a HUD, a debrief, an instructor
        // export - would have had to keep its own copy of the rules, and two copies of a scoring
        // rule are two rules the day someone edits one.

        public static int PointsPerTicket { get; private set; } = 100;
        public static int PointsForDiagnosisOnly { get; private set; } = 40;
        public static int MistakePenalty { get; private set; } = 10;

        /// <summary>Points for one finished ticket. A ticket still in progress scores nothing yet -
        /// its outcome is not settled until the learner leaves the site.</summary>
        public static int PointsFor(TicketAttempt attempt)
        {
            if (attempt == null || !attempt.started) return 0;
            if (attempt.Passed) return PointsPerTicket;
            if (attempt.diagnosed && !attempt.gaveUp) return PointsForDiagnosisOnly;
            return 0;
        }

        /// <summary>Points banked so far, mistakes already deducted. This is a real running total,
        /// not a prediction: only tickets the learner has finished contribute.</summary>
        public static int Score
        {
            get
            {
                int total = 0;
                for (int i = 0; i < attempts.Count; i++)
                {
                    if (i >= Index && IsActive) break;      // the ticket in hand is not settled
                    total += PointsFor(attempts[i]);
                }
                return Mathf.Max(0, total - TotalMistakes * MistakePenalty);
            }
        }

        public static int MaxScore => Mathf.Max(1, Count * PointsPerTicket);

        public static int ScorePercent => Mathf.RoundToInt(100f * Score / MaxScore);

        /// <summary>Seconds allowed for the WHOLE run. 0 means no limit.</summary>
        public static float TimeLimitSeconds { get; private set; }

        /// <summary>Seconds left, or float.MaxValue when there is no limit.</summary>
        public static float TimeRemaining =>
            TimeLimitSeconds <= 0f ? float.MaxValue : Mathf.Max(0f, TimeLimitSeconds - ElapsedSeconds);

        /// <summary>True when the run ended because the clock ran out rather than because the
        /// learner finished.</summary>
        public static bool TimedOut { get; private set; }

        private static readonly List<TicketAttempt> attempts = new List<TicketAttempt>();
        private static readonly List<MistakeEntry> mistakeLog = new List<MistakeEntry>();
        private static float bankedSeconds;
        private static float ticketStartedAt;
        private static string resultsSceneName = "COC4-Results";
        private static Ticker ticker;

        // True from the moment a ticket is closed until the next scene has loaded. While it is up,
        // the ticket just closed is graded and gone: a second "done" call (Give up pressed while
        // AutoReturnToHub is already leaving) must not close the NEXT ticket unseen, and a late
        // mistake from the old scene must not land on a ticket the learner has not reached.
        private static bool sceneChangePending;
        private static float sceneChangeStartedAt;

        /// <summary>Gives up waiting after 10 s, so a scene that never loads (missing from Build
        /// Settings) cannot leave the Give up button and the mistake count dead for the rest of
        /// the run.</summary>
        private static bool WaitingForScene =>
            sceneChangePending && Time.realtimeSinceStartup - sceneChangeStartedAt < 10f;

        // ==================================================================
        // Starting
        // ==================================================================

        /// <summary>
        /// Draws <paramref name="draw"/> tickets from the pool and loads the first one's scene.
        ///
        /// Sampled WITHOUT replacement, by shuffling a copy. Picking a random index five times over
        /// would hand the same ticket out twice often enough to matter - with a pool of eight it
        /// happens about half the time - and an assessment that sets the same fault twice has
        /// measured four things while claiming five.
        /// </summary>
        public static void Begin(IList<TicketData> pool, int draw, string resultsScene, float timeLimitSeconds,
                                 int pointsPerTicket, int pointsForDiagnosisOnly, int mistakePenalty,
                                 bool countRepeatedMistakes = false)
        {
            attempts.Clear();
            mistakeLog.Clear();
            TotalMistakes = 0;
            bankedSeconds = 0f;
            sceneChangePending = false;
            ResultRecorded = false;
            LastResult = null;
            CountRepeatedMistakes = countRepeatedMistakes;

            Index = 0;
            TimedOut = false;
            TimeLimitSeconds = Mathf.Max(0f, timeLimitSeconds);
            PointsPerTicket = Mathf.Max(0, pointsPerTicket);
            PointsForDiagnosisOnly = Mathf.Clamp(pointsForDiagnosisOnly, 0, PointsPerTicket);
            MistakePenalty = Mathf.Max(0, mistakePenalty);
            resultsSceneName = string.IsNullOrWhiteSpace(resultsScene) ? "COC4-Results" : resultsScene.Trim();

            var usable = new List<TicketData>();
            foreach (var ticket in pool)
            {
                if (ticket == null) continue;
                if (string.IsNullOrWhiteSpace(ticket.ticketId)) continue;
                if (string.IsNullOrWhiteSpace(ticket.sceneName)) continue;   // nowhere to send them
                usable.Add(ticket);
            }

            if (usable.Count == 0)
            {
                Debug.LogError("[COC4AssessmentRun] No usable tickets in the pool - every one is " +
                               "either empty, missing a Ticket Id, or missing a Scene Name. The " +
                               "assessment cannot start.");
                return;
            }

            Shuffle(usable);

            int take = Mathf.Clamp(draw, 1, usable.Count);
            if (take < draw)
                Debug.LogWarning($"[COC4AssessmentRun] Asked for {draw} tickets but only {usable.Count} " +
                                 $"are usable, so this run is {take} long. Add more TicketData assets " +
                                 "to the launcher's pool.");

            for (int i = 0; i < take; i++)
                attempts.Add(new TicketAttempt { ticket = usable[i] });

            IsActive = true;
            EnsureTicker();

            string order = "";
            foreach (var attempt in attempts) order += "\n    " + attempt.ticket.ticketId;
            Debug.Log($"[COC4AssessmentRun] Started with {attempts.Count} ticket(s)" +
                      (TimeLimitSeconds > 0f ? $", {TimeLimitSeconds / 60f:F0} minute limit" : ", no time limit") +
                      $":{order}");

            LoadCurrentTicket();
        }

        // ==================================================================
        // During a ticket
        // ==================================================================

        /// <summary>Kept for older callers. Records a mistake with no details.</summary>
        public static void ReportMistake()
        {
            ReportMistake("general", "Mistake recorded.", null);
        }

        /// <summary>
        /// Records one mistake: -MistakePenalty points, logged with its details for the report and
        /// the competency screen. Returns false when it was not counted.
        ///
        /// Normally reached through AssessmentRecorder.Mistake(), which every COC4 site script
        /// already calls - so a site script never has to know whether it is in an assessment.
        ///
        /// THE SAME MISTAKE ON THE SAME TICKET COUNTS ONCE (unless the launcher's Count Repeated
        /// Mistakes is on). Five taps at a locked part, or the same wrong cause clicked twice, is
        /// one wrong decision, not five - and a double-fired event must never cost 20 points. A
        /// DIFFERENT mistake, or the same one on another ticket, always counts.
        ///
        /// Not counted: no run is active, or the ticket has already been closed and the next scene
        /// is still loading (the ticket was graded when it closed).
        /// </summary>
        public static bool ReportMistake(string category, string description, string correction = null)
        {
            if (!IsActive) return false;

            if (WaitingForScene)
            {
                Debug.Log($"[COC4AssessmentRun] Mistake after the ticket was closed - not counted: {description}");
                return false;
            }

            category = string.IsNullOrWhiteSpace(category) ? "general" : category.Trim();
            description = string.IsNullOrWhiteSpace(description) ? "Mistake recorded." : description.Trim();

            TicketAttempt attempt = Current;

            if (attempt != null && !CountRepeatedMistakes &&
                !attempt.mistakeKeys.Add(category + "|" + description))
            {
                Debug.Log($"[COC4AssessmentRun] Same mistake again on {Progress} - counted once already: {description}");
                return false;
            }

            TotalMistakes++;
            if (attempt != null) attempt.mistakes++;

            mistakeLog.Add(new MistakeEntry
            {
                atSeconds = ElapsedSeconds,
                category = category,
                description = description,
                correction = correction ?? "",
                context = attempt != null && attempt.ticket != null
                    ? $"Ticket {Index + 1}: {attempt.ticket.title}"
                    : ""
            });

            Debug.Log($"[COC4AssessmentRun] Mistake #{TotalMistakes} on {Progress} ({category}, " +
                      $"-{MistakePenalty}): {description}");
            return true;
        }

        /// <summary>
        /// The learner finished with this ticket - fixed it, or pressed Give up.
        ///
        /// Reads the outcome from RepairTicketMode rather than taking it as an argument, because
        /// that is the same pair of flags the hub consumes on a normal run. One source of truth for
        /// "was it diagnosed, was it fixed" means the assessment cannot grade a ticket differently
        /// from the way story mode records it.
        /// </summary>
        public static void CompleteCurrentTicket(bool gaveUp)
        {
            if (!IsActive) return;

            // A second "done" for a ticket already closed - Give up pressed while AutoReturnToHub
            // was already on its way out. Acting on it would close the NEXT ticket, unseen, as a
            // failure.
            if (WaitingForScene)
            {
                Debug.Log("[COC4AssessmentRun] Ticket already closed; ignoring a second finish call.");
                return;
            }

            TicketAttempt attempt = Current;
            if (attempt == null) return;

            attempt.seconds = Time.realtimeSinceStartup - ticketStartedAt;
            bankedSeconds += attempt.seconds;

            // Restarted here so Finish() does not add this ticket's time a second time. It used to:
            // the run's duration came out as the real time PLUS the whole last ticket again.
            ticketStartedAt = Time.realtimeSinceStartup;

            attempt.gaveUp = gaveUp;

            RepairTicketMode.TryConsumeResult(out _, out bool diagnosed, out bool fixedIt);
            attempt.diagnosed = diagnosed;
            attempt.repaired = fixedIt;

            Debug.Log($"[COC4AssessmentRun] {Progress} finished: '{attempt.ticket.ticketId}' - " +
                      $"diagnosed={diagnosed}, repaired={fixedIt}, gaveUp={gaveUp}, " +
                      $"{attempt.seconds:F0}s, {attempt.mistakes} mistake(s).");

            Index++;

            if (Index >= attempts.Count) Finish();
            else LoadCurrentTicket();
        }

        // ==================================================================

        private static void LoadCurrentTicket()
        {
            TicketAttempt attempt = Current;
            if (attempt == null || attempt.ticket == null) { Finish(); return; }

            ticketStartedAt = Time.realtimeSinceStartup;
            attempt.started = true;

            // alreadyDiagnosed is FALSE, always. A ticket in an assessment is a fresh machine the
            // learner has never seen, even if they diagnosed the same fault in story mode an hour
            // ago - carrying that over would hand them the answer to a question being asked.
            RepairTicketMode.Begin(attempt.ticket, alreadyDiagnosed: false);

            // Statics survive a scene load, and a lock held by a station the learner was standing
            // in would follow them into the next site and freeze them there.
            PlayerInputLock.ReleaseAll();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            Debug.Log($"[COC4AssessmentRun] {Progress} - loading '{attempt.ticket.sceneName}' for " +
                      $"'{attempt.ticket.ticketId}'.");

            BeginSceneChange();
            SceneManager.LoadScene(attempt.ticket.sceneName);
        }

        /// <summary>Holds off "done" calls and mistakes until the scene being loaded has loaded.
        /// Cleared by the next sceneLoaded, whatever its name - the ticket's Scene Name may be a
        /// path, and matching names would leave a learner stuck if they differed.</summary>
        private static void BeginSceneChange()
        {
            sceneChangePending = true;
            sceneChangeStartedAt = Time.realtimeSinceStartup;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;   // an additive UI scene is not the new site

            SceneManager.sceneLoaded -= OnSceneLoaded;
            sceneChangePending = false;
        }

        private static void Finish()
        {
            bankedSeconds = ElapsedSeconds;   // the callers have already banked; this adds ~0
            IsActive = false;
            sceneChangePending = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DestroyTicker();

            Debug.Log($"[COC4AssessmentRun] Finished{(TimedOut ? " ON TIME LIMIT" : "")} - " +
                      $"{PassedCount} of {attempts.Count} ticket(s) resolved, " +
                      $"{TotalMistakes} mistake(s), {bankedSeconds:F0}s.");

            PlayerInputLock.ReleaseAll();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            SceneManager.LoadScene(resultsSceneName);
        }

        /// <summary>Abandons the run without a result. For a Quit button, not for a stuck learner -
        /// giving up on one ticket is CompleteCurrentTicket(gaveUp: true), which still reaches a
        /// report.</summary>
        public static void Abandon()
        {
            if (!IsActive) return;

            Debug.Log($"[COC4AssessmentRun] Abandoned at {Progress}. No result recorded.");

            IsActive = false;
            DestroyTicker();
            attempts.Clear();
            mistakeLog.Clear();
            Index = 0;
            TotalMistakes = 0;
            bankedSeconds = 0f;
            TimedOut = false;
            sceneChangePending = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ResultRecorded = false;
            LastResult = null;

            RepairTicketMode.TryConsumeResult(out _, out _, out _);
        }

        // ==================================================================
        // The clock
        // ==================================================================

        /// <summary>
        /// Called every frame while a run is live. Ends it the moment the limit is reached.
        ///
        /// WHY THE TICKER AND NOT THE HUD
        /// The obvious home for this is COC4AssessmentHud's Update - it already runs in every site
        /// and already reads the clock. But a limit that only applies where a canvas happens to be
        /// present is not a limit: one repair-site scene missing that object, and a learner who
        /// draws that ticket gets unlimited time, silently, in the middle of a graded run. Nobody
        /// would notice until an assessor did. A hidden DontDestroyOnLoad object created by the run
        /// itself is present for exactly as long as the run is, in every scene, whatever the scene
        /// contains.
        /// </summary>
        private static void Tick()
        {
            if (!IsActive) return;
            if (TimeLimitSeconds <= 0f) return;
            if (ElapsedSeconds < TimeLimitSeconds) return;

            TimedOut = true;

            // Banked once, here, and the ticket clock restarted - so Finish() cannot add the same
            // seconds again (it used to, doubling the last ticket in the recorded duration).
            float ticketSeconds = Time.realtimeSinceStartup - ticketStartedAt;
            bankedSeconds += ticketSeconds;
            ticketStartedAt = Time.realtimeSinceStartup;

            // The ticket in progress is closed with whatever the learner actually achieved, not
            // written off. Someone who diagnosed a fault thirty seconds before the bell has
            // demonstrated that, and the report should say so.
            TicketAttempt attempt = Current;
            if (attempt != null)
            {
                attempt.seconds = ticketSeconds;

                RepairTicketMode.TryConsumeResult(out _, out bool diagnosed, out bool fixedIt);
                attempt.diagnosed = diagnosed;
                attempt.repaired = fixedIt;
            }

            // Everything after it keeps started = false, so the report can say "not attempted"
            // rather than counting them as failures the learner never had a chance at.
            Index = attempts.Count;

            Debug.Log($"[COC4AssessmentRun] Time limit of {TimeLimitSeconds / 60f:F0} minutes reached.");

            Finish();
        }

        private static void EnsureTicker()
        {
            if (ticker != null) return;

            var go = new GameObject("[COC4 Assessment Clock]");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;   // it is plumbing, not scene content
            ticker = go.AddComponent<Ticker>();
        }

        private static void DestroyTicker()
        {
            if (ticker == null) return;
            Object.Destroy(ticker.gameObject);
            ticker = null;
        }

        /// <summary>The only reason this class needs a MonoBehaviour at all: something has to have
        /// an Update.</summary>
        private class Ticker : MonoBehaviour
        {
            private void Update() => Tick();
        }

        /// <summary>Fisher-Yates. Unity's Random is fine here - this is a draw, not a lottery.</summary>
        private static void Shuffle(IList<TicketData> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}