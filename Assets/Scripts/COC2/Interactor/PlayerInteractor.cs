using UnityEngine;
using TMPro;

namespace CSSSimulation.Interaction
{
    /// <summary>
    /// Attach this to the player. Each Interactable's own trigger reports in when the
    /// player enters/exits its range (see Interactable.cs) — this script just remembers
    /// whichever one is currently closest/active, shows a prompt, and fires Interact()
    /// on key press.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public static PlayerInteractor Instance { get; private set; }

        [Header("Input")]
        public KeyCode interactKey = KeyCode.E;

        [Header("Prompt UI (optional)")]
        [Tooltip("A simple UI panel/object showing the prompt. Enabled/disabled automatically.")]
        public GameObject promptUI;
        [Tooltip("Optional — assign a TMP_Text component here if you want the prompt to show " +
                  "each Interactable's own promptText. Leave empty to just show/hide promptUI as-is.")]
        public TMP_Text promptLabel;

        [Header("Mobile Button (optional)")]
        [Tooltip("On-screen interact button, mirrors the COC1 PlayerInteractor pattern. Wire its " +
                  "OnClick to RequestInteract(). Auto shown/hidden based on whether an Interactable " +
                  "is currently in range - leave unassigned for a PC-only build.")]
        public UnityEngine.UI.Button interactButton;

        Interactable current;
        bool interactRequested;

        void Awake()
        {
            Instance = this;
            if (promptUI != null) promptUI.SetActive(false);
            if (interactButton != null) interactButton.gameObject.SetActive(false);
        }

        public void SetNearbyInteractable(Interactable interactable)
        {
            current = interactable;
            if (promptUI != null) promptUI.SetActive(true);
            if (promptLabel != null) promptLabel.text = interactable.promptText;
            if (interactButton != null) interactButton.gameObject.SetActive(true);
        }

        public void ClearNearbyInteractable(Interactable interactable)
        {
            if (current != interactable) return; // a different interactable is still active — don't clear it
            current = null;
            if (promptUI != null) promptUI.SetActive(false);
            if (interactButton != null) interactButton.gameObject.SetActive(false);
        }

        /// <summary>Wire this to the mobile Interact button's OnClick.</summary>
        public void RequestInteract()
        {
            interactRequested = true;
        }

        void Update()
        {
            // Don't let a mobile tap or stray key press reach the world while a tutorial
            // popup is open, same guard the COC1 PlayerInteractor uses.
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
            {
                interactRequested = false;
                return;
            }

            if (current == null) return;

            bool pressed = Input.GetKeyDown(interactKey) || interactRequested;
            interactRequested = false;

            if (pressed)
                current.Interact();
        }
    }//
}