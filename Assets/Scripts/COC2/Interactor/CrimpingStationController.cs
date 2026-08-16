using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The missing "finish" step for the crimping minigame. WireDragController/WirePinSlot/
    /// ConnectorEnd handle arranging wires; this is what actually crimps the cable once both
    /// ends are arranged, checks them against the target CableTypeSO, locks the wires so a
    /// finished cable can't be pulled apart again, and reports completion to TaskManager.
    ///
    /// Setup:
    ///   1. Add this to the crimping station object (alongside/near CrimpingViewController).
    ///   2. Assign endOne + endTwo (the two ConnectorEnd components on this cable's housings).
    ///   3. Assign targetCableType (the CableTypeSO the player is meant to build, e.g. a
    ///      straight-through T568B/T568B or a crossover for this task). This now also
    ///      doubles as a fallback/default  if you've added CableTypeSelectionUI, it will
    ///      overwrite this at runtime via SetTargetCableType() once the player picks.
    ///   4. Assign taskId to match whatever you RegisterTask()'d for this module in TaskManager
    ///      (e.g. "crimp_cable"), or leave blank if this station isn't gated behind a task.
    ///   5. Wire the physical crimping tool's click (or the "C" key / mobile Crimp button) to
    ///      RequestCrimp(). OnCrimpResult(bool) fires either way so you can show a success/fail
    ///      dialog line via DialogManager, matching how ConnectorEnd.OnValidated is used.
    ///
    /// Auto-check on completion: separately from the manual crimp step above, the moment
    /// both ends have all 8 pins filled, OnWirePlaced() (called by WireDraggable.SnapIntoSlot)
    /// automatically checks the order against targetCableType and logs correct/wrong right
    /// away. A wrong arrangement sends every wire back to the tray so the player can retry 
    /// this is just early feedback though, it doesn't lock/crimp anything. The player still
    /// needs to use the crimping tool (RequestCrimp) once it's correct to actually finish.
    /// </summary>
    public class CrimpingStationController : MonoBehaviour
    {
        public static CrimpingStationController Instance { get; private set; }

        [Header("Connector Ends")]
        public ConnectorEnd endOne;
        public ConnectorEnd endTwo;

        [Header("Target")]
        [Tooltip("The cable the player is meant to build for this task. Both ends are checked against this. " +
                 "Acts as a default/fallback  overwritten at runtime if CableTypeSelectionUI.SetTargetCableType() is used.")]
        public CableTypeSO targetCableType;

        [Header("Task Reporting (optional)")]
        [Tooltip("Matches a taskId already registered with TaskManager.RegisterTask(). Leave blank to skip.")]
        public string taskId = "crimp_cable";

        [Header("Input")]
        [Tooltip("Keyboard shortcut for the crimping tool while in the close-up crimping view (desktop only).")]
        public KeyCode crimpKey = KeyCode.C;

        [Header("Events")]
        [Tooltip("Fired every time RequestCrimp() runs: true if both ends matched the target cable and the cable is now locked, false if the arrangement was wrong (nothing gets locked, player can keep adjusting).")]
        public UnityEvent<bool> OnCrimpResult;

        [Tooltip("Fired the moment both ends are fully wired and get auto-checked: true if the order was correct, false if the wires just got sent back to the tray. Separate from OnCrimpResult  this fires before the player even reaches for the crimping tool.")]
        public UnityEvent<bool> OnAutoCheckResult;

        [Tooltip("Fired whenever SetTargetCableType() runs (i.e. whenever the player picks Straight-Through " +
                 "or Crossover in CableTypeSelectionUI). Lets read-only UI like CrimpingGuideUI refresh " +
                 "itself without this controller needing to know that UI exists.")]
        public UnityEvent<CableTypeSO> OnTargetCableTypeChanged;

        public bool IsCrimped { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        void Update()
        {
            // Only listen for the keyboard shortcut while actually in the crimping view,
            // same guard CrimpingViewController itself uses for Escape.
            bool inCrimpingView = CrimpingViewController.Instance != null && CrimpingViewController.Instance.InCrimpingView;
            if (inCrimpingView && Input.GetKeyDown(crimpKey))
                RequestCrimp();
        }

        /// <summary>Called by CableTypeSelectionUI once the player picks Straight-Through or
        /// Crossover for this attempt. Also resets any previous crimped/locked state so a
        /// fresh attempt with a newly-chosen cable type always starts unlocked.</summary>
        public void SetTargetCableType(CableTypeSO cableType)
        {
            if (cableType == null)
            {
                Debug.LogWarning("[CrimpingStationController] SetTargetCableType() called with null  ignoring.", this);
                return;
            }

            targetCableType = cableType;
            ResetStation();
            Debug.Log($"[CrimpingStationController] Target cable set to {targetCableType.displayName}.");

            OnTargetCableTypeChanged?.Invoke(targetCableType);
        }

        /// <summary>Called by WireDraggable.SnapIntoSlot() every time a wire lands in a slot.
        /// Once both ends are fully filled, this immediately checks the order and gives
        /// feedback  correct arrangements are left in place (ready for the player to
        /// crimp), wrong ones get sent straight back to the tray.</summary>
        public void OnWirePlaced()
        {
            if (IsCrimped) return; // already finished, don't second-guess a locked cable

            if (endOne == null || endTwo == null)
            {
                Debug.LogWarning("[CrimpingStationController] endOne or endTwo isn't assigned in the Inspector  can't auto-check.", this);
                return;
            }

            // Diagnostic  shows how many of each end's 8 slots actually registered as
            // filled. If a wire looks visually seated but this count doesn't reach 8,
            // check that WireIdentity.endNumber on that wire matches this end's endNumber.
            Debug.Log($"[CrimpingStationController] Wire placed  End 1: {endOne.FilledCount()}/8, End 2: {endTwo.FilledCount()}/8.");

            if (!endOne.IsFullyArranged() || !endTwo.IsFullyArranged()) return; // still missing wires, nothing to check yet

            AutoValidateArrangement();
        }

        void AutoValidateArrangement()
        {
            if (targetCableType == null)
            {
                Debug.LogWarning("[CrimpingStationController] All pins filled but no targetCableType assigned  can't auto-check.", this);
                return;
            }

            bool oneOk = endOne.CheckCompletion(targetCableType);
            bool twoOk = endTwo.CheckCompletion(targetCableType);
            bool success = oneOk && twoOk;

            if (success)
            {
                Debug.Log($"[CrimpingStationController] Wire order correct for {targetCableType.displayName}! Ready to crimp.");
            }
            else
            {
                Debug.Log($"[CrimpingStationController] Wire order incorrect for {targetCableType.displayName}  " +
                          $"End 1 {(oneOk ? "OK" : "wrong")}, End 2 {(twoOk ? "OK" : "wrong")}. Sending wires back.");
                endOne.ReturnAllWiresHome();
                endTwo.ReturnAllWiresHome();
            }

            OnAutoCheckResult?.Invoke(success);
        }

        /// <summary>Wire this to the crimping tool's click and/or the mobile Crimp button's OnClick.</summary>
        public void RequestCrimp()
        {
            if (IsCrimped) return; // already done, nothing to re-check

            if (endOne == null || endTwo == null || targetCableType == null)
            {
                Debug.LogError("[CrimpingStationController] endOne, endTwo, and targetCableType must all be assigned.", this);
                return;
            }

            bool oneOk = endOne.CheckCompletion(targetCableType);
            bool twoOk = endTwo.CheckCompletion(targetCableType);
            bool success = oneOk && twoOk;

            // Cable-type-aware result log  makes it obvious during testing whether the
            // wiring was checked (and passed/failed) against Straight-Through or Crossover.
            if (success)
            {
                Debug.Log($"[CrimpingStationController] Correct! Cable matches {targetCableType.displayName}.");
            }
            else
            {
                Debug.Log($"[CrimpingStationController] Incorrect  End 1 {(oneOk ? "OK" : "wrong")}, " +
                          $"End 2 {(twoOk ? "OK" : "wrong")} for {targetCableType.displayName}.");
            }

            if (success)
            {
                IsCrimped = true;
                endOne.LockAllWires(true);
                endTwo.LockAllWires(true);

                if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                    TaskManager.Instance.ReportProgress(taskId);
            }

            OnCrimpResult?.Invoke(success);
        }

        /// <summary>Resets the station so the player can try again  clears the crimped/locked
        /// state without touching whatever's currently seated in the slots. Wire to a "Redo"
        /// button if you want to let players retry after a wrong crimp, rather than only ever
        /// blocking further presses until they fix the wires themselves.</summary>
        public void ResetStation()
        {
            IsCrimped = false;
            if (endOne != null) endOne.LockAllWires(false);
            if (endTwo != null) endTwo.LockAllWires(false);
        }
    }
}