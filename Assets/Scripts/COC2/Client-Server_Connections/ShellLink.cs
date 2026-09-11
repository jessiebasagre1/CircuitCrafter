// ShellLink.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// One button, one destination. Sits on the button and calls the shell's Open() with its own id.
    ///
    /// WHY A COMPONENT RATHER THAN A STRING ARGUMENT
    ///
    /// A UnityEvent listener that passes a string has to be bound by method name at edit time, and
    /// when that lookup fails - a renamed method, a refactor, a different assembly - it fails
    /// silently and leaves a button that looks wired and does nothing. A no-argument call into a
    /// component that already knows where it is going cannot break that way, and the destination is
    /// visible in the Inspector on the button itself rather than buried in an event row.
    ///
    /// THIS MUST BE ITS OWN FILE. Unity builds one MonoScript per .cs file, named after the file, so
    /// a MonoBehaviour declared inside another script's file has no MonoScript and can never be
    /// attached - not by Add Component, not by AddComponent&lt;T&gt;() from an editor tool. The class
    /// name and the file name have to match.
    /// </summary>
    public class ShellLink : MonoBehaviour
    {
        [Tooltip("The desktop this button belongs to.")]
        public WindowsShell shell;

        [Tooltip("Which window to open. Must match an id in the shell's Screens list.")]
        public string screenId = "";

        /// <summary>Hooked to the button's On Click.</summary>
        public void Go()
        {
            if (shell != null) shell.Open(screenId);
        }
    }
}