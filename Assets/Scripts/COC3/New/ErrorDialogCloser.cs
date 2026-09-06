using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Dismisses the shared Windows error dialog.
    ///
    /// The error dialog is deliberately NOT a WinWindow. A WinWindow is opened from another
    /// window and becomes its child, and closing the parent takes it down - which is right for a
    /// property sheet and wrong for an error, because the error is what tells the learner the
    /// property sheet has refused them. It has to be able to sit on top of the window that
    /// raised it without belonging to it.
    ///
    /// So it is a plain object with its own OK, which is all this is.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ErrorDialogCloser : MonoBehaviour
    {
        [Tooltip("The dialog root switched off when this is pressed.")]
        public GameObject target;

        private void Awake()
        {
            var button = GetComponent<Button>();
            button.onClick.RemoveListener(Dismiss);
            button.onClick.AddListener(Dismiss);
        }

        public void Dismiss()
        {
            if (target != null) target.SetActive(false);
        }
    }
}
