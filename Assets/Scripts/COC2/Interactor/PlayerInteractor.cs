using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CSSSimulation.Interaction
{
    /// <summary>
    /// Attach this to the player. It keeps the list of interactables the player is standing at,
    /// picks the nearest one, shows a prompt, and fires Interact() on the right control.
    ///
    /// A LIST, NOT A SINGLE REFERENCE
    ///
    /// This used to remember one "current" interactable, overwritten by whichever trigger reported
    /// in last. Two problems followed from that, and both have cost real time on this project.
    ///
    /// The first is stale buttons. A pickup deactivates its own GameObject the moment it is
    /// collected - the crimping tool and the crimped cable both do - and a deactivated object never
    /// sends OnTriggerExit. So nothing ever cleared the reference, and the Pick Up button stayed on
    /// screen long after the player had walked away from an object that no longer exists.
    ///
    /// The second is the wrong target. With two stations overlapping, the last one to report in
    /// won, whether or not it was the one the player was standing at - so E opened the client PC
    /// while the player faced the server.
    ///
    /// MEASURED EACH FRAME, NOT REMEMBERED FROM AN EVENT
    ///
    /// The list is still fed by the triggers, but the triggers are no longer the last word on it.
    /// Every frame this re-measures the real distance from the player to each interactable's
    /// trigger and corrects anything the events left wrong.
    ///
    /// That is what fixes "I configure the server, step away, and now E does nothing at either
    /// machine". A trigger event is a one-off: send it at a moment when the receiving component is
    /// switched off, or while the player object is hidden for a station view, and it is simply
    /// lost. Nothing re-sends it, because from physics' point of view nothing has happened since -
    /// the player never moved. The in-range flag is then stuck False for the rest of the session
    /// and the station is dead, while the player is standing directly in front of it.
    ///
    /// Re-measuring cannot get stuck, because it does not remember anything. Whatever went wrong -
    /// a component disabled, a player object hidden, a collider toggled by a camera switch, an
    /// event that arrived at nobody - the next frame's measurement says the player is 0.4 m from
    /// the server's trigger, and the server is in range again. The Console says so when it
    /// happens, so a scene fault still gets reported rather than silently papered over.
    ///
    /// TWO CONTROLS
    ///
    /// Every Interactable declares whether it is a pickup or a station. A pickup answers to G and
    /// to the Pick Up button; everything else answers to E and to the Interact button. Only the
    /// button belonging to the object in front of the player is shown, and the two never cover for
    /// each other - a control that silently does a second job is indistinguishable from one doing
    /// its own, so a missing button would go unnoticed until a learner on a phone could not lift
    /// the crimping tool. The missing button is reported at Start instead.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerInteractor : MonoBehaviour
    {
        public static PlayerInteractor Instance { get; private set; }

        /// <summary>
        /// Asks for the world prompt to stay hidden for this frame. Call it every frame the prompt
        /// should be off.
        ///
        /// Deliberately a request rather than a switch. Several things want the prompt gone at
        /// once - a station view, the cable choice panel, an open dialog - and with a plain
        /// on/off flag the last writer each frame would win, so whichever of them ran second would
        /// turn the prompt back on while the first still wanted it hidden. A per-frame request
        /// cannot conflict: anyone may raise it, nobody can lower it, and it resets itself.
        ///
        /// Kept as a static here rather than as a reference to the station code, so this component
        /// stays a generic interactor that knows nothing about COC2.
        /// </summary>
        public static void SuppressPromptThisFrame() { suppressRequested = true; }

        static bool suppressRequested;

        [Header("Input")]
        [Tooltip("Operates stations, panels and machines.")]
        public KeyCode interactKey = KeyCode.E;

        [Tooltip("Picks things up - the crimping tool, a crimped cable, a cable run.")]
        public KeyCode pickupKey = KeyCode.G;

        [Header("Prompt UI (optional)")]
        [Tooltip("A simple UI panel/object showing the prompt. Enabled/disabled automatically.")]
        public GameObject promptUI;
        [Tooltip("Optional - assign a TMP_Text component here if you want the prompt to show " +
                 "each Interactable's own promptText. Leave empty to just show/hide promptUI as-is.")]
        public TMP_Text promptLabel;

        [Tooltip("Show the text prompt on touch devices too.\n\n" +
                 "Off by default. On a phone the on-screen button is already labelled, and a line " +
                 "of text telling the player to press a key their device does not have is worse " +
                 "than no prompt at all.")]
        public bool promptOnTouchDevices = false;

        [Header("Mobile buttons (optional)")]
        [Tooltip("On-screen button for stations. Wire its OnClick to RequestInteract(). Shown only " +
                 "while a station is the nearest thing in range.")]
        public Button interactButton;

        [Tooltip("On-screen button for picking things up. Wire its OnClick to RequestPickup(). " +
                 "Shown only while a pickup is the nearest thing in range.\n\n" +
                 "Required for a touch build: without it there is no way to pick anything up on a " +
                 "phone. This component says so at Start rather than quietly letting the Interact " +
                 "button cover for it.")]
        public Button pickupButton;

        // ---------------------------------------------------------------- range recovery

        [Header("Range (measured, not remembered)")]
        [Tooltip("Re-measure every frame which interactables the player is actually standing at, " +
                 "and correct anything the trigger events got wrong.\n\n" +
                 "LEAVE THIS ON. It is what stops a station going permanently dead after the " +
                 "player opens it once: a trigger event sent while the station's component is " +
                 "switched off, or while the player object is hidden for a station view, is lost " +
                 "for good, because physics never fires it again for a player who never moved. " +
                 "Measuring cannot get stuck that way.")]
        public bool verifyRangeEachFrame = true;

        [Tooltip("How far outside a trigger still counts as standing at it, on top of the " +
                 "player's own width.\n\n" +
                 "Trigger events fire when the player's COLLIDER touches the trigger, but a " +
                 "measurement is from a point, so a little slack is needed for the two to agree. " +
                 "Raise it if a station has to be leaned into before it responds; lower it if " +
                 "standing between the server and the client offers the wrong one.")]
        public float reachSlack = 0.35f;

        [Tooltip("Extra distance the player has to be beyond Reach Slack before a measurement is " +
                 "allowed to take an interactable away.\n\n" +
                 "This gap is deliberate. Adding and removing at the same distance would let an " +
                 "object flicker in and out while the player stands still at the boundary, which " +
                 "is far more confusing than a trigger that is slightly generous on the way out.")]
        public float releaseMargin = 1f;

        [Tooltip("How often to look for interactables that have been added or destroyed, in " +
                 "seconds. The measuring itself happens every frame - this is only the search, " +
                 "which is the expensive half.")]
        public float rescanInterval = 0.5f;

        [Header("Diagnostics")]
        [Tooltip("Say when a measurement had to correct what the triggers reported.\n\n" +
                 "Worth leaving on. Each line is a trigger event that went missing, and names the " +
                 "object it went missing for - which is the only evidence that a scene is doing " +
                 "something to the player or to a station behind the physics engine's back.")]
        public bool logRangeRecovery = true;

        [Tooltip("Say in the Console when a press was swallowed - by an open dialog, or because " +
                 "nothing was in range. One line per press, and the fastest way to answer 'why did " +
                 "that do nothing'.")]
        public bool logIgnoredPresses = true;

        [Tooltip("Say when the nearest interactable changes. Useful while placing triggers; noisy " +
                 "once they are right.")]
        public bool logTargetChanges = false;

        readonly List<Interactable> inRange = new List<Interactable>();

        Interactable current;
        Interactable warnedAbout;
        bool interactRequested;
        bool pickupRequested;

        Interactable[] known = new Interactable[0];
        float nextRescan;
        Collider playerCollider;
        bool sweeping;

        void Awake()
        {
            Instance = this;
            if (promptUI != null) promptUI.SetActive(false);
            if (interactButton != null) interactButton.gameObject.SetActive(false);
            if (pickupButton != null) pickupButton.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- the list

        public void SetNearbyInteractable(Interactable interactable)
        {
            if (interactable == null) return;
            if (!inRange.Contains(interactable)) inRange.Add(interactable);

            // Not while a sweep is running: the sweep calls in here itself, and would otherwise
            // re-enter the whole refresh once per object it corrects.
            if (!sweeping) Refresh();
        }

        public void ClearNearbyInteractable(Interactable interactable)
        {
            inRange.Remove(interactable);
            if (!sweeping) Refresh();
        }

        void Update()
        {
            // Measured first, so a station recovered this frame can be used on this frame's key
            // press rather than the next one.
            VerifyRange();
            UpdateTargets();

            HandleInput();
        }

        void LateUpdate()
        {
            // Anything that hides or shows itself runs in between, so the visible state is settled
            // here rather than in Update. Recomputed from scratch, not patched: an object that
            // vanishes - deactivated, destroyed, switched off by a station - sends no exit event,
            // so anything waiting to be told would wait forever with a button still on screen.
            UpdateTargets();
            ApplyUI();
        }

        void Refresh() { UpdateTargets(); ApplyUI(); }

        /// <summary>
        /// Re-measures where the player really is, and corrects any interactable whose flag
        /// disagrees.
        ///
        /// Deliberately asymmetric. Adding uses the plain reach; removing needs the player to be
        /// Release Margin further out than that. Without the gap an object sitting exactly at the
        /// boundary would be added and removed on alternate frames while the player stands
        /// perfectly still, and the prompt would strobe.
        /// </summary>
        void VerifyRange()
        {
            if (!verifyRangeEachFrame) return;

            if (Time.unscaledTime >= nextRescan)
            {
                nextRescan = Time.unscaledTime + Mathf.Max(0.1f, rescanInterval);

                // Inactive ones included on purpose. A station switched off while its desktop is up
                // is exactly the case this exists for, and leaving it out of the search would mean
                // the one object that most needs correcting is the one never looked at.
                known = Object.FindObjectsByType<Interactable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            }

            Vector3 here;
            float reach = Mathf.Max(0f, reachSlack);

            Collider self = PlayerCollider;
            if (self != null && self.enabled && self.gameObject.activeInHierarchy)
            {
                here = self.bounds.center;
                reach += Mathf.Max(self.bounds.extents.x, self.bounds.extents.z);
            }
            else
            {
                // The player's collider is off or its object is hidden - which is normal while a
                // station view is up. Measure from the transform and carry on: the position is
                // still right, and this is precisely the window in which trigger events go astray.
                here = transform.position;
                reach += 0.3f;
            }

            sweeping = true;

            for (int i = 0; i < known.Length; i++)
            {
                Interactable it = known[i];
                if (it == null) continue;

                Collider t = it.Trigger;

                // No live trigger, so there is nothing to measure against - and nothing the player
                // could be standing at either. Anything still claiming them is let go.
                //
                // This is the third way the flag used to get stuck, and it is the opposite of the
                // other two. A trigger switched off while the player is INSIDE it leaves the flag
                // True with no event coming to clear it, because physics has nothing left to
                // report from. ConnectivityCheckStation does exactly this at Start when Open On
                // Interact is off, and a pickup does it by deactivating itself the moment it is
                // collected. Left alone, that dead station stays the nearest thing in range and
                // quietly eats every E press meant for the machine standing beside it.
                if (t == null || !t.enabled || !t.gameObject.activeInHierarchy)
                {
                    if (!it.IsPlayerInRange) continue;

                    it.SetPlayerInRange(false, "its trigger was switched off while the player was " +
                                               "inside it, so nothing can report them leaving");
                    inRange.Remove(it);

                    if (logRangeRecovery)
                        Debug.Log("[PlayerInteractor] Range CORRECTED: '" + it.name + "' has no live " +
                                  "trigger any more but still claimed the player. Letting it go - " +
                                  "otherwise it stays the nearest thing in range for the rest of the " +
                                  "session and swallows presses meant for whatever is next to it.", it);
                    continue;
                }

                float d = Vector3.Distance(here, t.ClosestPoint(here));

                if (d <= reach)
                {
                    if (it.IsPlayerInRange) continue;

                    it.SetPlayerInRange(true, "measured " + d.ToString("0.00") +
                                              "m from its trigger");
                    if (!inRange.Contains(it)) inRange.Add(it);

                    if (logRangeRecovery)
                        Debug.Log("[PlayerInteractor] Range CORRECTED: '" + it.name + "' is " +
                                  d.ToString("0.00") + "m away, so the player is standing at it, " +
                                  "but its in-range flag was False. An enter event went missing - " +
                                  "usually because the object, or the player, was switched off at " +
                                  "the moment physics tried to deliver it. Putting it back in " +
                                  "range.", it);
                }
                else if (d > reach + Mathf.Max(0f, releaseMargin))
                {
                    if (!it.IsPlayerInRange) continue;

                    it.SetPlayerInRange(false, "measured " + d.ToString("0.00") +
                                               "m from its trigger, well outside it");
                    inRange.Remove(it);

                    if (logRangeRecovery)
                        Debug.Log("[PlayerInteractor] Range CORRECTED: '" + it.name + "' is " +
                                  d.ToString("0.00") + "m away, but still claimed the player. An " +
                                  "exit event went missing. Taking it out of range - this is what " +
                                  "otherwise makes E at one machine open the other one.", it);
                }
            }

            sweeping = false;
        }

        Collider PlayerCollider
        {
            get
            {
                if (playerCollider != null) return playerCollider;
                playerCollider = GetComponent<Collider>();
                if (playerCollider == null) playerCollider = GetComponentInParent<Collider>();
                if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>(true);
                return playerCollider;
            }
        }

        /// <summary>Prunes the list and picks the nearest surviving entry.</summary>
        void UpdateTargets()
        {
            // Only two things take an interactable OFF the list: it no longer exists, or its
            // in-range flag says so - and that flag is now measured rather than remembered, so it
            // can no longer be stuck False by a lost event. Being switched off does NOT remove an
            // entry: a station disables itself while its desktop is up, and it should be the
            // player's again the moment it comes back, without them walking out of the trigger.
            for (int i = inRange.Count - 1; i >= 0; i--)
            {
                Interactable it = inRange[i];
                if (it == null || !it.IsPlayerInRange) inRange.RemoveAt(i);
            }

            Interactable nearest = Nearest();
            if (nearest == current) return;

            current = nearest;
            warnedAbout = null;

            if (logTargetChanges)
                Debug.Log("[PlayerInteractor] Nearest interactable: " +
                          (current != null ? "'" + current.name + "' (" + current.Channel + ")"
                                           : "none"), this);
        }

        void ApplyUI()
        {
            bool has = current != null;
            bool isPickup = has && current.Channel == Interactable.InputChannel.Pickup;

            // A dialog already stops every press reaching the world, so a prompt telling the
            // player to press a key that is being swallowed is worse than no prompt at all.
            bool dialogUp = DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;

            bool wantPrompt = has && !suppressRequested && !dialogUp &&
                              (promptOnTouchDevices || !TouchSession);

            if (promptUI != null) promptUI.SetActive(wantPrompt);
            if (promptLabel != null && wantPrompt) promptLabel.text = BuildPrompt(current);

            if (interactButton != null) interactButton.gameObject.SetActive(has && !isPickup);
            if (pickupButton != null) pickupButton.gameObject.SetActive(has && isPickup);

            // Cleared after reading, so every frame starts with nobody asking. Anything that still
            // wants the prompt hidden asks again next frame; anything that has finished simply
            // stops asking, and needs no code to undo itself.
            suppressRequested = false;
        }

        /// <summary>
        /// The closest interactable in range, measured to its trigger rather than its pivot.
        ///
        /// A station's transform origin is wherever the artist left it - often the base of a model,
        /// sometimes the far corner of a desk - so pivot distance regularly names the wrong one.
        /// The trigger is what defines "at this object", so it is what gets measured.
        /// </summary>
        Interactable Nearest()
        {
            if (inRange.Count == 0) return null;

            Interactable best = null;
            float bestDistance = float.MaxValue;
            Vector3 here = transform.position;

            foreach (Interactable it in inRange)
            {
                // Skipped, not removed. A switched-off interactable cannot be operated right now -
                // a pickup in the player's pocket, a station busy with its own desktop - but it is
                // still standing in front of the player, and the moment it comes back it should be
                // theirs again without needing to walk out of the trigger and back in.
                if (!it.isActiveAndEnabled) continue;

                float d;
                Collider trigger = it.Trigger;
                if (trigger != null)
                    d = Vector3.Distance(here, trigger.bounds.ClosestPoint(here));
                else
                    d = Vector3.Distance(here, it.transform.position);

                if (d < bestDistance) { bestDistance = d; best = it; }
            }
            return best;
        }

        // ---------------------------------------------------------------- input

        /// <summary>Wire this to the mobile Interact button's OnClick.</summary>
        public void RequestInteract() { interactRequested = true; }

        /// <summary>Wire this to the mobile Pick Up button's OnClick.</summary>
        public void RequestPickup() { pickupRequested = true; }

        void HandleInput()
        {
            bool keyThisFrame = Input.GetKeyDown(interactKey) || Input.GetKeyDown(pickupKey);

            // Don't let a mobile tap or stray key press reach the world while a tutorial
            // popup is open, same guard the COC1 PlayerInteractor uses.
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
            {
                // Swallowing the press is correct; swallowing it in silence is not. This is the
                // whole of "why do I have to press E twice" - the first press went into closing a
                // dialog the player had already stopped looking at, and nothing anywhere said so.
                if (logIgnoredPresses && (keyThisFrame || interactRequested || pickupRequested))
                    Debug.Log("[PlayerInteractor] Press IGNORED - a dialog is open, so the world is " +
                              "not listening. This is why a station can need two presses: the first " +
                              "one dismisses the dialog and never reaches the station.", this);

                interactRequested = false;
                pickupRequested = false;
                return;
            }

            if (current == null)
            {
                if (logIgnoredPresses && keyThisFrame)
                    Debug.Log("[PlayerInteractor] Press IGNORED - nothing is in range. " +
                              ReportRangeBriefly(), this);

                interactRequested = false;
                pickupRequested = false;
                return;
            }

            bool isPickup = current.Channel == Interactable.InputChannel.Pickup;

            // Each control does one thing. Interact enters a station and nothing else; Pickup picks
            // things up and nothing else. No fallback between them.
            bool tapped = isPickup ? pickupRequested : interactRequested;

            // The other button was tapped instead. Now that neither control covers for the other,
            // this is the failure a mis-wired button produces - a tap that does nothing at all -
            // and nothing on screen distinguishes it from a button that was never hooked up.
            bool wrongButton = isPickup ? interactRequested : pickupRequested;
            if (wrongButton && current != warnedAbout)
            {
                warnedAbout = current;
                Debug.LogWarning("[PlayerInteractor] The " + (isPickup ? "Interact" : "Pick Up") +
                                 " button was tapped, but '" + current.name + "' is " +
                                 (isPickup ? "a pickup - it answers to " + pickupKey + " and to the " +
                                             "Pick Up button."
                                           : "a station - it answers to " + interactKey + " and to " +
                                             "the Interact button.") + "\n\n" +
                                 "Most likely that button's On Click calls " +
                                 (isPickup ? "RequestInteract()" : "RequestPickup()") +
                                 " when it should call " +
                                 (isPickup ? "RequestPickup()" : "RequestInteract()") +
                                 " - the usual cause is duplicating one button to make the other " +
                                 "and not repointing the On Click.", this);
            }

            bool pressed = Input.GetKeyDown(isPickup ? pickupKey : interactKey) || tapped;

            interactRequested = false;
            pickupRequested = false;

            if (!pressed) return;

            Interactable target = current;
            target.Interact();

            // A pickup usually switches itself off inside Interact(), and the button must go with
            // it in the same frame - not one frame later, which is long enough to fat-finger a
            // second tap on a control for an object that is already in the player's pocket.
            Refresh();
        }

        /// <summary>
        /// Names the nearest interactable and how far away it is, for the "nothing is in range"
        /// line.
        ///
        /// "Nothing is in range" on its own is true and useless - it is the same sentence whether
        /// the player is in the middle of an empty room or standing with their nose against the
        /// server. Saying which object is closest, and by how much, separates "walk further in"
        /// from "this trigger is far too small" from "the trigger is nowhere near the model it
        /// belongs to" without anyone having to go and look.
        /// </summary>
        string ReportRangeBriefly()
        {
            Interactable closest = null;
            float best = float.MaxValue;
            Vector3 here = transform.position;

            for (int i = 0; i < known.Length; i++)
            {
                Interactable it = known[i];
                if (it == null) continue;
                Collider t = it.Trigger;
                if (t == null || !t.enabled || !t.gameObject.activeInHierarchy) continue;

                float d = Vector3.Distance(here, t.ClosestPoint(here));
                if (d < best) { best = d; closest = it; }
            }

            if (closest == null)
                return "No interactable in this scene has a trigger that is switched on right now.";

            return "The nearest one is '" + closest.name + "', " + best.ToString("0.00") +
                   "m from its trigger - walk closer, or that trigger is too small.";
        }

        // ---------------------------------------------------------------- self-reporting

        void Start()
        {
            Interactable[] all = Object.FindObjectsByType<Interactable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            known = all;

            if (PlayerCollider == null)
                Debug.LogWarning("[PlayerInteractor] There is no Collider on the player or anywhere " +
                                 "under it, so range is measured from this transform's origin " +
                                 "instead of from the player's body. If stations feel like they " +
                                 "have to be walked into rather than walked up to, raise Reach " +
                                 "Slack.", this);

            WarnAboutHardCodedPrompts(all);

            if (pickupButton != null) return;

            // Only worth saying when there is something to pick up. A scene of stations only has no
            // use for the second button, and warning about it there would train you to ignore this.
            int pickups = 0;
            string names = "";
            foreach (Interactable i in all)
            {
                if (i == null || i.Channel != Interactable.InputChannel.Pickup) continue;
                pickups++;
                if (pickups <= 4) names += (names.Length == 0 ? "" : ", ") + i.name;
            }

            if (pickups == 0) return;

            Debug.LogWarning("[PlayerInteractor] Pickup Button is empty, but " + pickups +
                             " object(s) in this scene can only be picked up with " + pickupKey +
                             " - " + names + (pickups > 4 ? ", ..." : "") + ".\n\n" +
                             "On a keyboard that is fine. On a touch build there is no way to pick " +
                             "any of them up, and the Interact button deliberately does NOT cover " +
                             "for it: make a Pick Up button and wire its On Click to " +
                             "RequestPickup().", this);
        }

        /// <summary>
        /// Names prompts that spell out a key instead of using {key}.
        ///
        /// A prompt is the only place the player learns which control to press, and a hard-coded
        /// one cannot follow the object when it changes channel - which just happened to every
        /// pickup in this project. "Press E to interact" on something that now answers to G is not
        /// a cosmetic problem: it is the game giving the player a wrong instruction.
        /// </summary>
        void WarnAboutHardCodedPrompts(Interactable[] all)
        {
            string wrong = "";
            int count = 0;

            foreach (Interactable i in all)
            {
                if (i == null || string.IsNullOrEmpty(i.promptText)) continue;
                if (i.promptText.Contains("{key}")) continue;

                KeyCode right = i.Channel == Interactable.InputChannel.Pickup ? pickupKey : interactKey;
                if (i.promptText.IndexOf(" " + right + " ", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;   // spells out the correct key, so it is merely brittle, not wrong

                count++;
                if (count <= 6)
                    wrong += "\n    '" + i.name + "'   says \"" + i.promptText +
                             "\"   but answers to " + right;
            }

            if (count == 0) return;

            Debug.LogWarning("[PlayerInteractor] " + count + " prompt(s) name the wrong control, or " +
                             "none at all:" + wrong + (count > 6 ? "\n    ..." : "") +
                             "\n\nWrite {key} where the control belongs - \"Press {key} to pick " +
                             "up\" - and it becomes the real binding, and keeps following it.", this);
        }

        /// <summary>
        /// Whether this is a touch session - asked of the touch controls rather than of the
        /// platform.
        ///
        /// Application.isMobilePlatform is FALSE in the Editor, with the build target set to
        /// Android, and in the Device Simulator. So a platform check says "PC" in exactly the
        /// situation you are testing the phone layout in, and the keyboard prompt sits on screen
        /// beside the touch buttons it was supposed to replace.
        ///
        /// MobileControlsVisibility already decides this question once, for the whole project, and
        /// has a Show In Editor tick for testing. Reading its answer means the prompt and the touch
        /// controls cannot disagree: if the joystick is on screen, this is a touch session, and the
        /// prompt stays off. With no such canvas in the scene it falls back to the platform.
        /// </summary>
        bool TouchSession
        {
            get
            {
                if (touchControls == null)
                    touchControls = Object.FindFirstObjectByType<MobileControlsVisibility>();

                if (touchControls != null) return touchControls.Wanted;
                return Application.isMobilePlatform;
            }
        }

        MobileControlsVisibility touchControls;

        /// <summary>
        /// Fills {key} in with the control that actually operates this object.
        ///
        /// A prompt is the only place the player learns which key to press, so a hard-coded
        /// "Press E" in the text is a bug waiting to happen the moment a binding changes or an
        /// object moves from one channel to the other. Prompts with no {key} are left alone.
        /// </summary>
        string BuildPrompt(Interactable interactable)
        {
            string text = interactable.promptText ?? "";
            if (text.IndexOf("{key}") < 0) return text;

            KeyCode key = interactable.Channel == Interactable.InputChannel.Pickup
                ? pickupKey
                : interactKey;
            return text.Replace("{key}", key.ToString());
        }

        /// <summary>Prints everything currently in range and which one owns the next press.</summary>
        [ContextMenu("Report interactables in range")]
        public void LogInRange()
        {
            string msg = "[PlayerInteractor] " + inRange.Count + " interactable(s) in range:";
            foreach (Interactable it in inRange)
                msg += "\n    " + (it == current ? " >" : "  ") +
                       (it != null ? "'" + it.name + "'   " + it.Channel : "(destroyed)");
            msg += "\n    ---- next press goes to: " +
                   (current != null ? "'" + current.name + "'" : "nothing");
            Debug.LogWarning(msg, this);
        }

        /// <summary>
        /// Measures the distance to every interactable in the scene and prints the lot.
        ///
        /// The one to run while standing at a station that is not responding. It shows the measured
        /// distance beside the flag the triggers left behind, so a disagreement between them is
        /// visible on one line instead of being inferred from behaviour.
        /// </summary>
        [ContextMenu("Measure every interactable")]
        public void LogMeasured()
        {
            Interactable[] all = Object.FindObjectsByType<Interactable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Vector3 here = transform.position;
            string msg = "[PlayerInteractor] " + all.Length + " interactable(s), measured from the " +
                         "player:";

            foreach (Interactable it in all)
            {
                if (it == null) continue;
                Collider t = it.Trigger;
                string d = t != null && t.enabled && t.gameObject.activeInHierarchy
                    ? Vector3.Distance(here, t.ClosestPoint(here)).ToString("0.00") + "m"
                    : "(no live trigger)";

                msg += "\n    '" + it.name + "'   " + d +
                       "   flag=" + it.IsPlayerInRange +
                       "   listed=" + inRange.Contains(it) +
                       (it.isActiveAndEnabled ? "" : "   (switched off)");
            }

            Debug.LogWarning(msg, this);
        }
    }
}