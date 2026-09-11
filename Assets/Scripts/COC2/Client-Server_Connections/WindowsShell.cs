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
        }

        [Header("Windows")]
        [Tooltip("Every window this desktop can show. Only one is visible at a time; the desktop " +
                 "itself is whatever sits behind them and is never hidden.")]
        public List<Screen> screens = new List<Screen>();

        [Header("Breadcrumb (optional)")]
        [Tooltip("Shows the route taken so far - 'Control Panel > Network and Sharing Center'. " +
                 "Useful while you are building the chain; hide it for a graded build if you want " +
                 "the trainee to navigate unaided.")]
        public TMP_Text breadcrumbText;
        public string breadcrumbSeparator = "  >  ";

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
            if (breadcrumbText != null)
            {
                string trail = "";
                foreach (string id in history)
                {
                    Screen s = Find(id);
                    string label = s != null && !string.IsNullOrEmpty(s.title) ? s.title : id;
                    trail += (trail.Length == 0 ? "" : breadcrumbSeparator) + label;
                }
                breadcrumbText.text = trail;
            }

            if (logNavigation && !string.IsNullOrEmpty(CurrentScreen))
                Debug.Log("[WindowsShell] " + name + ": opened '" + CurrentScreen + "'.", this);

            OnScreenChanged?.Invoke();
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