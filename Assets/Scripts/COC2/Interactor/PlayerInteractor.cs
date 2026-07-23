using UnityEngine;

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
        [Tooltip("Optional — assign a UI Text component here if you want the prompt to show " +
                  "each Interactable's own promptText. Leave empty to just show/hide promptUI as-is.")]
        public UnityEngine.UI.Text promptLabel;

        Interactable current;

        void Awake()
        {
            Instance = this;
            if (promptUI != null) promptUI.SetActive(false);
        }

        public void SetNearbyInteractable(Interactable interactable)
        {
            current = interactable;
            if (promptUI != null) promptUI.SetActive(true);
            if (promptLabel != null) promptLabel.text = interactable.promptText;
        }

        public void ClearNearbyInteractable(Interactable interactable)
        {
            if (current != interactable) return; // a different interactable is still active — don't clear it
            current = null;
            if (promptUI != null) promptUI.SetActive(false);
        }

        void Update()
        {
            if (current == null) return;
            if (Input.GetKeyDown(interactKey))
                current.Interact();
        }
    }
}