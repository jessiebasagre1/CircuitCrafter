// COC4StoryTicketRecorder.cs
using System.Collections.Generic;
using CircuitCrafter.Assessment;
using CircuitCrafter.Repair;   // ServiceCallState
using UnityEngine;

/// <summary>
/// Records COC 4 STORY mode into the competency screen - one record per repair ticket.
///
/// ATTACH THIS TO: the ticket hub scene, on the same object as TicketHubView or beside it.
///
/// WHY ONE RECORD PER TICKET, AND NOT ONE PER PLAYTHROUGH
/// Every other module has an end. COC 4 story mode does not - it is a hub of levels the learner
/// works through over days, leaving whenever they like. A recorder waiting for "the module
/// finished" would wait forever, which is exactly why nothing has been recorded so far.
///
/// A ticket is the right unit anyway. Observe, diagnose, repair, verify - that is one complete
/// service call, and one complete TESDA observation. Recording each one as it closes means:
///   - leaving COC 4 at any point loses nothing,
///   - adding tickets later changes nothing here, in the ledger, or in the definition,
///   - the competency screen fills in as the learner works instead of all at once.
///
/// IT CLOSES THE SAME STAGE IDS THE ASSESSMENT DOES.
/// coc4_identify, coc4_repair and coc4_verify are the ids COC4AssessmentReportView already closes,
/// so ONE CompetencyDefinition serves both modes and never has to name a ticket. The per-ticket
/// stage uses the ticket's own id, for traceability in the stored evidence file; nothing needs to
/// list it. coc4_all_resolved is deliberately NOT closed here - one ticket cannot evidence that,
/// and it is the id that keeps a five-ticket assessment meaningfully different.
///
/// GUIDED, SO ALWAYS PRACTISED. The hub hands the learner the symptom and the diagnosis panel
/// offers two to four choices. That is practice, and CompetencyLedger's first gate is
/// mode == Assessment, so nothing here can award Competent however well it went. That is the
/// intended meaning: exposure, not evidence.
///
/// IT RECORDS EACH TICKET ONCE, EVER. The baseline is read back from the learner's own stored
/// results, not from a flag in this scene - so it is idempotent across restarts, across profile
/// switches, and across a hub the learner walks in and out of twenty times.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-400)]
public class COC4StoryTicketRecorder : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Must match the Module Id on the COC 4 CompetencyDefinition asset, or the run is " +
             "saved and nothing is judged - the Console says so by name.")]
    public string moduleId = "COC4";

    [Tooltip("Left empty, the TicketManager in the scene is used.")]
    public TicketManager manager;

    [Header("Scoring")]
    [Tooltip("A ticket diagnosed AND repaired.")]
    public int pointsForResolved = 100;

    [Tooltip("A ticket where the cause was named but the fix never landed. Kept separate because " +
             "naming the fault is most of the skill and none of the result.")]
    public int pointsForDiagnosisOnly = 40;

    [Tooltip("Deducted per mistake. The score never goes below zero.\n\n" +
             "These three numbers are this component's own rather than read from " +
             "COC4AssessmentRun: those statics are set by the assessment launcher, so sharing them " +
             "would make a story record's score depend on whether an assessment happened to have " +
             "been played first in the same session.")]
    public int mistakePenalty = 10;

    [Header("Backfill")]
    [Tooltip("Also write a record for a ticket already sitting Resolved that has never been " +
             "recorded - work finished before this component existed.\n\n" +
             "Those records carry no observation detail, because that data died with the session " +
             "it happened in. They are marked as backfilled in the evidence file so nobody later " +
             "reads the missing detail as a learner who skipped the method.")]
    public bool backfillAlreadyResolved = true;

    public bool verbose = true;

    // ==================================================================

    /// <summary>What the service call looked like, captured in Awake before TicketHubView's Start
    /// consumes RepairTicketMode.</summary>
    private struct Visit
    {
        public bool valid;
        public string ticketId;
        public bool diagnosed;
        public bool fixedIt;

        public int diagnosisAttempts;
        public int observations;
        public int observationsBeforeFirstDiagnosis;
        public bool verificationRun;
        public bool verificationPassed;
        public int secondsSpent;
    }

    private Visit visit;
    private HashSet<string> alreadyRecorded;
    private bool assessmentRanThisSession;
    private bool swept;

    // ==================================================================

    /// <summary>
    /// AWAKE, AND THIS IS THE WHOLE REASON FOR THE EXECUTION ORDER.
    ///
    /// TicketHubView.Start() calls RepairTicketMode.TryConsumeResult(), which clears every field
    /// this needs - it is documented as the only place that clears them. Awake always runs before
    /// any Start, so reading here gets the visit intact and takes nothing away from the hub: this
    /// only reads, it never consumes.
    /// </summary>
    private void Awake()
    {
        if (manager == null) manager = FindAnyObjectByType<TicketManager>();

        if (!RepairTicketMode.IsActive) return;

        visit = new Visit
        {
            valid = true,
            ticketId = RepairTicketMode.ActiveTicketId,
            diagnosed = RepairTicketMode.IsDiagnosed,
            fixedIt = RepairTicketMode.ActiveTicketFixed,

            diagnosisAttempts = ServiceCallState.DiagnosisAttempts,
            observations = ServiceCallState.ObservationCount,
            observationsBeforeFirstDiagnosis = ServiceCallState.ObservationsBeforeFirstDiagnosis,
            verificationRun = ServiceCallState.VerificationRun,
            verificationPassed = ServiceCallState.VerificationPassed,
            secondsSpent = ServiceCallState.TimeSpentSeconds
        };

        if (verbose)
            Debug.Log($"[COC4StoryTicketRecorder] Came back from '{visit.ticketId}' " +
                      $"(diagnosed {visit.diagnosed}, fixed {visit.fixedIt}). Waiting for the hub " +
                      "to apply it.", this);
    }

    private void Start()
    {
        alreadyRecorded = LoadRecordedTicketIds();

        if (verbose)
            Debug.Log($"[COC4StoryTicketRecorder] {alreadyRecorded.Count} ticket(s) already have a " +
                      "record for this learner.", this);
    }

    /// <summary>
    /// Swept on the first Update, not in Start, because TicketHubView applies the visit result in
    /// ITS Start and script order between two plain MonoBehaviours is undefined. By the first
    /// Update every Start has run, so the ticket's status is settled whichever order they woke in.
    /// </summary>
    private void Update()
    {
        // Latched, exactly as StoryRunRecorder latches it. A live check alone is not enough: the
        // moment an assessment ends AssessmentMode.End() fires, and a component that only checked
        // live would wake up and record five assessment tickets a second time as story work.
        if (AssessmentMode.IsActive)
        {
            assessmentRanThisSession = true;
            return;
        }

        if (assessmentRanThisSession || swept) return;

        swept = true;
        Sweep();
    }

    private void Sweep()
    {
        if (manager == null)
        {
            Debug.LogWarning("[COC4StoryTicketRecorder] No TicketManager found - nothing can be " +
                             "recorded. Put this in the ticket hub scene.", this);
            return;
        }

        foreach (TicketData ticket in manager.AllTickets)
        {
            if (ticket == null || string.IsNullOrEmpty(ticket.ticketId)) continue;
            if (manager.GetStatus(ticket) != TicketStatus.Resolved) continue;
            if (alreadyRecorded.Contains(ticket.ticketId)) continue;

            bool isThisVisit = visit.valid && visit.ticketId == ticket.ticketId;

            if (!isThisVisit && !backfillAlreadyResolved)
            {
                if (verbose)
                    Debug.Log($"[COC4StoryTicketRecorder] '{ticket.ticketId}' is Resolved with no " +
                              "record, but Backfill Already Resolved is off - skipping.", this);
                continue;
            }

            Record(ticket, isThisVisit);
            alreadyRecorded.Add(ticket.ticketId);
        }
    }

    // ==================================================================

    private void Record(TicketData ticket, bool haveVisitDetail)
    {
        var profile = ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfile : null;

        bool diagnosed = haveVisitDetail ? visit.diagnosed : true;
        bool repaired = haveVisitDetail ? visit.fixedIt : true;

        // A Resolved ticket was fixed - TicketManager only sets Resolved on wasFixed. So a backfill
        // asserts what the status already proves and nothing more; the method detail is what is
        // genuinely unknown, and it is left unknown rather than assumed good.

        var result = new AssessmentResult
        {
            resultId = System.Guid.NewGuid().ToString("N").Substring(0, 12),
            profileId = profile != null ? profile.profileId : "default",
            learnerName = profile != null ? profile.displayName : "",
            learnerSection = profile != null ? profile.section : "",
            moduleId = moduleId,

            // GUIDED, ALWAYS. Set here rather than read from TrainingMode so it cannot drift if a
            // menu forgets to call Begin - the same choice StoryRunRecorder makes.
            mode = (int)SessionMode.Guided,

            startedUtc = "",
            finishedUtc = System.DateTime.UtcNow.ToString("o"),
            durationSeconds = haveVisitDetail ? visit.secondsSpent : 0f,

            // The service call WAS completed. This is what lifts every outcome to Practised, since
            // the ledger promotes on "have > 0 || result.completed". It cannot reach Competent -
            // mode is Guided, and that gate is checked first.
            completed = true
        };

        int score = repaired && diagnosed
            ? pointsForResolved
            : (diagnosed ? pointsForDiagnosisOnly : 0);

        AddMistakes(result, ticket, haveVisitDetail);

        score = Mathf.Max(0, score - result.mistakeCount * mistakePenalty);

        result.score = score;
        result.maxScore = Mathf.Max(1, pointsForResolved);

        // ONE graded attempt: this service call. First-attempt correct means they named the cause
        // without a wrong guess and the machine passed its test - the two things an employer would
        // call "did it properly the first time".
        result.gradedAttempts = 1;
        result.firstAttemptCorrect =
            haveVisitDetail && visit.diagnosisAttempts <= 1 && VerificationOk() ? 1 : 0;

        AddStages(result, ticket, diagnosed, repaired, score, haveVisitDetail);

        // Ledger first - RecordResult fills in result.criteria, so the file saved next carries the
        // judgement rather than an empty list. Same order the recorder and the COC 4 report use.
        if (CompetencyLedger.Instance != null)
        {
            CompetencyLedger.Instance.RecordResult(result);
        }
        else
        {
            Debug.LogWarning("[COC4StoryTicketRecorder] No CompetencyLedger in the hub scene, so " +
                             "nothing was judged. Add the AssessmentManagers prefab.", this);
        }

        ResultsRepository.Save(result);

        Debug.Log($"[COC4StoryTicketRecorder] Recorded STORY ticket '{ticket.ticketId}': " +
                  $"{score}/{result.maxScore}, {result.mistakeCount} mistake(s)" +
                  (haveVisitDetail
                      ? $", {visit.observations} observation(s), {visit.diagnosisAttempts} diagnosis " +
                        $"attempt(s), test {(visit.verificationRun ? (visit.verificationPassed ? "passed" : "failed") : "not run")}."
                      : " (backfilled - no observation detail)."), this);
    }

    private bool VerificationOk()
    {
        return !visit.verificationRun || visit.verificationPassed;
    }

    /// <summary>
    /// The mistakes worth counting on a service call, and every one of them is about METHOD rather
    /// than luck. A learner who guesses the cause without looking at the machine and happens to be
    /// right has not diagnosed anything, and an assessment that cannot say so is not measuring the
    /// thing COC 4 teaches.
    /// </summary>
    private void AddMistakes(AssessmentResult result, TicketData ticket, bool haveVisitDetail)
    {
        if (!haveVisitDetail)
        {
            // No entries at all, and mistakeCount left at zero. Inventing a mistake for a ticket
            // whose detail was lost would be worse than recording none: it would make an old,
            // properly-done repair look careless.
            result.mistakeCount = 0;
            return;
        }

        if (visit.diagnosisAttempts > 1)
            Add(result, "identification",
                $"Took {visit.diagnosisAttempts} tries to name the cause of '{ticket.title}'.");

        if (visit.observationsBeforeFirstDiagnosis == 0)
            Add(result, "method",
                $"Submitted a diagnosis for '{ticket.title}' without observing the machine first.");

        if (!visit.verificationRun)
            Add(result, "verification",
                $"Closed '{ticket.title}' without running the power-on test.");
        else if (!visit.verificationPassed)
            Add(result, "verification",
                $"Left '{ticket.title}' with the power-on test still failing.");

        result.mistakeCount = result.mistakes.Count;
    }

    private void Add(AssessmentResult result, string category, string description)
    {
        result.mistakes.Add(new MistakeEntry
        {
            atSeconds = visit.secondsSpent,
            category = category,
            description = description
        });
    }

    private void AddStages(AssessmentResult result, TicketData ticket,
                           bool diagnosed, bool repaired, int score, bool haveVisitDetail)
    {
        // Traceability: a line in the evidence file that leads back to the TicketData asset. The
        // definition never has to name it, which is what lets you add tickets later without
        // touching the ledger.
        AddStage(result, $"coc4_ticket_{ticket.ticketId}",
                 ticket.title, true, score, haveVisitDetail);

        // The three ids COC4AssessmentReportView also closes. This is the whole point: one
        // CompetencyDefinition, both modes.
        AddStage(result, "coc4_identify", "Fault identified", diagnosed, 0, haveVisitDetail);
        AddStage(result, "coc4_repair", "Fault repaired", repaired, 0, haveVisitDetail);
        AddStage(result, "coc4_verify", "Repair verified",
                 diagnosed && repaired && VerificationOk(), 0, haveVisitDetail);
    }

    private void AddStage(AssessmentResult result, string stageId, string displayName,
                          bool completed, int points, bool haveVisitDetail)
    {
        result.stages.Add(new StageOutcome
        {
            stageId = stageId,
            displayName = haveVisitDetail ? displayName : displayName + " (backfilled)",
            completed = completed,
            points = completed ? points : 0,
            completedAtSeconds = completed ? result.durationSeconds : -1f,
            attempts = 1,
            firstAttemptCorrect = completed && result.firstAttemptCorrect > 0
        });
    }

    // ==================================================================

    /// <summary>
    /// Which tickets this learner already has a record for, read back from their own stored
    /// results.
    ///
    /// THE BASELINE IS ON DISK, NOT IN THIS SCENE, and that is what makes the whole thing safe to
    /// run on every hub load. A flag here would be reset by walking out of COC 4 and back in, and
    /// the learner would collect a second record for the same repair every trip - twenty records
    /// for one ticket by the end of a lesson, each one inflating the attempt count on every
    /// outcome.
    /// </summary>
    private HashSet<string> LoadRecordedTicketIds()
    {
        var ids = new HashSet<string>();

        string profileId = ProfileManager.Instance != null
            ? ProfileManager.Instance.ActiveProfileId
            : "default";

        List<AssessmentResult> results = ResultsRepository.LoadAll(profileId);
        if (results == null) return ids;

        const string prefix = "coc4_ticket_";

        foreach (AssessmentResult result in results)
        {
            if (result == null || result.moduleId != moduleId) continue;
            if (result.Mode != SessionMode.Guided) continue;   // assessment records are not levels
            if (result.stages == null) continue;

            foreach (StageOutcome stage in result.stages)
            {
                if (stage == null || string.IsNullOrEmpty(stage.stageId)) continue;
                if (!stage.stageId.StartsWith(prefix)) continue;

                string ticketId = stage.stageId.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(ticketId)) ids.Add(ticketId);
            }
        }

        return ids;
    }
}
