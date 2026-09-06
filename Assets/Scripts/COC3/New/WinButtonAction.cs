using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Makes a drawn button do one shell thing: open a window, close one, or open the Start menu.
    ///
    /// ATTACH THIS TO: any Button the desktop builder creates - a Start-menu entry, a desktop
    /// icon, a "Change..." or "Properties..." button, a Cancel, a title-bar X.
    ///
    /// WHY A COMPONENT RATHER THAN AN OnClick ENTRY
    /// A persistent OnClick listener with a string argument has to be written through
    /// UnityEventTools at build time, and it is invisible in the Inspector afterwards - you get a
    /// row saying "OpenWindow" with an argument you cannot easily read or change. A component with
    /// a Window Id field shows you what the button does at a glance, and you can retarget it
    /// without touching the builder.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class WinButtonAction : MonoBehaviour
    {
        public enum Kind
        {
            OpenWindow,
            CloseWindow,
            ToggleStartMenu
        }

        [Tooltip("What pressing this button does.")]
        public Kind action = Kind.OpenWindow;

        [Tooltip("OpenWindow: which window, by its WinWindow.windowId.")]
        public string windowId = "";

        [Tooltip("OpenWindow: the window this button lives in, if any. Set it and the new window " +
                 "is opened as a CHILD - cancelling out of it returns here rather than to the " +
                 "bare desktop, and closing this one takes it with it.\n\n" +
                 "CloseWindow: the window to close. Usually the one this button is in.")]
        public WinWindow window;

        private void Awake()
        {
            var button = GetComponent<Button>();
            button.onClick.RemoveListener(Perform);
            button.onClick.AddListener(Perform);
        }

        public void Perform()
        {
            switch (action)
            {
                case Kind.OpenWindow:
                    OpenTarget();
                    break;

                case Kind.CloseWindow:
                    if (window != null) window.Close();
                    break;

                case Kind.ToggleStartMenu:
                    if (WinDesktop.Instance != null) WinDesktop.Instance.ToggleStartMenu();
                    break;
            }
        }

        private void OpenTarget()
        {
            if (WinDesktop.Instance == null)
            {
                Debug.LogWarning("[WinButtonAction] No WinDesktop in the scene.", this);
                return;
            }

            var target = WinDesktop.Instance.Find(windowId);

            if (target == null)
            {
                Debug.LogWarning($"[WinButtonAction] '{name}' wants to open '{windowId}', which no " +
                                 "WinWindow in the scene claims.", this);
                return;
            }

            WinDesktop.Instance.CloseStartMenu();
            target.Open(window);
        }
    }
}
