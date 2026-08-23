using System.Collections;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The third and final stage of the crimping station: the close-up on the cable tester.
    /// Structurally identical to StrippingViewController - swap cameras, freeze the player,
    /// enable only this stage's draggables, show this stage's UI - just one stage later.
    ///
    /// Full station flow, for reference:
    ///   Interact -> CableTypeSelectionUI
    ///            -> StrippingViewController   (drag tool onto cable, strip animation)
    ///            -> CrimpingViewController    (arrange pins, drag tool onto each end, crimp animation x2)
    ///            -> TestingViewController     (drag each end into the tester, LED wiremap test)
    ///
    /// It takes over automatically: CrimpingStationController fires OnBothEndsCrimped once the
    /// second crimp animation finishes, and this picks it up, exactly the way
    /// StrippingViewController listens for CrimpToolStripController.OnStripComplete.
    ///
    /// Setup:
    ///   1. Add this to the crimping station object, alongside CrimpingViewController and
    ///      StrippingViewController.
    ///   2. Assign playerCamera (the same one the other two use) and testingCamera - a new
    ///      camera framed on the tester.
    ///   3. Assign plugDraggables - the two CablePlugDraggable components. Leave them UNTICKED
    ///      in the Inspector; this enables them.
    ///   4. Assign playerControlScripts exactly as the other two controllers do.
    ///   5. Assign testerObjects - anything that should only exist during testing (the tester
    ///      model, its world-space Canvas). Assign crimpingStageObjects for anything that should
    ///      disappear now (the crimping tool visual, the guide panels).
    ///   6. Assign testingUI (a "plug both ends in, then press Test" prompt) and the mobile
    ///      control arrays, same as the other stages.
    /// </summary>
    public class TestingViewController : MonoBehaviour
    {
        public static TestingViewController Instance { get; private set; }

        [Header("Cameras")]
        [Tooltip("The player's normal 3D camera - same reference the other two view controllers use.")]
        public Camera playerCamera;
        [Tooltip("Close-up on the cable tester. A separate camera from crimpingCamera and strippingCamera.")]
        public Camera testingCamera;

        [Header("Stage objects")]
        [Tooltip("Shown only during testing - the tester model, its world-space Canvas, the proxy plugs.")]
        public GameObject[] testerObjects;
        [Tooltip("Hidden when testing starts - the crimping tool visual, the pin guide panels, anything " +
                 "left over from the crimping stage.")]
        public GameObject[] crimpingStageObjects;

        [Header("Plugs")]
        [Tooltip("The two CablePlugDraggable components. Enabled only while in this view, so the cable " +
                 "ends aren't draggable from the crimping stage.")]
        public CablePlugDraggable[] plugDraggables;

        [Header("Player control")]
        [Tooltip("Whatever script(s) handle player movement/look - disabled while testing.")]
        public MonoBehaviour[] playerControlScripts;

        [Header("UI")]
        [Tooltip("e.g. a 'plug both ends into the tester, then press Test' prompt.")]
        public GameObject testingUI;

        [Header("Mobile")]
        [Tooltip("Mobile controls hidden while in the testing view - joystick, touch-look field, etc.")]
        public GameObject[] mobileControlsToHide;
        [Tooltip("Mobile controls shown only in the testing view - e.g. Test and Exit buttons.")]
        public GameObject[] mobileTestingControls;

        [Header("Handoff")]
        [Tooltip("Seconds waited after the second crimp finishes before the testing view opens, so the " +
                 "crimp animation gets a beat to land instead of cutting straight away.")]
        public float entryDelay = 0.5f;
        [Tooltip("Untick if you'd rather the player walk away and come back (via a separate Interactable) " +
                 "instead of being taken straight into testing after crimping.")]
        public bool autoEnterAfterCrimping = true;

        public bool InTestingView { get; private set; }

        void Awake()
        {
            Instance = this;
            SetTestingViewActive(false);
        }

        void Start()
        {
            // Subscribed in Start so CrimpingStationController.Instance is guaranteed set,
            // regardless of script execution order - same reasoning as StrippingViewController.
            if (CrimpingStationController.Instance != null)
            {
                CrimpingStationController.Instance.OnBothEndsCrimped += HandleBothEndsCrimped;
            }
            else
            {
                Debug.LogWarning("[TestingViewController] No CrimpingStationController in the scene - " +
                                 "crimping won't be able to hand off to the testing view.", this);
            }
        }

        void OnDestroy()
        {
            if (CrimpingStationController.Instance != null)
                CrimpingStationController.Instance.OnBothEndsCrimped -= HandleBothEndsCrimped;
        }

        void Update()
        {
            if (InTestingView && Input.GetKeyDown(KeyCode.Escape))
                ExitTestingView();
        }

        void HandleBothEndsCrimped()
        {
            if (!autoEnterAfterCrimping) return;
            StartCoroutine(EnterAfterDelay());
        }

        IEnumerator EnterAfterDelay()
        {
            if (entryDelay > 0f) yield return new WaitForSeconds(entryDelay);

            // Close the crimping view first so its camera and UI are down before ours come up.
            if (CrimpingViewController.Instance != null)
                CrimpingViewController.Instance.ExitCrimpingView();

            EnterTestingView();
        }

        /// <summary>Also safe to call from a button or a separate Interactable if you'd rather the
        /// player walk over to the tester themselves (untick autoEnterAfterCrimping in that case).</summary>
        public void EnterTestingView()
        {
            if (InTestingView) return;

            var station = CrimpingStationController.Instance;
            if (station != null && !station.IsCrimped)
            {
                Debug.Log("[TestingViewController] Both ends need to be crimped before the cable can be tested.", this);
                return;
            }

            SetTestingViewActive(true);
        }

        public void ExitTestingView()
        {
            if (!InTestingView) return;

            if (!CanCancel())
            {
                Debug.Log("[TestingViewController] Can't back out mid-test - the tester is still running.", this);
                return;
            }

            SetTestingViewActive(false);
        }

        /// <summary>Wire this to the mobile Exit/Back button's OnClick - there's no Escape key on Android.</summary>
        public void RequestExitTestingView()
        {
            ExitTestingView();
        }

        bool CanCancel()
        {
            var tester = CableTesterController.Instance;
            if (tester != null && tester.IsTesting) return false;

            var plugs = CableTesterPlugController.Instance;
            if (plugs != null && plugs.IsSeatingPlug) return false;

            return true;
        }

        void SetTestingViewActive(bool active)
        {
            InTestingView = active;

            if (playerCamera != null) playerCamera.gameObject.SetActive(!active);
            if (testingCamera != null) testingCamera.gameObject.SetActive(active);

            foreach (var obj in testerObjects)
            {
                if (obj != null) obj.SetActive(active);
            }

            // Only hide the crimping leftovers - don't put them back on exit, the player has
            // finished with them.
            if (active)
            {
                foreach (var obj in crimpingStageObjects)
                {
                    if (obj != null) obj.SetActive(false);
                }
            }

            foreach (var plug in plugDraggables)
            {
                // Don't re-enable a plug that's already seated in a jack.
                if (plug == null) continue;
                var plugs = CableTesterPlugController.Instance;
                bool alreadySeated = plugs != null && plugs.IsPlugSeated(plug.endNumber);
                plug.enabled = active && !alreadySeated;
            }

            foreach (var script in playerControlScripts)
            {
                if (script != null) script.enabled = !active;
            }

            if (testingUI != null) testingUI.SetActive(active);

            foreach (var obj in mobileControlsToHide)
            {
                if (obj != null) obj.SetActive(!active);
            }
            foreach (var obj in mobileTestingControls)
            {
                if (obj != null) obj.SetActive(active);
            }

            // Wires shouldn't be grabbable from the testing camera - they're crimped in anyway,
            // but this stops a stray tap raycasting into the connector.
            if (WireDragController.Instance != null && active)
                WireDragController.Instance.enabled = false;

            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;
        }
    }
}
