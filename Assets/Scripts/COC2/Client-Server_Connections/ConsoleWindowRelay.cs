// ConsoleWindowRelay.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Sits on the Command Prompt window and keeps ConnectivityCheckStation in step with it.
    ///
    /// WHY THIS EXISTS
    ///
    /// The shell shows and hides windows by switching GameObjects. The check station tracks its own
    /// IsOpen flag, prints the banner when it opens, and refuses to run a command while it thinks it
    /// is closed. Nothing connected the two, so opening the console from the Start menu put the
    /// window on screen with the station still believing it was shut - and SubmitCommand returned on
    /// its very first line. A prompt you could type into and a Ping button that did nothing.
    ///
    /// Reacting to OnEnable and OnDisable means the station cannot disagree with what is actually on
    /// screen, however the window got there - Start menu, Back(), CloseAll(), or the whole desktop
    /// being switched off as the player walks away.
    /// </summary>
    public class ConsoleWindowRelay : MonoBehaviour
    {
        [Tooltip("The station that owns this console. It must be the station belonging to THIS " +
                 "machine - the one whose Console Panel is this very GameObject.")]
        public ConnectivityCheckStation station;

        bool warned;

        void OnEnable()
        {
            if (!Check()) return;

            // The single line that settles "which machine am I actually on". Both desktops hold a
            // window called CommandPrompt and a station called CheckPoint, so every other log entry
            // reads the same whichever one opened. The full path does not.
            Debug.Log("[ConsoleWindowRelay] " + Path(transform) + " opened -> station '" +
                      station.stationName + "'. This is the machine the next ping will credit.", this);

            station.OpenConsole();
        }

        static string Path(Transform t)
        {
            string path = t.name;
            Transform p = t.parent;
            while (p != null) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        void OnDisable()
        {
            if (station != null) station.CloseConsole();
        }

        /// <summary>
        /// Refuses to run when this relay points at another machine's station.
        ///
        /// Two desktops, two consoles, two stations - and one field on each relay deciding which
        /// pairs with which. Get it wrong and everything still looks like it works: the console
        /// opens, the ping runs, a result appears. It is just the WRONG machine's result, so one
        /// station is credited twice, the other never runs, the task sticks at 1 of 2 and only one
        /// success dialog ever fires. Nothing about that points at the field that caused it.
        ///
        /// The station's own Console Panel already records the pairing, so a mismatch between it and
        /// this GameObject is unambiguous - and cheap to catch here, once, at the moment it matters.
        /// </summary>
        bool Check()
        {
            if (station == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[ConsoleWindowRelay] '" + name + "' has no Station assigned, " +
                                     "so this console will open but nothing will answer the Ping " +
                                     "button.", this);
                }
                return false;
            }

            if (station.consolePanel != null && station.consolePanel != gameObject)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogError("[ConsoleWindowRelay] '" + name + "' points at station '" +
                                   station.stationName + "', but THAT station's Console Panel is '" +
                                   station.consolePanel.name + "', not this window. Two desktops are " +
                                   "sharing one station: it will be credited twice and the other " +
                                   "machine will never run, so the task sticks partway and only one " +
                                   "success dialog fires. Point this relay at the station belonging " +
                                   "to this machine.", this);
                }
                return false;
            }

            return true;
        }
    }
}