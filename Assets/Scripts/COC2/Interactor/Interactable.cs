using UnityEngine;

namespace CSSSimulation.Interaction
{
    /// <summary>
    /// Base class for anything in the world the player can walk up to and press E on.
    /// Attach to an object with a trigger collider sized to the interaction range, and
    /// make sure the player's collider/root object is tagged "Player".
    ///
    /// This is deliberately generic — the crimping station is the first thing using it,
    /// but any future station (server rack, cable tester, etc.) can subclass this too.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class Interactable : MonoBehaviour
    {
        [Tooltip("Text shown in the interact prompt, e.g. 'Press E to crimp cable'.")]
        public string promptText = "Press E to interact";

        public bool IsPlayerInRange { get; private set; }

        void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            IsPlayerInRange = true;
            if (PlayerInteractor.Instance != null)
                PlayerInteractor.Instance.SetNearbyInteractable(this);
        }

        void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            IsPlayerInRange = false;
            if (PlayerInteractor.Instance != null)
                PlayerInteractor.Instance.ClearNearbyInteractable(this);
        }

        /// <summary>Called when the player presses the interact key while this is the active nearby interactable.</summary>
        public abstract void Interact();
    }
}