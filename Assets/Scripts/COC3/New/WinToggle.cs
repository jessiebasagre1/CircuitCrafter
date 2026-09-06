using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// A checkbox or radio button that actually holds state.
    ///
    /// ATTACH THIS TO: the row created by COC3WinWidgets.Checkbox() or .Radio(). Those draw the
    /// control and return a Button, but the tick or dot they draw is FIXED at build time - the
    /// factory paints a ticked box or an empty one and has no notion of the learner changing it.
    /// This is what makes it respond.
    ///
    /// THE INDICATOR IS AN IMAGE, NOT A GLYPH
    /// The widget factory uses "✓" and "◉" characters, and LiberationSans has neither - the
    /// Console has already been telling you so about "✕". A missing glyph renders as a hollow box
    /// and logs a warning on every rebuild. Point `indicator` at a small Image instead and it
    /// renders identically everywhere, on every font, on a phone included.
    ///
    /// RADIO BEHAVIOUR comes from `groupId`. Leave it empty for a checkbox. Give two or more
    /// toggles the same non-empty groupId and they behave as a radio set: turning one on turns
    /// the others off, and clicking the one already on does nothing - exactly like Windows.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class WinToggle : MonoBehaviour
    {
        [Header("State")]
        [Tooltip("Starting state, and the state the reset restores.")]
        public bool isOn;

        [Tooltip("Empty = checkbox. Shared non-empty value = radio group. Only toggles under the " +
                 "same WinWindow are grouped, so the same id can be reused in another window.")]
        public string groupId = "";

        [Header("Look")]
        [Tooltip("Shown when on, hidden when off. Use a small Image - a tick shape for a checkbox, " +
                 "a filled dot for a radio.")]
        public Graphic indicator;

        [Header("Events")]
        public UnityEvent<bool> OnChanged;

        private Button button;
        private bool initialState;

        // ------------------------------------------------------------------

        private void Awake()
        {
            initialState = isOn;

            button = GetComponent<Button>();
            button.onClick.RemoveListener(Clicked);
            button.onClick.AddListener(Clicked);

            Apply();
        }

        private void OnEnable()
        {
            // A window reopened after a Cancel should look the way it did before, not the way the
            // learner left it half-filled.
            Apply();
        }

        private void Clicked()
        {
            if (!string.IsNullOrEmpty(groupId))
            {
                // Radio: clicking the selected one is a no-op, and never leaves the group empty.
                if (isOn) return;
                Set(true);
            }
            else
            {
                Set(!isOn);
            }
        }

        /// <summary>Set the state and update the group. Also callable from a UnityEvent.</summary>
        public void Set(bool on)
        {
            isOn = on;
            Apply();

            if (on && !string.IsNullOrEmpty(groupId)) TurnOffSiblings();

            OnChanged?.Invoke(isOn);
        }

        /// <summary>Back to how the window was built. Called when a step is reset.</summary>
        public void ResetToInitial()
        {
            isOn = initialState;
            Apply();
        }

        private void Apply()
        {
            if (indicator != null) indicator.enabled = isOn;
        }

        /// <summary>
        /// Scoped to the owning WinWindow rather than the whole scene, so "Use the following IP
        /// address" in one window cannot switch off a same-named radio in another.
        /// </summary>
        private void TurnOffSiblings()
        {
            Transform scope = transform;
            var owner = GetComponentInParent<WinWindow>();
            if (owner != null) scope = owner.transform;

            var all = scope.GetComponentsInChildren<WinToggle>(true);

            foreach (var other in all)
            {
                if (other == null || other == this) continue;
                if (other.groupId != groupId) continue;
                if (!other.isOn) continue;

                other.isOn = false;
                other.Apply();
                other.OnChanged?.Invoke(false);
            }
        }
    }
}
