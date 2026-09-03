using System.Collections.Generic;
using UnityEngine;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Owns every fault at a repair site: counts them, watches for repairs,
    /// enforces the diagnose-before-you-fix rule, and closes the ticket.
    ///
    /// ATTACH THIS TO: the Managers object in each repair-site scene
    /// (COC1-Copy and friends). One per scene. The TicketFault components can be
    /// anywhere in the scene - it finds them.
    ///
    /// EVERYTHING THAT WAS DUPLICATED NOW LIVES HERE
    /// The three existing reporters each carry their own copy of: check
    /// RepairTicketMode, check the ticket id, check RequiredTaskId exists,
    /// register a 1-step task, poll, block the fix until diagnosed, warn once,
    /// report progress, MarkFixed. That is the same twenty lines three times,
    /// and a fourth copy for every fault added later. It is written once here.
    ///
    /// THE DIAGNOSIS GATE IS THE PEDAGOGICAL POINT
    /// A learner who reseats every component until something works has not
    /// diagnosed anything - they have shotgunned it, which is the exact habit
    /// this module exists to break. So a physical fix is not accepted until the
    /// probable cause has been correctly identified. The fix still HAPPENS; it
    /// just doesn't close the ticket, and the learner is told why.
    /// </summary>
    public class TicketFaultCoordinator : MonoBehaviour
    {
        [Tooltip("Shown once, the first time the learner fixes something before diagnosing it. Optional - there is a Console message either way.")]
        [SerializeField] private DialogSequence diagnoseFirstWarning;

        [Tooltip("Shown when the last outstanding fault is repaired and the ticket can be closed. Optional.")]
        [SerializeField] private DialogSequence allFaultsFixedDialog;

        [Tooltip("An AssessmentRecorder stage id for this ticket, e.g. \"ticket_coc1_ram_missing\". Leave blank to record nothing.")]
        [SerializeField] private string stageId = "";

        private readonly List<TicketFault> activeFaults = new List<TicketFault>();
        private int repairedCount;
        private bool warnedAboutDiagnosis;
        private bool ticketClosed;

        public int ActiveFaultCount => activeFaults.Count;
        public int RepairedFaultCount => repairedCount;

        /// <summary>Every fault at this site. The verification panel walks these to
        /// build its test report - which is why the test needs no knowledge of what
        /// kind of fault each one is.</summary>
        public IReadOnlyList<TicketFault> ActiveFaults => activeFaults;

        /// <summary>True when every fault is genuinely repaired RIGHT NOW, asked
        /// fresh rather than read from the credited count. The distinction matters:
        /// a fault credited before diagnosis is still credited, but a learner who
        /// pulled the part back out afterwards should not pass verification.</summary>
        public bool AllFaultsCurrentlyRepaired
        {
            get
            {
                if (activeFaults.Count == 0) return false;
                foreach (var fault in activeFaults)
                {
                    if (fault == null || !fault.IsRepaired()) return false;
                }
                return true;
            }
        }

        // ==================================================================

        private void Start()
        {
            if (!RepairTicketMode.IsActive)
            {
                // Normal campaign play in this scene - no ticket, nothing to watch.
                enabled = false;
                return;
            }

            if (string.IsNullOrEmpty(RepairTicketMode.RequiredTaskId))
            {
                Debug.LogWarning("[TicketFaultCoordinator] A ticket is active but has no Required Task Id - check the TicketData asset. Nothing can be reported.", this);
                enabled = false;
                return;
            }

            // Safe to read IsActiveForThisVisit here: TicketFault decides it in
            // Awake, which has run for every object by the time any Start does.
            var found = FindObjectsByType<TicketFault>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var fault in found)
            {
                if (fault != null && fault.IsActiveForThisVisit) activeFaults.Add(fault);
            }

            if (activeFaults.Count == 0)
            {
                Debug.LogWarning($"[TicketFaultCoordinator] Ticket '{RepairTicketMode.ActiveTicketId}' is active, but no TicketFault in this scene is scoped to it. " +
                                 "The machine will look perfectly healthy and the ticket can never be closed. Check the Active For Ticket Id fields.", this);
                enabled = false;
                return;
            }

            // One step per fault, so a two-fault ticket genuinely needs both fixed.
            TaskManager.Instance.RegisterTask(RepairTicketMode.RequiredTaskId, activeFaults.Count);

            Debug.Log($"[TicketFaultCoordinator] Watching {activeFaults.Count} fault(s) for ticket '{RepairTicketMode.ActiveTicketId}'.", this);
        }

        private void Update()
        {
            if (ticketClosed) return;

            foreach (var fault in activeFaults)
            {
                if (fault == null || fault.HasBeenCredited) continue;
                if (!fault.IsRepaired()) continue;

                if (!RepairTicketMode.IsDiagnosed)
                {
                    WarnOnce();
                    // Deliberately does NOT credit it. The learner can leave it
                    // fixed and go diagnose; it will be credited the moment they
                    // do, without having to break and redo the repair.
                    continue;
                }

                Credit(fault);
            }
        }

        // ==================================================================

        private void Credit(TicketFault fault)
        {
            fault.HasBeenCredited = true;
            repairedCount++;

            TaskManager.Instance.ReportProgress(RepairTicketMode.RequiredTaskId);
            Debug.Log($"[TicketFaultCoordinator] Repaired: {fault.Summary} ({repairedCount}/{activeFaults.Count}).", this);

            if (repairedCount < activeFaults.Count) return;

            ticketClosed = true;
            RepairTicketMode.MarkFixed();

            if (!string.IsNullOrEmpty(stageId))
            {
                AssessmentRecorder.Attempt(stageId, !warnedAboutDiagnosis);
                AssessmentRecorder.Stage(stageId);
            }

            if (allFaultsFixedDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(allFaultsFixedDialog);

            Debug.Log($"[TicketFaultCoordinator] All faults repaired - ticket '{RepairTicketMode.ActiveTicketId}' can be closed.", this);
        }

        private void WarnOnce()
        {
            if (warnedAboutDiagnosis) return;
            warnedAboutDiagnosis = true;

            // Recorded as a mistake, not just a nudge. Reaching for the hardware
            // before identifying the cause is the specific behaviour this module
            // is trying to train out, so it belongs in the report.
            if (!string.IsNullOrEmpty(stageId))
                AssessmentRecorder.Mistake("procedure", "Attempted a repair before diagnosing the fault.");

            Debug.Log("[TicketFaultCoordinator] Something was fixed before the fault was diagnosed - run the diagnostic tool first. The repair still stands and will count once diagnosed.", this);

            if (diagnoseFirstWarning != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(diagnoseFirstWarning);
        }

        /// <summary>Every fault at this site and whether it has been fixed - for a
        /// service report or a debrief screen.</summary>
        public string BuildFaultSummary()
        {
            if (activeFaults.Count == 0) return "No faults were injected at this site.";

            var lines = new List<string>();
            foreach (var fault in activeFaults)
            {
                if (fault == null) continue;
                lines.Add($"  [{(fault.HasBeenCredited ? "fixed" : "outstanding")}] {fault.Summary}");
            }
            return string.Join("\n", lines);
        }
    }
}