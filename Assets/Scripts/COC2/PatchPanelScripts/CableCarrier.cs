// CableCarrier.cs
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// A cable the player picks up, CARRIES while they walk, and plugs in where they stand.
    ///
    /// Deliberately NOT a PatchCordBoard phase. A phase is a fixed camera looking at one place while
    /// cords are dragged with the pointer; this is the opposite - the player is in the world, on
    /// their feet, and the whole point is walking the cable from one device to another. Nothing here
    /// touches the station.
    ///
    /// WHAT IT CARRIES IS ONE PLUG, NOT THE CABLE. The cable object stays where it is; only the end
    /// being run gets parented to the player's hold anchor. The VerletRope then stretches between
    /// the anchored end and the hand, which is exactly what pulling a cable across a room looks
    /// like - and it means plugging the first end simply leaves the cable there while the player
    /// walks off with the second.
    ///
    /// IT REUSES EVERYTHING. Sockets are the RJ45Socket components already in the scene; seating is
    /// PatchCord.Plug(), the same call the drag phases make. There is no second notion of "plugged
    /// in" anywhere in the project.
    ///
    /// Setup:
    ///   1. Put this on the cable's ROOT object (the parent holding both plugs and the rope).
    ///   2. Add a trigger Collider sized to how close the player must be to pick it up.
    ///   3. Assign Hold Anchor - an empty parented under the player's camera, out in front.
    ///   4. Fill First End Groups and Second End Groups with the groupIds each end must reach.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CableCarrier : Interactable
    {
        [Header("Identity")]
        [Tooltip("What this cable is called, in hints and in the Console.")]
        public string cableName = "Server cable";

        [Tooltip("What kind of cable this is, in hints and dialog - 'straight-through', 'crossover'.\n\n" +
                 "A PC or a server to a patch panel is ALWAYS straight-through. A crossover is for " +
                 "like to like - switch to switch, PC to PC - and putting one here would be a fault. " +
                 "Both of these cables are straight-through; saying so where the player can read it " +
                 "is half the lesson.")]
        public string cableTypeLabel = "straight-through";

        [Header("Availability - the player has to MAKE this cable first")]
        [Tooltip("Task ids that must all be complete before this cable exists. The default gates on " +
                 "the cable test, which covers the whole chain: the tester only reports a PASS, and " +
                 "the player cannot reach it without stripping, wiring and crimping first.\n\n" +
                 "Every id here must be registered by COC2TaskSetup or it can never complete and the " +
                 "cable never appears. Empty = available from the start.")]
        public string[] requiredTaskIds = new string[] { "test_cable" };

        [Tooltip("IGNORE the task ids and wait to be told. The cable appears only when something " +
                 "calls MakeAvailable() on this component.\n\n" +
                 "This is what you want when 'the player has made the cable' is not the same moment " +
                 "as 'the player needs the cable'. Gating on test_cable makes the cable appear at the " +
                 "very start of COC2, right after crimping - long before there is anywhere to plug " +
                 "it in. Tick this instead and hook the LAST plugging phase's On Phase Passed to " +
                 "MakeAvailable(), and it turns up at the bench exactly when the run reaches it.")]
        public bool waitForUnlockCall = false;

        [Tooltip("TESTING ONLY. The cable is there from the first frame, whatever the player has or " +
                 "hasn't made, and whatever Wait For Unlock Call says. It warns in the Console on " +
                 "every Play, so it cannot ship by accident.")]
        public bool bypassGateForTesting = false;

        [Tooltip("Where the finished cable appears - an empty on the bench beside the tester. The " +
                 "whole cable is moved here the moment the gate opens.\n\n" +
                 "Leave empty and it simply becomes visible wherever it already is, which is fine if " +
                 "you parked it on the bench in the editor.")]
        public Transform spawnPoint;

        [Tooltip("Keep the cable invisible and un-pickable until the gate opens, so a cable the " +
                 "player has not made yet is not lying about the room.\n\n" +
                 "RENDERERS AND THE COLLIDER, not SetActive - deactivating the object would stop its " +
                 "VerletRope simulating and leave the rope in a strange state when it came back.")]
        public bool hiddenUntilAvailable = true;

        [Header("Cable length")]
        [Tooltip("How long this cable is, in metres. The rope is rebuilt to this length when the " +
                 "cable appears.\n\n" +
                 "THIS MATTERS MORE THAN IT LOOKS. VerletRope sizes its segment count ONCE, from " +
                 "however far apart its anchors were at Start - and a coiled cable on a bench is a " +
                 "few centimetres end to end. Carry that across the room and the rope has nowhere " +
                 "near enough segments: it goes bar-straight and thin, like a wire under tension, " +
                 "instead of hanging. Set this to the real length and it sags properly the whole way " +
                 "- and goes taut if the player walks past the end of it, which is honest.")]
        public float cableLength = 3f;

        [Header("The cable")]
        [Tooltip("The end the player runs FIRST. Leave empty and the first PatchCord under this " +
                 "object is used.")]
        public PatchCord firstEnd;

        [Tooltip("The end the player runs SECOND. Leave empty and the other PatchCord under this " +
                 "object is used.")]
        public PatchCord secondEnd;

        [Header("Carrying")]
        [Tooltip("An empty parented under the PLAYER'S CAMERA, positioned out in front of them at " +
                 "about waist height. The end being run is parented here, so this is where the plug " +
                 "appears to be held.")]
        public Transform holdAnchor;

        [Tooltip("Local offset from Hold Anchor, so you can nudge the plug without moving the anchor.")]
        public Vector3 holdOffset = Vector3.zero;

        [Tooltip("Local rotation of the held plug, in degrees. Point the connector away from the " +
                 "camera so the player sees it end-on, ready to insert.")]
        public Vector3 holdRotation = Vector3.zero;

        [Header("Plugging in")]
        [Tooltip("How close, in world units, the held plug has to be to a socket before it can go " +
                 "in. This is a walk-up distance, not a snap distance - about an arm's length.")]
        public float plugRange = 0.8f;

        [Tooltip("Key that plugs the held end into the socket in range.\n\n" +
                 "DELIBERATELY NOT E. E belongs to PlayerInteractor, and the player will be standing " +
                 "right next to the patch-down workstation when they plug the first end in - one " +
                 "press would plug the cable AND open the punch-down station. F stays out of the way " +
                 "of both that and the Q the station uses to close.")]
        public KeyCode plugKey = KeyCode.F;

        [Tooltip("Key that puts the cable down where the player is standing. Set to None to make a " +
                 "picked-up cable impossible to put down until it is run.")]
        public KeyCode dropKey = KeyCode.G;

        [Header("Where each end goes")]
        [Tooltip("The groupIds the FIRST end may be plugged into - 'panel', 'outlet'. Anything else " +
                 "is not a candidate, so the player cannot accidentally put this end in the PC.")]
        public string[] firstEndGroups = new string[] { "panel" };

        [Tooltip("The groupIds the SECOND end may be plugged into - 'server', 'client'.")]
        public string[] secondEndGroups = new string[] { "server" };

        [Header("Order")]
        [Tooltip("Optional. This cable cannot be picked up until that one is fully run. Chain two " +
                 "carriers this way and the player does them one at a time, which is the whole " +
                 "point - a cable in each hand is not a thing.")]
        public CableCarrier requires;

        [Header("Dialog (optional)")]
        [Tooltip("Shown the moment the finished cable appears on the bench. Tell the player what it " +
                 "is and where it goes.")]
        public DialogSequence availableDialog;

        [Tooltip("Shown the first time the player picks the cable up.")]
        public DialogSequence pickedUpDialog;

        [Tooltip("Shown after the FIRST end goes in, when the player has to walk the other end " +
                 "somewhere else. This is the one that stops them standing at the panel wondering " +
                 "why nothing happened.")]
        public DialogSequence firstEndPluggedDialog;

        [Tooltip("Shown once both ends are in.")]
        public DialogSequence completeDialog;

        [Tooltip("Each of these plays once per session. Untick to have them repeat every time the " +
                 "player picks the cable up again after dropping it.")]
        public bool dialogsOnlyOnce = true;

        [Header("Readouts (optional)")]
        [Tooltip("Short nudges - 'Walk to the patch panel', 'Press F to plug in'.")]
        public TMP_Text hintText;
        public float hintSeconds = 2.5f;

        [Header("Task (optional)")]
        [Tooltip("Reported to TaskManager when both ends are in. It must be registered by " +
                 "COC2TaskSetup or nothing will listen for it.")]
        public string taskId = "";

        [Header("Events")]
        [Tooltip("Fired the moment the finished cable appears on the bench. Hook a sound, a " +
                 "highlight, a dialog line - anything that tells the player to go and get it.")]
        public UnityEvent OnAvailable;
        public UnityEvent OnPickedUp;
        public UnityEvent OnEndPlugged;
        public UnityEvent OnComplete;

        /// <summary>True while the player is holding an end of this cable.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>
        /// The cable end currently in the player's hand, across every CableCarrier in the scene, or
        /// null.
        ///
        /// There are two of these and neither is a singleton, so a shared Plug or Drop button would
        /// otherwise have to search the scene every frame to find out which cable it is meant to act
        /// on. PickUp and the three ways of letting go already know, so they say so here.
        /// </summary>
        public static CableCarrier Held { get; private set; }

        /// <summary>
        /// True when the held plug is close enough to a socket it is allowed to go into.
        ///
        /// Exactly what the Update loop tests before it offers the plug key, so a Plug button
        /// driven by this cannot be offered at a moment when pressing it would be refused - and
        /// cannot be missing at a moment when it would work.
        /// </summary>
        public bool SocketInReach { get { return IsHeld && SocketInRange() != null; } }

        /// <summary>True once BOTH ends are plugged in correctly.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>The end currently in the player's hand, or null.</summary>
        public PatchCord Carried { get; private set; }

        Transform cableRoot;
        RJ45Socket highlighted;
        float hintClearAt;

        bool shownAvailable, shownPickedUp, shownFirstEnd, shownComplete;
        Renderer[] cableRenderers;
        Collider pickupCollider;
        bool appeared;

        /// <summary>True once the player has made this cable and it is lying by the tester.</summary>
        public bool IsAvailable { get { return appeared; } }

        /// <summary>How many of this cable's two ends are plugged in - 0, 1 or 2.</summary>
        public int EndsPlugged
        {
            get
            {
                int n = 0;
                if (firstEnd != null && firstEnd.IsPlugged) n++;
                if (secondEnd != null && secondEnd.IsPlugged) n++;
                return n;
            }
        }

        /// <summary>
        /// True once the player has taken this cable off the bench - and it STAYS true for the rest
        /// of the run.
        ///
        /// IsHeld alone is not enough for anything that has to remember: it drops to false for a
        /// moment each time an end seats, and again for good once the cable is finished. Anything
        /// asking "has the pick-up step been done" wants this, which only ever goes one way.
        /// </summary>
        public bool HasBeenPickedUp { get { return IsHeld || IsComplete || EndsPlugged > 0; } }

        /// <summary>True once the FIRST end is in, whichever socket that was. The point at which
        /// the player should be heading for the far end of the run.</summary>
        public bool FirstEndPlugged { get { return EndsPlugged >= 1; } }

        void Awake()
        {
            cableRoot = transform;

            var ends = GetComponentsInChildren<PatchCord>(true);
            if (firstEnd == null && ends.Length > 0) firstEnd = ends[0];
            if (secondEnd == null && ends.Length > 1) secondEnd = ends[1];

            // Locked from the moment the scene loads. These cables are lying about in the world
            // where the player can see them long before this step, and a cable that can be dragged
            // with the mouse from across the room is not something anyone wants to discover.
            SetEndsLocked(true);

            cableRenderers = GetComponentsInChildren<Renderer>(true);
            pickupCollider = GetComponent<Collider>();

            if (hiddenUntilAvailable && !bypassGateForTesting && FirstUnfinishedTask() != null)
                SetVisible(false);
            else
                Appear();
        }

        /// <summary>Null when every required task is finished, otherwise the first id that isn't.</summary>
        string FirstUnfinishedTask()
        {
            if (bypassGateForTesting) return null;

            // Held shut regardless of what the player has finished. The name is what the hint and
            // the Console print, so it has to read as a reason rather than a task id.
            if (waitForUnlockCall) return "the step that needs this cable";
            if (requiredTaskIds == null || requiredTaskIds.Length == 0) return null;
            if (TaskManager.Instance == null) return null;   // nothing to check against - let it through

            foreach (string id in requiredTaskIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!TaskManager.Instance.IsTaskComplete(id)) return id;
            }
            return null;
        }

        /// <summary>
        /// The cable the player just made, on the bench beside the tester.
        ///
        /// It is the same object that was in the scene all along, moved and switched on - not an
        /// Instantiate. Everything already pointing at it in the Inspector keeps pointing at it,
        /// which a spawned copy would not.
        /// </summary>
        void Appear()
        {
            if (appeared) return;
            appeared = true;

            if (spawnPoint != null)
                cableRoot.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

            SetVisible(true);
            SizeRope();

            SetHint("Your " + cableTypeLabel + " " + cableName.ToLower() + " is ready on the bench.");
            OnAvailable?.Invoke();
            Say(availableDialog, ref shownAvailable);

            Debug.LogWarning("[CableCarrier] '" + cableName + "' is now available" +
                             (spawnPoint != null ? " at " + spawnPoint.name : "") +
                             ". The player can pick it up.", this);

            WarnIfAnotherCarrierCoversThisSpot();
        }

        /// <summary>
        /// Says so if another cable's pick-up trigger reaches over this one's spawn point.
        ///
        /// Two overlapping triggers is a scene problem, not a code one: the player presses the
        /// interact key, PlayerInteractor picks whichever it found, and the wrong cable answers.
        /// Nothing about that looks like a bug from the Console - it looks like the key not working.
        /// RefreshPickupCollider removes the common case (a FINISHED cable shadowing the next one),
        /// but two cables that are both still to be run would still collide, so this names it.
        /// </summary>
        void WarnIfAnotherCarrierCoversThisSpot()
        {
            if (pickupCollider == null || !pickupCollider.enabled) return;

            Vector3 here = cableRoot.position;

            CableCarrier[] all = FindObjectsByType<CableCarrier>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None);
            foreach (CableCarrier other in all)
            {
                if (other == null || other == this) continue;

                Collider oc = other.pickupCollider != null
                    ? other.pickupCollider
                    : other.GetComponent<Collider>();

                if (oc == null || !oc.enabled) continue;
                if (!oc.bounds.Contains(here)) continue;

                Debug.LogWarning("[CableCarrier] '" + cableName + "' has just appeared INSIDE the " +
                                 "pick-up trigger of '" + other.cableName + "' (on '" + oc.name +
                                 "'). Standing here, the player is in reach of both, and the interact " +
                                 "key may well answer for the other one instead of this one.\n\n" +
                                 "Move the two spawn points further apart, or shrink that trigger.", this);
            }
        }

        /// <summary>
        /// Puts the finished cable on the bench, whatever the gate says.
        ///
        /// PUBLIC so a UnityEvent can call it - that is the whole point of Wait For Unlock Call.
        /// Hook the last plugging phase's On Phase Passed here and the cable turns up at the moment
        /// the player actually needs it, rather than at whichever earlier moment happens to have a
        /// task id attached to it.
        ///
        /// Safe to call more than once; the second call does nothing.
        /// </summary>
        public void MakeAvailable()
        {
            if (appeared) return;
            Debug.LogWarning("[CableCarrier] '" + cableName + "' unlocked by MakeAvailable().", this);
            Appear();
        }

        void SetVisible(bool on)
        {
            if (cableRenderers != null)
            {
                foreach (var r in cableRenderers) { if (r != null) r.enabled = on; }
            }
            RefreshPickupCollider();
        }

        /// <summary>
        /// The pick-up trigger is on ONLY while this cable is something the player can actually pick
        /// up: it has appeared, and it is not already run.
        ///
        /// THIS IS THE FIX FOR "I CAN'T PICK UP THE CLIENT CABLE".
        ///
        /// The trigger used to be tied to visibility alone, so a finished cable kept a live pick-up
        /// trigger sitting on the bench for the rest of the module. The server cable finishes at the
        /// exact moment the client cable appears, and the two spawn points are close enough together
        /// that the player standing at Spawn_ClientCable is inside BOTH triggers. PlayerInteractor
        /// picks one of them, the finished server cable can win, and pressing the interact key then
        /// answers "Server cable is already run" - while the client cable, a few centimetres away,
        /// is never asked.
        ///
        /// Interact() already refused a completed cable, which is exactly why this was so hard to
        /// see: the refusal was correct, it was just answering for the wrong object. A cable that
        /// can no longer be picked up should not be in the running to be chosen at all.
        ///
        /// Note this is about the PICK-UP trigger only. The cable's own geometry and its PatchCord
        /// ends are untouched - a finished run stays visible and stays plugged in.
        /// </summary>
        void RefreshPickupCollider()
        {
            if (pickupCollider == null) return;
            pickupCollider.enabled = appeared && !IsComplete;
        }

        /// <summary>
        /// Rebuilds the rope with enough segments for the cable's real length.
        ///
        /// Without this the cable behaves like the few centimetres it was coiled into: VerletRope
        /// fixes its segment count at Start from the anchor distance it saw, and every step the
        /// player takes past that just stretches the same handful of segments into a straight line.
        /// </summary>
        void SizeRope()
        {
            VerletRope r = firstEnd != null ? firstEnd.rope : null;
            if (r == null && secondEnd != null) r = secondEnd.rope;
            if (r == null) return;

            int want = Mathf.CeilToInt(cableLength / Mathf.Max(0.001f, r.segmentLength)) + 1;
            if (want > r.maxSegments)
            {
                Debug.LogWarning("[CableCarrier] '" + cableName + "' wants " + want + " rope segments " +
                                 "for " + cableLength + "m at a segment length of " + r.segmentLength +
                                 ", but Max Segments on the rope is " + r.maxSegments + ". The cable " +
                                 "will run short and go taut early. Raise Max Segments, or raise " +
                                 "Segment Length so fewer are needed.", this);
            }

            r.minSegments = Mathf.Clamp(want, 2, r.maxSegments);
            r.ForceRebuild();
        }

        void Start()
        {
            ValidateSetup();
        }

        // ------------------------------------------------------------------
        // Picking it up
        // ------------------------------------------------------------------

        // Picking the cable up to run it is a pickup, so it answers to G and to the Pick Up
        // button. Plugging the ends in afterwards is handled by the sockets, not by this key.
        protected override InputChannel DefaultChannel { get { return InputChannel.Pickup; } }

        public override void Interact()
        {
            if (IsComplete)
            {
                SetHint(cableName + " is already run.");
                return;
            }

            // Belt and braces. A hidden cable has its collider off, so Interactable cannot see the
            // player at all - but if Hidden Until Available is ever unticked, this is what stops a
            // cable being picked up before it exists.
            if (!appeared)
            {
                string missing = FirstUnfinishedTask();
                SetHint(waitForUnlockCall
                    ? "You don't need this cable yet."
                    : "You haven't made this cable yet - finish '" + missing + "' first.");
                return;
            }

            if (requires != null && !requires.IsComplete)
            {
                SetHint("Run the " + requires.cableName + " first.");
                return;
            }

            if (IsHeld) { SetHint("You are already holding this."); return; }

            PickUp();
        }

        void PickUp()
        {
            PatchCord end = NextEnd();
            if (end == null)
            {
                Debug.LogWarning("[CableCarrier] '" + cableName + "' has no free end to pick up - " +
                                 "both PatchCords are already plugged in.", this);
                return;
            }

            if (holdAnchor == null)
            {
                Debug.LogWarning("[CableCarrier] '" + cableName + "' has no Hold Anchor, so there is " +
                                 "nowhere to carry it. Assign an empty under the player's camera.", this);
                return;
            }

            IsHeld = true;

            Held = this;
            Carried = end;

            // The PLUG is parented to the hand, not the cable. The cable stays where it is and the
            // rope stretches out behind the player - which is both what carrying a cable looks like
            // and what makes plugging the first end simply leave the cable there.
            Carried.transform.SetParent(holdAnchor, true);
            Carried.transform.localPosition = holdOffset;
            Carried.transform.localRotation = Quaternion.Euler(holdRotation);
            Carried.RebuildRope();

            SetHint("Carrying the " + cableTypeLabel + " " + cableName.ToLower() + ". Walk to the " +
                    DescribeTarget() + " and press " + plugKey + ".");
            OnPickedUp?.Invoke();
            Say(pickedUpDialog, ref shownPickedUp);

            Debug.LogWarning("[CableCarrier] Picked up '" + cableName + "' - carrying " +
                             Carried.name + ", which goes to " + DescribeTarget() + ".", this);
        }

        /// <summary>The first end that is not yet plugged in, in order.</summary>
        PatchCord NextEnd()
        {
            if (firstEnd != null && !firstEnd.IsPlugged) return firstEnd;
            if (secondEnd != null && !secondEnd.IsPlugged) return secondEnd;
            return null;
        }

        string[] GroupsFor(PatchCord end)
        {
            return end == secondEnd ? secondEndGroups : firstEndGroups;
        }

        string DescribeTarget()
        {
            string[] g = GroupsFor(Carried != null ? Carried : NextEnd());
            if (g == null || g.Length == 0) return "socket";
            return g[0];
        }

        // ------------------------------------------------------------------
        // Carrying
        // ------------------------------------------------------------------

        void Update()
        {
            if (hintSeconds > 0f && hintClearAt > 0f && Time.unscaledTime >= hintClearAt)
            {
                hintClearAt = 0f;
                if (hintText != null) hintText.text = "";
            }

            // Cheap: only runs while the cable does not exist yet, and stops for good once it does.
            if (!appeared && FirstUnfinishedTask() == null) Appear();

            if (!IsHeld || Carried == null) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            RJ45Socket target = SocketInRange();
            Highlight(target);

            if (target != null && plugKey != KeyCode.None && Input.GetKeyDown(plugKey))
            {
                PlugInto(target);
                return;
            }

            if (dropKey != KeyCode.None && Input.GetKeyDown(dropKey)) Drop();
        }

        /// <summary>
        /// The nearest free socket the held end is allowed to go into, within reach.
        ///
        /// Measured from the PLUG rather than the player, because the plug is what is being offered
        /// up to the jack - and it is held out in front, which is the difference between "close
        /// enough" feeling right and feeling a foot off.
        /// </summary>
        RJ45Socket SocketInRange()
        {
            if (Carried == null) return null;

            string[] allowed = GroupsFor(Carried);
            Vector3 from = Carried.transform.position;

            RJ45Socket best = null;
            float bestDist = plugRange;

            foreach (var s in RJ45Socket.All)
            {
                if (s == null || s.IsOccupied) continue;
                if (!IsAllowed(s.groupId, allowed)) continue;

                float d = Vector3.Distance(from, s.Snap.position);
                if (d <= bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        static bool IsAllowed(string groupId, string[] allowed)
        {
            if (allowed == null || allowed.Length == 0) return true;
            foreach (string g in allowed)
            {
                if (g == groupId) return true;
            }
            return false;
        }

        void Highlight(RJ45Socket target)
        {
            if (highlighted == target) return;
            if (highlighted != null) highlighted.SetHighlighted(false);
            highlighted = target;
            if (highlighted != null)
            {
                highlighted.SetHighlighted(true);
                SetHint("Press " + plugKey + " to plug in.");
            }
        }

        // ------------------------------------------------------------------
        // Plugging in
        // ------------------------------------------------------------------

        void PlugInto(RJ45Socket socket)
        {
            PatchCord end = Carried;

            // Out of the hand BEFORE seating. Plug() sets a world pose; leaving the plug parented to
            // a moving camera would have it seated correctly for exactly one frame and then dragged
            // away the moment the player turned their head.
            end.transform.SetParent(cableRoot, true);

            IsHeld = false;

            if (Held == this) Held = null;
            Carried = null;
            Highlight(null);

            end.Plug(socket, false);
            end.isLocked = true;            // run and dressed - not coming back out by hand
            OnEndPlugged?.Invoke();

            Debug.LogWarning("[CableCarrier] '" + cableName + "' - " + end.name + " plugged into " +
                             socket.groupId + " port " + socket.portNumber + ".", this);

            PatchCord next = NextEnd();
            if (next != null)
            {
                // Straight on to the other end, still in hand. Putting the cable down between the
                // two ends would only be an extra walk back to it.
                Carried = next;
                IsHeld = true;
                Held = this;
                Carried.transform.SetParent(holdAnchor, true);
                Carried.transform.localPosition = holdOffset;
                Carried.transform.localRotation = Quaternion.Euler(holdRotation);
                Carried.RebuildRope();

                SetHint("Now take the other end to the " + DescribeTarget() +
                        " and press " + plugKey + ".");
                Say(firstEndPluggedDialog, ref shownFirstEnd);
                return;
            }

            Complete();
        }

        void Complete()
        {
            IsComplete = true;
            IsHeld = false;
            if (Held == this) Held = null;
            Carried = null;
            Highlight(null);
            SetEndsLocked(true);

            // Stand down as an interactable. Done BEFORE OnComplete fires, because OnComplete is
            // what makes the NEXT cable appear - and that cable spawns close enough to this one that
            // a pick-up trigger left live here can shadow it. See RefreshPickupCollider.
            RefreshPickupCollider();

            if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                TaskManager.Instance.ReportProgress(taskId);

            SetHint(cableName + " is run.");
            OnComplete?.Invoke();
            Say(completeDialog, ref shownComplete);

            Debug.LogWarning("[CableCarrier] '" + cableName + "' is fully run - both ends in.", this);
        }

        /// <summary>Puts the held end down where the player is standing.</summary>
        public void Drop()
        {
            if (!IsHeld || Carried == null) return;

            Carried.transform.SetParent(cableRoot, true);
            Carried.RebuildRope();
            Carried = null;
            IsHeld = false;
            if (Held == this) Held = null;
            Highlight(null);

            SetHint(cableName + " put down.");
        }

        /// <summary>Public so a mobile button can drive it - there is no key on a phone. Wire a
        /// button's On Click to this and to Drop().</summary>
        public void PlugNow()
        {
            if (!IsHeld) return;
            RJ45Socket target = SocketInRange();
            if (target == null) { SetHint("Nothing in reach to plug into."); return; }
            PlugInto(target);
        }

        void SetEndsLocked(bool locked)
        {
            if (firstEnd != null) firstEnd.isLocked = locked;
            if (secondEnd != null) secondEnd.isLocked = locked;
        }

        /// <summary>
        /// Shows a briefing, if there is one and nothing else is already talking.
        ///
        /// Never queues and never interrupts. A line that would have landed on top of another is
        /// dropped instead - the alternative is two boxes fighting over the screen at the exact
        /// moment the player is trying to work out what to do.
        /// </summary>
        void Say(DialogSequence sequence, ref bool alreadyShown)
        {
            if (sequence == null) return;
            if (dialogsOnlyOnce && alreadyShown) return;
            if (DialogManager.Instance == null || DialogManager.Instance.IsDialogOpen) return;

            alreadyShown = true;
            DialogManager.Instance.ShowDialog(sequence);
        }

        void SetHint(string message)
        {
            if (hintText == null) return;

            // GUIDANCE: these lines coach the cable run - Story Mode only. Empty in an assessment.
            if (!CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.Hints)) message = null;
            hintText.text = message ?? "";
            hintClearAt = (hintSeconds > 0f && !string.IsNullOrEmpty(message))
                ? Time.unscaledTime + hintSeconds
                : 0f;
        }

        // ------------------------------------------------------------------

        /// <summary>Says out loud what is missing, rather than the cable quietly doing nothing.</summary>
        [ContextMenu("Check my setup")]
        public void ValidateSetup()
        {
            var sb = new System.Text.StringBuilder();

            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
                sb.AppendLine("  - The Collider on this object is NOT a trigger, so Interactable can " +
                              "never see the player and the cable can never be picked up.");

            if (firstEnd == null || secondEnd == null)
                sb.AppendLine("  - Fewer than two PatchCord components under this object. A cable " +
                              "needs both ends.");
            else if (firstEnd == secondEnd)
                sb.AppendLine("  - First End and Second End are the same plug.");

            if (holdAnchor == null)
                sb.AppendLine("  - Hold Anchor is empty. Make an empty under the player's camera, " +
                              "out in front at about waist height, and assign it here.");

            if (firstEnd != null && firstEnd.startPlugged)
                sb.AppendLine("  - '" + firstEnd.name + "' has Start Plugged ticked, so it is already " +
                              "in a socket and there is nothing for the player to run. Untick it.");
            if (secondEnd != null && secondEnd.startPlugged)
                sb.AppendLine("  - '" + secondEnd.name + "' has Start Plugged ticked. Untick it.");

            CheckGroupExists(firstEndGroups, "First End Groups", sb);
            CheckGroupExists(secondEndGroups, "Second End Groups", sb);

            if (waitForUnlockCall)
                Debug.LogWarning("[CableCarrier] '" + cableName + "' is waiting to be unlocked. " +
                                 "Nothing will make it appear until something calls MakeAvailable() " +
                                 "on it - hook the last plugging phase's On Phase Passed to it.", this);

            if (!waitForUnlockCall && requiredTaskIds != null && requiredTaskIds.Length > 0)
            {
                var setup = Object.FindFirstObjectByType<COC2TaskSetup>();
                if (setup == null)
                    sb.AppendLine("  - Required Task Ids are set but there is no COC2TaskSetup in the " +
                                  "scene to register them, so they can never complete and this cable " +
                                  "would never appear.");
            }

            if (spawnPoint == null && hiddenUntilAvailable)
                sb.AppendLine("  - Hidden Until Available is on but Spawn Point is empty, so the cable " +
                              "will appear wherever it is sitting now rather than by the tester. Fine " +
                              "if that is already the bench; otherwise assign one.");

            if (bypassGateForTesting)
                Debug.LogWarning("[CableCarrier] '" + cableName + "' has GATE BYPASSED - it exists " +
                                 "from the first frame whether or not the player made it. Untick " +
                                 "'Bypass Gate For Testing' before a real run.", this);

            if (availableDialog == null && pickedUpDialog == null && firstEndPluggedDialog == null)
                sb.AppendLine("  - No dialog assigned anywhere on this cable. The player is given a " +
                              "cable and no instruction; the hint line alone is easy to miss. At " +
                              "minimum fill First End Plugged Dialog, which is the moment they are " +
                              "most likely to stall.");

            if (requires == this)
                sb.AppendLine("  - Requires points at this same cable, so it can never be picked up.");

            if (sb.Length == 0)
            {
                Debug.LogWarning("[CableCarrier] '" + cableName + "' looks complete - two ends, hold " +
                                 "anchor set, target groups present.", this);
                return;
            }
            Debug.LogWarning("[CableCarrier] '" + cableName + "' setup is incomplete:\n" + sb, this);
        }

        void CheckGroupExists(string[] groups, string fieldName, System.Text.StringBuilder sb)
        {
            if (groups == null || groups.Length == 0)
            {
                sb.AppendLine("  - " + fieldName + " is empty, so this end will go into ANY free " +
                              "socket in the scene, including the wrong device.");
                return;
            }

            foreach (string g in groups)
            {
                bool found = false;
                foreach (var s in RJ45Socket.All)
                {
                    if (s != null && s.groupId == g) { found = true; break; }
                }
                if (!found)
                    sb.AppendLine("  - " + fieldName + " asks for group '" + g + "', but no RJ45Socket " +
                                  "in the scene has that Group Id. It is a case-sensitive string " +
                                  "compare, so check the spelling.");
            }
        }
    }
}