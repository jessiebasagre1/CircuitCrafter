using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// One simulated Windows window - System Properties, Ethernet Properties, DNS Manager, and
    /// so on. Everything the learner opens is one of these.
    ///
    /// ATTACH THIS TO: the window's root object, the one drawn by COC3WinWidgets.Window().
    /// The object should start switched OFF; WinDesktop turns it on when the learner opens it.
    ///
    /// WHAT IT DELIBERATELY DOES NOT DO
    /// No dragging, no resizing, no minimise. The windows this module needs are the modal
    /// property sheets and wizards of Windows Server administration, and those are not dragged
    /// around during a task - they are opened, filled in, and dismissed with OK or Cancel.
    /// Leaving that out keeps the whole system small enough to trust.
    /// </summary>
    public class WinWindow : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique within the scene. This is what WinDesktop.OpenWindow() and any launch " +
                 "button ask for, e.g. \"SystemProperties\", \"TcpIpv4\".")]
        public string windowId = "Window";

        [Tooltip("Shown in the title bar and in the taskbar button.")]
        public string title = "Window";

        [Header("Behaviour")]
        [Tooltip("Optional full-screen dim drawn behind this window. A modal property sheet in " +
                 "Windows blocks the window that opened it, and this is how that is represented.")]
        public GameObject modalDim;

        [Tooltip("Closing this window also closes anything it opened. A learner who cancels out " +
                 "of Ethernet Properties should not leave TCP/IPv4 floating behind it.")]
        public bool closeChildrenOnClose = true;

        [Header("Events")]
        public UnityEvent OnOpened;
        public UnityEvent OnClosed;

        /// <summary>The window this one was opened from. Closing returns focus there.</summary>
        public WinWindow OpenedFrom { get; private set; }

        public bool IsOpen { get { return gameObject.activeSelf; } }

        // ------------------------------------------------------------------

        /// <summary>Show this window, on top of anything already open.</summary>
        public void Open()
        {
            Open(null);
        }

        public void Open(WinWindow parent)
        {
            OpenedFrom = parent;

            gameObject.SetActive(true);
            if (modalDim != null) modalDim.SetActive(true);

            // Sibling order is what decides which window is in front. A window opened later must
            // draw over one opened earlier, exactly as it would on a real desktop.
            transform.SetAsLastSibling();

            OnOpened?.Invoke();
        }

        /// <summary>Wire this to the title bar's X, and to Cancel.</summary>
        public void Close()
        {
            if (closeChildrenOnClose && WinDesktop.Instance != null)
                WinDesktop.Instance.CloseChildrenOf(this);

            if (modalDim != null) modalDim.SetActive(false);
            gameObject.SetActive(false);

            OnClosed?.Invoke();

            // Bring whatever opened this back to the front, so dismissing TCP/IPv4 leaves the
            // learner looking at Ethernet Properties rather than at the desktop.
            if (OpenedFrom != null && OpenedFrom.IsOpen)
                OpenedFrom.transform.SetAsLastSibling();

            OpenedFrom = null;
        }

        /// <summary>Open a window from this one. Wire a "Properties..." button here.</summary>
        public void OpenChild(WinWindow child)
        {
            if (child != null) child.Open(this);
        }
    }
}
