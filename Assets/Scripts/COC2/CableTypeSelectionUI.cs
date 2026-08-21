using UnityEngine;

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

        public bool IsOpen { get; private set; }

        void Awake()
        {
            if (panel == null)
            {
                Debug.LogError("[CableTypeSelectionUI] Panel is not assigned in the Inspector. " +
                                "Drag your button panel GameObject into the Panel field.", this);
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

        /// <summary>Called by CrimpingStationInteractable instead of entering the crimping view directly.</summary>
        public void Open()
        {
            if (panel == null)
            {
                Debug.LogError("[CableTypeSelectionUI] Can't open  Panel is not assigned in the Inspector.", this);
                return;
            }

            IsOpen = true;
            panel.SetActive(true);

            // Player camera is still active at this point (crimping view hasn't opened
            // yet), so the cursor would otherwise still be locked for look-around.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Closes the panel without picking a cable type, re-locking the cursor
        /// since control is going back to the player camera. Wire to Escape/a Back button.</summary>
        public void Cancel()
        {
            IsOpen = false;
            if (panel != null) panel.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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