using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Handles the NEW first stage of the crimping station: close-up on the cable with the
    /// crimping tool draggable, before the pin-arranging crimping view opens. Sits between
    /// CrimpingStationInteractable/CableTypeSelectionUI (which used to call
    /// CrimpingViewController.EnterCrimpingView() directly) and CrimpingViewController itself.
    ///
    /// Flow: EnterStrippingView() -> player drags the tool (CrimpToolDraggable) onto the cable
    /// -> CrimpToolStripController plays CrimpTool_Strip_UTP -> OnStripComplete fires ->
    /// this hands off to CrimpingViewController.EnterCrimpingView(), same shape as
    /// CrimpingViewController's own SetCrimpingViewActive(), just one stage earlier.
    ///
    /// Setup:
    ///   1. Add this to the crimping station object, alongside CrimpingViewController.
    ///   2. Assign playerCamera (same one CrimpingViewController uses) and strippingCamera
    ///      (a new camera framed on the cable's strip point - separate from crimpingCamera).
    ///   3. Assign crimpingToolVisual to the SAME GameObject as
    ///      CrimpingViewController.crimpingToolVisual, and toolDraggable to the
    ///      CrimpToolDraggable component on that same object.
    ///   4. Assign playerControlScripts the same way CrimpingViewController does.
    ///   5. In CrimpingStationInteractable and CableTypeSelectionUI, change the call from
    ///      CrimpingViewController.Instance.EnterCrimpingView() to
    ///      StrippingViewController.Instance.EnterStrippingView() - this becomes the new
    ///      first stop. CrimpingViewController.EnterCrimpingView() is no longer called from
    ///      outside; only this script calls it now, once stripping finishes.
    /// </summary>
    public class StrippingViewController : MonoBehaviour
    {
        public static StrippingViewController Instance { get; private set; }

        [Header("Cameras")]
        [Tooltip("The player's normal 3D camera - same reference as CrimpingViewController.playerCamera.")]
        public Camera playerCamera;
        [Tooltip("Close-up on the cable's strip point. Separate camera from crimpingCamera.")]
        public Camera strippingCamera;

        [Header("Crimping Tool")]
        [Tooltip("Same GameObject as CrimpingViewController.crimpingToolVisual - this project uses one " +
                 "tool object for both stripping and the final crimp click. Popped into view here, at " +
                 "the start of stripping, instead of when the crimping view opens.")]
        public GameObject crimpingToolVisual;
        [Tooltip("The CrimpToolDraggable component on crimpingToolVisual. Enabled only while actually " +
                 "in this stage, so the tool isn't draggable once the player's moved on to arranging pins.")]
        public CrimpToolDraggable toolDraggable;

        [Header("Player control")]
        [Tooltip("Whatever script(s) handle player movement/look - assign so they get disabled while stripping.")]
        public MonoBehaviour[] playerControlScripts;

        [Header("UI")]
        [Tooltip("e.g. a 'drag the tool onto the cable' prompt, an ESC-to-cancel hint.")]
        public GameObject strippingUI;

        [Header("Mobile")]
        [Tooltip("Mobile controls hidden while in the stripping view - joystick, touch-look field, etc. Same idea as CrimpingViewController.mobileControlsToHide.")]
        public GameObject[] mobileControlsToHide;
        [Tooltip("Mobile controls shown only in the stripping view - e.g. a Cancel/Back button, since there's no on-screen Escape key on Android.")]
        public GameObject[] mobileStrippingControls;

        public bool InStrippingView { get; private set; }

        void Awake()
        {
            Instance = this;
            SetStrippingViewActive(false);
        }

        void Start()
        {
            // Subscribed in Start (not Awake) so CrimpToolStripController.Instance is
            // guaranteed to already be set, regardless of script execution order.
            if (CrimpToolStripController.Instance != null)
            {
                CrimpToolStripController.Instance.OnStripComplete += HandleStripComplete;
            }
            else
            {
                Debug.LogWarning("[StrippingViewController] No CrimpToolStripController found in the " +
                                  "scene - stripping won't be able to hand off to the crimping view.", this);
            }
        }

        void OnDestroy()
        {
            if (CrimpToolStripController.Instance != null)
                CrimpToolStripController.Instance.OnStripComplete -= HandleStripComplete;
        }

        /// <summary>Called by CrimpingStationInteractable/CableTypeSelectionUI - the new first stop
        /// when the player enters the station, instead of CrimpingViewController.EnterCrimpingView().</summary>
        public void EnterStrippingView()
        {
            if (InStrippingView) return;

            // Reuse CrimpingViewController's existing check + dialog rather than duplicating it -
            // no reason to let the player into the stripping view without the tool either.
            if (CrimpingViewController.Instance != null && CrimpingViewController.Instance.WarnIfNoTool())
                return;

            SetStrippingViewActive(true);
        }

        /// <summary>Backs out to the player camera without stripping anything. Wired to Escape
        /// below, and to a mobile Cancel/Back button's OnClick via RequestExitStrippingView().</summary>
        public void ExitStrippingView()
        {
            if (!InStrippingView) return;

            if (!CanCancel())
            {
                Debug.Log("[StrippingViewController] Can't back out mid-strip - the animation is playing.", this);
                return;
            }

            SetStrippingViewActive(false);
        }

        /// <summary>Wire this to the mobile Cancel/Back button's OnClick.</summary>
        public void RequestExitStrippingView()
        {
            // Leaving half-way sends the player back to stripping on the way in, with the cable already
            // stripped - a dead end. See CrimpingStationController.Lock Player In Until Crimped.
            if (CrimpingStationController.ExitLocked)
            {
                Debug.Log("[StrippingViewController] Leaving is locked until the cable is crimped.", this);
                return;
            }

            ExitStrippingView();
        }

        bool CanCancel()
        {
            var strip = CrimpToolStripController.Instance;
            return strip == null || !strip.IsStripping;
        }

        void Update()
        {
            if (InStrippingView && !CrimpingStationController.ExitLocked && Input.GetKeyDown(KeyCode.Escape))
                ExitStrippingView();
        }

        /// <summary>Fired once CrimpToolStripController finishes playing CrimpTool_Strip_UTP.
        /// SetStrippingViewActive(false) and CrimpingViewController.EnterCrimpingView() both run
        /// synchronously back-to-back here, before the next frame renders, so there's no visible
        /// flicker back to the player camera in between - crimpingToolVisual briefly toggles off
        /// then back on the same way, invisibly.</summary>
        void HandleStripComplete()
        {
            SetStrippingViewActive(false);
            CrimpingViewController.Instance?.EnterCrimpingView();
        }

        void SetStrippingViewActive(bool active)
        {
            InStrippingView = active;

            if (playerCamera != null) playerCamera.gameObject.SetActive(!active);
            if (strippingCamera != null) strippingCamera.gameObject.SetActive(active);

            if (crimpingToolVisual != null) crimpingToolVisual.SetActive(active);
            if (toolDraggable != null) toolDraggable.enabled = active;

            foreach (var script in playerControlScripts)
            {
                if (script != null) script.enabled = !active;
            }

            if (strippingUI != null) strippingUI.SetActive(active);

            foreach (var obj in mobileControlsToHide)
            {
                if (obj != null) obj.SetActive(!active);
            }
            foreach (var obj in mobileStrippingControls)
            {
                if (obj != null) obj.SetActive(active);
            }

            // No Cancel / Exit button while leaving is locked.
            if (active) CrimpingStationController.HideExitButtons(mobileStrippingControls, strippingUI);

            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;
        }
    }
}