using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Handles switching between normal 3D player movement and the close-up crimping view.
    /// Swaps active cameras, disables player movement/look while in the crimping view, and hands
    /// the active camera over to WireDragController so dragging lines up with what's on screen.
    ///
    /// WHAT CHANGED: this stage now has two jobs, not one. The player still arranges the 8 wires
    /// per end, but they also crimp each end here by dragging the crimping tool onto it - so this
    /// controller now enables CrimpToolCrimpDraggable alongside WireDragController, and disables
    /// it again on the way out. Both draggables are live at the same time; each one refuses to
    /// start while the other is mid-drag, so they don't fight over the pointer.
    /// </summary>
    public class CrimpingViewController : MonoBehaviour
    {
        public static CrimpingViewController Instance { get; private set; }

        [Header("Crimping Tool")]
        [Tooltip("The crimping tool's visual sitting at the station itself. Hidden by default - " +
                 "pops into view here the moment the player enters the crimping view (they already " +
                 "have the tool 'in their pocket' at that point, see CrimpingToolInventory/ " +
                 "CrimpingStationInteractable). Purely a visual toggle, no hand attachment involved.")]
        public GameObject crimpingToolVisual;

        [Tooltip("The CrimpToolCrimpDraggable on crimpingToolVisual - the component that lets the " +
                 "player drag the tool onto a connector end to crimp it. Enabled only while in this " +
                 "view, exactly the way StrippingViewController enables CrimpToolDraggable for its " +
                 "own stage. Leave it UNTICKED in the Inspector; this turns it on.")]
        public CrimpToolCrimpDraggable crimpToolDraggable;

        [Tooltip("Shown once the player is in the crimping view if they haven't picked up the " +
                 "crimping tool yet. They can still see the station, just no tool on it.")]
        public DialogSequence noToolDialog;

        [Header("Cameras")]
        [Tooltip("The player's normal 3D camera (first/third person).")]
        public Camera playerCamera;
        [Tooltip("A separate camera framed close-up on the connector. Set it to Orthographic " +
                 "for a flatter, more 2D feel - Perspective also works, just frame it tight and locked.")]
        public Camera crimpingCamera;

        [Header("Player control")]
        [Tooltip("Whatever script(s) handle player movement/look - assign so they get disabled while crimping.")]
        public MonoBehaviour[] playerControlScripts;

        [Header("UI")]
        [Tooltip("e.g. an 'ESC to exit' prompt, progress panel - anything specific to the crimping view.")]
        public GameObject crimpingUI;

        [Header("Mobile")]
        [Tooltip("Mobile controls hidden while in the crimping view - the joystick, touch-look " +
                 "field, and any other station's on-screen buttons. Without this, the invisible " +
                 "full-screen touch-look panel sits on top of the wires and clutters/steals taps " +
                 "meant for dragging them (same class of bug as StationCameraSwitch's disable array).")]
        public GameObject[] mobileControlsToHide;
        [Tooltip("Mobile controls shown only while in the crimping view - e.g. a Crimp button and " +
                 "an Exit button, since there's no on-screen Escape key on Android.")]
        public GameObject[] mobileCrimpingControls;

        public bool InCrimpingView { get; private set; }

        void Awake()
        {
            Instance = this;
            SetCrimpingViewActive(false);
        }

        void Start()
        {
            // Convenience: if the drag camera wasn't set on the tool's crimp draggable, point it
            // at this view's camera. Saves one of the easier Inspector fields to forget, and a
            // null dragCamera makes the tool silently un-grabbable with no error.
            if (crimpToolDraggable != null && crimpToolDraggable.dragCamera == null)
                crimpToolDraggable.dragCamera = crimpingCamera;
        }

        public void EnterCrimpingView()
        {
            if (InCrimpingView) return;
            SetCrimpingViewActive(true);

            WarnIfNoTool();
        }

        /// <summary>
        /// Shows the "no tool yet" warning dialog if the player doesn't have the crimping tool.
        /// Centralized here so EnterCrimpingView(), StrippingViewController.EnterStrippingView()
        /// and WireDraggable.TryBeginDrag() all show the exact same dialog instead of each caller
        /// keeping its own copy of the hasTool check. Returns true if the tool is missing --
        /// callers should bail out of whatever they were about to do when this returns true.
        /// </summary>
        public bool WarnIfNoTool()
        {
            bool hasTool = CrimpingToolInventory.Instance != null && CrimpingToolInventory.Instance.HasTool;
            if (hasTool) return false;

            if (noToolDialog != null && DialogManager.Instance != null)
            {
                // Don't stack a second ShowDialog call on top of one already open -- e.g. the
                // player tapping several wires in a row while the first warning is still up.
                if (!DialogManager.Instance.IsDialogOpen)
                    DialogManager.Instance.ShowDialog(noToolDialog);
            }
            else
            {
                Debug.Log("[CrimpingViewController] Player doesn't have the crimping tool yet.", this);
            }

            return true;
        }

        public void ExitCrimpingView()
        {
            if (!InCrimpingView) return;
            SetCrimpingViewActive(false);
        }

        /// <summary>Wire this to the mobile Exit button's OnClick - there's no Escape key on Android.</summary>
        public void RequestExitCrimpingView()
        {
            ExitCrimpingView();
        }

        void Update()
        {
            if (!InCrimpingView) return;

            // Don't let Escape yank the camera away mid-crimp - the animation owns the tool and
            // the connector visuals at that point. Same guard StrippingViewController.CanCancel()
            // applies during the strip clip.
            if (CrimpToolCrimpController.Instance != null && CrimpToolCrimpController.Instance.IsCrimping)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
                ExitCrimpingView();
        }

        void SetCrimpingViewActive(bool active)
        {
            InCrimpingView = active;

            if (playerCamera != null) playerCamera.gameObject.SetActive(!active);
            if (crimpingCamera != null) crimpingCamera.gameObject.SetActive(active);

            bool hasTool = CrimpingToolInventory.Instance != null && CrimpingToolInventory.Instance.HasTool;
            bool showTool = active && hasTool;

            if (crimpingToolVisual != null)
            {
                crimpingToolVisual.SetActive(showTool);
                if (showTool) Debug.Log("[CrimpingViewController] Crimping tool popped up at the station.");
            }

            // The tool is only draggable-for-crimping inside this view, and only if the player
            // actually has it. Outside this view it stays off so the tool can't be grabbed while
            // the player is just standing at the station, or during the stripping stage (where
            // CrimpToolDraggable is the one that's live instead).
            if (crimpToolDraggable != null) crimpToolDraggable.enabled = showTool;

            foreach (var script in playerControlScripts)
            {
                if (script != null) script.enabled = !active;
            }

            if (crimpingUI != null) crimpingUI.SetActive(active);

            foreach (var obj in mobileControlsToHide)
            {
                if (obj != null) obj.SetActive(!active);
            }
            foreach (var obj in mobileCrimpingControls)
            {
                if (obj != null) obj.SetActive(active);
            }

            if (WireDragController.Instance != null)
            {
                WireDragController.Instance.worldCamera = active ? crimpingCamera : playerCamera;
                WireDragController.Instance.enabled = active;
            }

            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;
        }
    }
}
