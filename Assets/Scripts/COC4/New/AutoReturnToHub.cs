using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Takes the learner back to the hub on its own once the job is done, instead
    /// of leaving them standing in a fixed machine's room looking for a button.
    ///
    /// ATTACH THIS TO: the Managers object in each repair-site scene. One per
    /// scene. It needs nothing assigned except, if your hub is not called
    /// "COC4-Hub", the scene name.
    ///
    /// WHEN "DONE" HAPPENS IS THE WHOLE DESIGN DECISION, so it is a dropdown
    /// rather than a hard-coded moment:
    ///
    ///   When Test Passes (default) - the machine has been switched on and passed
    ///     every check, and the cause was recorded. This is the one to use. It
    ///     keeps the power-on test in the loop, because the test is what triggers
    ///     the exit; skip it and nothing happens.
    ///
    ///   When Faults Repaired - the moment the last part goes back in. Faster, and
    ///     it quietly deletes the verification step: a learner never has to switch
    ///     the machine on, because the game leaves for them. That is the callback
    ///     habit the module was built to break, so choose it knowingly.
    ///
    /// IT DOES NOT CONSUME THE TICKET. Like ReturnToHubButton, it only loads the
    /// hub scene - the hub reads RequiredTaskId and ActiveTicketFixed when it
    /// loads and clears them itself via TryConsumeResult. Clearing here would erase
    /// the result before the hub ever saw it, and the ticket would come back marked
    /// unfinished.
    /// </summary>
    public class AutoReturnToHub : MonoBehaviour
    {
        public enum Trigger
        {
            /// <summary>The machine passed the power-on test AND the cause was named.</summary>
            WhenTestPasses = 0,

            /// <summary>Every fault repaired, tested or not.</summary>
            WhenFaultsRepaired = 1,
        }

        [Header("When to leave")]
        [Tooltip("When Test Passes keeps the power-on test meaningful - the test is what triggers the exit. When Faults Repaired leaves as soon as the last part is back in, which removes any reason to test at all.")]
        [SerializeField] private Trigger trigger = Trigger.WhenTestPasses;

        [Tooltip("Turn off to disable the automatic return entirely and go back to the button only.")]
        [SerializeField] private bool enabledForThisScene = true;

        [Header("Timing")]
        [Tooltip("Seconds to wait before the scene changes, so the learner can read the result rather than being yanked away from it.")]
        [Min(0f)]
        [SerializeField] private float delaySeconds = 2.5f;

        [Tooltip("Also wait for any open dialog to be dismissed. Leave on - without it the 'that should be it' line gets half a second on screen.")]
        [SerializeField] private bool waitForDialogToClose = true;

        [Header("Optional")]
        [Tooltip("Shown just before leaving, e.g. \"Job done. Back to the workshop.\" Leave empty to skip it.")]
        [SerializeField] private DialogSequence leavingDialog;

        [Tooltip("Exact scene name of the COC4 Ticket Hub.")]
        [SerializeField] private string hubSceneName = "COC4-Hub";

        [Tooltip("The boot screen. Found automatically - inactive included - if left empty. Only needed for the When Test Passes trigger.")]
        [SerializeField] private PCPowerStation powerStation;

        [Tooltip("The coordinator. Found automatically if left empty.")]
        [SerializeField] private TicketFaultCoordinator coordinator;

        private bool leaving;
        private float unresolvedSince = -1f;
        private bool warnedAboutUnresolved;

        // ==================================================================

        private void Start()
        {
            if (!RepairTicketMode.IsActive)
            {
                // Normal campaign play in this scene - there is no hub to go back to.
                enabled = false;
                return;
            }

            if (!enabledForThisScene)
            {
                enabled = false;
                return;
            }

            // Include inactive: the boot screen hides itself in Start, so by the
            // time anything looks for it, it is switched off.
            if (powerStation == null)
                powerStation = FindAnyObjectByType<PCPowerStation>(FindObjectsInactive.Include);

            if (coordinator == null)
                coordinator = FindAnyObjectByType<TicketFaultCoordinator>();

            if (trigger == Trigger.WhenTestPasses && powerStation == null)
            {
                Debug.LogWarning("[AutoReturnToHub] Trigger is When Test Passes but there is no PCPowerStation in this scene, " +
                                 "so the learner would never leave. Falling back to When Faults Repaired.", this);
                trigger = Trigger.WhenFaultsRepaired;
            }
        }

        private void Update()
        {
            if (leaving) return;
            if (!RepairTicketMode.IsActive) return;

            if (!IsJobDone()) return;

            leaving = true;
            StartCoroutine(LeaveAfterDelay());
        }

        /// <summary>
        /// Both triggers require ActiveTicketFixed, and that is the part that keeps
        /// this from causing the very problem it looks like it should cause.
        ///
        /// CanCloseTicket only means "the machine passed and a cause was named". It
        /// does NOT mean the coordinator credited the repair - and the two can come
        /// apart. A site whose TicketFaultCoordinator found no fault scoped to this
        /// ticket has nothing to break, so the machine boots perfectly on the first
        /// press and CanCloseTicket goes true while ActiveTicketFixed never does.
        /// Leaving on that would drop the learner back at the hub with the ticket
        /// still marked In Progress and nothing on screen to explain why - which is
        /// exactly the confusing failure the whole diagnosis-gate work was undoing.
        ///
        /// ActiveTicketFixed is set by TicketFaultCoordinator.MarkFixed the moment
        /// the LAST fault is credited, so requiring it also handles multi-fault
        /// tickets: fixing one of the two on ticket_coc1_transport_damage will not
        /// send anyone home early.
        /// </summary>
        private bool IsJobDone()
        {
            bool ticketClosable = RepairTicketMode.ActiveTicketFixed;

            if (trigger == Trigger.WhenFaultsRepaired) return ticketClosable;

            bool testPassed = powerStation != null && powerStation.CanCloseTicket;
            if (!testPassed) return false;

            if (ticketClosable) return true;

            // Passed the test but the ticket is not closable. Almost always a setup
            // problem, and silence here reads as "the auto-return is broken", so it
            // says so once rather than leaving you to work it out.
            WarnUnresolved();
            return false;
        }

        private void WarnUnresolved()
        {
            if (unresolvedSince < 0f) unresolvedSince = Time.time;
            if (warnedAboutUnresolved || Time.time - unresolvedSince < 2f) return;

            warnedAboutUnresolved = true;
            Debug.LogWarning($"[AutoReturnToHub] The machine passed its test, but ticket '{RepairTicketMode.ActiveTicketId}' " +
                             "has not been marked fixed, so returning now would leave it In Progress. Staying put.\n" +
                             "Usually this means the TicketFaultCoordinator found no fault scoped to this ticket - check the " +
                             "Active For Ticket Id fields on your TicketFault components. The Return To Hub button still works.", this);
        }

        // ==================================================================

        private IEnumerator LeaveAfterDelay()
        {
            Debug.Log($"[AutoReturnToHub] Job done on '{RepairTicketMode.ActiveTicketId}' ({trigger}) - returning to '{hubSceneName}'.", this);

            if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);

            if (leavingDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(leavingDialog);

            if (waitForDialogToClose && DialogManager.Instance != null)
            {
                // A learner mid-sentence should finish reading. Nothing can undo
                // the job at this point, so waiting indefinitely is safe.
                while (DialogManager.Instance.IsDialogOpen)
                    yield return null;
            }

            if (string.IsNullOrEmpty(hubSceneName))
            {
                Debug.LogWarning("[AutoReturnToHub] No Hub Scene Name set - staying put.", this);
                leaving = false;
                yield break;
            }

            // Statics survive a scene load, and a lock held by a station the learner
            // was standing in would follow them to the hub and freeze them there.
            PlayerInputLock.ReleaseAll();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Deliberately does NOT call TryConsumeResult - see the class summary.
            SceneManager.LoadScene(hubSceneName);
        }
    }
}
