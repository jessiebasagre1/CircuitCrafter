using System.Collections.Generic;
using UnityEngine;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// The desktop shell: taskbar, Start menu, and the register of every window in the scene.
    ///
    /// ATTACH THIS TO: the desktop root object - the one holding the wallpaper, the icons and
    /// the taskbar. It must be ACTIVE from scene start, because it is what switches the windows
    /// on and a switched-off object cannot switch anything on.
    ///
    /// WHY A REGISTER RATHER THAN DIRECT REFERENCES
    /// A launch button says "open SystemProperties" by name. That means the Start menu, a desktop
    /// icon, a taskbar button and a "Properties..." button inside another window can all open the
    /// same window without four separate Inspector references to keep in sync - and a step can
    /// ask "is DNS Manager open?" without holding a reference to it either.
    /// </summary>
    public class WinDesktop : MonoBehaviour
    {
        public static WinDesktop Instance { get; private set; }

        [HeaderAttribute("Shell")]
        [TooltipAttribute("The Start menu panel. Starts switched off.")]
        public GameObject startMenu;

        [TooltipAttribute("Every window this desktop owns. Found automatically if left empty - see Awake.")]
        public List<WinWindow> windows = new List<WinWindow>();

        [HeaderAttribute("Start state")]
        [TooltipAttribute("Close every window when the desktop is first shown, so a scene saved with a " +
                 "window left open in the editor still starts clean.")]
        public bool closeAllOnAwake = true;

        // ------------------------------------------------------------------

        private void Awake()
        {
            Instance = this;

            // A fresh run starts on a desktop nothing has been done to: no Users folder in File
            // Explorer, no STUDENTS in the permissions list. Without this, a second playthrough in
            // the same session would open with the first one's work already showing.
            WinActionBus.Forget();

            // Include inactive: every window starts switched off, so an active-only search would
            // find none of them. This is the single most common way a registry like this ends up
            // silently empty.
            if (windows == null || windows.Count == 0)
                windows = new List<WinWindow>(GetComponentsInChildren<WinWindow>(true));

            if (closeAllOnAwake) CloseAll();
            if (startMenu != null) startMenu.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // Windows
        // ------------------------------------------------------------------

        public WinWindow Find(string windowId)
        {
            if (string.IsNullOrEmpty(windowId)) return null;

            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                if (w != null && w.windowId == windowId) return w;
            }
            return null;
        }

        /// <summary>True if that window is currently on screen. Used by step gates.</summary>
        public bool IsOpen(string windowId)
        {
            var w = Find(windowId);
            return w != null && w.IsOpen;
        }

        /// <summary>Open by id. Wire Start-menu entries and desktop icons to WinLaunchButton,
        /// which calls this.</summary>
        public void OpenWindow(string windowId)
        {
            var w = Find(windowId);

            if (w == null)
            {
                Debug.LogWarning($"[WinDesktop] No window with id '{windowId}'. Check the id on " +
                                 "the WinWindow component and on whatever tried to open it.", this);
                return;
            }

            // The other machine's window. Refused OUT LOUD rather than silently: a launch button
            // that does nothing is indistinguishable from a broken one, and this project has
            // already spent an evening on exactly that.
            if (WinMachineFilter.Instance != null && !WinMachineFilter.Instance.Allows(w))
            {
                Debug.Log($"[WinDesktop] '{windowId}' belongs to the other machine - you are at " +
                          $"{WinMachineFilter.Instance.Current}. Walk to the other computer.", this);
                return;
            }

            CloseStartMenu();
            w.Open();
        }

        public void CloseWindow(string windowId)
        {
            var w = Find(windowId);
            if (w != null) w.Close();
        }

        public void CloseAll()
        {
            for (int i = 0; i < windows.Count; i++)
                if (windows[i] != null && windows[i].IsOpen) windows[i].Close();
        }

        /// <summary>
        /// Closes anything opened FROM the given window. Called by WinWindow.Close() so cancelling
        /// out of a property sheet takes its child dialogs with it.
        /// </summary>
        public void CloseChildrenOf(WinWindow parent)
        {
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                if (w != null && w != parent && w.IsOpen && w.OpenedFrom == parent)
                    w.Close();
            }
        }

        // ------------------------------------------------------------------
        // Start menu
        // ------------------------------------------------------------------

        /// <summary>Wire the Start button here.</summary>
        public void ToggleStartMenu()
        {
            if (startMenu == null) return;

            startMenu.SetActive(!startMenu.activeSelf);
            if (startMenu.activeSelf) startMenu.transform.SetAsLastSibling();
        }

        public void CloseStartMenu()
        {
            if (startMenu != null) startMenu.SetActive(false);
        }
    }
}