using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The "which cable am I building?" prompt shown right before the player steps into
    /// the close-up crimping view. Two buttons let them choose Straight-Through or
    /// Crossover; whichever they pick becomes CrimpingStationController.targetCableType
    /// for that attempt, then the crimping view opens.
    ///
    /// Cursor handling: this panel opens while the player camera is still active (the
    /// crimping view hasn't been entered yet), so the cursor is normally locked for
    /// look-around at that point. Open() unlocks/shows it so the buttons are clickable;
    /// Choose() hands off to CrimpingViewController, which locks/unlocks the cursor
    /// itself from then on. Cancel() (wired to Escape) re-locks it, since backing out
    /// returns control to the player camera.
    ///
    /// IMPORTANT  this component itself must live on a GameObject that starts ACTIVE
    /// in the scene (Awake() never runs on a disabled object, so it can't be the thing
    /// that gets hidden). Put it on a small always-active "manager" object (or straight
    /// on the Canvas), and point the `panel` field at the actual button panel, which is
    /// the thing that starts disabled.
    ///
    /// Setup:
    ///   1. Create an empty GameObject (e.g. "CableTypeSelectionUI") that stays active,
    ///      and add this script to it.
    ///   2. Under your Canvas, build the actual button panel (e.g. "CableChoicePanel")
    ///      with the two buttons as children  set THIS object inactive by default in
    ///      the Inspector (the checkbox next to its name at the top).
    ///   3. Drag "CableChoicePanel" into this script's Panel field.
    ///   4. Assign straightThroughCableType and crossoverCableType  drag in the
    ///      StraightThrough_Cable and CrossOver_Cable assets from Cable_Match/.
    ///   5. Assign station  the CrimpingStationController for this cable.
    ///   6. Wire the Straight-Through button's OnClick to ChooseStraightThrough(),
    ///      and the Crossover button's OnClick to ChooseCrossover(). Optionally wire a
    ///      third "Back"/"Cancel" button to Cancel() for a non-Escape way out (handy on
    ///      mobile where there's no Escape key).
    ///   7. On CrimpingStationInteractable, assign this component to its
    ///      cableTypeSelection field  Interact() will open this panel instead of
    ///      jumping straight into the crimping view.
    /// </summary>
    public class CableTypeSelectionUI : MonoBehaviour
    {
        [Header("Cable Types")]
        public CableTypeSO straightThroughCableType;
        public CableTypeSO crossoverCableType;

        [Header("Refs")]
        public CrimpingStationController station;

        [Header("Panel")]
        [Tooltip("The panel GameObject holding the two buttons, set inactive by default in the scene. " +
                 "Must be assigned in the Inspector  this script's own GameObject needs to stay active " +
                 "(Awake doesn't run on disabled objects), so it can't default to itself.")]
        public GameObject panel;

        [Tooltip("Closes the panel without picking a cable, and hands the player back their legs. " +
                 "Its On Click is wired for you at Awake - do not hook anything in the Inspector, " +
                 "or Cancel runs twice.\n\n" +
                 "This is the ONLY way off the panel on a touch build: the Escape key below needs a " +
                 "keyboard, so without this button a phone player who opens the choice by accident " +
                 "is stuck looking at it.")]
        public Button closeButton;

        [Header("While the panel is open")]
        [Tooltip("Switched off while the choice is on screen, and back on when it closes.\n\n" +
                 "Drag the player's MOVEMENT script and MOUSE-LOOK script in here. A player who can " +
                 "walk away while the panel is up leaves it floating over the room, and one who can " +
                 "still look around is fighting a free cursor with a camera that keeps turning.")]
        public MonoBehaviour[] freezeWhileOpen = new MonoBehaviour[0];

        [Tooltip("Hidden while the choice is on screen - the crosshair, the mobile joystick and " +
                 "look field, the Interact button. Shown again when it closes.")]
        public GameObject[] hideWhileOpen = new GameObject[0];

        [Tooltip("Free the mouse cursor while the panel is up and re-lock it afterwards.\n\n" +
                 "Untick only if something else in your scene already owns the cursor - two scripts " +
                 "writing Cursor.lockState is how you end up with a mouse that never comes back.")]
        public bool manageCursor = true;

        [Tooltip("One line in the Console each time the panel opens. Cheap, and the fastest way to " +
                 "tell 'the press never arrived' from 'it opened and something closed it'.")]
        public bool logOpen = true;

        public bool IsOpen { get; private set; }

        bool reportedVanish;

        void Awake()
        {
            // Wired first, before any of the checks below can return early. A close button that
            // stops working because the panel is misconfigured would take away the one control
            // that lets the player back out of the mess.
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Cancel);
                closeButton.onClick.AddListener(Cancel);
            }

            if (panel == null)
            {
                Debug.LogError("[CableTypeSelectionUI] Panel is not assigned in the Inspector. " +
                                "Drag your button panel GameObject into the Panel field.", this);
                return;
            }

            // A condition that can never be correct, so it is worth refusing rather than obeying.
            // With Panel pointing at this same object, the first Open() switches the object on,
            // which runs this Awake for the first time, which switches it straight back off - so
            // the panel appears only on the SECOND press, and nothing anywhere explains why.
            if (panel == gameObject)
            {
                Debug.LogError("[CableTypeSelectionUI] Panel is this component's OWN GameObject. " +
                               "That makes the first Open() switch the object on, run this Awake, " +
                               "and switch it off again - so the panel needs pressing twice. Move " +
                               "this component to an empty always-active object and point Panel at " +
                               "the button panel.", this);
                return;
            }

            panel.SetActive(false);
        }

        void Update()
        {
            // Desktop-only bail-out  lets the player back out to the player camera
            // without picking a cable type. Mobile players use a wired-up Cancel button
            // instead (see the class summary).
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
                Cancel();
        }

        void Start()
        {
            if (closeButton == null)
                Debug.LogWarning("[CableTypeSelectionUI] Close Button is empty. Escape still backs " +
                                 "out on a keyboard, but on a touch build there is no way off this " +
                                 "panel at all - make one and drag it in.", this);
            else if (closeButton.onClick.GetPersistentEventCount() > 0)
                Debug.LogWarning("[CableTypeSelectionUI] Close Button has something wired into its " +
                                 "On Click in the Inspector as well. This component adds its own " +
                                 "listener, so Cancel would run twice - clear the On Click list.", this);

            if (freezeWhileOpen.Length == 0)
                Debug.LogWarning("[CableTypeSelectionUI] Freeze While Open is empty, so the player " +
                                 "can still walk and look around with the cable choice on screen. " +
                                 "Drag the player's movement and mouse-look scripts in.", this);

            foreach (MonoBehaviour mb in freezeWhileOpen)
                if (mb == null)
                    Debug.LogWarning("[CableTypeSelectionUI] Freeze While Open has an EMPTY element. " +
                                     "An empty slot looks filled in at a glance and does nothing at " +
                                     "all - that control is not being switched off.", this);
        }

        /// <summary>Called by CrimpingStationInteractable instead of entering the crimping view directly.</summary>
        public void Open()
        {
            if (panel == null)
            {
                Debug.LogError("[CableTypeSelectionUI] Can't open  Panel is not assigned in the Inspector.", this);
                return;
            }

            bool wasActive = panel.activeSelf;

            IsOpen = true;

            // Before the panel goes up, not after. The player is still driving a first-person
            // controller at this instant; freeing the cursor without stopping the camera first
            // means one frame of the view swinging round under a mouse that is no longer steering it.
            HoldPlayer(true);

            panel.SetActive(true);

            // Checked immediately AFTER the call, because SetActive runs Awake and OnEnable
            // synchronously - so anything that switches the panel back off does it before this line
            // and the panel is already closed again by the time Open() returns. "It opened" and
            // "it is on screen" are different claims, and only the second one matters.
            if (!panel.activeSelf)
            {
                Debug.LogError("[CableTypeSelectionUI] '" + panel.name + "' was switched on and " +
                               "something switched it straight back OFF inside the same call. That " +
                               "is why the panel needs a second press. Usual cause: this component " +
                               "sits on the panel itself, so SetActive ran its Awake for the first " +
                               "time and Awake closes the panel.", this);
            }
            else if (!panel.activeInHierarchy)
            {
                Debug.LogError("[CableTypeSelectionUI] '" + panel.name + "' is switched on but a " +
                               "PARENT of it is not, so it cannot render. Walk up its hierarchy - " +
                               "the Canvas is the usual culprit - and switch the parent on. Nothing " +
                               "a panel does to itself can overcome a parent that is off.", this);
            }
            else if (logOpen)
            {
                Debug.Log("[CableTypeSelectionUI] Opened '" + panel.name + "'" +
                          (wasActive ? " (it was already showing)" : "") + ".", this);
            }

            // Player camera is still active at this point (crimping view hasn't opened
            // yet), so the cursor would otherwise still be locked for look-around.
            if (manageCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>
        /// Stops or restarts the player. One method for both directions, so a control switched off
        /// on the way in cannot be forgotten on the way out - which is the failure that leaves a
        /// learner unable to move with no panel on screen to explain why.
        /// </summary>
        void HoldPlayer(bool held)
        {
            foreach (MonoBehaviour mb in freezeWhileOpen)
                if (mb != null && mb != this) mb.enabled = !held;

            foreach (GameObject go in hideWhileOpen)
                if (go != null && go != gameObject) go.SetActive(!held);
        }

        /// <summary>
        /// Catches a panel closed by something else a frame or more after it opened.
        ///
        /// The check inside Open() only sees the same frame. A second component that hides the
        /// panel on its own Start, or a view controller switching a UI group off, lands later - and
        /// produces exactly the same symptom from the player's side.
        /// </summary>
        void LateUpdate()
        {
            if (!IsOpen || panel == null) return;
            if (panel.activeInHierarchy) { reportedVanish = false; return; }
            if (reportedVanish) return;

            reportedVanish = true;
            Debug.LogError("[CableTypeSelectionUI] '" + panel.name + "' is no longer on screen, but " +
                           "nothing here closed it - IsOpen is still true. Something else in the " +
                           "scene switched it, or a parent of it, off after it opened. Search the " +
                           "scene for other references to that object.", this);
        }

        /// <summary>Closes the panel without picking a cable type, re-locking the cursor
        /// since control is going back to the player camera. Wire to Escape/a Back button.</summary>
        public void Cancel()
        {
            IsOpen = false;

            // Restored BEFORE the panel is hidden. When this component lives on the panel itself,
            // deactivating it first would stop this method's own object mid-call in some Unity
            // versions - and a player left frozen with nothing on screen has no way to recover.
            HoldPlayer(false);

            if (panel != null) panel.SetActive(false);

            if (manageCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>Wire the Straight-Through button's OnClick to this.</summary>
        public void ChooseStraightThrough() => Choose(straightThroughCableType);

        /// <summary>Wire the Crossover button's OnClick to this.</summary>
        public void ChooseCrossover() => Choose(crossoverCableType);

        void Choose(CableTypeSO cableType)
        {
            if (cableType == null)
            {
                Debug.LogError("[CableTypeSelectionUI] That cable type slot isn't assigned in the Inspector.", this);
                return;
            }

            if (station != null)
                station.SetTargetCableType(cableType);
            else
                Debug.LogWarning("[CableTypeSelectionUI] No CrimpingStationController assigned  choice won't be applied.", this);

            IsOpen = false;

            // Handed back rather than kept held. The stripping view disables its own list of player
            // scripts a moment later, so holding on here would mean two components owning the same
            // switch - and whichever released last would decide whether the player can move.
            HoldPlayer(false);

            if (panel != null) panel.SetActive(false);

            // StrippingViewController.EnterStrippingView() -> SetStrippingViewActive(true)
            // sets Cursor.lockState = None itself, so no cursor handling needed here -
            // it's just picking up where Open() left off. Goes to the stripping view first
            // now, not straight into the pin-arranging crimping view.
            if (StrippingViewController.Instance != null)
                StrippingViewController.Instance.EnterStrippingView();
        }
    }
}