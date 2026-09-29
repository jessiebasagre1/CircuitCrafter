// WindowsShell.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The desktop the player sits down at: a Start button, a taskbar, and a set of windows that
    /// open on top of each other. It owns nothing but navigation - which window is showing, and how
    /// the player got there.
    ///
    /// WHY THE NAVIGATION IS THE POINT
    ///
    /// Setting a static IP is four numbers. Finding the screen that takes them is six clicks through
    /// Control Panel, and that is the half a trainee actually gets stuck on in the shop. An assessor
    /// watching someone set an address is watching the route as much as the values. So this makes
    /// the route real: Start, Control Panel, Network and Sharing Center, Change adapter settings,
    /// Ethernet Properties, TCP/IPv4 Properties - and records the wrong turns along the way.
    ///
    /// It deliberately does NOT lock the player or hide the HUD. The station that opened the shell
    /// already does that, and having two components fight over Cursor.lockState is how you end up
    /// with a mouse that will not come back.
    ///
    /// SETUP: the Config Panel Builder generates this and every window under it, already wired.
    /// Doing it by hand: put this on the shell root, list each window in Screens with an id, and
    /// hook buttons to Open("id"), Back() and CloseAll().
    /// </summary>
    [DefaultExecutionOrder(215)]
    public class WindowsShell : MonoBehaviour
    {
        [System.Serializable]
        public class Screen
        {
            [Tooltip("Short name used by Open(). 'controlpanel', 'tcpip', 'cmd'.")]
            public string id = "window";
            [Tooltip("The window. Leave it INACTIVE in the scene.")]
            public GameObject panel;
            [Tooltip("Shown in the breadcrumb, e.g. 'Network and Sharing Center'. Blank uses the id.")]
            public string title = "";

            [Tooltip("GUIDE BREADCRUMB: the words on the thing the player CLICKS to open this window - " +
                     "'Change adapter settings' opens the Network Connections window. Leave blank: the " +
                     "windows the desktop builder makes are known by id, and anything else uses Title.")]
            public string clickLabel = "";
        }

        /// <summary>What the breadcrumb across the top shows.</summary>
        public enum BreadcrumbMode
        {
            /// <summary>The whole route before the player starts, with the NEXT thing to click
            /// highlighted - a guide to follow.</summary>
            GuideAhead,
            /// <summary>Only the windows already opened - a record of the player's route.</summary>
            History
        }

        [Header("Windows")]
        [Tooltip("Every window this desktop can show. Only one is visible at a time; the desktop " +
                 "itself is whatever sits behind them and is never hidden.")]
        public List<Screen> screens = new List<Screen>();

        [Header("Breadcrumb (optional)")]
        [Tooltip("The route across the top of the desktop. With Breadcrumb Mode on Guide Ahead it is a " +
                 "guide: the whole route is shown before the first click, and the next thing to click " +
                 "is highlighted. In an assessment it falls back to the History trail unless Guide In " +
                 "Assessment is ticked.")]
        public TMP_Text breadcrumbText;
        public string breadcrumbSeparator = "  >  ";

        [Tooltip("GUIDE AHEAD (default): the breadcrumb shows the whole route from the start, with the " +
                 "next thing to click highlighted - 'Start' is lit before Start is pressed. Needs Expected " +
                 "Path below.\n\n" +
                 "HISTORY: the old behaviour - only the windows already opened.")]
        public BreadcrumbMode breadcrumbMode = BreadcrumbMode.GuideAhead;

        [Tooltip("Show the guide in an assessment too. Off by default: it tells the learner what to " +
                 "click, which is exactly what an assessment is checking they know. With this off, an " +
                 "assessment gets the History breadcrumb instead.")]
        public bool guideInAssessment = false;

        [Tooltip("The route once this machine's IP address is set - to the Command Prompt, for the ping. " +
                 "Used when the DeviceConfigStation that owns this desktop reports Is Configured.")]
        public string[] pingPath = new string[] { "start", "cmd" };

        [Tooltip("Written in front of the route.")]
        public string guidePrefix = "Click in order:   ";

        [Tooltip("The last step on the address route, after the TCP/IPv4 window is open.")]
        public string guideFinishConfig = "Type the job order values, then click OK";

        [Tooltip("The last step on the ping route, after the Command Prompt is open.")]
        public string guideFinishPing = "Type the ping command, then press Enter";

        [Tooltip("Shown instead of the route once the address is set and the ping has passed.")]
        public string guideDoneText = "This computer is done. Press E or click Leave PC to step away.";

        [Tooltip("Added after the route when the window on screen is not on it.")]
        public string offRouteNote = "This window is not on the route - press Escape to go back.";

        [Tooltip("Steps already done.")]
        public Color guideDoneColour = new Color32(0x8F, 0xA3, 0xB8, 0xFF);
        [Tooltip("The next thing to click.")]
        public Color guideNextColour = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        [Tooltip("Steps still ahead.")]
        public Color guideAheadColour = new Color32(0xE6, 0xEE, 0xF5, 0xFF);
        [Tooltip("The off-route note.")]
        public Color guideWarnColour = new Color32(0xFF, 0x8A, 0x80, 0xFF);

        [Header("Marking the route (optional)")]
        [Tooltip("The ids the player is meant to visit, in order. Leave empty and nothing is marked.\n\n" +
                 "Opening a window that is not the next one in this list counts as a wrong turn. " +
                 "That number is worth recording - a trainee who walks straight to the setting and " +
                 "one who opened four wrong panels first have not demonstrated the same thing.")]
        public string[] expectedPath = new string[0];

        [Header("Leaving")]
        [Tooltip("Closes the whole desktop - hook this to the station's Close(). Escape does it too.")]
        public UnityEvent OnCloseRequested;
        public KeyCode exitKey = KeyCode.Escape;
        [Tooltip("With a window open, Escape closes just that window and Back() steps out. Only on " +
                 "the bare desktop does Escape leave the PC. That is how a real machine behaves and " +
                 "it stops one keypress throwing away six clicks of navigation.")]
        public bool escapeClosesWindowFirst = true;

        [Header("Events")]
        public UnityEvent OnScreenChanged;

        [Header("Diagnostics")]
        public bool logNavigation = true;

        /// <summary>Id of the window on top, or empty when the player is looking at the desktop.</summary>
        public string CurrentScreen { get; private set; }

        /// <summary>How many times a window was opened out of order. Read this into an assessment.</summary>
        public int WrongTurns { get; private set; }

        readonly List<string> history = new List<string>();
        int expectedIndex;

        // The machine this desktop belongs to - read so the guide can switch to the ping route.
        DeviceConfigStation ownerStation;
        ConnectivityCheckStation ownerCheck;
        bool ownerLooked;
        int lastOwnerState = -1;

        void OnEnable()
        {
            // Every visit starts at the desktop. Without this the shell reopens on whichever window
            // the player left it on, so the second machine begins halfway down the route and the
            // navigation is never practised twice.
            history.Clear();
            expectedIndex = 0;
            WrongTurns = 0;
            CloseAll();
        }

        void Update()
        {
            // The route changes when the address is accepted or the ping passes, and neither of those
            // goes through Open/Back/CloseAll - so the guide watches for it here. Two bools compared per
            // frame; the text is only rebuilt when one of them changes.
            if (UsingGuide())
            {
                int state = OwnerState();
                if (state != lastOwnerState)
                {
                    lastOwnerState = state;
                    RefreshBreadcrumb();
                }
            }

            if (!Input.GetKeyDown(exitKey)) return;

            if (escapeClosesWindowFirst && !string.IsNullOrEmpty(CurrentScreen)) Back();
            else OnCloseRequested?.Invoke();
        }

        // ---------------------------------------------------------------- navigation

        /// <summary>Shows one window and hides the rest. Hook this to a button, id typed in the box.</summary>
        public void Open(string id)
        {
            Screen target = Find(id);
            if (target == null)
            {
                Debug.LogWarning("[WindowsShell] " + name + ": nothing on this desktop has the id '" + id +
                                 "'. Check the button's Open() argument against the Screens list.", this);
                return;
            }

            foreach (Screen s in screens)
                if (s != null && s.panel != null)
                    s.panel.SetActive(s == target);

            if (CurrentScreen != id) history.Add(id);
            CurrentScreen = id;

            MarkRoute(id);
            Refresh();
        }

        /// <summary>Steps back one window, or to the desktop when there is nothing left.</summary>
        public void Back()
        {
            if (history.Count > 0) history.RemoveAt(history.Count - 1);

            if (history.Count == 0) { CloseAll(); return; }
            string previous = history[history.Count - 1];

            Screen target = Find(previous);
            foreach (Screen s in screens)
                if (s != null && s.panel != null)
                    s.panel.SetActive(s == target);

            CurrentScreen = previous;
            Refresh();
        }

        /// <summary>Hides every window, leaving the bare desktop.</summary>
        public void CloseAll()
        {
            foreach (Screen s in screens)
                if (s != null && s.panel != null && s.panel.activeSelf)
                    s.panel.SetActive(false);

            history.Clear();
            CurrentScreen = "";
            Refresh();
        }

        /// <summary>Leaves the machine entirely. Hook a taskbar power button here.</summary>
        public void RequestClose() { OnCloseRequested?.Invoke(); }

        // ---------------------------------------------------------------- marking

        /// <summary>
        /// Advances through Expected Path, counting anything opened out of turn.
        ///
        /// Deliberately forgiving about going BACK: retracing your steps is how people navigate, and
        /// penalising it would mark hesitation rather than ignorance. Only opening a window that is
        /// neither the next step nor one already visited counts against the trainee.
        /// </summary>
        void MarkRoute(string id)
        {
            if (expectedPath == null || expectedPath.Length == 0) return;
            if (expectedIndex >= expectedPath.Length) return;

            if (expectedPath[expectedIndex] == id) { expectedIndex++; return; }

            for (int i = 0; i < expectedIndex; i++)
                if (expectedPath[i] == id) return;   // been here already, not a wrong turn

            WrongTurns++;
            if (logNavigation)
                Debug.Log("[WindowsShell] " + name + ": wrong turn " + WrongTurns + " - opened '" + id +
                          "' when the next step was '" + expectedPath[expectedIndex] + "'.", this);
        }

        void Refresh()
        {
            RefreshBreadcrumb();

            if (logNavigation && !string.IsNullOrEmpty(CurrentScreen))
                Debug.Log("[WindowsShell] " + name + ": opened '" + CurrentScreen + "'.", this);

            OnScreenChanged?.Invoke();
        }

        // ---------------------------------------------------------------- breadcrumb

        bool UsingGuide()
        {
            if (breadcrumbText == null || breadcrumbMode != BreadcrumbMode.GuideAhead) return false;
            if (AssessmentMode.IsActive && !guideInAssessment) return false;
            return expectedPath != null && expectedPath.Length > 0;
        }

        void RefreshBreadcrumb()
        {
            if (breadcrumbText == null) return;

            if (!UsingGuide())
            {
                breadcrumbText.text = HistoryTrail();
                return;
            }

            // The colours are rich-text tags, so the label has to read them rather than print them.
            breadcrumbText.richText = true;
            breadcrumbText.text = GuideText();
        }

        /// <summary>0 = address not set, 1 = set, 3 = set and the ping has passed.</summary>
        int OwnerState()
        {
            LookForOwner();
            if (ownerStation == null || !ownerStation.IsConfigured) return 0;
            return ownerCheck != null && ownerCheck.IsConnected ? 3 : 1;
        }

        /// <summary>The old breadcrumb: the windows opened so far.</summary>
        string HistoryTrail()
        {
            string trail = "";
            foreach (string id in history)
            {
                Screen s = Find(id);
                string label = s != null && !string.IsNullOrEmpty(s.title) ? s.title : id;
                trail += (trail.Length == 0 ? "" : breadcrumbSeparator) + label;
            }
            return trail;
        }

        /// <summary>
        /// The whole route, with how far along it the player is right now.
        ///
        /// Progress is read from the windows currently open (the Back stack), not from everything ever
        /// clicked - so stepping Back, or OK closing the TCP/IPv4 window, moves the highlight back to
        /// match what is really on screen. Before anything is open, the first step - Start - is lit.
        /// </summary>
        string GuideText()
        {
            LookForOwner();

            string[] route = expectedPath;
            string finish = guideFinishConfig;
            if (ownerStation != null && ownerStation.IsConfigured)
            {
                if (ownerCheck != null && ownerCheck.IsConnected)
                    return Colour(guideDoneText, guideDoneColour);
                if (pingPath != null && pingPath.Length > 0) { route = pingPath; finish = guideFinishPing; }
            }

            // How many steps of the route are open, in order.
            int done = 0;
            foreach (string id in history)
                if (done < route.Length && id == route[done]) done++;

            var sb = new System.Text.StringBuilder();
            sb.Append(Colour(guidePrefix, guideAheadColour));
            for (int i = 0; i < route.Length; i++)
            {
                if (i > 0) sb.Append(Colour(breadcrumbSeparator, guideDoneColour));
                string label = ClickLabel(route[i]);

                if (i < done) sb.Append(Colour(label, guideDoneColour));
                else if (i == done) sb.Append("<b>").Append(Colour(label, guideNextColour)).Append("</b>");
                else sb.Append(Colour(label, guideAheadColour));
            }

            // What to do once the last window is open - lit when that is where the player is.
            if (!string.IsNullOrEmpty(finish))
            {
                sb.Append(Colour(breadcrumbSeparator, guideDoneColour));
                if (done >= route.Length) sb.Append("<b>").Append(Colour(finish, guideNextColour)).Append("</b>");
                else sb.Append(Colour(finish, guideAheadColour));
            }

            // A window that is not on the route - say so, and how to get back.
            if (!string.IsNullOrEmpty(CurrentScreen) && System.Array.IndexOf(route, CurrentScreen) < 0 &&
                !string.IsNullOrEmpty(offRouteNote))
                sb.Append("\n").Append(Colour(offRouteNote, guideWarnColour));

            return sb.ToString();
        }

        /// <summary>The words on the thing the player clicks to open this window.</summary>
        string ClickLabel(string id)
        {
            Screen s = Find(id);
            if (s != null && !string.IsNullOrEmpty(s.clickLabel)) return s.clickLabel;

            // The windows WindowsDesktopBuilder makes, named by what is clicked to reach them.
            switch (id)
            {
                case "start": return "Start";
                case "controlpanel": return "Control Panel";
                case "networkcenter": return "Network and Sharing Center";
                case "adapters": return "Change adapter settings";
                case "ethernetprops": return "Ethernet";
                case "tcpip": return "Internet Protocol Version 4 (TCP/IPv4)";
                case "cmd": return "Command Prompt";
            }
            return s != null && !string.IsNullOrEmpty(s.title) ? s.title : id;
        }

        static string Colour(string text, Color c)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + text + "</color>";
        }

        /// <summary>The config and check stations this desktop belongs to, found once.</summary>
        void LookForOwner()
        {
            if (ownerLooked) return;
            ownerLooked = true;

            DeviceConfigStation[] stations = Object.FindObjectsByType<DeviceConfigStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (DeviceConfigStation s in stations)
            {
                if (s == null || s.panel == null) continue;
                if (s.panel == gameObject || transform.IsChildOf(s.panel.transform) ||
                    s.panel.transform.IsChildOf(transform))
                { ownerStation = s; break; }
            }
            if (ownerStation == null) return;

            ConnectivityCheckStation[] checks = Object.FindObjectsByType<ConnectivityCheckStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ConnectivityCheckStation c in checks)
                if (c != null && c.requiresConfig == ownerStation) { ownerCheck = c; break; }
        }

        Screen Find(string id)
        {
            foreach (Screen s in screens)
                if (s != null && s.id == id) return s;
            return null;
        }

        // ---------------------------------------------------------------- self-reporting

        void Start() { ValidateSetup(); }

        void ValidateSetup()
        {
            List<string> problems = new List<string>();

            if (screens.Count == 0)
                problems.Add("Screens is empty - this desktop has no windows to open.");

            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < screens.Count; i++)
            {
                Screen s = screens[i];
                if (s == null) { problems.Add("Screen " + i + " is empty."); continue; }
                if (string.IsNullOrEmpty(s.id)) problems.Add("Screen " + i + " has no id.");
                else if (!seen.Add(s.id)) problems.Add("Two screens share the id '" + s.id +
                                                       "'. Only the first can ever be opened.");
                if (s.panel == null) problems.Add("Screen '" + s.id + "' has no panel assigned.");
            }

            foreach (string id in expectedPath)
                if (Find(id) == null)
                    problems.Add("Expected Path names '" + id + "', which is not in Screens. That " +
                                 "step can never be reached, so the route can never be completed.");

            if (OnCloseRequested.GetPersistentEventCount() == 0)
                problems.Add("On Close Requested is empty, so Escape on the desktop does nothing and " +
                             "the player has no way off the machine. Hook it to the station's Close().");

            if (problems.Count == 0)
            {
                Debug.Log("[WindowsShell] " + name + ": " + screens.Count + " window(s) wired" +
                          (expectedPath.Length > 0 ? ", route of " + expectedPath.Length + " step(s) marked." : "."),
                          this);
                return;
            }

            string msg = "[WindowsShell] " + name + " setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>Prints every window and the route, so a button wired to a typo is obvious.</summary>
        [ContextMenu("Report windows")]
        public void LogWindows()
        {
            string msg = "[WindowsShell] " + name + ", " + screens.Count + " window(s):";
            foreach (Screen s in screens)
            {
                if (s == null) { msg += "\n    (empty)"; continue; }
                msg += "\n    " + (s.id == CurrentScreen ? " >" : "  ") + "'" + s.id + "'   " +
                       (string.IsNullOrEmpty(s.title) ? "" : s.title + "   ") +
                       (s.panel != null ? (s.panel.activeSelf ? "showing" : "hidden") : "NO PANEL");
            }
            if (expectedPath.Length > 0)
                msg += "\n    ---- route: " + string.Join(" > ", expectedPath) +
                       "   at step " + expectedIndex + " of " + expectedPath.Length +
                       ", wrong turns: " + WrongTurns;
            Debug.LogWarning(msg, this);
        }
    }
}