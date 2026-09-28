using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The "finish" step for the crimping minigame. WireDragController/WirePinSlot/ConnectorEnd
    /// handle arranging wires; this owns what happens once an end is arranged - locking that
    /// end's wires when it gets crimped, remembering the exact pinout that was crimped so the
    /// cable tester can read it back later, and reporting completion to TaskManager.
    ///
    /// WHAT CHANGED FROM THE EARLIER VERSION
    /// -------------------------------------
    /// 1. Crimping is now PER END. The player crimps End 1, then End 2, by dragging the crimping
    ///    tool onto each connector (CrimpToolCrimpDraggable -> CrimpToolCrimpController). This
    ///    script no longer does the crimping itself; it listens for OnEndCrimpComplete and does
    ///    the bookkeeping (lock wires, snapshot pinout, fire events).
    /// 2. A wrong arrangement is no longer auto-rejected by default. autoCheckMode now defaults
    ///    to FeedbackOnly, so a miswired cable CAN be crimped - which is the whole point of
    ///    having a cable tester afterwards. Set it back to ReturnWiresWhenWrong for the old
    ///    behaviour, or Off to remove the hint entirely (hardest, best for assessment).
    /// 3. CrimpedArrangement snapshots. Once an end is crimped its 8-colour pin order is frozen
    ///    here, so CableTesterController can build a wiremap from what was ACTUALLY crimped
    ///    rather than re-reading live slot state.
    ///
    /// Setup:
    ///   1. Add this to the crimping station object (alongside/near CrimpingViewController).
    ///   2. Assign endOne + endTwo (the two ConnectorEnd components on this cable's housings).
    ///   3. Assign targetCableType (the CableTypeSO the player is meant to build). Overwritten
    ///      at runtime by CableTypeSelectionUI.SetTargetCableType() once the player picks.
    ///   4. Assign taskId to match whatever you RegisterTask()'d in TaskManager, or leave blank.
    ///   5. Pick an autoCheckMode - see the tooltip on that field.
    /// </summary>
    public class CrimpingStationController : MonoBehaviour
    {
        /// <summary>What happens the moment both ends are fully arranged, BEFORE the player
        /// reaches for the crimping tool.</summary>
        public enum AutoCheckMode
        {
            [Tooltip("No hint at all. The player crimps whatever they arranged and only finds out at the tester.")]
            Off,
            [Tooltip("Log/fire OnAutoCheckResult so a subtle hint can show, but leave the wires alone - a wrong cable can still be crimped and will fail the tester.")]
            FeedbackOnly,
            [Tooltip("Original behaviour: a wrong arrangement sends every wire back to the tray so only a correct cable can ever be crimped.")]
            ReturnWiresWhenWrong
        }

        public static CrimpingStationController Instance { get; private set; }

        [Header("Connector Ends")]
        public ConnectorEnd endOne;
        public ConnectorEnd endTwo;

        [Header("Target")]
        [Tooltip("The cable the player is meant to build for this task. Both ends are checked against this. " +
                 "Acts as a default/fallback - overwritten at runtime if CableTypeSelectionUI.SetTargetCableType() is used.")]
        public CableTypeSO targetCableType;

        [Header("Auto-check")]
        [Tooltip("What happens the moment both ends are fully arranged. FeedbackOnly is the default now, " +
                 "so a miswired cable can actually be crimped and then caught by the cable tester.")]
        public AutoCheckMode autoCheckMode = AutoCheckMode.FeedbackOnly;

        [Header("Task Reporting (optional)")]
        [Tooltip("Matches a taskId already registered with TaskManager.RegisterTask(). Leave blank to skip.")]
        public string taskId = "crimp_cable";

        [Header("Leaving the station")]
        [Tooltip("Keep the player at the crimping station from the moment they start stripping until both " +
                 "ends are crimped: no Exit / Back button, and Escape and E do not leave the stripping, " +
                 "wiring or crimping views.\n\n" +
                 "Leaving half-way used to send the player back to stripping on the way in, with the cable " +
                 "already stripped - a dead end. Once both ends are crimped the station closes by itself, as " +
                 "before. Untick to allow leaving again.")]
        public bool lockPlayerInUntilCrimped = true;

        [Header("Input")]
        [Tooltip("Keyboard shortcut that crimps the next ready end without dragging the tool (desktop convenience). " +
                 "It still plays the full crimp animation - it just skips the drag.")]
        public KeyCode crimpKey = KeyCode.C;

        [Header("Events")]
        [Tooltip("Fired once BOTH ends are crimped: true if the finished cable matches the target type, false if it's miswired. " +
                 "Note this is now informational only - nothing is blocked, the tester is where the player finds out.")]
        public UnityEvent<bool> OnCrimpResult;

        [Tooltip("Fired the moment both ends are fully wired and get auto-checked (unless autoCheckMode is Off): " +
                 "true if the order was correct. Fires BEFORE the player reaches for the crimping tool.")]
        public UnityEvent<bool> OnAutoCheckResult;

        [Tooltip("Fired with the end number (1 or 2) each time an end finishes being crimped. " +
                 "Handy for a 'End 1 done - now do End 2' prompt.")]
        public UnityEvent<int> OnEndCrimped;

        [Tooltip("Fired whenever SetTargetCableType() runs. Lets read-only UI like CrimpingGuideUI refresh itself.")]
        public UnityEvent<CableTypeSO> OnTargetCableTypeChanged;

        /// <summary>True once BOTH ends have been crimped. (Previously this meant 'crimped AND correct' -
        /// it now just means the physical crimping is finished, correct or not.)</summary>
        public bool IsCrimped => endOneCrimped && endTwoCrimped;

        /// <summary>Fired once both ends are crimped. TestingViewController subscribes to this to
        /// take over the stage, the same way StrippingViewController subscribes to
        /// CrimpToolStripController.OnStripComplete.</summary>
        public System.Action OnBothEndsCrimped;

        bool endOneCrimped;
        bool endTwoCrimped;

        // ------------------------------------------------------------------
        // Leaving the station
        // ------------------------------------------------------------------

        /// <summary>
        /// True while the player may NOT leave the stripping, wiring or crimping views: the lock is on
        /// and this cable is not crimped yet. StrippingViewController, CrimpingViewController,
        /// CrimpStageViewController and StationExitButton all ask this before letting the player out.
        /// Scripts that close the views themselves (a reset after a failed test, say) are not affected.
        /// </summary>
        public static bool ExitLocked =>
            Instance != null && Instance.lockPlayerInUntilCrimped && !Instance.IsCrimped;

        static readonly string[] ExitMethodNames =
        {
            "RequestExitStrippingView", "ExitStrippingView",
            "RequestExitCrimpingView", "ExitCrimpingView",
            "RequestBack"
        };

        /// <summary>
        /// Hides every button under these objects whose On Click leaves the station - the mobile
        /// Cancel / Exit / Back buttons, or a desktop Exit button - while ExitLocked is true. Found by
        /// what the button calls, so a Crimp button sitting in the same list is left alone.
        /// </summary>
        public static void HideExitButtons(GameObject[] roots, GameObject extraRoot = null)
        {
            if (!ExitLocked) return;

            if (roots != null)
                foreach (GameObject root in roots) HideExitButtonsUnder(root);

            HideExitButtonsUnder(extraRoot);
        }

        static void HideExitButtonsUnder(GameObject root)
        {
            if (root == null) return;

            foreach (UnityEngine.UI.Button button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button == null) continue;

                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    if (System.Array.IndexOf(ExitMethodNames, button.onClick.GetPersistentMethodName(i)) < 0) continue;
                    button.gameObject.SetActive(false);
                    break;
                }
            }
        }

        // The pin order each end was actually crimped with, frozen at crimp time. Index 0 = pin 1.
        WireColor[] crimpedEndOne;
        WireColor[] crimpedEndTwo;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            // Subscribed in Start (not Awake) so CrimpToolCrimpController.Instance is guaranteed
            // to be set regardless of script execution order - same reasoning as
            // StrippingViewController's subscription to CrimpToolStripController.
            if (CrimpToolCrimpController.Instance != null)
            {
                CrimpToolCrimpController.Instance.OnEndCrimpComplete += HandleEndCrimpComplete;
            }
            else
            {
                Debug.LogWarning("[CrimpingStationController] No CrimpToolCrimpController in the scene - " +
                                 "the crimp animation won't be able to report finished ends back here.", this);
            }
        }

        void OnDestroy()
        {
            if (CrimpToolCrimpController.Instance != null)
                CrimpToolCrimpController.Instance.OnEndCrimpComplete -= HandleEndCrimpComplete;
        }

        void Update()
        {
            // Only listen for the shortcut while actually in the crimping view, same guard
            // CrimpingViewController uses for Escape.
            bool inCrimpingView = CrimpingViewController.Instance != null && CrimpingViewController.Instance.InCrimpingView;
            if (inCrimpingView && Input.GetKeyDown(crimpKey))
                RequestCrimp();
        }

        // ------------------------------------------------------------------
        // Cable type
        // ------------------------------------------------------------------

        /// <summary>Called by CableTypeSelectionUI once the player picks Straight-Through or
        /// Crossover. Also fully resets the station so a fresh attempt starts unlocked.</summary>
        public void SetTargetCableType(CableTypeSO cableType)
        {
            if (cableType == null)
            {
                Debug.LogWarning("[CrimpingStationController] SetTargetCableType() called with null - ignoring.", this);
                return;
            }

            targetCableType = cableType;
            ResetStation();
            Debug.Log($"[CrimpingStationController] Target cable set to {targetCableType.displayName}.");

            OnTargetCableTypeChanged?.Invoke(targetCableType);
        }

        // ------------------------------------------------------------------
        // Arranging
        // ------------------------------------------------------------------

        /// <summary>Called by WireDraggable.SnapIntoSlot() every time a wire lands in a slot.</summary>
        public void OnWirePlaced()
        {
            if (IsCrimped) return; // finished cable, don't second-guess it

            if (endOne == null || endTwo == null)
            {
                Debug.LogWarning("[CrimpingStationController] endOne or endTwo isn't assigned - can't auto-check.", this);
                return;
            }

            // Diagnostic - if a wire looks visually seated but this count doesn't reach 8, check
            // that WireIdentity.endNumber on that wire matches the ConnectorEnd's endNumber.
            Debug.Log($"[CrimpingStationController] Wire placed - End 1: {endOne.FilledCount()}/8, End 2: {endTwo.FilledCount()}/8.");

            if (!endOne.IsFullyArranged() || !endTwo.IsFullyArranged()) return;

            if (autoCheckMode != AutoCheckMode.Off)
                AutoValidateArrangement();
        }

        void AutoValidateArrangement()
        {
            if (targetCableType == null)
            {
                Debug.LogWarning("[CrimpingStationController] All pins filled but no targetCableType assigned - can't auto-check.", this);
                return;
            }

            bool oneOk = endOne.CheckCompletion(targetCableType);
            bool twoOk = endTwo.CheckCompletion(targetCableType);
            bool success = oneOk && twoOk;

            if (success)
            {
                Debug.Log($"[CrimpingStationController] Wire order correct for {targetCableType.displayName}. Ready to crimp.");
            }
            else
            {
                Debug.Log($"[CrimpingStationController] Wire order incorrect for {targetCableType.displayName} - " +
                          $"End 1 {(oneOk ? "OK" : "wrong")}, End 2 {(twoOk ? "OK" : "wrong")}.");

                if (autoCheckMode == AutoCheckMode.ReturnWiresWhenWrong)
                {
                    Debug.Log("[CrimpingStationController] autoCheckMode is ReturnWiresWhenWrong - sending wires back.");
                    endOne.ReturnAllWiresHome();
                    endTwo.ReturnAllWiresHome();
                }
            }

            OnAutoCheckResult?.Invoke(success);
        }

        // ------------------------------------------------------------------
        // Crimping (per end)
        // ------------------------------------------------------------------

        /// <summary>True when this end has all 8 pins seated and hasn't been crimped yet.
        /// CrimpToolCrimpController asks this before letting a crimp animation start, so the
        /// "is this legal yet" rule lives in exactly one place.</summary>
        public bool CanCrimpEnd(int endNumber)
        {
            ConnectorEnd end = GetEnd(endNumber);
            if (end == null) return false;
            if (IsEndCrimped(endNumber)) return false;
            return end.IsFullyArranged();
        }

        public bool IsEndCrimped(int endNumber) => endNumber == 1 ? endOneCrimped : endTwoCrimped;

        /// <summary>The 8-colour pin order this end was crimped with (index 0 = pin 1), or null
        /// if it hasn't been crimped. This is what CableTesterController builds its wiremap from.</summary>
        public WireColor[] GetCrimpedArrangement(int endNumber) => endNumber == 1 ? crimpedEndOne : crimpedEndTwo;

        /// <summary>Called by CrimpToolCrimpController.OnEndCrimpComplete once that end's clip finishes.
        /// Also safe to call directly if you ever need to crimp an end without the animation.</summary>
        public void CrimpEnd(int endNumber)
        {
            ConnectorEnd end = GetEnd(endNumber);
            if (end == null)
            {
                Debug.LogError($"[CrimpingStationController] No ConnectorEnd assigned for end {endNumber}.", this);
                return;
            }

            if (IsEndCrimped(endNumber)) return;

            // Freeze the pinout BEFORE locking, so the tester reads exactly what got crimped.
            WireColor[] snapshot = end.GetArrangement();
            if (endNumber == 1) { crimpedEndOne = snapshot; endOneCrimped = true; }
            else { crimpedEndTwo = snapshot; endTwoCrimped = true; }

            // Locked wires can't be dragged back out - a crimped connector is permanent, right
            // or wrong. That's what makes the tester meaningful.
            end.LockAllWires(true);

            Debug.Log($"[CrimpingStationController] End {endNumber} crimped and locked.", this);
            OnEndCrimped?.Invoke(endNumber);

            if (IsCrimped)
                HandleBothEndsCrimped();
        }

        void HandleEndCrimpComplete(int endNumber)
        {
            CrimpEnd(endNumber);
        }

        void HandleBothEndsCrimped()
        {
            bool matchesTarget = EvaluateAgainstTarget();

            Debug.Log(matchesTarget
                ? $"[CrimpingStationController] Both ends crimped and the cable matches {targetCableType?.displayName}."
                : "[CrimpingStationController] Both ends crimped, but the pinout does NOT match the target cable. " +
                  "The tester will show where.");

            // The crimping TASK is done either way - the player physically crimped the cable.
            // Whether it works is the tester's task to report.
            if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                TaskManager.Instance.ReportProgress(taskId);

            OnCrimpResult?.Invoke(matchesTarget);
            OnBothEndsCrimped?.Invoke();
        }

        /// <summary>Compares the crimped pinouts against targetCableType. Read-only - it doesn't
        /// change any state, so UI can call it freely.</summary>
        public bool EvaluateAgainstTarget()
        {
            if (targetCableType == null || crimpedEndOne == null || crimpedEndTwo == null) return false;
            return targetCableType.ValidateEnd(1, crimpedEndOne) && targetCableType.ValidateEnd(2, crimpedEndTwo);
        }

        /// <summary>Convenience shortcut wired to the crimpKey and the mobile Crimp button: crimps
        /// the next end that's ready, without needing the drag. Still plays the full animation.</summary>
        public void RequestCrimp()
        {
            int next = NextCrimpableEnd();
            if (next == 0)
            {
                Debug.Log("[CrimpingStationController] Nothing to crimp - either both ends are done, " +
                          "or the next end still has empty pins.", this);
                return;
            }

            if (CrimpToolCrimpController.Instance != null)
                CrimpToolCrimpController.Instance.TryDropOnCrimpAnchor(next);
            else
                CrimpEnd(next); // no animation rig in the scene - crimp instantly rather than dead-ending
        }

        /// <summary>Lowest-numbered end that's fully arranged and not yet crimped, or 0 if there isn't one.</summary>
        public int NextCrimpableEnd()
        {
            if (CanCrimpEnd(1)) return 1;
            if (CanCrimpEnd(2)) return 2;
            return 0;
        }

        // ------------------------------------------------------------------
        // Reset
        // ------------------------------------------------------------------

        /// <summary>Full reset so the player can start a fresh cable - unlocks both ends, clears the
        /// crimped snapshots, and rewinds the crimp animation rig. Wire this to a "Redo" button.</summary>
        public void ResetStation()
        {
            endOneCrimped = false;
            endTwoCrimped = false;
            crimpedEndOne = null;
            crimpedEndTwo = null;

            if (endOne != null) endOne.LockAllWires(false);
            if (endTwo != null) endTwo.LockAllWires(false);

            if (CrimpToolCrimpController.Instance != null)
                CrimpToolCrimpController.Instance.ResetForReplay();
        }

        ConnectorEnd GetEnd(int endNumber) => endNumber == 1 ? endOne : endNumber == 2 ? endTwo : null;
    }
}