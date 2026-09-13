// StationExitButton.cs
using UnityEngine;
using UnityEngine.UI;

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
        }

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
            CSSSimulation.Interaction.PlayerInteractor.SuppressPrompt =
                CurrentStation != Station.None;
        }

        void OnDisable()
        {
            // Never leave the prompt suppressed behind us - this component being switched off must
            // not be the reason a player in the room is told nothing.
            CSSSimulation.Interaction.PlayerInteractor.SuppressPrompt = false;
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

            if (visible != wasVisible) Show(visible);

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
        /// Switches on any exit button a scene left inactive.
        ///
        /// This component hides itself with a CanvasGroup, which means its GameObject has to be
        /// active for it to work at all - and a button authored switched off has no code of its own
        /// running to notice. Something outside it has to look, exactly as MobileControlsVisibility
        /// does for the touch controls.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RevealAll()
        {
            StationExitButton[] found = Object.FindObjectsByType<StationExitButton>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (StationExitButton b in found)
            {
                if (b == null || b.gameObject.activeSelf) continue;
                b.gameObject.SetActive(true);
                Debug.Log("[StationExitButton] '" + b.name + "' was switched off in this scene - " +
                          "turned on, because it hides itself and cannot do that while inactive.", b);
            }
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