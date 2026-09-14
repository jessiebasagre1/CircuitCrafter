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
    ///
    /// IN RANGE IS MEASURED, NOT REMEMBERED
    ///
    /// The trigger events below are fast but they are not reliable enough to be the only record of
    /// where the player is, and this project has now lost three separate evenings to that. A
    /// trigger event is a one-off notification: if nothing is listening at the instant it is sent,
    /// it is gone, and there is no second chance because the player never physically moved.
    ///
    /// All three of these produce exactly the same dead end - a station the player is standing
    /// right in front of that answers nothing:
    ///
    ///   - the station switches its own component off while its desktop is up, so the enter event
    ///     that would restore it arrives at a component that is not listening;
    ///   - something hides or deactivates the PLAYER for the duration of a view, which sends an
    ///     exit event on the way in and, depending on how the object comes back, no enter event on
    ///     the way out;
    ///   - a collider is toggled, re-parented or moved by a camera switch, which also counts as
    ///     leaving.
    ///
    /// So Interactable no longer trusts the events as the final word. It records what they say,
    /// and PlayerInteractor re-measures the real distance every frame and corrects this flag when
    /// the two disagree. The events remain the fast path; the measurement is the backstop that
    /// makes a stuck flag impossible instead of merely unlikely.
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

        [Header("Diagnostics")]
        [Tooltip("Say in the Console every time this object gains or loses the player, and print " +
                 "the state of everything involved at that instant.\n\n" +
                 "Leave this ON while a station is misbehaving. 'Why can I not interact with this " +
                 "any more' always comes down to one of a handful of things having quietly changed " +
                 "- the player object was switched off, a collider was disabled, the trigger moved " +
                 "with a camera - and the line printed here names which one it was, at the frame it " +
                 "happened. Untick it once the scene is behaving; it is one line per doorway.")]
        public bool logRangeChanges = true;

        public bool IsPlayerInRange { get; private set; }

        /// <summary>
        /// This object's trigger, cached.
        ///
        /// Fetched through a property rather than in Awake on purpose: subclasses in this project
        /// declare their own Awake, and a private Awake on a subclass HIDES one on the base class -
        /// Unity then calls only the subclass's, and the base class's would silently never run. A
        /// property cannot be hidden by accident.
        ///
        /// The backing field is named cachedTrigger rather than the obvious 'trigger' because
        /// CrimpedCablePickup already has a field by that name. Unity flattens the whole
        /// inheritance chain into one serialized record, so a base class and a subclass cannot both
        /// have a field called 'trigger' - it refuses the type outright rather than guessing which
        /// one a saved value belongs to. Marked NonSerialized as well, since this is a cache
        /// rebuilt on demand and has no business being written into the scene file.
        /// </summary>
        public Collider Trigger
        {
            get
            {
                if (cachedTrigger == null) cachedTrigger = GetComponent<Collider>();
                return cachedTrigger;
            }
        }

        [System.NonSerialized] Collider cachedTrigger;

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
            SetPlayerInRange(true, "its trigger reported the player entering");
        }

        void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            SetPlayerInRange(false, "its trigger reported the player leaving");
        }

        /// <summary>
        /// Records whether the player is at this object, and keeps PlayerInteractor's list in step.
        ///
        /// Public because the trigger events are no longer the only thing allowed to answer this
        /// question - PlayerInteractor re-measures the distance each frame and calls in here when
        /// the measurement disagrees with what the events left behind. Reason is what gets printed,
        /// so every change to this flag can be traced to whatever caused it.
        /// </summary>
        public void SetPlayerInRange(bool inRange, string reason)
        {
            if (IsPlayerInRange == inRange) return;
            IsPlayerInRange = inRange;

            if (logRangeChanges) Report(inRange, reason);

            if (PlayerInteractor.Instance == null) return;

            if (inRange) PlayerInteractor.Instance.SetNearbyInteractable(this);
            else PlayerInteractor.Instance.ClearNearbyInteractable(this);
        }

        /// <summary>
        /// Prints the state of everything that decides whether this object can be interacted with.
        ///
        /// The point of listing all of it, rather than just saying "lost the player", is that the
        /// cause is always one of these lines and it is always visible in them. A player object
        /// switched off by a station's Hide While Open list, a collider disabled by a camera
        /// switch, a component switched off by a Disable While Open list - each shows up as one
        /// False in a row of Trues, at the frame it happened, which is a five second diagnosis
        /// instead of an afternoon.
        /// </summary>
        void Report(bool gained, string reason)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            Collider playerCollider = playerObject != null
                ? playerObject.GetComponentInChildren<Collider>(true) : null;

            Debug.Log("[Interactable] '" + name + "' " + (gained ? "GAINED" : "LOST") +
                      " the player on frame " + Time.frameCount + " - " + reason + "." +
                      "\n    this object active   : " + gameObject.activeInHierarchy +
                      "\n    this component on    : " + enabled +
                      "\n    its trigger on       : " + (Trigger != null ? Trigger.enabled.ToString()
                                                                         : "NO COLLIDER") +
                      "\n    is a trigger         : " + (Trigger != null ? Trigger.isTrigger.ToString()
                                                                         : "-") +
                      "\n    player object found  : " + (playerObject != null ? "'" + playerObject.name + "'"
                                                                             : "NONE tagged Player") +
                      "\n    player active        : " + (playerObject != null
                                                         ? playerObject.activeInHierarchy.ToString() : "-") +
                      "\n    player collider on   : " + (playerCollider != null
                                                         ? playerCollider.enabled.ToString() : "-"),
                      this);
        }

        /// <summary>Called when the player presses this object's control while it is the active
        /// nearby interactable.</summary>
        public abstract void Interact();
    }
}