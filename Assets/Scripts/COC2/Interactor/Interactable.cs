using UnityEngine;

namespace CSSSimulation.Interaction
{
    /// <summary>
    /// Base class for anything in the world the player can walk up to and operate.
    /// Attach to an object with a trigger collider sized to the interaction range, and
    /// make sure the player's collider/root object is tagged "Player".
    ///
    /// This is deliberately generic - the crimping station is the first thing using it,
    /// but any future station (server rack, cable tester, etc.) can subclass this too.
    ///
    /// TWO CONTROLS, NOT ONE
    ///
    /// Picking something up and operating a station are different actions, and running both off
    /// one key teaches the trainee nothing about either. Taking a tool off the bench is G (or the
    /// Pick Up button on touch); sitting down at a machine is E (or Interact). A component says
    /// which of the two it is by overriding DefaultChannel, so the right key and the right
    /// on-screen button appear without anything to tick per object.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>Which of the player's two controls operates this.</summary>
        public enum InputChannel
        {
            /// <summary>E, or the Interact button. Stations, panels, machines.</summary>
            Interact,
            /// <summary>G, or the Pick Up button. Anything that goes into the player's hands.</summary>
            Pickup
        }

        public enum ChannelSetting
        {
            /// <summary>Use whatever this component is - a pickup uses G, everything else uses E.</summary>
            Auto,
            UseInteract,
            UsePickup
        }

        [Tooltip("Text shown in the interact prompt.\n\n" +
                 "Write {key} where the control belongs - 'Press {key} to pick up the crimping " +
                 "tool' - and it becomes E, G, or whichever key is actually bound. That way " +
                 "rebinding the key cannot leave a prompt telling the player to press the wrong " +
                 "one. Prompts without {key} are shown exactly as typed.")]
        public string promptText = "Press {key} to interact";

        [Tooltip("Leave on Auto. A pickup then uses G and the Pick Up button, and everything else " +
                 "uses E and the Interact button, with nothing to set per object.\n\n" +
                 "The two overrides exist for the odd exception - a tool that is operated in place " +
                 "rather than carried, say - not for normal setup.")]
        public ChannelSetting inputChannel = ChannelSetting.Auto;

        public bool IsPlayerInRange { get; private set; }

        /// <summary>
        /// What this component is, before any per-object override. Interactables are stations by
        /// default; the three pickups override this.
        /// </summary>
        protected virtual InputChannel DefaultChannel { get { return InputChannel.Interact; } }

        /// <summary>The control that actually operates this object. Read by PlayerInteractor.</summary>
        public InputChannel Channel
        {
            get
            {
                if (inputChannel == ChannelSetting.UseInteract) return InputChannel.Interact;
                if (inputChannel == ChannelSetting.UsePickup) return InputChannel.Pickup;
                return DefaultChannel;
            }
        }

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

        /// <summary>Called when the player presses this object's control while it is the active
        /// nearby interactable.</summary>
        public abstract void Interact();
    }
}