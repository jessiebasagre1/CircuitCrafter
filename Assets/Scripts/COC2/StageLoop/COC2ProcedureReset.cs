// COC2ProcedureReset.cs
using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Puts the whole COC2 procedure back to the very beginning: jacket back on, wires back in the
    /// tray, both connectors un-crimped, tester cleared, and the player returned to the stripping
    /// stage. This is what runs when a cable fails the tester - in the field a bad crimp can't be
    /// undone, you cut both ends off and start again, and that's exactly what this models.
    ///
    /// It is add-only. Nothing here edits an existing script; it just calls the ResetForReplay() /
    /// ResetStation() methods those scripts already expose, in an order that works:
    ///
    ///     1. close whichever view is open (tester, crimp, wiring, stripping)
    ///     2. CrimpingStationController.ResetStation()   - unlocks the wires, clears the crimped
    ///                                                     pinout snapshots, rewinds the crimp rig
    ///     3. ConnectorEnd.ReturnAllWiresHome() x2       - sends the now-unlocked wires to the tray
    ///                                                     (this MUST come after step 2: a locked
    ///                                                     wire refuses to move)
    ///     4. CrimpToolStripController.ResetForReplay()  - jacket back on
    ///     5. re-arm the finished-cable pickup on the workbench
    ///     6. drop the player back in at the stripping stage
    ///
    /// Setup:
    ///   1. Add this to the same always-active Managers object that holds DialogManager.
    ///   2. Assign the three Animator fields if your rigs use an idle state (strongly recommended -
    ///      see the tooltips). Everything else is optional.
    ///   3. Assign this component to COC2StageDialogs > Procedure Reset.
    ///
    /// You can also wire RestartProcedure() straight to a "Try Again" button's OnClick.
    /// </summary>
    public class COC2ProcedureReset : MonoBehaviour
    {
        /// <summary>Where the player ends up once everything has been wound back.</summary>
        public enum RestartEntry
        {
            [Tooltip("Cut straight back to the stripping close-up, keeping the cable type they already picked.")]
            EnterStrippingView,
            [Tooltip("Reopen the Straight-Through / Crossover panel so they choose again before stripping.")]
            OpenCableChoice,
            [Tooltip("Hand control back to the player and make them walk to the station and press E themselves.")]
            ReturnToPlayer
        }

        public static COC2ProcedureReset Instance { get; private set; }

        [Header("Where the retry starts")]
        [Tooltip("EnterStrippingView is the default: the camera cuts straight back to the stripping " +
                 "close-up so the player is immediately doing something.")]
        public RestartEntry restartEntry = RestartEntry.EnterStrippingView;

        [Tooltip("Only used when Restart Entry is OpenCableChoice. Assign the same CableTypeSelectionUI " +
                 "that CrimpingStationInteractable uses.")]
        public CableTypeSelectionUI cableTypeSelection;

        [Header("Rig rewind (optional, but assign these)")]
        [Tooltip("The Animator on your stripping rig - the same object CrimpToolStripController sits on. " +
                 "Assigned, this plays its idle state so the jacket visibly goes back on. Left empty, the " +
                 "rig may sit at the end of the strip clip and the cable will still look stripped.")]
        public Animator strippingRigAnimator;
        [Tooltip("Name of the empty default state in that Animator Controller. Must match exactly.")]
        public string strippingIdleState = "Idle";

        [Tooltip("The Animator on your crimping rig - the object CrimpToolCrimpController sits on.")]
        public Animator crimpingRigAnimator;
        public string crimpingIdleState = "Idle";

        [Tooltip("Idle state name on the LAN tester's Animator. The tester Animator itself is read from " +
                 "NetworkTestStation, so there's nothing to assign here beyond the state name.")]
        public string testerIdleState = "Idle";

        [Header("Extra objects (optional)")]
        [Tooltip("Switched ON by a restart - e.g. an 'intact jacket' mesh if your cable uses swapped " +
                 "models rather than a baked animation.")]
        public GameObject[] showOnRestart;
        [Tooltip("Switched OFF by a restart - e.g. a 'stripped jacket' mesh, or a results panel.")]
        public GameObject[] hideOnRestart;

        [Header("Move the player back (optional)")]
        [Tooltip("The player object to move. Leave empty to leave the player standing wherever they are.")]
        public Transform playerRoot;
        [Tooltip("Where the player is put on a restart - stand this in front of the crimping station.")]
        public Transform playerRestartPoint;

        [Header("Events")]
        [Tooltip("Fires after everything has been wound back, just before the player is dropped into " +
                 "the stripping stage. Handy for a 'attempt 2' counter or a score penalty.")]
        public UnityEvent OnProcedureRestarted;

        /// <summary>How many times the procedure has been restarted this session. Read it for a
        /// retry counter or a grade.</summary>
        public int RestartCount { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        // ------------------------------------------------------------------

        /// <summary>The whole thing. Safe to call at any time, from any stage. Also safe to wire
        /// directly to a "Try Again" button.</summary>
        public void RestartProcedure()
        {
            CloseAllViews();
            RewindStation();
            RewindStrippingRig();
            RewindTester();
            RearmCablePickup();
            ApplyExtraObjects();
            MovePlayerBack();

            RestartCount++;
            Debug.Log($"[COC2ProcedureReset] Procedure reset - restart #{RestartCount}. " +
                      "Jacket back on, wires back in the tray, both ends un-crimped.", this);

            OnProcedureRestarted?.Invoke();

            EnterFirstStage();
        }

        // ------------------------------------------------------------------

        void CloseAllViews()
        {
            // Tester first - it's the one that's open when a failure happens.
            var tester = NetworkTestStation.Instance;
            if (tester != null && tester.InTesterView)
                tester.RequestExit();

            if (CrimpStageViewController.Instance != null)
                CrimpStageViewController.Instance.ResetForReplay();

            if (CrimpingViewController.Instance != null)
                CrimpingViewController.Instance.ExitCrimpingView();

            if (StrippingViewController.Instance != null)
                StrippingViewController.Instance.ExitStrippingView();

            if (cableTypeSelection != null && cableTypeSelection.IsOpen)
                cableTypeSelection.Cancel();
        }

        void RewindStation()
        {
            var station = CrimpingStationController.Instance;
            if (station == null)
            {
                Debug.LogWarning("[COC2ProcedureReset] No CrimpingStationController in the scene - " +
                                 "nothing to wind back.", this);
                return;
            }

            // Unlocks both ends, clears the crimped pinout snapshots, and calls
            // CrimpToolCrimpController.ResetForReplay() (which clears the per-end `done` flags and
            // puts the real connector meshes back).
            station.ResetStation();

            // ORDER MATTERS: ForceReturnHome() deliberately no-ops on a locked wire, so this has to
            // run after ResetStation() has unlocked them. Do it the other way round and the wires
            // stay sitting in the pins looking finished.
            if (station.endOne != null) station.endOne.ReturnAllWiresHome();
            if (station.endTwo != null) station.endTwo.ReturnAllWiresHome();

            PlayIdle(crimpingRigAnimator, crimpingIdleState);
        }

        void RewindStrippingRig()
        {
            if (CrimpToolStripController.Instance != null)
                CrimpToolStripController.Instance.ResetForReplay();

            // ResetForReplay() rewinds to frame 0 of the STRIP clip, which then plays forward again
            // on its own. Playing the controller's idle state afterwards is what actually parks it.
            PlayIdle(strippingRigAnimator, strippingIdleState);

            // The stripping stage owns the tool's visibility while it's open. If the retry is not
            // going straight into that view, put the tool away by hand so it isn't left floating
            // at the station.
            var strip = StrippingViewController.Instance;
            if (restartEntry != RestartEntry.EnterStrippingView &&
                strip != null && strip.crimpingToolVisual != null)
            {
                strip.crimpingToolVisual.SetActive(false);
            }
        }

        void RewindTester()
        {
            var tester = NetworkTestStation.Instance;
            if (tester == null) return;

            // Clears HasTested, unplugs the RJ45 and sends it back to its home anchor.
            tester.ResetStation();

            // Any trigger still sitting in the Animator's queue would fire the pass/fail clip again
            // the moment the tester is next used.
            var animator = tester.testerAnimator;
            if (animator != null)
            {
                ClearTrigger(animator, tester.plugInTrigger);
                ClearTrigger(animator, tester.passTrigger);
                ClearTrigger(animator, tester.failTrigger);
                PlayIdle(animator, testerIdleState);
            }

            if (tester.resultText != null) tester.resultText.text = "";

            // The cable is neither in the pocket nor in the tester any more - it's about to be
            // rebuilt from scratch.
            if (CableInventory.Instance != null) CableInventory.Instance.Consume();
        }

        void RearmCablePickup()
        {
            // Found rather than assigned so there's one less Inspector field to fill, and so this
            // still works if you have more than one workbench. Include is essential: the pickup
            // deactivates its own GameObject once the player pockets the cable.
            CrimpedCablePickup[] pickups =
                Object.FindObjectsByType<CrimpedCablePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var pickup in pickups)
            {
                if (pickup != null) pickup.ResetForReplay();
            }
        }

        void ApplyExtraObjects()
        {
            foreach (var obj in showOnRestart)
            {
                if (obj != null) obj.SetActive(true);
            }
            foreach (var obj in hideOnRestart)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        void MovePlayerBack()
        {
            if (playerRoot == null || playerRestartPoint == null) return;

            // A CharacterController writes its own position back every frame and will fight a
            // direct transform assignment, so it has to be switched off across the teleport. This
            // is the same dance every first-person teleport in Unity has to do.
            var controller = playerRoot.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            playerRoot.SetPositionAndRotation(playerRestartPoint.position, playerRestartPoint.rotation);

            if (controller != null) controller.enabled = true;
        }

        void EnterFirstStage()
        {
            switch (restartEntry)
            {
                case RestartEntry.EnterStrippingView:
                    if (StrippingViewController.Instance != null)
                    {
                        StrippingViewController.Instance.EnterStrippingView();
                    }
                    else
                    {
                        Debug.LogWarning("[COC2ProcedureReset] No StrippingViewController in the scene - " +
                                         "the player has been left on the player camera.", this);
                    }
                    break;

                case RestartEntry.OpenCableChoice:
                    if (cableTypeSelection != null)
                    {
                        cableTypeSelection.Open();
                    }
                    else
                    {
                        Debug.LogWarning("[COC2ProcedureReset] Restart Entry is OpenCableChoice but no " +
                                         "CableTypeSelectionUI is assigned - falling back to the stripping view.", this);
                        if (StrippingViewController.Instance != null)
                            StrippingViewController.Instance.EnterStrippingView();
                    }
                    break;

                case RestartEntry.ReturnToPlayer:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
            }
        }

        // ------------------------------------------------------------------

        static void PlayIdle(Animator animator, string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;

            animator.Play(stateName, 0, 0f);
            animator.Update(0f); // apply it this frame instead of next, so nothing renders mid-clip
        }

        static void ClearTrigger(Animator animator, string trigger)
        {
            if (animator == null || string.IsNullOrEmpty(trigger)) return;
            animator.ResetTrigger(trigger);
        }
    }
}