// CableActionButton.cs
using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// A Plug or Drop button that appears only while it would actually do something.
    ///
    /// WHY VISIBILITY IS THE FEATURE
    ///
    /// On a keyboard these are F and G, and a key that does nothing costs nothing - the player
    /// presses it, nothing happens, they move on. A button is different: it occupies screen space,
    /// invites a tap, and a tap that does nothing reads as the game being broken rather than as the
    /// player being in the wrong place. So Plug is on screen only when there is a socket in reach,
    /// and Drop only when something is actually in hand.
    ///
    /// That also makes the button teach the task. A trainee who sees Plug light up as they bring
    /// the connector to the patch panel has learned where the cable is meant to go, without being
    /// told.
    ///
    /// ASKS, RATHER THAN BEING TOLD
    ///
    /// CableCarrier already knows which cable is held and whether a socket is in range - the same
    /// two facts its own Update tests before it offers the F key. This reads those, every frame,
    /// instead of keeping a copy that could disagree with them. There is nothing to keep in sync,
    /// and nothing to wire per cable: the server cable and the client cable share one button,
    /// because the player only ever holds one at a time.
    ///
    /// SETUP
    ///   1. Make a Plug button and a Drop button in your mobile controls canvas, both ACTIVE -
    ///      they hide themselves with a CanvasGroup on the first frame.
    ///   2. Add this component to each, and set Action.
    ///   3. Nothing else - no cable reference, no On Click.
    /// </summary>
    [DefaultExecutionOrder(250)]
    public class CableActionButton : MonoBehaviour
    {
        public enum Action
        {
            /// <summary>Seats the held end in the nearest socket it is allowed to enter.</summary>
            Plug,
            /// <summary>Puts the held end down where the player is standing.</summary>
            Drop
        }

        [Header("What this button does")]
        public Action action = Action.Plug;

        [Header("Button")]
        [Tooltip("Left empty, the Button on this GameObject is used - the normal case.")]
        public Button button;

        [Tooltip("What gets shown and hidden. Left empty, the button's own GameObject is used.")]
        public GameObject visualRoot;

        [Header("When to show it")]
        [Tooltip("Hide while a dialog is on screen. CableCarrier ignores the plug and drop keys " +
                 "then too, so a button left showing would be the one control that disagrees.")]
        public bool hideWhileDialogOpen = true;

        [Tooltip("Show only on a touch device. Leave OFF while building - a mouse can click it and " +
                 "the keys still work.")]
        public bool touchOnly = false;

        [Tooltip("Treat the Editor as a touch device, so Touch Only can be tested without a build.")]
        public bool showInEditor = true;

        [Header("Diagnostics")]
        public bool logPresses = true;

        bool wasVisible;
        CanvasGroup group;

        void Reset() { button = GetComponent<Button>(); }

        void Awake()
        {
            if (button == null) button = GetComponent<Button>();

            if (button != null)
            {
                button.onClick.RemoveListener(Press);
                button.onClick.AddListener(Press);
            }

            Show(false);
        }

        void Start() { ValidateSetup(); }

        void LateUpdate()
        {
            bool visible = Wanted && WantedOnThisDevice;
            if (hideWhileDialogOpen && DialogManager.Instance != null &&
                DialogManager.Instance.IsDialogOpen)
                visible = false;

            if (visible != wasVisible) Show(visible);
        }

        /// <summary>Whether this action would do anything right now.</summary>
        public bool Wanted
        {
            get
            {
                CableCarrier held = CableCarrier.Held;
                if (held == null) return false;

                // Drop only needs something in hand. Plug needs somewhere for it to go, which is
                // the same test CableCarrier makes before it offers the plug key.
                return action == Action.Drop || held.SocketInReach;
            }
        }

        /// <summary>Runs the action. Public, so a hardware button can call it too.</summary>
        public void Press()
        {
            CableCarrier held = CableCarrier.Held;
            if (held == null) return;

            if (action == Action.Drop)
            {
                if (logPresses) Debug.Log("[CableActionButton] Dropping '" + held.cableName + "'.", this);
                held.Drop();
                return;
            }

            if (!held.SocketInReach)
            {
                // CableCarrier writes its own "nothing in reach" hint, so this stays out of the
                // player's way and only says it where a developer would look.
                if (logPresses)
                    Debug.Log("[CableActionButton] Plug pressed with no socket in reach.", this);
            }

            if (logPresses) Debug.Log("[CableActionButton] Plugging in '" + held.cableName + "'.", this);
            held.PlugNow();
        }

        // ---------------------------------------------------------------- plumbing

        bool WantedOnThisDevice
        {
            get
            {
                if (!touchOnly) return true;
#if UNITY_EDITOR
                if (showInEditor) return true;
#endif
                return Application.isMobilePlatform;
            }
        }

        /// <summary>
        /// Hides by fading the button out rather than switching its GameObject off.
        ///
        /// A button that hides itself with SetActive can never come back: Unity does not run Awake,
        /// Update or LateUpdate on a deactivated object, so the component whose job is to show it
        /// again is the one component that has stopped running. A CanvasGroup at alpha 0 with
        /// raycasts off is invisible and untappable while leaving the component alive to keep
        /// watching.
        /// </summary>
        void Show(bool visible)
        {
            wasVisible = visible;

            GameObject target = visualRoot != null ? visualRoot
                                                   : (button != null ? button.gameObject : gameObject);

            if (target != gameObject)
            {
                if (target.activeSelf != visible) target.SetActive(visible);
                return;
            }

            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        /// <summary>
        /// Switches on any cable button a scene left inactive.
        ///
        /// These hide themselves with a CanvasGroup, which means the GameObject has to be active
        /// for them to work at all - and a button authored switched off has no code of its own
        /// running to notice. Something outside has to look, exactly as MobileControlsVisibility
        /// does for the touch controls.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RevealAll()
        {
            CableActionButton[] found = Object.FindObjectsByType<CableActionButton>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (CableActionButton b in found)
            {
                if (b == null || b.gameObject.activeSelf) continue;
                b.gameObject.SetActive(true);
                Debug.Log("[CableActionButton] '" + b.name + "' was switched off in this scene - " +
                          "turned on, because it hides itself and cannot do that while inactive.", b);
            }
        }

        void ValidateSetup()
        {
            if (button == null)
            {
                Debug.LogWarning("[CableActionButton] '" + name + "' has no Button, so nothing can " +
                                 "be tapped. Put this component on the button itself.", this);
                return;
            }

            if (button.onClick.GetPersistentEventCount() > 0)
                Debug.LogWarning("[CableActionButton] '" + name + "' has something wired into its " +
                                 "On Click as well. This component adds its own listener, so the " +
                                 "action would run twice - clear the On Click list.", this);

            CableCarrier[] cables = Object.FindObjectsByType<CableCarrier>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (cables.Length == 0)
            {
                Debug.LogWarning("[CableActionButton] '" + name + "' is set to " + action +
                                 ", but there is no CableCarrier in this scene, so it can never " +
                                 "appear.", this);
                return;
            }

            Debug.Log("[CableActionButton] '" + name + "' = " + action + ", covering " +
                      cables.Length + " cable(s)" + (touchOnly ? ", on touch devices only" : "") +
                      ".", this);
        }

        /// <summary>Prints what is held and whether this button should be showing.</summary>
        [ContextMenu("Report cable button")]
        public void LogState()
        {
            CableCarrier held = CableCarrier.Held;
            Debug.LogWarning("[CableActionButton] '" + name + "'" +
                             "\n    action        : " + action +
                             "\n    in hand       : " + (held != null ? "'" + held.cableName + "'" : "nothing") +
                             "\n    socket in reach: " + (held != null && held.SocketInReach) +
                             "\n    showing       : " + wasVisible, this);
        }
    }
}