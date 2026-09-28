using System.Collections;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The missing third stage for a 3-camera COC2 station.
    ///
    /// The original code only had two views: StrippingViewController (strip the jacket) and
    /// CrimpingViewController (arrange the 8 wires into their pins). The crimp itself happened
    /// inside that second view, on the same camera. If your station has a SEPARATE camera for the
    /// crimping action, nothing ever switched to it - this is what does.
    ///
    /// New flow:
    ///     StrippingViewController   camera 1 - drag the tool onto the cable, jacket comes off
    ///     CrimpingViewController    camera 2 - arrange all 8 wires on both ends   ("wiring")
    ///     THIS                      camera 3 - drag the tool onto each end, crimp animation x2
    ///     TestingViewController     camera 4 - plug into the tester, LED wiremap
    ///
    /// It watches the two ConnectorEnds itself and takes over the moment all 16 pins are seated,
    /// so NO existing script needs editing - this is add-and-wire-up only.
    ///
    /// Setup:
    ///   1. Add this to the same object that holds CrimpingViewController and
    ///      StrippingViewController.
    ///   2. Assign playerCamera and crimpCamera (your THIRD camera, the crimping close-up).
    ///   3. Assign crimpingToolVisual and toolDraggable.
    ///   4. On CrimpingViewController, CLEAR the "Crimp Tool Draggable" field. That field made the
    ///      tool draggable during wiring, which is exactly what you don't want now - this script
    ///      owns the draggable instead.
    ///   5. On CrimpToolCrimpDraggable, set Drag Camera to crimpCamera (camera 3), NOT the wiring
    ///      camera. Do this explicitly - CrimpingViewController auto-fills that field with the
    ///      wiring camera if it's left empty.
    ///   6. Point CrimpToolCrimpController's toolDropAnchors at poses framed by camera 3.
    /// </summary>
    public class CrimpStageViewController : MonoBehaviour
    {
        public static CrimpStageViewController Instance { get; private set; }

        [Header("Cameras")]
        [Tooltip("The player's normal 3D camera - the same reference the other view controllers use.")]
        public Camera playerCamera;
        [Tooltip("Your THIRD camera: the crimping close-up, framed on the connector ends where the " +
                 "crimp animation plays. Not the wiring camera.")]
        public Camera crimpCamera;

        [Header("Crimping Tool")]
        [Tooltip("The tool object the player drags. Same GameObject the other stages use.")]
        public GameObject crimpingToolVisual;
        [Tooltip("The CrimpToolCrimpDraggable on that object. Enabled only while this view is open - " +
                 "so make sure CrimpingViewController's own Crimp Tool Draggable field is EMPTY, or " +
                 "the tool stays grabbable during wiring too.")]
        public CrimpToolCrimpDraggable toolDraggable;

        [Header("Player control")]
        [Tooltip("Same array you already filled in on CrimpingViewController.")]
        public MonoBehaviour[] playerControlScripts;

        [Header("UI")]
        [Tooltip("Shown only in this view - e.g. a 'drag the crimping tool onto each connector' prompt.")]
        public GameObject crimpUI;
        [Tooltip("Hidden when this view opens - anything left over from the wiring stage that isn't " +
                 "already handled by CrimpingViewController.crimpingUI.")]
        public GameObject[] wiringStageObjects;

        [Header("Mobile")]
        [Tooltip("Mobile controls hidden while in this view - joystick, touch-look field.")]
        public GameObject[] mobileControlsToHide;
        [Tooltip("Mobile controls shown only in this view - e.g. a Crimp button and a Back button.")]
        public GameObject[] mobileCrimpControls;

        [Header("Handoff")]
        [Tooltip("Enter this view automatically the moment all 16 pins are seated. Untick to drive it " +
                 "yourself from a button wired to EnterCrimpStage().")]
        public bool autoEnterWhenBothEndsWired = true;
        [Tooltip("Seconds waited after the last wire lands before the camera switches, so the player " +
                 "sees the wire settle instead of being cut away instantly.")]
        public float entryDelay = 0.4f;

        [Header("Back out")]
        [Tooltip("What Escape (and RequestBack()) does in this view: tick to go back to the wiring " +
                 "camera so the player can rearrange, untick to drop straight to the player camera.")]
        public bool escapeReturnsToWiring = true;

        public bool InCrimpView { get; private set; }

        // Goes false the moment we auto-enter, and re-arms only when a wire is pulled back out.
        // Without this, backing out to wiring would instantly re-trigger the entry, because all 16
        // pins are of course still seated.
        bool autoEnterArmed = true;
        bool entering;

        void Awake()
        {
            Instance = this;
            SetCrimpViewActive(false, true);
        }

        void Start()
        {
            if (CrimpingStationController.Instance != null)
            {
                CrimpingStationController.Instance.OnBothEndsCrimped += HandleBothEndsCrimped;
            }
            else
            {
                Debug.LogWarning("[CrimpStageViewController] No CrimpingStationController in the scene - " +
                                 "this stage can't tell when wiring is finished.", this);
            }

            // The single most common way to wire this stage up wrong: leaving the draggable pointed at
            // the wiring camera, which makes the tool impossible to grab because the raycast comes from
            // a camera that isn't rendering.
            if (toolDraggable != null && crimpCamera != null && toolDraggable.dragCamera != crimpCamera)
            {
                Debug.LogWarning($"[CrimpStageViewController] CrimpToolCrimpDraggable.dragCamera is " +
                                 $"'{(toolDraggable.dragCamera == null ? "empty" : toolDraggable.dragCamera.name)}' " +
                                 $"but this stage renders with '{crimpCamera.name}'. Setting it to match - " +
                                 "assign it in the Inspector to silence this.", this);
                toolDraggable.dragCamera = crimpCamera;
            }

            if (CrimpingViewController.Instance != null &&
                CrimpingViewController.Instance.crimpToolDraggable != null)
            {
                Debug.LogWarning("[CrimpStageViewController] CrimpingViewController.crimpToolDraggable is " +
                                 "assigned. Clear that field - it makes the crimping tool draggable during " +
                                 "the WIRING stage, which this stage is meant to replace.", this);
            }
        }

        void OnDestroy()
        {
            if (CrimpingStationController.Instance != null)
                CrimpingStationController.Instance.OnBothEndsCrimped -= HandleBothEndsCrimped;
        }

        void Update()
        {
            if (InCrimpView)
            {
                // Never yank the camera away mid-animation.
                if (CrimpToolCrimpController.Instance != null && CrimpToolCrimpController.Instance.IsCrimping)
                    return;

                if (!CrimpingStationController.ExitLocked && Input.GetKeyDown(KeyCode.Escape))
                    RequestBack();

                return;
            }

            if (!autoEnterWhenBothEndsWired || entering) return;

            bool bothWired = BothEndsFullyWired();

            // A wire came back out - re-arm so finishing again re-triggers the stage.
            if (!bothWired)
            {
                autoEnterArmed = true;
                return;
            }

            if (!autoEnterArmed) return;

            // Only take over FROM the wiring view. If the player is standing in the world, or still
            // stripping, this isn't our moment.
            if (CrimpingViewController.Instance == null || !CrimpingViewController.Instance.InCrimpingView) return;

            var station = CrimpingStationController.Instance;
            if (station != null && station.IsCrimped) return; // already finished this cable

            autoEnterArmed = false;
            StartCoroutine(EnterAfterDelay());
        }

        /// <summary>True when all 8 pins are seated on BOTH ends. Deliberately checks the ConnectorEnds
        /// directly rather than relying on an event, so no existing script needs modifying.</summary>
        public bool BothEndsFullyWired()
        {
            var station = CrimpingStationController.Instance;
            if (station == null || station.endOne == null || station.endTwo == null) return false;
            return station.endOne.IsFullyArranged() && station.endTwo.IsFullyArranged();
        }

        IEnumerator EnterAfterDelay()
        {
            entering = true;
            if (entryDelay > 0f) yield return new WaitForSeconds(entryDelay);
            EnterCrimpStage();
            entering = false;
        }

        /// <summary>Also safe to wire to a "Ready to crimp" button if you untick autoEnterWhenBothEndsWired.</summary>
        public void EnterCrimpStage()
        {
            if (InCrimpView) return;

            if (!BothEndsFullyWired())
            {
                Debug.Log("[CrimpStageViewController] All 8 pins on both ends need a wire before crimping.", this);
                return;
            }

            // Reuse the existing "do you actually have the tool" check rather than duplicating it.
            if (CrimpingViewController.Instance != null && CrimpingViewController.Instance.WarnIfNoTool())
                return;

            // Close the wiring view and open this one back-to-back in the SAME frame, so the player
            // camera never flashes on in between - same trick StrippingViewController.HandleStripComplete
            // uses when it hands off.
            if (CrimpingViewController.Instance != null)
                CrimpingViewController.Instance.ExitCrimpingView();

            SetCrimpViewActive(true, false);
        }

        /// <summary>Escape, and the mobile Back button. Goes back to wiring by default so the player can
        /// still fix a mistake before committing to a crimp.</summary>
        public void RequestBack()
        {
            if (!InCrimpView) return;

            // While leaving is locked there is no Back from here either. Going back to wiring with all
            // sixteen pins still seated would leave the player in the wiring view with nothing to do and
            // no way out: this stage only re-opens once a wire is moved. See CrimpingStationController.
            // Lock Player In Until Crimped.
            if (CrimpingStationController.ExitLocked)
            {
                Debug.Log("[CrimpStageViewController] Back is locked until both ends are crimped.", this);
                return;
            }

            if (CrimpToolCrimpController.Instance != null && CrimpToolCrimpController.Instance.IsCrimping)
            {
                Debug.Log("[CrimpStageViewController] Can't back out mid-crimp - the animation is playing.", this);
                return;
            }

            var station = CrimpingStationController.Instance;
            if (station != null && (station.IsEndCrimped(1) || station.IsEndCrimped(2)))
            {
                Debug.Log("[CrimpStageViewController] One end is already crimped - there's no going back " +
                          "to wiring now. Finish the other end, or use ResetStation() to start over.", this);
                return;
            }

            if (escapeReturnsToWiring && CrimpingViewController.Instance != null)
            {
                SetCrimpViewActive(false, false);
                CrimpingViewController.Instance.EnterCrimpingView();
            }
            else
            {
                SetCrimpViewActive(false, true);
            }
        }

        void HandleBothEndsCrimped()
        {
            StartCoroutine(CloseAfterCrimping());
        }

        IEnumerator CloseAfterCrimping()
        {
            // Nothing left to drag - switch the tool off immediately even though the camera lingers.
            if (toolDraggable != null) toolDraggable.enabled = false;

            var testing = TestingViewController.Instance;
            bool testingTakesOver = testing != null && testing.autoEnterAfterCrimping;

            if (!testingTakesOver)
            {
                SetCrimpViewActive(false, true);
                yield break;
            }

            // TestingViewController waits out its own entryDelay before switching cameras. Hold this
            // camera up until its camera is actually live, otherwise there's a gap with no camera
            // rendering at all and the screen goes black.
            float timeout = 5f;
            while (!testing.InTestingView && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            SetCrimpViewActive(false, timeout <= 0f);
        }

        /// <summary>Clears this stage so a fresh cable can be crimped. Call alongside
        /// CrimpingStationController.ResetStation() from your Redo button.</summary>
        public void ResetForReplay()
        {
            StopAllCoroutines();
            entering = false;
            autoEnterArmed = true;
            if (InCrimpView) SetCrimpViewActive(false, true);
        }

        /// <param name="restorePlayerCamera">False when another view is taking the screen over, so the
        /// player camera doesn't flicker on for a frame in between.</param>
        void SetCrimpViewActive(bool active, bool restorePlayerCamera)
        {
            InCrimpView = active;

            if (playerCamera != null)
            {
                if (active) playerCamera.gameObject.SetActive(false);
                else if (restorePlayerCamera) playerCamera.gameObject.SetActive(true);
            }

            if (crimpCamera != null) crimpCamera.gameObject.SetActive(active);

            if (crimpingToolVisual != null) crimpingToolVisual.SetActive(active);
            if (toolDraggable != null) toolDraggable.enabled = active;

            foreach (var obj in wiringStageObjects)
            {
                if (obj != null && active) obj.SetActive(false);
            }

            foreach (var script in playerControlScripts)
            {
                if (script != null) script.enabled = !active;
            }

            if (crimpUI != null) crimpUI.SetActive(active);

            foreach (var obj in mobileControlsToHide)
            {
                if (obj != null) obj.SetActive(!active);
            }
            foreach (var obj in mobileCrimpControls)
            {
                if (obj != null) obj.SetActive(active);
            }

            // No Back button while leaving is locked. A Crimp button in the same list stays.
            if (active) CrimpingStationController.HideExitButtons(mobileCrimpControls, crimpUI);

            // Wires are finished being arranged - don't let a stray tap into the connector start a
            // drag from this camera.
            if (WireDragController.Instance != null && active)
                WireDragController.Instance.enabled = false;

            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;
        }
    }
}