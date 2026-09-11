// TaskWaypointArrow.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The "where do I go next" arrow.
    ///
    /// You give it an ORDERED list of steps. Each step names a place in the world and a condition
    /// that means "done". Every frame it walks the list from the top, finds the first step that is
    /// NOT done, and points at that one. Nothing else in the project has to know this exists - the
    /// arrow reads the state that is already there.
    ///
    /// WHY IT READS STATE INSTEAD OF BEING TOLD
    ///
    /// The obvious design is a queue that other scripts push to: the crimping station calls
    /// NextWaypoint(), the tester calls NextWaypoint() again, and so on. That breaks the first time
    /// the player does something out of order, reloads, or a step is skipped for testing - the queue
    /// and the world drift apart and the arrow starts pointing at a step the player finished ten
    /// minutes ago. Reading the same booleans the rest of the sim already keeps means the arrow
    /// cannot disagree with reality. Untick a gate, press F11, restart a procedure: the arrow just
    /// recomputes and is right again.
    ///
    /// EXECUTION ORDER 300, deliberately - after every board, station and carrier has had its Start.
    /// The arrow only ever reads, so it wants to run last and see settled values.
    ///
    /// SETUP - the short version:
    ///   1. Empty GameObject under Managers, call it "TaskWaypoint". Add this component.
    ///   2. Drop your arrow model in the scene as a separate object, drag it into World Arrow.
    ///      Any mesh will do. It gets moved and switched on/off for you - do not parent it here.
    ///   3. Fill the Steps list: a Label, a Target transform, and how the step is judged done.
    ///   4. Play. If a step is set up wrongly the Console says which one and why.
    ///
    /// Everything else on this component is optional polish.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public class TaskWaypointArrow : MonoBehaviour
    {
        /// <summary>How a step decides it is finished. Each one reads something that already exists.</summary>
        public enum DoneWhen
        {
            /// <summary>TaskManager says this task id is complete. Registered ids in COC2 are
            /// 'crimp_cable', 'test_cable' and 'punch_patch_panel'.</summary>
            TaskComplete,
            /// <summary>A PatchCordBoard phase has finished and passed.</summary>
            PhasePassed,
            /// <summary>A CableCarrier has both ends plugged in.</summary>
            CableRun,
            /// <summary>A CableCarrier has appeared on the bench - use this to point AT the cable
            /// only once it exists, on the step before running it.</summary>
            CableAvailable,
            /// <summary>A GameObject is active. Good for "the crimped cable is on the table".</summary>
            ObjectActive,
            /// <summary>A GameObject is inactive. The mirror of the above - good for "the tool is
            /// gone from the rack because the player picked it up".</summary>
            ObjectInactive,
            /// <summary>The finished cable is lying on the crimping table, waiting to be picked up.
            /// Use it for the CRIMP step: done the moment the cable exists.</summary>
            CableOnTable,
            /// <summary>The player has the crimped cable in their pocket. Note this is cleared again
            /// when the cable goes into the tester, so a step using it wants Latch When Done ticked
            /// unless you actually want the arrow to come back.</summary>
            CableInPocket,
            /// <summary>The player simply walked close enough. A pure signposting step.</summary>
            PlayerArrived,
            /// <summary>Nothing detects this - you call CompleteStep from a UnityEvent. The escape
            /// hatch for anything the others cannot see.</summary>
            Manual,

            // ---- ADDED LATER - AND THEREFORE ADDED AT THE END --------------
            //
            // Unity serialises an enum by its NUMBER, not its name. Slotting a new value into the
            // middle of this list renumbers every value below it, and every Step already saved in
            // every scene silently changes meaning - a step set to Object Active would reopen as
            // something else entirely, with nothing to show it had moved.
            //
            // New conditions go here, at the bottom, however out of place they read.

            /// <summary>The player has TAKEN a CableCarrier off the bench. Use it for the
            /// "go and pick up the cable" step: the moment it is in hand the arrow moves on to
            /// where the first end goes.
            ///
            /// It stays true for the rest of the run, so it needs no latch - unlike IsHeld, which
            /// blinks off every time an end seats.</summary>
            CablePickedUp,

            /// <summary>A CableCarrier has its FIRST end plugged in, whichever socket that was.
            /// Use it for the "plug it into the patch panel" step, so the arrow then sends the
            /// player on to the far end of the run.</summary>
            CableFirstEndIn
        }

        [System.Serializable]
        public class Step
        {
            [Tooltip("Shown to the player, e.g. 'Crimp the cable'. Keep it an instruction, not a " +
                     "place name - the arrow already shows the place.")]
            public string label = "Next step";

            [Tooltip("Where the arrow points. Usually the station's own transform, but an empty " +
                     "child placed at eye height reads better than a pivot on the floor.")]
            public Transform target;

            [Tooltip("How this step decides it is finished.")]
            public DoneWhen doneWhen = DoneWhen.TaskComplete;

            [Header("The one field your Done When needs")]
            [Tooltip("Done When = Task Complete. The id as registered with TaskManager.")]
            public string taskId = "";
            [Tooltip("Done When = Phase Passed.")]
            public PatchCordBoard phase;
            [Tooltip("Done When = Cable Run, Cable Available, Cable Picked Up or Cable First End In.\n\n" +
                     "One cable usually wants THREE steps in a row, all pointing at the same " +
                     "CableCarrier but at different places:\n" +
                     "  1. Cable Picked Up   -> the spawn point\n" +
                     "  2. Cable First End In -> the patch panel\n" +
                     "  3. Cable Run          -> the server (or client)")]
            public CableCarrier cable;
            [Tooltip("Done When = Object Active or Object Inactive.")]
            public GameObject witness;
            [Tooltip("Done When = Cable On Table.")]
            public CrimpedCablePickup pickup;
            [Tooltip("Done When = Player Arrived. Metres. 0 falls back to Arrival Distance.")]
            public float arriveWithin = 0f;

            [Header("Optional")]
            [Tooltip("Tick for a step that happens inside a station view, where a world arrow " +
                     "floating in the room is just clutter. The label still updates.")]
            public bool hideArrowDuringStep = false;

            [Tooltip("Once this step goes done, keep it done - even if the thing it watches goes " +
                     "back to how it was.\n\n" +
                     "This is what stops the arrow walking backwards. 'Crimp the cable' is judged " +
                     "by the cable being on the table; the moment the player picks that cable up " +
                     "it is not on the table any more, and without a latch the arrow would send " +
                     "them back to the crimping station while they are carrying it to the tester. " +
                     "Latches are cleared by Reset Steps, so the second cable still rewinds them.")]
            public bool latchWhenDone = false;

            [Tooltip("Raised once, the first time this step becomes the active one.")]
            public UnityEvent OnStepBegan;

            // Runtime only.
            [System.NonSerialized] public bool manualDone;
            [System.NonSerialized] public bool latched;
            [System.NonSerialized] public bool announced;
        }

        [Header("Steps, in order")]
        [Tooltip("Walked from the top every frame. The first one that is not done is the one the " +
                 "arrow points at, so the ORDER here is the order the player is sent around.")]
        public List<Step> steps = new List<Step>();

        [Header("World Arrow")]
        [Tooltip("Your arrow model, sitting loose in the scene. Moved to hover over the current " +
                 "target and switched off when there is nothing to point at. Leave empty if you " +
                 "only want the screen arrow or the label.")]
        public Transform worldArrow;
        [Tooltip("Height above the target the arrow floats at.")]
        public float hoverHeight = 0.6f;
        [Tooltip("How far it bobs up and down. 0 = perfectly still.")]
        public float bobAmount = 0.08f;
        [Tooltip("Bobs per second.")]
        public float bobSpeed = 1.5f;
        [Tooltip("Spin around the WORLD vertical axis, degrees per second. 0 = no spin.\n\n" +
                 "The spin is applied on top of however you rotated the arrow in the editor, so a " +
                 "cone tilted to point down keeps pointing down while it turns.")]
        public float spinSpeed = 45f;
        [Tooltip("Turn the arrow to face the camera as well as spinning. Tick this if your model " +
                 "is a flat sprite-like arrow; untick it for a 3D cone that reads from any angle.\n\n" +
                 "Like the spin, this is a yaw applied on top of your authored tilt - it will not " +
                 "stand a downward-pointing arrow back up.")]
        public bool billboardToCamera = false;
        [Tooltip("Extra rotation applied to the arrow, on top of how it sits in the editor.\n\n" +
                 "Leave this at 0,0,0 and the arrow keeps exactly the orientation you gave it in " +
                 "the Scene view. Use it when the model itself is built pointing the wrong way and " +
                 "you would rather not re-rotate it: X 180 flips a cone from up to down, X 90 tips " +
                 "a forward-pointing arrow to face the floor.")]
        public Vector3 rotationOffset = Vector3.zero;

        [Header("Screen Arrow (optional)")]
        [Tooltip("A UI arrow on your canvas. It swings around the middle of the screen pointing " +
                 "the way the player should turn, and hides itself once the target is on screen " +
                 "and the world arrow has taken over. Its image must point UP at 0 rotation.")]
        public RectTransform screenArrow;
        [Tooltip("Distance from screen centre the screen arrow orbits at, as a fraction of screen " +
                 "height. 0.25 puts it a quarter of the way out - clear of a crosshair, well " +
                 "inside the edges on any aspect ratio.")]
        public float screenArrowRadius = 0.25f;

        [Header("Label (optional)")]
        [Tooltip("Shows the current step's label.")]
        public TMP_Text labelText;
        [Tooltip("Shows how far away it is. Can be the same object as Label Text - then you get " +
                 "one line with the distance appended.")]
        public TMP_Text distanceText;
        [Tooltip("Format for the distance. {0} is metres, already rounded.")]
        public string distanceFormat = "{0} m";

        [Header("Behaviour")]
        [Tooltip("Within this many metres the player has clearly arrived, so the arrow gets out of " +
                 "the way. The label stays up.")]
        public float arrivalDistance = 2.5f;
        [Tooltip("Hide everything once every step is done, rather than leaving the last label up.")]
        public bool hideWhenAllDone = true;
        [Tooltip("The player. Left empty, the object tagged 'Player' is found at Start.")]
        public Transform player;

        [Header("Diagnostics")]
        [Tooltip("Console line every time the active step changes. Leave this on while you are " +
                 "building the list - it is one line per step, not per frame.")]
        public bool logStepChanges = true;

        [Header("Events")]
        [Tooltip("Raised when the last step is finished.")]
        public UnityEvent OnAllStepsDone;

        Camera cam;
        int activeIndex = -1;
        bool suppressed;
        bool lockedOffForAssessment;
        bool allDoneFired;
        float bobPhase;
        float spinAngle;

        // The arrow's orientation as you left it in the Scene view, read once in Awake - which runs
        // before anything in the scene has had a chance to move it. Every frame's rotation is built
        // from this, so however you tilted the model is the pose that survives.
        Quaternion authoredRotation = Quaternion.identity;

        /// <summary>Index of the step being pointed at, or -1 when there is nothing to point at.</summary>
        public int ActiveIndex { get { return activeIndex; } }

        /// <summary>The step being pointed at, or null.</summary>
        public Step ActiveStep
        {
            get { return activeIndex >= 0 && activeIndex < steps.Count ? steps[activeIndex] : null; }
        }

        void Awake()
        {
            // Awake, not Start. Nothing else has run yet, so this is the last moment the editor
            // pose is guaranteed untouched.
            if (worldArrow != null) authoredRotation = worldArrow.rotation;
        }

        void Start()
        {
            if (player == null)
            {
                GameObject tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            // No guidance during a graded run. Done before ValidateSetup so an assessment does not
            // also print a page of setup warnings about steps nobody is going to see.
            if (AssessmentMode.IsActive)
            {
                LockOffForAssessment();
                Debug.Log("[TaskWaypointArrow] Assessment mode - the arrow and its label stay off " +
                          "for this run.", this);
                return;
            }

            ValidateSetup();
        }

        void LateUpdate()
        {
            // LateUpdate, not Update. Stations move cameras and the player in Update; pointing at
            // where things were a frame ago is exactly the kind of one-frame swim that reads as the
            // arrow lagging behind the player's head.
            //
            // Unscaled time throughout - dialogs set timeScale to 0 and the arrow should keep
            // bobbing rather than freezing mid-air.
            float dt = Time.unscaledDeltaTime;
            bobPhase += dt * bobSpeed;
            spinAngle += dt * spinSpeed;

            int next = FirstUnfinishedStep();
            if (next != activeIndex) ChangeStep(next);

            Step step = ActiveStep;
            if (step == null || suppressed)
            {
                ShowWorldArrow(false);
                ShowScreenArrow(false);
                if (step == null && hideWhenAllDone)
                {
                    SetText(labelText, "");
                    if (distanceText != labelText) SetText(distanceText, "");
                }
                return;
            }

            SetText(labelText, step.label);

            if (step.target == null) { ShowWorldArrow(false); ShowScreenArrow(false); return; }

            float distance = player != null
                ? Vector3.Distance(player.position, step.target.position)
                : 0f;

            if (distanceText != null)
            {
                string d = string.Format(distanceFormat, Mathf.RoundToInt(distance));
                SetText(distanceText, distanceText == labelText ? step.label + "   " + d : d);
            }

            bool arrived = player != null && distance <= arrivalDistance;
            bool wanted = !step.hideArrowDuringStep && !arrived;

            PlaceWorldArrow(step.target, wanted);
            PlaceScreenArrow(step.target, wanted);
        }

        // ---------------------------------------------------------------- step selection

        /// <summary>
        /// First step in the list that is not finished, or -1 if they all are.
        ///
        /// Walked top to bottom EVERY frame rather than remembering a cursor. It costs a handful of
        /// boolean reads and it means a step finishing out of order - or a procedure restart putting
        /// an earlier one back - is picked up on the very next frame with no bookkeeping.
        /// </summary>
        int FirstUnfinishedStep()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                Step s = steps[i];
                if (s == null) continue;
                if (!IsStepDone(s)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Whether a step counts as finished, latch included.
        ///
        /// The latch is checked FIRST and set here rather than anywhere else, so there is exactly one
        /// place in the file where "done" is decided. Anything that asks - the arrow, the report,
        /// the validator - gets the same answer.
        /// </summary>
        bool IsStepDone(Step s)
        {
            if (s.latched) return true;

            bool done = EvaluateCondition(s);
            if (done && s.latchWhenDone) s.latched = true;
            return done;
        }

        bool EvaluateCondition(Step s)
        {
            switch (s.doneWhen)
            {
                case DoneWhen.TaskComplete:
                    if (string.IsNullOrEmpty(s.taskId)) return false;
                    // No TaskManager means nothing can ever report in, so treating the step as
                    // unfinished would pin the arrow there forever. Better to fall through.
                    if (TaskManager.Instance == null) return true;
                    return TaskManager.Instance.IsTaskComplete(s.taskId);

                case DoneWhen.PhasePassed:
                    return s.phase != null && s.phase.HasFinished && s.phase.LastResultPassed;

                case DoneWhen.CableRun:
                    return s.cable != null && s.cable.IsComplete;

                case DoneWhen.CableAvailable:
                    return s.cable != null && s.cable.IsAvailable;

                case DoneWhen.CablePickedUp:
                    return s.cable != null && s.cable.HasBeenPickedUp;

                case DoneWhen.CableFirstEndIn:
                    return s.cable != null && s.cable.FirstEndPlugged;

                case DoneWhen.ObjectActive:
                    return s.witness != null && s.witness.activeInHierarchy;

                case DoneWhen.ObjectInactive:
                    return s.witness != null && !s.witness.activeInHierarchy;

                case DoneWhen.CableOnTable:
                    return s.pickup != null && s.pickup.IsAvailable;

                case DoneWhen.CableInPocket:
                    return CableInventory.Instance != null && CableInventory.Instance.HasCable;

                case DoneWhen.PlayerArrived:
                    if (s.target == null || player == null) return false;
                    float within = s.arriveWithin > 0f ? s.arriveWithin : arrivalDistance;
                    return Vector3.Distance(player.position, s.target.position) <= within;

                case DoneWhen.Manual:
                    return s.manualDone;
            }
            return false;
        }

        void ChangeStep(int next)
        {
            activeIndex = next;

            Step s = ActiveStep;
            if (s != null)
            {
                if (!s.announced)
                {
                    s.announced = true;
                    s.OnStepBegan?.Invoke();
                }
                if (logStepChanges)
                    Debug.LogWarning("[TaskWaypointArrow] Now pointing at step " + activeIndex +
                                     ", '" + s.label + "'" +
                                     (s.target != null ? " -> " + s.target.name : " (NO TARGET SET)") +
                                     ".", this);
            }
            else
            {
                if (logStepChanges)
                    Debug.LogWarning("[TaskWaypointArrow] Every step is done. The arrow is off.", this);
                if (!allDoneFired) { allDoneFired = true; OnAllStepsDone?.Invoke(); }
            }
        }

        // ---------------------------------------------------------------- the two arrows

        void PlaceWorldArrow(Transform target, bool wanted)
        {
            if (worldArrow == null) return;
            if (!wanted) { ShowWorldArrow(false); return; }

            ShowWorldArrow(true);

            float bob = bobAmount * Mathf.Sin(bobPhase * Mathf.PI * 2f);
            worldArrow.position = target.position + Vector3.up * (hoverHeight + bob);

            // Yaw is computed on its own and MULTIPLIED ONTO the pose the arrow was authored with,
            // never assigned over it.
            //
            // The first version assigned Quaternion.Euler(0, spin, 0) straight to .rotation, which
            // is a complete orientation, not a nudge - so every frame it threw away the X 180 that
            // made the cone point down and stood the arrow back upright. Anything that spins or
            // billboards a marker has to keep the tilt the artist gave it, so: build a yaw, apply it
            // to the authored rotation, done. With Spin Speed 0 and Billboard off, yaw is identity
            // and the arrow is left exactly as it sits in the Scene view.
            Quaternion yaw = Quaternion.identity;

            if (billboardToCamera)
            {
                Camera c = ActiveCamera();
                if (c != null)
                {
                    // Yaw only. A full LookAt tips the arrow over whenever the player looks up or
                    // down, which makes a floating marker look like it is falling.
                    Vector3 flat = worldArrow.position - c.transform.position;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > 0.0001f)
                        yaw = Quaternion.LookRotation(flat.normalized);
                }
            }
            else if (spinSpeed != 0f)
            {
                yaw = Quaternion.Euler(0f, spinAngle, 0f);
            }

            worldArrow.rotation = yaw * authoredRotation * Quaternion.Euler(rotationOffset);
        }

        void PlaceScreenArrow(Transform target, bool wanted)
        {
            if (screenArrow == null) return;

            Camera c = ActiveCamera();
            if (!wanted || c == null) { ShowScreenArrow(false); return; }

            Vector3 vp = c.WorldToViewportPoint(target.position);

            // z <= 0 means the target is BEHIND the camera, where the viewport maths mirrors the
            // point and the arrow would confidently send the player the wrong way. Flip it back.
            bool behind = vp.z <= 0f;
            if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }

            bool onScreen = !behind && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f;
            if (onScreen) { ShowScreenArrow(false); return; }   // world arrow has it covered

            ShowScreenArrow(true);

            Vector2 fromCentre = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (fromCentre.sqrMagnitude < 0.000001f) fromCentre = new Vector2(0f, 1f);
            fromCentre = fromCentre.normalized;

            float radius = Screen.height * screenArrowRadius;
            screenArrow.anchoredPosition = new Vector2(fromCentre.x * radius, fromCentre.y * radius);

            // The image is authored pointing up, so 0 degrees is +Y and the angle is measured from
            // there, clockwise-negative - which is what Unity's Z rotation wants.
            float angle = Mathf.Atan2(fromCentre.y, fromCentre.x) * Mathf.Rad2Deg - 90f;
            screenArrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        void ShowWorldArrow(bool on)
        {
            if (worldArrow != null && worldArrow.gameObject.activeSelf != on)
                worldArrow.gameObject.SetActive(on);
        }

        void ShowScreenArrow(bool on)
        {
            if (screenArrow != null && screenArrow.gameObject.activeSelf != on)
                screenArrow.gameObject.SetActive(on);
        }

        /// <summary>
        /// The camera to measure against. Re-fetched whenever the cached one has gone away or been
        /// switched off, because the stations swap cameras and a stale reference points the screen
        /// arrow using a camera nobody is looking through.
        /// </summary>
        Camera ActiveCamera()
        {
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            return cam;
        }

        void SetText(TMP_Text t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }

        // ---------------------------------------------------------------- public API

        /// <summary>
        /// Marks a Manual step done, by index. Hook this to a UnityEvent when none of the automatic
        /// conditions can see what you need.
        /// </summary>
        public void CompleteStep(int index)
        {
            if (index < 0 || index >= steps.Count) return;
            steps[index].manualDone = true;
        }

        /// <summary>Marks the step currently being pointed at as done. For a UnityEvent with no
        /// argument - a station's On Stage Closed, say.</summary>
        public void CompleteCurrentStep()
        {
            if (activeIndex >= 0) CompleteStep(activeIndex);
        }

        /// <summary>
        /// Hides the arrow without forgetting where it was.
        ///
        /// Hook a station's On Stage Opened here and On Stage Closed to Resume(). Inside a station
        /// the player is looking through a fixed camera at a patch panel; an arrow hovering over a
        /// bench somewhere behind them is noise.
        /// </summary>
        public void Suppress() { SetSuppressed(true); }

        /// <summary>The other half of Suppress.</summary>
        public void Resume() { SetSuppressed(false); }

        public void SetSuppressed(bool on)
        {
            // An assessment run has the arrow off for the whole run, and nothing gets to turn it
            // back on. Stations call Resume() when their close-up closes - PunchDownStation's On
            // Stage Closed is wired to it - so without this latch the arrow would reappear the
            // moment the learner stepped out of the first station, which is precisely the guidance
            // an assessment is supposed to withhold.
            if (lockedOffForAssessment && !on) return;

            if (suppressed == on) return;
            suppressed = on;
            if (on) { ShowWorldArrow(false); ShowScreenArrow(false); }
        }

        /// <summary>
        /// Switches the arrow off for a graded run and keeps it off.
        ///
        /// WHY THE ARROW MUST GO
        /// The waypoint is teaching scaffolding: it tells the learner where the next step happens.
        /// Under assessment the whole point is to find out whether they know where to go, so an
        /// arrow pointing at the patch panel hands them the answer to the question being asked.
        /// TESDA competency evidence gathered with the arrow up is not evidence of competence.
        ///
        /// Called from Start when AssessmentMode is active. Also public so an assessment manager
        /// can call it directly.
        /// </summary>
        public void LockOffForAssessment()
        {
            lockedOffForAssessment = false;   // let the SetSuppressed call through
            SetSuppressed(true);
            lockedOffForAssessment = true;

            // labelText and distanceText are allowed to be the SAME object - that is how you get
            // one line with the distance appended - so the second call would be switching off
            // something already off. Harmless, but the null checks are what matter: both are
            // optional fields and a scene may have neither.
            if (labelText != null) labelText.gameObject.SetActive(false);
            if (distanceText != null && distanceText != labelText)
                distanceText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Rewinds the list: every Manual step back to not-done, every latch released.
        ///
        /// Hook this to COC2ProcedureReset's On Procedure Restarted. The player builds two cables in
        /// COC2, and the second run puts the crimping table back to how it was - the waypoints have
        /// to rewind with it or the arrow starts the second cable pointing at the tester.
        /// </summary>
        public void ResetSteps()
        {
            foreach (Step s in steps)
                if (s != null) { s.manualDone = false; s.latched = false; s.announced = false; }
            allDoneFired = false;
        }

        /// <summary>Old name for ResetSteps, kept so existing UnityEvent wiring keeps working.</summary>
        public void ResetManualSteps() { ResetSteps(); }

        // ---------------------------------------------------------------- self-reporting

        /// <summary>
        /// Says what is wrong with the list, at Play, in one message. A waypoint that silently
        /// points nowhere is worse than no waypoint - the player trusts it and walks the wrong way.
        /// </summary>
        void ValidateSetup()
        {
            List<string> problems = new List<string>();

            if (steps.Count == 0)
                problems.Add("Steps is empty. Nothing will ever be pointed at.");

            if (worldArrow == null && screenArrow == null && labelText == null)
                problems.Add("World Arrow, Screen Arrow and Label Text are all empty, so this " +
                             "component has no way to show the player anything. Assign at least one.");

            if (worldArrow != null && worldArrow.IsChildOf(transform))
                problems.Add("World Arrow '" + worldArrow.name + "' is a child of this object. It " +
                             "gets moved around the room, so parenting it here drags this object's " +
                             "children with it. Move it out to the scene root.");

            if (player == null)
                problems.Add("No object tagged 'Player' was found, so distance checks and Player " +
                             "Arrived steps cannot work. Tag the player, or fill the Player field.");

            for (int i = 0; i < steps.Count; i++)
            {
                Step s = steps[i];
                string where = "Step " + i + " ('" + (s != null ? s.label : "null") + "')";

                if (s == null) { problems.Add("Step " + i + " is empty."); continue; }
                if (s.target == null) problems.Add(where + " has no Target, so it cannot be pointed at.");

                switch (s.doneWhen)
                {
                    case DoneWhen.TaskComplete:
                        if (string.IsNullOrEmpty(s.taskId))
                            problems.Add(where + " is judged by Task Complete but Task Id is blank. " +
                                         "COC2 registers 'crimp_cable', 'test_cable' and " +
                                         "'punch_patch_panel'.");
                        break;
                    case DoneWhen.PhasePassed:
                        if (s.phase == null) problems.Add(where + " is judged by Phase Passed but Phase is empty.");
                        break;
                    case DoneWhen.CableRun:
                    case DoneWhen.CableAvailable:
                    case DoneWhen.CablePickedUp:
                    case DoneWhen.CableFirstEndIn:
                        if (s.cable == null) problems.Add(where + " is judged by a cable but Cable is empty.");
                        break;
                    case DoneWhen.ObjectActive:
                    case DoneWhen.ObjectInactive:
                        if (s.witness == null) problems.Add(where + " is judged by an object but Witness is empty.");
                        break;
                    case DoneWhen.CableOnTable:
                        if (s.pickup == null) problems.Add(where + " is judged by Cable On Table but Pickup is empty.");
                        break;
                    case DoneWhen.CableInPocket:
                        if (!s.latchWhenDone)
                            problems.Add(where + " is judged by Cable In Pocket without Latch When " +
                                         "Done. The pocket is emptied again when the cable goes into " +
                                         "the tester, so the arrow will jump back here mid-run.");
                        break;
                }

                // A step that is already finished at Play is not an error - the player may be
                // resuming - but a step that can NEVER be finished is, and it is worth saying which.
                if (s.doneWhen == DoneWhen.Manual && !HasManualCaller())
                    problems.Add(where + " is Manual. Remember to call CompleteStep(" + i + ") from " +
                                 "a UnityEvent, or the arrow will stop here forever.");
            }

            if (problems.Count == 0)
            {
                Debug.LogWarning("[TaskWaypointArrow] " + steps.Count + " step(s), all wired. " +
                                 ArrowPoseLine() +
                                 " Right-click the component > Report waypoints to see the run order.", this);
                return;
            }

            string msg = "[TaskWaypointArrow] Setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>
        /// Prints the orientation the arrow was captured with, so "my arrow is not pointing down"
        /// has a number attached to it rather than being a matter of opinion.
        /// </summary>
        string ArrowPoseLine()
        {
            if (worldArrow == null) return "No World Arrow assigned.";

            Vector3 e = authoredRotation.eulerAngles;
            string line = "World Arrow '" + worldArrow.name + "' kept at its editor rotation (" +
                          e.x.ToString("F0") + ", " + e.y.ToString("F0") + ", " + e.z.ToString("F0") + ")";
            if (rotationOffset != Vector3.zero)
                line += " plus a Rotation Offset of (" + rotationOffset.x.ToString("F0") + ", " +
                        rotationOffset.y.ToString("F0") + ", " + rotationOffset.z.ToString("F0") + ")";
            return line + ". If it points the wrong way, rotate the arrow object itself in the " +
                   "Scene view - the spin no longer overwrites it.";
        }

        // There is no way to inspect UnityEvent wiring from another object at runtime, so this is a
        // deliberate stub: it exists so the Manual warning above reads as a reminder rather than an
        // accusation, and so the intent is obvious to whoever changes this next.
        bool HasManualCaller() { return false; }

        /// <summary>
        /// Prints the whole run order and what each step is waiting for. The fastest way to answer
        /// "why is the arrow stuck there".
        /// </summary>
        [ContextMenu("Report waypoints")]
        public void LogWaypoints()
        {
            string msg = "[TaskWaypointArrow] " + steps.Count + " step(s):";
            for (int i = 0; i < steps.Count; i++)
            {
                Step s = steps[i];
                if (s == null) { msg += "\n    " + i + ". (empty)"; continue; }

                bool done = IsStepDone(s);
                msg += "\n    " + (i == activeIndex ? " >" : "  ") + i + ". " + s.label +
                       "   target: " + (s.target != null ? s.target.name : "NONE") +
                       "   done when: " + s.doneWhen + " " + WaitingOn(s) +
                       "   now: " + (done ? "DONE" : "waiting") +
                       (s.latched ? " (latched)" : s.latchWhenDone ? " (latching)" : "");
            }
            msg += "\n    ---- active step: " + (activeIndex < 0 ? "none, all done" : activeIndex.ToString()) +
                   (suppressed ? "   (SUPPRESSED - something called Suppress and never called Resume)" : "");
            Debug.LogWarning(msg, this);
        }

        string WaitingOn(Step s)
        {
            switch (s.doneWhen)
            {
                case DoneWhen.TaskComplete: return "'" + s.taskId + "'";
                case DoneWhen.PhasePassed: return s.phase != null ? "'" + s.phase.phaseName + "'" : "(none)";
                case DoneWhen.CableRun:
                case DoneWhen.CableAvailable:
                case DoneWhen.CablePickedUp:
                case DoneWhen.CableFirstEndIn: return s.cable != null ? "'" + s.cable.cableName + "'" : "(none)";
                case DoneWhen.ObjectActive:
                case DoneWhen.ObjectInactive: return s.witness != null ? "'" + s.witness.name + "'" : "(none)";
                case DoneWhen.CableOnTable: return s.pickup != null ? "'" + s.pickup.name + "'" : "(none)";
                case DoneWhen.CableInPocket: return "CableInventory";
                case DoneWhen.PlayerArrived: return "within " + (s.arriveWithin > 0f ? s.arriveWithin : arrivalDistance) + "m";
                default: return "";
            }
        }
    }
}