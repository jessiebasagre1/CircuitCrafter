// PatchCordBoard.cs
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// A PLUGGING PHASE of the punch-down station: run each cord to the port it belongs in.
    ///
    /// WHAT IT IS AND IS NOT. This board owns no dragging, no picking and no snapping. All of that
    /// already exists and is already tuned: PatchCord reads PointerInput, picks off renderer bounds,
    /// finds sockets through RJ45Socket.All and drives a VerletRope between two plugs. This is only
    /// the supervisor around it - it decides WHEN the cords are live, WHICH socket each one belongs
    /// in, and WHETHER the finished job is right.
    ///
    /// WHY THAT SPLIT. The cords are physical objects in the world, and the player can be looking at
    /// them from the station or walking past them. Putting the rules in the cord would mean the cord
    /// behaves differently depending on which stage is running, which is exactly the kind of hidden
    /// state that makes a bug impossible to find. The cord always behaves the same; this board just
    /// unlocks it and marks it.
    ///
    /// HOW IT KNOWS WHERE A CORD BELONGS, WITH NO PER-CORD SETUP. Every cord is two PatchCords under
    /// one parent. One end is fixed - it starts plugged and stays locked; the other is the one the
    /// player runs. The rule is simply "the free end goes to the same port number the fixed end came
    /// from, on the equipment named in Target Group Id". So a cord whose fixed plug sits in panel 3
    /// must reach switch 3, and nothing has to be typed in per cord.
    ///
    /// Setup:
    ///   1. Put this on an empty child of the station.
    ///   2. Fill Search Roots with whatever your cords live under.
    ///   3. Set Target Group Id to the groupId of the sockets they should reach.
    ///   4. Assign Camera Pose.
    ///   5. Drop it into Patch Phases on PunchDownStation.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class PatchCordBoard : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("What this phase is called, in the Console and in hints - 'Panel to switch', " +
                 "'Cable to outlet'. Matters once you have more than one plugging phase, at which " +
                 "point two boards logging the same line is thoroughly confusing.")]
        public string phaseName = "Panel to switch";

        [Tooltip("What the player is plugging into, for the hint text: 'switch port', 'outlet', " +
                 "'tester'. Kept separate from Phase Name so the nudges read naturally.")]
        public string socketNoun = "switch port";

        [Header("The view")]
        [Tooltip("The camera the cords are dragged in. PunchDownStation hands this over on entry, " +
                 "and it is pushed into every supervised PatchCord's Drag Camera - so you never have " +
                 "to keep eight Drag Camera fields in step with which view is actually rendering.")]
        public Camera stageCamera;

        [Tooltip("An empty posed so BOTH ends are comfortably in frame - where the cords start and " +
                 "where they have to reach. The camera flies here when this phase begins.\n\n" +
                 "Leave it empty and the camera stays wherever the previous phase left it, which " +
                 "after the punching is close in on port 8 - far too tight to run a cord.")]
        public Transform cameraPose;

        [Tooltip("Seconds for the move to Camera Pose.")]
        public float cameraMoveDuration = 1.0f;

        [Header("Which cords")]
        [Tooltip("The hierarchies the cords live under - typically the parent holding PatchCord_01 " +
                 "to _08. Leave empty to search under this object only.")]
        public Transform[] searchRoots;

        [Tooltip("The FREE end of each cord is the plug whose name contains this. The other plug " +
                 "under the same parent is the fixed end and is never unlocked.\n\n" +
                 "Matches the naming in the patch-cord guide: Plug_A is seated at the panel, Plug_B " +
                 "is the end the player runs. Change it if you named yours differently.")]
        public string freePlugNameContains = "Plug_B";

        [Tooltip("Found from the search roots by the name above - leave empty. Fill it only if your " +
                 "cords are named inconsistently and you want to point at the eight plugs by hand.")]
        public PatchCord[] freePlugs;

        [Header("The target")]
        [Tooltip("The groupId of the sockets these cords must reach - 'switch', 'router', 'outlet'. " +
                 "Must match what you set on those RJ45Socket components.")]
        public string targetGroupId = "switch";

        [Tooltip("ON: the free end must land on the port number its own fixed end came from - panel " +
                 "3 to switch 3. This is the whole lesson of a cross-connect, and it needs nothing " +
                 "typed in per cord.\n\n" +
                 "OFF: any socket in the target group will do. Right for a phase where one plug goes " +
                 "into one tester and the numbering means nothing.")]
        public bool matchPartnerPortNumber = true;

        [Tooltip("When the fixed end is NOT plugged in - because this cord starts completely loose - " +
                 "read the required port number off the cord's own name instead. 'PatchCord_03' " +
                 "means port 3.\n\n" +
                 "Without this, a cord with both ends unplugged has no number to match against and " +
                 "ANY port in the target group is accepted, silently. Trailing digits on the cord's " +
                 "parent object are used, so PatchCord_03, Cord 3 and Patch-3 all work.")]
        public bool deriveNumberFromCordName = true;

        [Header("Rules")]
        [Tooltip("Pull every supervised plug out of its socket when the phase starts, so the player " +
                 "does the work rather than finding it already done.\n\n" +
                 "Untick only if the cords are already unplugged in the scene.")]
        public bool unplugOnBegin = true;

        [Tooltip("How far to draw a plug back out of its jack when this phase unplugs it, along the " +
                 "plug's own axis. Purely so an unplugged cord does not sit inside the port looking " +
                 "plugged in. Ignored when the cord has a Home Anchor, which wins.")]
        public float unplugOffset = 0.05f;

        [Tooltip("Keep the cords locked except while this phase is running, so they cannot be pulled " +
                 "out during the punching or while the player walks past them in the world.")]
        public bool lockWhenIdle = true;

        [Tooltip("While this phase runs, a dragged cord can only snap into sockets in Target Group " +
                 "Id. Everything else stops being a candidate.\n\n" +
                 "LEAVE THIS ON when the equipment sits close together. PatchCord picks its target " +
                 "with RJ45Socket.NearestFree, which knows nothing about groups - so a cord being " +
                 "run to the switch will snap into a router jack a couple of centimetres away, get " +
                 "refused by the rules, and pop back out. Correct, but it reads as the cord refusing " +
                 "to go where the player plainly put it. Filtering here means the wrong jack is " +
                 "never offered, and the highlight never lights it up either.")]
        public bool restrictSnapToTargetGroup = true;

        [Tooltip("ON: a cord plugged into the wrong port pops straight back out with a hint naming " +
                 "the right one. Immediate, and it teaches the mapping directly.\n\n" +
                 "OFF: any free socket in the target group accepts any cord, and the whole job is " +
                 "judged at the end. Harder, and closer to a graded assessment.")]
        public bool requireCorrectPort = true;

        [Header("On begin")]
        [Tooltip("Switched OFF when this phase starts. The punch-down tool icon belongs here - there " +
                 "is nothing left to strike, and leaving it on screen invites a pointless drag.")]
        public GameObject[] hideOnBegin;

        [Tooltip("Switched ON when this phase starts. Anything specific to this phase - a port map, " +
                 "a different instruction panel. Leave them DISABLED in the editor.")]
        public GameObject[] showOnBegin;

        [Tooltip("Disabled when this phase starts. PunchStageCamera belongs here, so nothing else " +
                 "can fight this board for the camera.")]
        public MonoBehaviour[] disableOnBegin;

        [Header("Out of shot while this phase runs")]
        [Tooltip("Objects that block THIS phase's camera. Every renderer under each one is switched " +
                 "off when the phase starts and switched back on the moment it passes - so the UTP " +
                 "cables can be out of the way while the player runs the patch cords, and back in " +
                 "place the instant they are done.\n\n" +
                 "Different from Hide On Begin, which stays hidden for the rest of the visit. Use " +
                 "that for the punch tool icon; use this for scenery that is only in the way for " +
                 "one phase.\n\n" +
                 "RENDERERS, not SetActive, deliberately. Deactivating a cable would stop its " +
                 "VerletRope simulating and leave the rope in a strange state when it came back - " +
                 "and it would take the object's own scripts down with it. Switching renderers off " +
                 "also removes the object's SHADOW, which is the other half of a blocked shot.")]
        public Transform[] hideWhileRunning;

        [Tooltip("Optional. Shown once when this phase begins.")]
        public DialogSequence introDialog;

        [Header("Readouts (optional)")]
        public TMP_Text progressText;
        public TMP_Text hintText;
        [Tooltip("Seconds a hint stays up. 0 to leave it until the next one.")]
        public float hintSeconds = 2.5f;

        public Color passColor = new Color(0.15f, 0.8f, 0.3f);
        public Color failColor = new Color(0.9f, 0.25f, 0.2f);
        public Color progressColor = new Color(0.85f, 0.85f, 0.88f);

        [Header("Diagnostics")]
        [Tooltip("Print the roster when the phase starts, and a running count on every cord " +
                 "successfully run.\n\n" +
                 "Worth leaving on until the phase plays through. Without the readout TMP fields " +
                 "assigned there is otherwise NO feedback at all, and a phase that quietly never " +
                 "reaches its count looks identical to one that is broken.")]
        public bool logProgress = true;

        [Tooltip("Record where every plug sits in the EDITOR, then report how far it moved once " +
                 "PatchCord.Start() seated it.\n\n" +
                 "This catches the commonest setup mistake by a mile. A socket anchor is not a " +
                 "marker for where a jack is - it IS the pose the plug is teleported to, position " +
                 "AND rotation. Place the anchor by eye and every cord snaps somewhere slightly " +
                 "wrong the instant you press Play, which looks like the cords being broken rather " +
                 "than the anchor being off.")]
        public bool reportStartJump = true;

        [Tooltip("How far a plug may move at Start before it is reported, in world units. 0.002 is " +
                 "2 mm - below that nobody would notice.")]
        public float startJumpTolerance = 0.002f;

        [Tooltip("How far a plug may TURN at Start before it is reported, in degrees.")]
        public float startTurnTolerance = 2f;

        [Header("Task (optional)")]
        [Tooltip("Reported to TaskManager when this phase passes. It must be registered by " +
                 "COC2TaskSetup or nothing will listen for it.")]
        public string taskId = "";

        [Header("Events")]
        [Tooltip("Deliberately non-generic - an open generic UnityEvent<bool> only serialises from " +
                 "Unity 2020.1 and comes back null when it fails, taking the whole flow down silently.")]
        public UnityEvent OnPhaseBegan;
        public UnityEvent OnCordPatched;
        public UnityEvent OnPhasePassed;
        public UnityEvent OnPhaseFailed;

        /// <summary>Raised once every cord is in and judged. PunchDownStation subscribes to this.</summary>
        public System.Action<bool, string> OnFinished;

        public bool IsRunning { get; private set; }
        public bool HasFinished { get; private set; }
        public bool LastResultPassed { get; private set; }

        Coroutine cameraRoutine;
        float hintClearAt;
        int wrongAttempts;
        bool suppressCallback;

        // Recorded in Awake - which runs before ANY Start, and therefore before PatchCord.Start()
        // has seated anything. That ordering is the whole trick: it is the only moment the authored
        // editor pose still exists to be compared against.
        PatchCord[] watched;
        Vector3[] authoredPos;
        Quaternion[] authoredRot;

        Renderer[] blockers;
        bool[] blockerWasEnabled;
        bool blockersHidden;

        // ------------------------------------------------------------------

        public Transform[] Roots
        {
            get
            {
                if (searchRoots != null)
                {
                    var live = new System.Collections.Generic.List<Transform>();
                    foreach (var t in searchRoots) { if (t != null) live.Add(t); }
                    if (live.Count > 0) return live.ToArray();
                }
                return new Transform[] { transform };
            }
        }

        void Awake()
        {
            if (freePlugs == null || freePlugs.Length == 0) freePlugs = CollectFreePlugs();

            foreach (var p in freePlugs)
            {
                if (p == null) continue;
                p.OnConnectionChanged += HandleConnectionChanged;
                if (lockWhenIdle) p.isLocked = true;
            }

            RecordAuthoredPoses();

            IsRunning = false;
            HasFinished = false;
        }

        /// <summary>
        /// Snapshots where every plug sits before anything has moved it.
        ///
        /// Both ends, not just the free one - the fixed end is the half seated against the panel,
        /// and it is just as likely to be the one whose anchor is off.
        /// </summary>
        void RecordAuthoredPoses()
        {
            if (!reportStartJump) return;

            var list = new System.Collections.Generic.List<PatchCord>();
            foreach (var p in freePlugs)
            {
                if (p == null) continue;
                if (!list.Contains(p)) list.Add(p);
                PatchCord partner = Partner(p);
                if (partner != null && !list.Contains(partner)) list.Add(partner);
            }

            watched = list.ToArray();
            authoredPos = new Vector3[watched.Length];
            authoredRot = new Quaternion[watched.Length];
            for (int i = 0; i < watched.Length; i++)
            {
                authoredPos[i] = watched[i].transform.position;
                authoredRot[i] = watched[i].transform.rotation;
            }
        }

        /// <summary>
        /// Reports any plug that jumped when it was seated at Start.
        ///
        /// A socket anchor is not a label for where a jack is - PatchCord.Plug() teleports the plug
        /// to the anchor's world position AND rotation. So if the anchor was placed by eye, every
        /// cord shifts the instant Play is pressed, and it reads as the cords being broken rather
        /// than the anchor being a few millimetres and a few degrees out.
        /// </summary>
        void ReportStartJumps(StringBuilder sb)
        {
            if (!reportStartJump || watched == null) return;

            for (int i = 0; i < watched.Length; i++)
            {
                PatchCord p = watched[i];
                if (p == null || !p.IsPlugged) continue;

                float moved = Vector3.Distance(authoredPos[i], p.transform.position);
                float turned = Quaternion.Angle(authoredRot[i], p.transform.rotation);
                if (moved <= startJumpTolerance && turned <= startTurnTolerance) continue;

                string anchor = p.startSocket == null
                    ? "its socket"
                    : (p.startSocket.snapPoint != null
                        ? p.startSocket.name + " (via its Snap Point '" + p.startSocket.snapPoint.name + "')"
                        : p.startSocket.name);

                sb.AppendLine("  - '" + p.name + "' JUMPED " + moved.ToString("F4") + " units and " +
                              turned.ToString("F0") + "� when it seated into " + anchor + ". The " +
                              "anchor is not where the plug was sitting in the editor.\n" +
                              "      Fix: put the plug exactly where you want it, right-click its " +
                              "Transform > Copy Component, then right-click the anchor's Transform > " +
                              "Paste Component Values. The anchor IS the final pose, rotation included.");
            }
        }

        void OnDestroy()
        {
            // Static state outlives this component, so a board destroyed mid-phase would leave every
            // other group in the scene permanently unsnappable.
            RJ45Socket.OnlyGroup = null;

            if (freePlugs == null) return;
            foreach (var p in freePlugs)
            {
                if (p != null) p.OnConnectionChanged -= HandleConnectionChanged;
            }
        }

        void Start()
        {
            ValidateSetup();
        }

        void Update()
        {
            if (hintSeconds > 0f && hintClearAt > 0f && Time.unscaledTime >= hintClearAt)
            {
                hintClearAt = 0f;
                if (hintText != null) hintText.text = "";
            }
        }

        /// <summary>
        /// Every PatchCord under the search roots whose name marks it as the free end.
        ///
        /// A cord is two PatchCords under one parent. Picking by name rather than by "is it plugged
        /// in" is deliberate: both ends start plugged in the scene, so at Awake there is nothing to
        /// tell them apart except which one you called the free one.
        /// </summary>
        PatchCord[] CollectFreePlugs()
        {
            var found = new System.Collections.Generic.List<PatchCord>();
            foreach (var root in Roots)
            {
                foreach (var c in root.GetComponentsInChildren<PatchCord>(true))
                {
                    if (c == null || found.Contains(c)) continue;
                    if (!string.IsNullOrEmpty(freePlugNameContains) &&
                        !c.name.Contains(freePlugNameContains)) continue;
                    found.Add(c);
                }
            }
            return found.ToArray();
        }

        /// <summary>The other plug on the same cord - the fixed end.</summary>
        public static PatchCord Partner(PatchCord plug)
        {
            if (plug == null || plug.transform.parent == null) return null;
            foreach (var other in plug.transform.parent.GetComponentsInChildren<PatchCord>(true))
            {
                if (other != null && other != plug) return other;
            }
            return null;
        }

        /// <summary>The port number this plug has to reach, read off its own fixed end. -1 when that
        /// cannot be worked out - no partner, or the partner is not plugged into anything.</summary>
        public int RequiredPort(PatchCord plug)
        {
            if (!matchPartnerPortNumber || plug == null) return -1;

            // First choice: the socket the cord's own fixed end is sitting in. It is the truth,
            // because it is where the cord physically comes from.
            PatchCord partner = Partner(plug);
            if (partner != null && partner.PluggedSocket != null)
                return partner.PluggedSocket.portNumber;

            // Fallback for a cord that starts completely loose - nothing is plugged, so there is no
            // socket to read. The cord's own name carries the number instead.
            if (deriveNumberFromCordName && plug.transform.parent != null)
                return TrailingNumber(plug.transform.parent.name);

            return -1;
        }

        /// <summary>The number at the end of a name: 'PatchCord_03' gives 3. -1 if it ends in
        /// anything else.</summary>
        public static int TrailingNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;

            int i = s.Length - 1;
            while (i >= 0 && char.IsDigit(s[i])) i--;
            if (i == s.Length - 1) return -1;              // no trailing digits at all

            int value;
            return int.TryParse(s.Substring(i + 1), out value) ? value : -1;
        }

        bool IsCorrectlyPlugged(PatchCord plug)
        {
            if (plug == null || !plug.IsPlugged || plug.PluggedSocket == null) return false;
            if (plug.PluggedSocket.groupId != targetGroupId) return false;

            int want = RequiredPort(plug);
            if (want < 0) return true;                       // nothing to match against
            return plug.PluggedSocket.portNumber == want;
        }

        // ------------------------------------------------------------------
        // Starting
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts this phase. TRUE means it took over, which is what tells PunchDownStation to keep
        /// the view open. FALSE when there is nothing to do - no cords found, or already completed
        /// this visit - so a half-built phase is skipped rather than trapping the player.
        /// </summary>
        public bool Begin()
        {
            if (IsRunning) return true;
            if (HasFinished) return false;
            if (freePlugs == null || freePlugs.Length == 0)
            {
                Debug.LogWarning("[PatchCordBoard] '" + phaseName + "' has no cords under its search " +
                                 "root(s) whose name contains '" + freePlugNameContains + "', so " +
                                 "there is nothing to plug in. Skipping this phase.", this);
                return false;
            }

            IsRunning = true;
            wrongAttempts = 0;

            foreach (var p in freePlugs)
            {
                if (p == null) continue;

                // The cord raycasts from whatever is in its own Drag Camera field. Handing it the
                // live stage camera here means a camera swapped at runtime can never leave a cord
                // picking from a camera that is not rendering - the commonest way a drag stage ends
                // up quietly dead.
                if (stageCamera != null) p.dragCamera = stageCamera;

                if (unplugOnBegin) PullOut(p);
                p.isLocked = false;
            }

            foreach (var go in hideOnBegin) { if (go != null) go.SetActive(false); }
            foreach (var go in showOnBegin) { if (go != null) go.SetActive(true); }
            foreach (var mb in disableOnBegin) { if (mb != null) mb.enabled = false; }
            SetBlockersHidden(true);

            if (restrictSnapToTargetGroup) RJ45Socket.OnlyGroup = targetGroupId;

            MoveCameraToPose();
            RefreshUI();
            SetHint("Run each cord to the matching " + socketNoun + ".");

            OnPhaseBegan?.Invoke();

            if (introDialog != null && DialogManager.Instance != null && !DialogManager.Instance.IsDialogOpen)
                DialogManager.Instance.ShowDialog(introDialog);

            if (logProgress) LogRoster();
            else Debug.LogWarning("[PatchCordBoard] '" + phaseName + "' started - " + freePlugs.Length +
                                  " cords to run into group '" + targetGroupId + "'.", this);
            return true;
        }

        /// <summary>
        /// Unplugs a cord and moves it somewhere that reads as unplugged.
        ///
        /// Unplug() on its own only clears the connection - the plug stays sitting exactly where it
        /// was, which on screen is still inside the jack. Home Anchor wins if the cord has one;
        /// otherwise the plug is drawn back along its own axis, which is the direction it would come
        /// out of a real jack.
        /// </summary>
        void PullOut(PatchCord plug)
        {
            // Whether it was actually IN anything decides what happens next, so it has to be read
            // before Unplug() clears it.
            bool wasPlugged = plug.IsPlugged;

            suppressCallback = true;
            plug.Unplug();
            suppressCallback = false;

            if (plug.homeAnchor != null)
            {
                // Home wins in every case. A cord that starts loose has its resting pose here, and
                // a retry should put it back there rather than wherever the last drag ended.
                plug.transform.SetPositionAndRotation(plug.homeAnchor.position, plug.homeAnchor.rotation);
            }
            else if (wasPlugged && unplugOffset > 0f)
            {
                // The offset only makes sense as "back it out of the jack it was in".
                //
                // Applying it unconditionally was a bug: this runs from ResetStage() on every entry
                // to the station and from Retry() after every failure, so a cord that was ALREADY
                // unplugged would creep backwards by Unplug Offset each time - five centimetres per
                // visit, drifting further from the panel the more the player retried.
                plug.transform.position -= plug.transform.forward * unplugOffset;
            }

            plug.RebuildRope();
        }

        /// <summary>
        /// Takes the objects in Hide While Running out of shot, and puts them back exactly as they
        /// were.
        ///
        /// The restore records what it found rather than switching everything on. A cable rig has
        /// renderers that are deliberately off - PunchStageConductorRig hides each source mesh once
        /// it has built the rope that replaces it - and blanket-enabling them on the way out would
        /// bring back eight straight cylinders sitting inside eight simulated wires.
        /// </summary>
        void SetBlockersHidden(bool hidden)
        {
            if (hideWhileRunning == null || hideWhileRunning.Length == 0) return;

            if (hidden)
            {
                if (blockersHidden) return;
                blockersHidden = true;

                if (blockers == null)
                {
                    var list = new System.Collections.Generic.List<Renderer>();
                    foreach (var t in hideWhileRunning)
                    {
                        if (t == null) continue;
                        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r != null && !list.Contains(r)) list.Add(r);
                        }
                    }
                    blockers = list.ToArray();
                    blockerWasEnabled = new bool[blockers.Length];
                }

                for (int i = 0; i < blockers.Length; i++)
                {
                    if (blockers[i] == null) continue;
                    blockerWasEnabled[i] = blockers[i].enabled;
                    blockers[i].enabled = false;
                }
                return;
            }

            // Nothing was hidden, so there is nothing to restore. Without this guard a restore that
            // runs before any hide - ResetStage() on the station's first entry - would apply an
            // all-false record and switch the objects off instead of on.
            if (!blockersHidden) return;
            blockersHidden = false;

            if (blockers == null) return;
            for (int i = 0; i < blockers.Length; i++)
            {
                if (blockers[i] == null) continue;
                blockers[i].enabled = blockerWasEnabled[i];
            }
        }

        /// <summary>
        /// Prints exactly which plugs this phase is supervising and what each one needs.
        ///
        /// This is the line that answers "why did it never say complete". The phase finishes when
        /// the number of cords sitting in the target group reaches the number of plugs it collected,
        /// so if it collected sixteen plugs for eight cords - which is what happens when Free Plug
        /// Name Contains is empty and BOTH ends of every cord are picked up - that count can never
        /// be reached and nothing looks wrong anywhere else.
        /// </summary>
        [ContextMenu("Report cord state")]
        public void LogRoster()
        {
            var sb = new StringBuilder();
            sb.Append("[PatchCordBoard] '").Append(phaseName).Append("' supervising ")
              .Append(freePlugs == null ? 0 : freePlugs.Length)
              .Append(" plug(s), all of which must reach group '").Append(targetGroupId)
              .Append("' for the phase to complete:\n");

            if (freePlugs != null)
            {
                foreach (var p in freePlugs)
                {
                    if (p == null) { sb.AppendLine("    (empty slot)"); continue; }

                    int want = RequiredPort(p);
                    sb.Append("    ").Append(p.transform.parent != null ? p.transform.parent.name + "/" : "")
                      .Append(p.name)
                      .Append("   needs: ").Append(want > 0 ? socketNoun + " " + want : "any " + socketNoun)
                      .Append("   now: ");

                    if (!p.IsPlugged || p.PluggedSocket == null) sb.Append("unplugged");
                    else sb.Append(p.PluggedSocket.groupId).Append(" ").Append(p.PluggedSocket.portNumber)
                           .Append(IsCorrectlyPlugged(p) ? "  OK" : "  WRONG");
                    sb.Append('\n');
                }
            }

            int inGroup = 0;
            foreach (var sock in RJ45Socket.All)
            {
                if (sock != null && sock.groupId == targetGroupId) inGroup++;
            }
            sb.Append("    ---- ").Append(PatchedCount()).Append(" of ")
              .Append(freePlugs == null ? 0 : freePlugs.Length)
              .Append(" run. ").Append(inGroup).Append(" socket(s) exist in group '")
              .Append(targetGroupId).Append("'.");

            Debug.LogWarning(sb.ToString(), this);
        }

        /// <summary>
        /// Prints every RJ45Socket in the scene, grouped, with its port number - and flags any group
        /// where two sockets claim the same number.
        ///
        /// Port Number is the one field that CANNOT be multi-edited, so it is the one most often
        /// left at its default across a whole row. Eight sockets all reading 4 look completely
        /// normal in the Scene view and produce exactly this: a cord dropped into the jack you can
        /// see is the right one is reported as port 4, and refused.
        /// </summary>
        [ContextMenu("Report sockets")]
        public void LogSockets()
        {
            var groups = new System.Collections.Generic.Dictionary<string,
                             System.Collections.Generic.List<RJ45Socket>>();

            foreach (var s in RJ45Socket.All)
            {
                if (s == null) continue;
                string key = s.groupId ?? "";
                if (!groups.ContainsKey(key))
                    groups[key] = new System.Collections.Generic.List<RJ45Socket>();
                groups[key].Add(s);
            }

            var sb = new StringBuilder();
            sb.Append("[PatchCordBoard] Sockets in the scene, by group:\n");

            foreach (var pair in groups)
            {
                sb.Append("  group '").Append(pair.Key).Append("'  (")
                  .Append(pair.Value.Count).Append(" sockets)\n");

                var seen = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var s in pair.Value)
                {
                    Vector3 wp = s.Snap.position;
                    sb.Append("      port ").Append(s.portNumber).Append("   ").Append(s.name)
                      .Append("   at ").Append(wp.x.ToString("F3")).Append(", ")
                      .Append(wp.y.ToString("F3")).Append(", ").Append(wp.z.ToString("F3"));
                    if (seen.ContainsKey(s.portNumber))
                        sb.Append("   <-- DUPLICATE, '").Append(seen[s.portNumber]).Append("' already claims it");
                    else
                        seen[s.portNumber] = s.name;
                    sb.Append('\n');
                }
            }

            sb.Append("  Remember: Port Number cannot be multi-edited. Set each one individually.");
            Debug.LogWarning(sb.ToString(), this);
        }

        /// <summary>Duplicate port numbers inside one group, which make a socket unreachable and a
        /// cord refusable no matter where the player puts it.</summary>
        void CheckSocketNumbering(StringBuilder sb)
        {
            var seen = new System.Collections.Generic.Dictionary<int, string>();
            int inGroup = 0;

            foreach (var s in RJ45Socket.All)
            {
                if (s == null || s.groupId != targetGroupId) continue;
                inGroup++;

                if (seen.ContainsKey(s.portNumber))
                    sb.AppendLine("  - '" + s.name + "' and '" + seen[s.portNumber] + "' both claim " +
                                  targetGroupId + " port " + s.portNumber + ". Port Number cannot be " +
                                  "multi-edited - set each socket's individually. Right-click this " +
                                  "component > Report sockets for the full list.");
                else
                    seen[s.portNumber] = s.name;
            }

            if (inGroup > 0 && seen.Count < inGroup)
                sb.AppendLine("  - Group '" + targetGroupId + "' has " + inGroup + " sockets but only " +
                              seen.Count + " distinct port numbers between them, so some cords can " +
                              "never be placed correctly.");
        }

        /// <summary>
        /// Are the sockets in the target group actually in different PLACES?
        ///
        /// PatchCord picks its target with RJ45Socket.NearestFree, so two anchors sitting on top of
        /// each other are indistinguishable to it: every drop resolves to whichever one comes first
        /// in the list, and the player is told they aimed at a port they never went near. Duplicating
        /// an anchor and forgetting to offset it produces exactly that.
        /// </summary>
        void CheckSocketSpacing(StringBuilder sb)
        {
            var live = new System.Collections.Generic.List<RJ45Socket>();
            foreach (var s in RJ45Socket.All)
            {
                if (s != null && s.groupId == targetGroupId) live.Add(s);
            }
            if (live.Count < 2) return;

            float minGap = float.MaxValue;
            RJ45Socket a = null, b = null;

            for (int i = 0; i < live.Count; i++)
            {
                for (int j = i + 1; j < live.Count; j++)
                {
                    float d = Vector3.Distance(live[i].Snap.position, live[j].Snap.position);
                    if (d < minGap) { minGap = d; a = live[i]; b = live[j]; }
                }
            }

            if (minGap < 0.001f)
                sb.AppendLine("  - '" + a.name + "' and '" + b.name + "' are in the SAME PLACE (" +
                              minGap.ToString("F4") + " apart). A dropped cord cannot tell them " +
                              "apart - it always resolves to whichever comes first - so it will " +
                              "report the same port however carefully the player aims. Move the " +
                              "anchors onto their own jacks.");
            else if (minGap < 0.005f)
                sb.AppendLine("  - '" + a.name + "' and '" + b.name + "' are only " +
                              minGap.ToString("F4") + " apart, which is far tighter than " +
                              "PatchCord's Snap Distance. Cords will land in the wrong one.");
        }

        void MoveCameraToPose()
        {
            if (cameraPose == null || stageCamera == null) return;
            if (cameraRoutine != null) StopCoroutine(cameraRoutine);
            cameraRoutine = StartCoroutine(FlyCamera(cameraPose.position, cameraPose.rotation));
        }

        System.Collections.IEnumerator FlyCamera(Vector3 toPos, Quaternion toRot)
        {
            SetCordsLocked(true);

            Transform cam = stageCamera.transform;
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;

            float t = 0f;
            float duration = Mathf.Max(0.0001f, cameraMoveDuration);
            while (t < 1f)
            {
                // Unscaled: the intro dialog can stop the clock, and a move on Time.deltaTime would
                // sit frozen mid-flight with the player staring at nothing.
                t += Time.unscaledDeltaTime / duration;
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                cam.position = Vector3.Lerp(fromPos, toPos, f);
                cam.rotation = Quaternion.Slerp(fromRot, toRot, f);
                yield return null;
            }
            cam.SetPositionAndRotation(toPos, toRot);

            if (IsRunning) SetCordsLocked(false);
            cameraRoutine = null;
        }

        /// <summary>Locked cords ignore all input. Used to hold them still while the camera flies -
        /// a drag started mid-flight is computed from a camera that is still moving, so the plug
        /// slides across the screen on its own.</summary>
        void SetCordsLocked(bool locked)
        {
            if (freePlugs == null) return;
            foreach (var p in freePlugs) { if (p != null) p.isLocked = locked; }
        }

        // ------------------------------------------------------------------
        // Watching the cords
        // ------------------------------------------------------------------

        void HandleConnectionChanged(RJ45Socket socket, bool connected)
        {
            if (suppressCallback || !IsRunning || HasFinished) return;
            if (!connected) { RefreshUI(); return; }

            PatchCord plug = socket != null ? socket.Current : null;
            if (plug == null) { RefreshUI(); return; }

            if (socket.groupId != targetGroupId)
            {
                Reject(plug, "That is not a " + socketNoun + ".");
                return;
            }

            if (requireCorrectPort && !IsCorrectlyPlugged(plug))
            {
                int want = RequiredPort(plug);
                Reject(plug, want > 0
                    ? "This cord goes to " + socketNoun + " " + want + ", not " + socket.portNumber + "."
                    : "That is the wrong " + socketNoun + " for this cord.");
                return;
            }

            OnCordPatched?.Invoke();

            int done = PatchedCount();
            if (logProgress)
                Debug.LogWarning("[PatchCordBoard] '" + phaseName + "' - " + plug.name + " into " +
                                 socketNoun + " " + socket.portNumber + ". " + done + " of " +
                                 freePlugs.Length + " run.", this);

            if (done >= freePlugs.Length) { Finish(); return; }

            SetHint(PatchedCount() + " of " + freePlugs.Length + " run.");
            RefreshUI();
        }

        /// <summary>
        /// Pops a wrongly-placed cord straight back out.
        ///
        /// Deferred by a frame on purpose. This is called from inside PatchCord's own connection
        /// event, and unplugging there would re-enter the same event while the first one is still
        /// unwinding - the classic way a listener list gets modified mid-iteration.
        /// </summary>
        void Reject(PatchCord plug, string why)
        {
            wrongAttempts++;
            SetHint(why);

            // To the Console as well as the hint, because Hint Text is optional and most of the
            // time it is not assigned - which makes a refusal completely silent. The cord goes in,
            // pops back out a frame later, and it reads as "plugging it in does nothing" rather
            // than "that was the wrong port".
            if (logProgress)
            {
                int want = RequiredPort(plug);
                string got = plug.PluggedSocket != null
                    ? "group '" + plug.PluggedSocket.groupId + "' port " + plug.PluggedSocket.portNumber
                    : "nothing";

                Debug.LogWarning("[PatchCordBoard] '" + phaseName + "' REFUSED " + plug.name +
                                 " - it went into " + got + ", but this phase wants group '" +
                                 targetGroupId + "'" + (want > 0 ? " port " + want : " (any port)") +
                                 ".\n    " + why, this);
            }

            StartCoroutine(RejectNextFrame(plug));
        }

        System.Collections.IEnumerator RejectNextFrame(PatchCord plug)
        {
            yield return null;
            if (plug != null) PullOut(plug);
            RefreshUI();
        }

        public int PatchedCount()
        {
            int n = 0;
            if (freePlugs != null)
            {
                foreach (var p in freePlugs)
                {
                    if (p != null && p.IsPlugged && p.PluggedSocket != null &&
                        p.PluggedSocket.groupId == targetGroupId) n++;
                }
            }
            return n;
        }

        // ------------------------------------------------------------------
        // Verdict
        // ------------------------------------------------------------------

        void Finish()
        {
            string reason;
            bool passed = Evaluate(out reason);

            HasFinished = true;
            IsRunning = false;
            LastResultPassed = passed;

            if (lockWhenIdle) SetCordsLocked(true);

            // Hand the sockets back to everyone else. Leaving the filter set would make every other
            // group in the scene unsnappable for the rest of the session.
            RJ45Socket.OnlyGroup = null;

            // Back in shot the moment the phase is over - a failure keeps them hidden, because
            // Retry() re-hides them and the player is still working in this same framing.
            SetBlockersHidden(false);

            if (progressText != null)
            {
                progressText.text = passed ? "PASS - " + phaseName + " complete." : "FAIL";
                progressText.color = passed ? passColor : failColor;
            }
            if (hintText != null)
            {
                // GUIDANCE: the fail reason explains the fault - Story only. The review screen has it.
                hintText.text = passed || !CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.CorrectiveFeedback)
                    ? "" : reason;
                hintClearAt = 0f;
            }

            if (passed)
            {
                if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                    TaskManager.Instance.ReportProgress(taskId);
                OnPhasePassed?.Invoke();
            }
            else
            {
                OnPhaseFailed?.Invoke();
            }

            Debug.LogWarning("[PatchCordBoard] '" + phaseName + "' " + (passed ? "PASSED" : "FAILED") +
                             ". " + reason, this);

            OnFinished?.Invoke(passed, reason);
        }

        public bool Evaluate(out string reason)
        {
            var wrong = new StringBuilder();
            int bad = 0;

            if (freePlugs != null)
            {
                foreach (var p in freePlugs)
                {
                    if (p == null || IsCorrectlyPlugged(p)) continue;

                    bad++;
                    if (bad > 4) continue;
                    if (wrong.Length > 0) wrong.Append(" ");

                    int want = RequiredPort(p);
                    if (!p.IsPlugged)
                        wrong.Append("Cord " + (want > 0 ? want.ToString() : p.name) +
                                     " was never plugged in.");
                    else
                        wrong.Append("Cord " + (want > 0 ? want.ToString() : p.name) + " is in " +
                                     socketNoun + " " + p.PluggedSocket.portNumber + ".");
                }
            }

            if (bad == 0)
            {
                reason = wrongAttempts == 0
                    ? "Every cord is in its matching " + socketNoun + "."
                    : "Every cord is in its matching " + socketNoun + ", after " + wrongAttempts +
                      " wrong attempt(s).";
                return true;
            }

            reason = "The cross-connect doesn't line up. " + wrong;
            if (bad > 4) reason += " (" + (bad - 4) + " more are also wrong.)";
            return false;
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        public void SetHint(string message)
        {
            if (hintText == null) return;

            // GUIDANCE: these lines coach ("each cord goes to the port with its own number") - Story
            // Mode only. In an assessment the line stays empty.
            if (!CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.Hints)) message = null;
            hintText.text = message ?? "";
            hintClearAt = (hintSeconds > 0f && !string.IsNullOrEmpty(message))
                ? Time.unscaledTime + hintSeconds
                : 0f;
        }

        void RefreshUI()
        {
            // An idle phase must not touch the readout. It is normal to point every board on the
            // station at ONE progress line and ONE hint line, and without this guard a phase that is
            // merely being reset - which happens to every phase on entry - would overwrite whatever
            // the live one had written. Two phases and a punch board sharing a label means the last
            // one to run ResetStage wins, which is nothing to do with what the player is doing.
            if (!IsRunning) return;

            if (HasFinished || progressText == null || freePlugs == null) return;
            progressText.text = PatchedCount() + " of " + freePlugs.Length + " run";
            progressText.color = progressColor;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Pulls every cord back out for another go, WITHOUT leaving the phase.
        ///
        /// Distinct from ResetStage() on purpose. ResetStage undoes Begin() entirely - it puts the
        /// punch tool back on screen and re-enables the punch camera, which is right for a fresh
        /// visit to the station and wrong for a retry, where the player should simply find the cords
        /// back where they started and the shot unchanged.
        /// </summary>
        public void Retry()
        {
            HasFinished = false;
            LastResultPassed = false;
            IsRunning = true;
            wrongAttempts = 0;

            if (freePlugs != null)
            {
                foreach (var p in freePlugs)
                {
                    if (p == null) continue;
                    PullOut(p);
                    p.isLocked = false;
                }
            }

            SetBlockersHidden(true);

            SetHint("Try again - each cord goes to the " + socketNoun + " with its own number.");
            RefreshUI();
        }

        /// <summary>
        /// Pulls every cord back out and undoes everything Begin() switched.
        ///
        /// That last part matters more than it looks. Begin() DISABLES the components in Disable On
        /// Begin - PunchStageCamera among them - and a disabled component never runs OnEnable again.
        /// Without putting them back, the second visit to the station would open with a camera script
        /// that never re-arms.
        /// </summary>
        [ContextMenu("Reset this phase")]
        public void ResetStage()
        {
            IsRunning = false;
            HasFinished = false;
            LastResultPassed = false;
            wrongAttempts = 0;

            if (cameraRoutine != null) { StopCoroutine(cameraRoutine); cameraRoutine = null; }

            foreach (var go in hideOnBegin) { if (go != null) go.SetActive(true); }
            foreach (var go in showOnBegin) { if (go != null) go.SetActive(false); }
            foreach (var mb in disableOnBegin) { if (mb != null) mb.enabled = true; }
            SetBlockersHidden(false);
            RJ45Socket.OnlyGroup = null;

            if (freePlugs != null)
            {
                foreach (var p in freePlugs)
                {
                    if (p == null) continue;
                    PullOut(p);
                    if (lockWhenIdle) p.isLocked = true;
                }
            }

            SetHint(null);
            RefreshUI();
        }

        /// <summary>Says out loud what is missing, rather than the phase quietly doing nothing.</summary>
        [ContextMenu("Check my setup")]
        public void ValidateSetup()
        {
            var sb = new StringBuilder();

            // An empty filter is the trap. CollectFreePlugs skips the name test entirely when this
            // is blank, so BOTH ends of every cord are picked up - sixteen plugs for eight cords.
            // The fixed ends sit in 'panel' and never count toward the target group, so the phase
            // can never reach its own total and simply never completes.
            if (string.IsNullOrEmpty(freePlugNameContains))
                sb.AppendLine("  - Free Plug Name Contains is EMPTY, so every PatchCord under the " +
                              "search root(s) was collected - both ends of every cord. This phase " +
                              "can never complete, because it waits for all of them to reach group '" +
                              targetGroupId + "' and the fixed ends never will. Set it to 'Plug_B'.");

            int n = freePlugs == null ? 0 : freePlugs.Length;
            if (n == 0)
                sb.AppendLine("  - No cords found. This looks for PatchCord components under the " +
                              "search root(s) whose name contains '" + freePlugNameContains + "'. " +
                              "Check Search Roots, and check your plugs are named to match.");

            int inGroup = 0;
            foreach (var s in RJ45Socket.All)
            {
                if (s != null && s.groupId == targetGroupId) inGroup++;
            }
            if (inGroup == 0)
                sb.AppendLine("  - No RJ45Socket in the scene has Group Id '" + targetGroupId +
                              "', so these cords have nowhere to go. Check the spelling - it is " +
                              "case-sensitive.");
            else if (inGroup < n)
                sb.AppendLine("  - " + n + " cords but only " + inGroup + " sockets with Group Id '" +
                              targetGroupId + "'. At least one cord can never be plugged in.");

            if (freePlugs != null)
            {
                foreach (var p in freePlugs)
                {
                    if (p == null) continue;

                    if (Partner(p) == null)
                        sb.AppendLine("  - '" + p.name + "' has no second PatchCord under its parent, " +
                                      "so its fixed end cannot be found and there is no port number " +
                                      "to match against.");
                    else if (matchPartnerPortNumber && !Partner(p).startPlugged &&
                             RequiredPort(p) < 0)
                        sb.AppendLine("  - '" + p.name + "' has nothing to match a port against: its " +
                                      "fixed end is not plugged at start, and its cord '" +
                                      (p.transform.parent != null ? p.transform.parent.name : "?") +
                                      "' does not end in a number either. ANY port in group '" +
                                      targetGroupId + "' will be accepted. Either tick Start Plugged " +
                                      "on the fixed end, or rename the cord to end in its port " +
                                      "number (PatchCord_03), or untick Match Partner Port Number " +
                                      "if the numbering genuinely does not matter here.");

                    if (p.rope == null)
                        sb.AppendLine("  - '" + p.name + "' has no Rope assigned, so nothing will " +
                                      "be drawn between the plugs.");
                }
            }

            // Two plugs pointed at one socket. Plug() returns early when a socket is already
            // occupied, so the second cord simply never seats - silently, with no error, and the
            // cord just sits wherever the editor left it.
            if (watched != null)
            {
                var claimed = new System.Collections.Generic.Dictionary<RJ45Socket, PatchCord>();
                foreach (var p in watched)
                {
                    if (p == null || !p.startPlugged || p.startSocket == null) continue;
                    if (claimed.TryGetValue(p.startSocket, out PatchCord first))
                        sb.AppendLine("  - '" + p.name + "' and '" + first.name + "' both have Start " +
                                      "Socket set to '" + p.startSocket.name + "'. Only the first " +
                                      "one seats - the second is refused because the socket is " +
                                      "already occupied, and stays wherever the editor left it.");
                    else
                        claimed[p.startSocket] = p;
                }
            }

            CheckSocketNumbering(sb);
            CheckSocketSpacing(sb);
            ReportStartJumps(sb);

            if (cameraPose == null)
                sb.AppendLine("  - Camera Pose is empty, so the camera stays wherever the previous " +
                              "phase left it and what the player has to reach may be off screen.");

            if (sb.Length == 0)
            {
                Debug.LogWarning("[PatchCordBoard] '" + phaseName + "' looks complete - " + n +
                                 " cords, " + inGroup + " sockets in group '" + targetGroupId +
                                 "', camera pose set.", this);
                return;
            }
            Debug.LogWarning("[PatchCordBoard] '" + phaseName + "' setup is incomplete:\n" + sb, this);
        }
    }
}