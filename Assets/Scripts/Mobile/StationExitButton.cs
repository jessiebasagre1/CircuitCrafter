// StationExitButton.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// ONE on-screen Back button that leaves whichever station, stage view or PC desktop the
    /// player is currently in. Shown only while they are in one, hidden the rest of the time.
    ///
    /// WHY ONE BUTTON AND NOT SIX
    ///
    /// The player is only ever inside one station at a time, so six buttons would mean five of them
    /// hidden at any moment and a sixth that has to be the right one. That is five chances to bind a
    /// button to the wrong station, and the failure is invisible until a learner is stuck inside a
    /// stage with a Back button that does nothing. It also puts the control in a different place on
    /// screen depending on which station you are in, which is the opposite of what a Back button is
    /// for.
    ///
    /// So this asks the stations instead of being told. Every COC2 station already exposes a public
    /// "am I inside" flag and a public exit method; this finds the one that says yes and calls its
    /// exit. Adding a station later means one line here, not another button in the scene.
    ///
    /// INNERMOST FIRST
    ///
    /// The stage views nest - the crimp close-up sits inside the crimping view. When two say yes,
    /// this leaves the inner one, because that is what Back means to the person pressing it: undo
    /// the last step inward, not throw away the whole station.
    ///
    /// VISIBILITY IS DERIVED, NOT PUSHED
    ///
    /// Every frame it re-asks. A button switched on by whoever opened a stage and off by whoever
    /// closed it drifts out of sync the first time a THIRD thing closes it - a failed test, a
    /// procedure reset, a dialog that ejects the player. Reading the station's own flag cannot
    /// drift, because the station is the only thing that decides.
    ///
    /// SETUP
    ///   1. Put one Back button in your mobile controls canvas. Leave it ACTIVE - it hides itself
    ///      with a CanvasGroup, because the exit key has to keep working while the button is not
    ///      showing, and Unity does not run Update on a deactivated object.
    ///   2. Add this component to the button.
    ///   3. Nothing else. There is no station to choose.
    ///
    /// On a PC the same job is done by the exit key, which works whether or not the button is on
    /// screen - so a keyboard player never has to find it.
    ///
    /// Do NOT wire anything into the button's On Click - this adds the listener itself, and a second
    /// listener would run the exit twice.
    /// </summary>
    [DefaultExecutionOrder(250)]
    public class StationExitButton : MonoBehaviour
    {
        /// <summary>The stations this button knows how to leave, innermost first.</summary>
        public enum Station
        {
            None,
            /// <summary>Stage 3 close-up, inside the crimping view. CrimpStageViewController.</summary>
            CrimpStageView,
            /// <summary>Stage 4, the cable tester close-up. TestingViewController.</summary>
            TestingView,
            /// <summary>Stage 2, arranging the wires. CrimpingViewController.</summary>
            CrimpingView,
            /// <summary>Stage 1. StrippingViewController.</summary>
            StrippingView,
            /// <summary>The tester bench in the world. NetworkTestStation.</summary>
            TesterBench,
            /// <summary>The patch panel punch-down stage. PunchDownStation.</summary>
            PunchDownStation,
            /// <summary>The server or client PC desktop. DeviceConfigStation.</summary>
            Workstation
        }

        [Header("Button")]
        [Tooltip("The button itself. Left empty, the Button on this GameObject is used, which is " +
                 "the normal case.")]
        public Button button;

        [Tooltip("What gets shown and hidden. Left empty, the button's own GameObject is used.\n\n" +
                 "Set it when the button sits inside a group that should appear with it - a panel " +
                 "holding a Back button and a label, say.")]
        public GameObject visualRoot;

        [Header("When to show it")]
        [Tooltip("Hide while a dialog is on screen. A Back button poking out from under a briefing " +
                 "popup invites a tap that leaves the station instead of dismissing the dialog.")]
        public bool hideWhileDialogOpen = true;

        [Tooltip("Show only on a touch device.\n\n" +
                 "Leave this OFF while building - a mouse can click it and the keyboard exits still " +
                 "work. Turn it ON for a build where the button would be redundant clutter on PC.")]
        public bool touchOnly = false;

        [Tooltip("Treat the Editor as a touch device, so Touch Only can be tested without making a " +
                 "build. Application.isMobilePlatform is false in the Editor even with the build " +
                 "target set to Android.")]
        public bool showInEditor = true;

        [Header("Drawing on top")]
        [Tooltip("While a station view is up, draw this button above every other canvas.\n\n" +
                 "LEAVE THIS ON. Several stations fill the screen with a canvas of their own - the " +
                 "testing view, the PC desktop - and uGUI draws canvases in sorting order, so a " +
                 "button on the touch controls ends up UNDERNEATH. It is switched on, active, " +
                 "correctly wired and completely invisible, with nothing in the Console to suggest " +
                 "anything is wrong. On a keyboard nobody notices, because the exit key still works. " +
                 "On a phone the learner is stuck.")]
        public bool keepOnTopInStationView = true;

        [Tooltip("How far above the highest canvas found to sit.")]
        public int sortingOrderMargin = 10;

        [Tooltip("Keep re-checking the draw order while a station view is open, instead of setting " +
                 "it once on the way in.\n\n" +
                 "LEAVE THIS ON. Canvases appear mid-view - a dialog, a result panel, a report - and " +
                 "one that was switched off when the order was worked out can come up ABOVE the " +
                 "button a moment later. Checking once is enough right up until the moment it is not.")]
        public bool keepCheckingOrder = true;

        [Header("Diagnostics")]
        [Tooltip("When the button is on screen and a tap lands inside it but something else " +
                 "receives that tap, say WHAT received it.\n\n" +
                 "This is the answer to 'the button is right there and nothing happens'. A " +
                 "full-screen panel with Raycast Target left on swallows every tap over the whole " +
                 "screen while being completely invisible, and there is nothing in the Inspector, " +
                 "the Hierarchy or the Console that points at it.")]
        public bool reportStolenTaps = true;

        [Header("Restrict (optional)")]
        [Tooltip("Leave empty - the normal case - and this button leaves whichever station the " +
                 "player is in.\n\n" +
                 "Naming stations here limits it to those, for the rare scene that wants a second " +
                 "button somewhere else. Anything not listed is ignored, and the button stays " +
                 "hidden inside it.")]
        public Station[] onlyTheseStations = new Station[0];

        [Header("Keyboard")]
        [Tooltip("Leaves the station on a keyboard, from anywhere - not only when this button is " +
                 "visible.\n\n" +
                 "E is what the player used to sit down, so it is the key their hand is already on. " +
                 "Set to None to leave every station to its own key.")]
        public KeyCode exitKey = KeyCode.E;

        [Tooltip("Ignore the exit key on a touch device, where there is no keyboard and the button " +
                 "is the control. The Editor counts as a PC.")]
        public bool exitKeyOnPCOnly = true;

        [Header("Diagnostics")]
        public bool logExit = true;

        bool wasVisible;
        Station lastShownFor = Station.None;
        Station lastStation = Station.None;
        int enteredFrame = -999;
        CanvasGroup group;

        void Reset()
        {
            button = GetComponent<Button>();
        }

        void Awake()
        {
            if (button == null) button = GetComponent<Button>();

            // Wired in code rather than left to the Inspector, for the same reason every other
            // button in this project is: an On Click that was never hooked looks exactly like one
            // that was, and the failure - tapping Back and staying put - gives no clue where to look.
            if (button != null)
            {
                button.onClick.RemoveListener(Exit);
                button.onClick.AddListener(Exit);
            }

            Show(false);

            if (!all.Contains(this)) all.Add(this);
        }

        void OnDestroy() { all.Remove(this); }

        void Start() { ValidateSetup(); }

        /// <summary>
        /// Tells PlayerInteractor to hold its prompt back while the player is inside a station.
        ///
        /// Done in Update rather than LateUpdate on purpose: every Update in the scene runs before
        /// any LateUpdate, and PlayerInteractor decides the prompt in its LateUpdate. Setting the
        /// flag here means it is read the same frame instead of one frame late, which is the
        /// difference between a clean transition and the prompt flickering as the view opens.
        /// </summary>
        void Update()
        {
            if (CurrentStation != Station.None)
                CSSSimulation.Interaction.PlayerInteractor.SuppressPromptThisFrame();
        }

        void LateUpdate()
        {
            Station here = CurrentStation;

            // When the player arrives, remember the frame. The same E press that opened the station
            // is still reported by GetKeyDown for the rest of that frame, and this component runs
            // after PlayerInteractor - so without this the player would sit down and stand straight
            // back up, having pressed the key once.
            if (here != lastStation)
            {
                if (here != Station.None) enteredFrame = Time.frameCount;
                lastStation = here;
            }

            if (here != Station.None && exitKey != KeyCode.None && KeyboardWanted &&
                Time.frameCount > enteredFrame + 1 && Input.GetKeyDown(exitKey))
            {
                Exit();
                return;
            }

            bool visible = here != Station.None && WantedOnThisDevice;
            if (hideWhileDialogOpen && DialogManager.Instance != null &&
                DialogManager.Instance.IsDialogOpen)
                visible = false;

            KeepOnTop(here);

            if (visible != wasVisible) Show(visible);

            if (visible) CheckForStolenTaps();

            if (visible && here != lastShownFor)
            {
                lastShownFor = here;
                if (logExit) Debug.Log("[StationExitButton] Back now leaves " + here + ".", this);
            }
            if (!visible) lastShownFor = Station.None;
        }

        // ---------------------------------------------------------------- which station

        /// <summary>
        /// The station the player is in right now, or None. Checked innermost first, so a stage
        /// nested inside another is what Back leaves.
        /// </summary>
        public Station CurrentStation
        {
            get
            {
                if (Allowed(Station.CrimpStageView) &&
                    CrimpStageViewController.Instance != null &&
                    CrimpStageViewController.Instance.InCrimpView)
                    return Station.CrimpStageView;

                if (Allowed(Station.TestingView) &&
                    TestingViewController.Instance != null &&
                    TestingViewController.Instance.InTestingView)
                    return Station.TestingView;

                if (Allowed(Station.CrimpingView) &&
                    CrimpingViewController.Instance != null &&
                    CrimpingViewController.Instance.InCrimpingView)
                    return Station.CrimpingView;

                if (Allowed(Station.StrippingView) &&
                    StrippingViewController.Instance != null &&
                    StrippingViewController.Instance.InStrippingView)
                    return Station.StrippingView;

                if (Allowed(Station.TesterBench) &&
                    NetworkTestStation.Instance != null &&
                    NetworkTestStation.Instance.InTesterView)
                    return Station.TesterBench;

                if (Allowed(Station.PunchDownStation) &&
                    CSSSimulation.Networking.PunchDownStation.Instance != null &&
                    CSSSimulation.Networking.PunchDownStation.Instance.InStageView)
                    return Station.PunchDownStation;

                // Last, because a desktop covers the whole screen and nothing else can be open
                // underneath it - so if the player is at a PC, that is unambiguously where they are.
                if (Allowed(Station.Workstation) && DeviceConfigStation.OpenStation != null)
                    return Station.Workstation;

                return Station.None;
            }
        }

        bool Allowed(Station s)
        {
            if (onlyTheseStations == null || onlyTheseStations.Length == 0) return true;
            foreach (Station allowed in onlyTheseStations)
                if (allowed == s) return true;
            return false;
        }

        // ---------------------------------------------------------------- leaving

        /// <summary>Leaves whichever station the player is in. Also public, so a hardware Back key
        /// or a Q keybind can call it.</summary>
        public void Exit()
        {
            Station here = CurrentStation;

            // Not worth a warning: a hidden button cannot be tapped, so this only happens when
            // something else calls Exit() directly while the player is in the world.
            if (here == Station.None) return;

            if (logExit) Debug.Log("[StationExitButton] Leaving " + here + ".", this);

            switch (here)
            {
                case Station.CrimpStageView:
                    // RequestBack, not an exit - this stage backs out to the view it opened from
                    // rather than to the world.
                    CrimpStageViewController.Instance.RequestBack();
                    break;

                case Station.TestingView:
                    TestingViewController.Instance.RequestExitTestingView();
                    break;

                case Station.CrimpingView:
                    CrimpingViewController.Instance.RequestExitCrimpingView();
                    break;

                case Station.StrippingView:
                    StrippingViewController.Instance.RequestExitStrippingView();
                    break;

                case Station.TesterBench:
                    NetworkTestStation.Instance.RequestExit();
                    break;

                case Station.PunchDownStation:
                    CSSSimulation.Networking.PunchDownStation.Instance.RequestExit();
                    break;

                case Station.Workstation:
                    // Close(), the same call the taskbar's Leave PC button and Escape already make,
                    // so the three routes off a machine cannot behave differently from each other.
                    DeviceConfigStation.OpenStation.Close();
                    break;
            }
        }

        // ---------------------------------------------------------------- plumbing

        bool KeyboardWanted
        {
            get
            {
                if (!exitKeyOnPCOnly) return true;
#if UNITY_EDITOR
                return true;
#else
                return !Application.isMobilePlatform;
#endif
            }
        }

        bool WantedOnThisDevice
        {
            get
            {
                if (!touchOnly) return true;
#if UNITY_EDITOR
                if (showInEditor) return true;
#endif
                return Application.isMobilePlatform;
            }
        }

        /// <summary>
        /// Hides by fading the button out rather than by switching its GameObject off.
        ///
        /// The exit KEY has to keep working while the button is hidden - on a PC the button may
        /// never be shown at all - and Unity does not run Update on a deactivated object. Switching
        /// the object off would therefore take the keyboard down with the button, which is the
        /// opposite of what this component is for. A CanvasGroup at alpha 0 with raycasts off is
        /// invisible and untappable while leaving the component running.
        /// </summary>
        void Show(bool visible)
        {
            wasVisible = visible;

            GameObject target = visualRoot != null ? visualRoot
                                                   : (button != null ? button.gameObject : gameObject);

            if (target != gameObject)
            {
                if (target.activeSelf != visible) target.SetActive(visible);
                return;
            }

            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        /// <summary>
        /// Lifts the button above every other canvas while a station view is on screen, and puts it
        /// back when the player leaves.
        ///
        /// WHY THIS IS NOT A LAYOUT PREFERENCE
        ///
        /// uGUI draws canvases in sorting order, and a station that fills the screen with its own
        /// canvas will happily draw over the touch controls. Nothing about that is an error: the
        /// button is active, its parents are active, its raycast target is on, and it is simply
        /// behind a wall. There is no warning to find, and the symptom is identical to the button
        /// not existing at all.
        ///
        /// A nested Canvas with Override Sorting is the uGUI way to say "this bit draws above
        /// that", and it needs a GraphicRaycaster of its own to keep receiving taps once it does.
        /// Both are added here rather than left as a setup step, because having one without the
        /// other gives a button that is visible and dead - worse than one that is missing.
        /// </summary>
        void KeepOnTop(Station here)
        {
            bool wanted = keepOnTopInStationView && here != Station.None;

            // Already lifted and still should be: re-check now and then, because a canvas that was
            // switched off when the order was worked out can appear above it a moment later. That
            // is not hypothetical - a dialog and a result panel both arrive mid-view.
            if (wanted && lifted)
            {
                if (!keepCheckingOrder || Time.unscaledTime < nextOrderCheck) return;
                nextOrderCheck = Time.unscaledTime + 0.25f;

                Canvas mine = GetComponent<Canvas>();
                if (mine == null) return;

                int target = HighestSortingOrder() + Mathf.Max(1, sortingOrderMargin);
                if (mine.sortingOrder >= target) return;

                mine.sortingOrder = target;
                if (logExit)
                    Debug.Log("[StationExitButton] Something new drew above Back, so it has been " +
                              "raised again to sorting order " + target + ".", this);
                return;
            }

            if (wanted == lifted) return;

            lifted = wanted;
            nextOrderCheck = 0f;

            Canvas own = GetComponent<Canvas>();

            if (!wanted)
            {
                if (own != null) own.overrideSorting = false;
                return;
            }

            if (own == null)
            {
                own = gameObject.AddComponent<Canvas>();
                gameObject.AddComponent<GraphicRaycaster>();
            }

            own.overrideSorting = true;
            own.sortingOrder = HighestSortingOrder() + Mathf.Max(1, sortingOrderMargin);

            if (logExit)
                Debug.Log("[StationExitButton] " + here + " fills the screen, so Back has been " +
                          "lifted to sorting order " + own.sortingOrder + " to stay on top of it.",
                          this);
        }

        /// <summary>
        /// Says who actually received a tap that landed on this button.
        ///
        /// WHY THIS IS THE ONLY WAY TO FIND IT
        ///
        /// A tap that does nothing has no error, no warning and no trace. The button is visible,
        /// active, interactable and correctly wired; the press simply goes to whatever uGUI decides
        /// is in front, and a full-screen Image with Raycast Target left ticked is in front of
        /// everything while being completely invisible. Nothing in the Inspector, the Hierarchy or
        /// the Console points at it - the only thing that knows is the raycast itself, so this asks
        /// it and prints the answer.
        /// </summary>
        void CheckForStolenTaps()
        {
            if (!reportStolenTaps || EventSystem.current == null) return;

            Vector2 where;
            if (!TapStartedThisFrame(out where)) return;

            RectTransform rect = transform as RectTransform;
            if (rect == null) return;

            // Only care about taps aimed AT the button. A tap anywhere else landing elsewhere is
            // just a tap.
            if (!RectTransformUtility.RectangleContainsScreenPoint(rect, where, null)) return;

            PointerEventData data = new PointerEventData(EventSystem.current);
            data.position = where;

            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);

            if (hits.Count > 0)
            {
                Transform top = hits[0].gameObject.transform;
                if (top == transform || top.IsChildOf(transform)) return;   // the tap arrived
            }

            string blame = hits.Count == 0
                ? "nothing at all received it - there may be no GraphicRaycaster on the canvas in " +
                  "front, or an EventSystem is missing"
                : "'" + Path(hits[0].gameObject.transform) + "' received it instead";

            string others = "";
            for (int i = 0; i < hits.Count && i < 5; i++)
                others += "\n    " + (i + 1) + ". " + Path(hits[i].gameObject.transform) +
                          "   (canvas order " + hits[i].sortingOrder + ")";

            Debug.LogError("[StationExitButton] A tap landed ON the Back button while the player " +
                           "was inside " + CurrentStation + ", but " + blame + "." +
                           "\n\nWhat the raycast hit, front to back:" + others +
                           "\n\nThe thing at the top of that list is covering Back. Nine times out " +
                           "of ten it is a full-screen panel with Raycast Target still ticked on its " +
                           "Image - untick it, or raise this button's Sorting Order Margin above " +
                           "that canvas.", this);
        }

        static bool TapStartedThisFrame(out Vector2 position)
        {
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                position = t.position;
                return t.phase == TouchPhase.Began;
            }

            position = Input.mousePosition;
            return Input.GetMouseButtonDown(0);
        }

        static string Path(Transform t)
        {
            string path = t.name;
            Transform p = t.parent;
            while (p != null) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        int HighestSortingOrder()
        {
            int highest = 0;
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (Canvas c in canvases)
            {
                if (c == null || c.gameObject == gameObject) continue;
                if (c.sortingOrder > highest) highest = c.sortingOrder;
            }
            return highest;
        }

        /// <summary>
        /// Watches from outside, so the one failure this component cannot report on itself gets
        /// reported anyway.
        ///
        /// If something switches the button's GameObject - or any parent of it - off, this
        /// component stops running entirely. Its Update, its LateUpdate and its logging all stop
        /// together, so the single most useful moment to say something is exactly the moment it has
        /// lost the ability to. A station view that hides the touch controls takes the exit button
        /// with it, and nothing anywhere says so.
        ///
        /// This runs on its own hidden object, which nothing in the scene knows about and so
        /// nothing can switch off. It costs one check per frame and exists purely to make sure a
        /// learner is never silently trapped in a station.
        /// </summary>
        class ExitButtonWatchdog : MonoBehaviour
        {
            bool warned;

            void LateUpdate()
            {
                if (warned) return;

                foreach (StationExitButton b in all)
                {
                    if (b == null || b.CurrentStation == Station.None) continue;
                    if (b.gameObject.activeInHierarchy) continue;

                    warned = true;

                    string blocker = "its own GameObject";
                    Transform t = b.transform;
                    while (t != null)
                    {
                        if (!t.gameObject.activeSelf)
                        {
                            blocker = t == b.transform ? "its own GameObject"
                                                       : "its parent '" + t.name + "'";
                            break;
                        }
                        t = t.parent;
                    }

                    Debug.LogError("[StationExitButton] The player is inside " + b.CurrentStation +
                                   " and the Back button '" + b.name + "' is SWITCHED OFF, because " +
                                   blocker + " is off. On a touch device there is now no way out of " +
                                   "this station.\n\n" +
                                   "Something hid it on the way in - a station's Mobile Controls To " +
                                   "Hide list is the usual one. Either take the Back button out of " +
                                   "that list, or add it to the station's own mobile controls list " +
                                   "so it is switched back ON for that view.", b);
                    return;
                }
            }
        }

        static readonly List<StationExitButton> all = new List<StationExitButton>();
        bool lifted;
        float nextOrderCheck;

        /// <summary>
        /// Switches on any exit button a scene left inactive.
        ///
        /// This component hides itself with a CanvasGroup, which means its GameObject has to be
        /// active for it to work at all - and a button authored switched off has no code of its own
        /// running to notice. Something outside it has to look, exactly as MobileControlsVisibility
        /// does for the touch controls.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            // AfterSceneLoad fires ONCE, for whichever scene the game started in - the main menu.
            // It does not fire again for a scene loaded afterwards, which is every scene the
            // learner actually plays in. Doing the work only in that callback therefore fixed the
            // exit button in the one scene that has no stations in it, and in none of the scenes
            // that do.
            //
            // The subscription is what makes it per-scene. MobileControlsVisibility already learned
            // this lesson and does the same thing, which is why the touch canvas comes back in
            // every scene while this did not.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            RevealAll();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { RevealAll(); }

        /// <summary>Switches on any exit button the current scenes left inactive.</summary>
        public static void RevealAll()
        {
            StationExitButton[] found = Object.FindObjectsByType<StationExitButton>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (StationExitButton b in found)
            {
                if (b == null) continue;

                // An ancestor being off cannot be fixed from here. Switching on a whole parent
                // panel to reach one button would put the rest of that panel on screen too, which
                // is a bigger change than this is entitled to make - so it says so instead.
                GameObject blocker = FirstInactiveAncestor(b.transform);
                if (blocker != null)
                {
                    Debug.LogError("[StationExitButton] '" + b.name + "' cannot run, because its " +
                                   "parent '" + blocker.name + "' is switched off. Nothing here can " +
                                   "fix that without putting the rest of that object on screen as " +
                                   "well.\n\nMove the Back button out from under '" + blocker.name +
                                   "' - onto the always-on touch controls canvas is the usual home - " +
                                   "or switch that parent on.", b);
                    continue;
                }

                bool wasOff = !b.gameObject.activeSelf;
                bool wasDisabled = !b.enabled;

                if (wasOff) b.gameObject.SetActive(true);

                // The component too, not just the object. A ticked-off component is just as silent,
                // and reads exactly the same way in the Inspector at a glance.
                if (wasDisabled) b.enabled = true;

                if (wasOff || wasDisabled)
                    Debug.Log("[StationExitButton] '" + b.name + "' was " +
                              (wasOff && wasDisabled ? "switched off and its component unticked"
                               : wasOff ? "switched off" : "unticked") +
                              " in this scene - turned back on, because it hides ITSELF with a " +
                              "CanvasGroup and cannot do that while it is not running. It will now " +
                              "be invisible until the player is inside a station, which is the same " +
                              "thing you were trying to achieve by switching it off.", b);
            }

            if (found.Length == 0 || Object.FindFirstObjectByType<ExitButtonWatchdog>() != null)
                return;

            GameObject watcher = new GameObject("StationExitButtonWatchdog");
            watcher.hideFlags = HideFlags.HideAndDontSave;
            watcher.AddComponent<ExitButtonWatchdog>();
        }

        /// <summary>The nearest switched-off parent above this transform, or null.</summary>
        static GameObject FirstInactiveAncestor(Transform t)
        {
            Transform p = t.parent;
            while (p != null)
            {
                if (!p.gameObject.activeSelf) return p.gameObject;
                p = p.parent;
            }
            return null;
        }

        // ---------------------------------------------------------------- self-reporting

        void ValidateSetup()
        {
            if (button == null)
            {
                Debug.LogWarning("[StationExitButton] '" + name + "' has no Button, so nothing can " +
                                 "be tapped. Put this component on the Back button itself, or drag " +
                                 "the button into the Button field.", this);
                return;
            }

            if (button.onClick.GetPersistentEventCount() > 0)
                Debug.LogWarning("[StationExitButton] '" + name + "' has something wired into its " +
                                 "On Click in the Inspector as well. This component adds its own " +
                                 "listener, so the exit would run twice - clear the On Click list.",
                                 this);

            int found = 0;
            string roster = "";
            foreach (Station s in new[] { Station.CrimpStageView, Station.TestingView,
                                          Station.CrimpingView, Station.StrippingView,
                                          Station.TesterBench, Station.PunchDownStation,
                                          Station.Workstation })
            {
                bool present = ControllerPresent(s);
                bool allowed = Allowed(s);
                if (present && allowed) found++;
                roster += "\n    " + (present ? (allowed ? "  covers  " : "  skipped ") : "  absent  ") + s;
            }

            if (found == 0)
            {
                Debug.LogWarning("[StationExitButton] '" + name + "' covers NO station in this scene, " +
                                 "so it can never appear." + roster +
                                 "\n    Either this scene has no COC2 stations, or Only These " +
                                 "Stations is filtering out the ones it does have.", this);
                return;
            }

            Debug.Log("[StationExitButton] '" + name + "' leaves " + found + " station(s)" +
                      (touchOnly ? ", on touch devices only" : "") + "." + roster, this);
        }

        bool ControllerPresent(Station s)
        {
            switch (s)
            {
                case Station.CrimpStageView: return CrimpStageViewController.Instance != null;
                case Station.TestingView: return TestingViewController.Instance != null;
                case Station.CrimpingView: return CrimpingViewController.Instance != null;
                case Station.StrippingView: return StrippingViewController.Instance != null;
                case Station.TesterBench: return NetworkTestStation.Instance != null;
                case Station.PunchDownStation:
                    return CSSSimulation.Networking.PunchDownStation.Instance != null;
                case Station.Workstation:
                    return Object.FindObjectsByType<DeviceConfigStation>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0;
            }
            return false;
        }

        /// <summary>Prints which station Back would leave right now, and why it is or is not showing.</summary>
        [ContextMenu("Report exit button")]
        public void LogState()
        {
            string roster = "";
            foreach (Station s in new[] { Station.CrimpStageView, Station.TestingView,
                                          Station.CrimpingView, Station.StrippingView,
                                          Station.TesterBench, Station.PunchDownStation,
                                          Station.Workstation })
                roster += "\n    " + (ControllerPresent(s) ? (Allowed(s) ? "  covers  " : "  skipped ")
                                                           : "  absent  ") + s;

            Station here = CurrentStation;
            Debug.LogWarning("[StationExitButton] '" + name + "'" +
                             "\n    player is in  : " + (here == Station.None ? "the world" : here.ToString()) +
                             "\n    showing       : " + wasVisible +
                             "\n    touch only    : " + touchOnly +
                             roster +
                             (hideWhileDialogOpen && DialogManager.Instance != null &&
                              DialogManager.Instance.IsDialogOpen
                                  ? "\n    ---- a dialog is open, which is why it is hidden right now"
                                  : ""), this);
        }
    }
}