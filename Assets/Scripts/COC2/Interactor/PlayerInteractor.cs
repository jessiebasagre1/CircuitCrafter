using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CSSSimulation.Interaction
{
    /// <summary>
    /// Attach this to the player. Each Interactable's own trigger reports in when the player enters
    /// or leaves its range (see Interactable.cs); this keeps the list, picks the nearest one, shows
    /// a prompt, and fires Interact() on the right control.
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
    /// Keeping every interactable in range and choosing the nearest one each frame fixes both,
    /// because the answer is recomputed from what is true now rather than remembered from whenever
    /// a trigger last fired.
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
        /// Set while the player is inside a station view, to keep the world prompt off screen.
        ///
        /// Kept here as a flag rather than a reference to the station code, so this component stays
        /// a generic interactor that knows nothing about COC2. StationExitButton already works out
        /// which station the player is in every frame; it sets this, and nothing has to be wired.
        ///
        /// A station is a close-up of a workbench, not the room - so "Press E to start crimping"
        /// floating over the crimping view is an instruction the player has already followed, on
        /// top of a thing they are already doing.
        /// </summary>
        public static bool SuppressPrompt { get; set; }

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
                 "than no prompt at all. The Editor counts as a PC, so you still see it while " +
                 "building.")]
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

        [Header("Diagnostics")]
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
            Refresh();
        }

        public void ClearNearbyInteractable(Interactable interactable)
        {
            inRange.Remove(interactable);
            Refresh();
        }

        /// <summary>
        /// Recomputed every frame, not only when a trigger fires.
        ///
        /// That is the whole point: an object that vanishes - deactivated, destroyed, or switched
        /// off by a station - sends no exit event, so anything waiting to be told would wait
        /// forever with a button still on screen.
        /// </summary>
        void LateUpdate() { Refresh(); }

        void Refresh()
        {
            for (int i = inRange.Count - 1; i >= 0; i--)
            {
                Interactable it = inRange[i];

                // isActiveAndEnabled covers the case no event ever reports: a pickup that switched
                // its own GameObject off as it went into the player's pocket.
                if (it == null || !it.isActiveAndEnabled || !it.IsPlayerInRange)
                    inRange.RemoveAt(i);
            }

            Interactable nearest = Nearest();

            if (nearest != current)
            {
                current = nearest;
                warnedAbout = null;

                if (logTargetChanges)
                    Debug.Log("[PlayerInteractor] Nearest interactable: " +
                              (current != null ? "'" + current.name + "' (" + current.Channel + ")"
                                               : "none"), this);
            }

            bool has = current != null;
            bool isPickup = has && current.Channel == Interactable.InputChannel.Pickup;

            bool wantPrompt = has && !SuppressPrompt &&
                              (promptOnTouchDevices || !Application.isMobilePlatform);

            if (promptUI != null) promptUI.SetActive(wantPrompt);
            if (promptLabel != null && wantPrompt) promptLabel.text = BuildPrompt(current);

            if (interactButton != null) interactButton.gameObject.SetActive(has && !isPickup);
            if (pickupButton != null) pickupButton.gameObject.SetActive(has && isPickup);
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
            if (inRange.Count == 1) return inRange[0];

            Interactable best = null;
            float bestDistance = float.MaxValue;
            Vector3 here = transform.position;

            foreach (Interactable it in inRange)
            {
                float d;
                Collider trigger = it.GetComponent<Collider>();
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

        void Update()
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
                    Debug.Log("[PlayerInteractor] Press IGNORED - nothing is in range. The player is " +
                              "outside every trigger, so there is nothing to operate.", this);

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

        // ---------------------------------------------------------------- self-reporting

        void Start()
        {
            if (pickupButton != null) return;

            // Only worth saying when there is something to pick up. A scene of stations only has no
            // use for the second button, and warning about it there would train you to ignore this.
            Interactable[] all = Object.FindObjectsByType<Interactable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int pickups = 0;
            string names = "";
            foreach (Interactable i in all)
            {
                if (i == null || i.Channel != Interactable.InputChannel.Pickup) continue;
                pickups++;
                if (pickups <= 4) names += (names.Length == 0 ? "" : ", ") + i.name;
            }

            WarnAboutHardCodedPrompts(all);

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
    }
}