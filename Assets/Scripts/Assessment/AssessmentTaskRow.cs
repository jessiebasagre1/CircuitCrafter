using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// One line of the assessment task list: a checkbox, and what it is you have to do.
    ///
    /// ATTACH THIS TO: each row object under the list body. The builder makes them; you should
    /// not need to add one by hand.
    ///
    /// IT HOLDS NO STATE AND DECIDES NOTHING. AssessmentTaskList reads TaskManager and tells
    /// each row what it is. Keeping the row dumb is what lets the list be rebuilt, reordered or
    /// refilled without any row having an opinion about it.
    ///
    /// WHY A TICKED BOX AND A STRUCK-THROUGH LABEL RATHER THAN JUST A COLOUR
    /// Colour alone is the weakest possible signal: about one boy in twelve cannot separate the
    /// green from the grey, the game is shown on projectors that wash it out, and a learner
    /// glancing at this while holding a screwdriver has no time to compare shades. A box that
    /// is either empty or ticked reads instantly, survives being printed in black and white,
    /// and still says the same thing if the colours are restyled later.
    /// </summary>
    public class AssessmentTaskRow : MonoBehaviour
    {
        public enum State
        {
            /// <summary>Not started, and not the one to do next.</summary>
            Pending = 0,
            /// <summary>The next task in order - what the learner should be doing now.</summary>
            Current = 1,
            /// <summary>Finished.</summary>
            Done = 2,
        }

        [Header("Parts")]
        [Tooltip("The square outline. Its colour changes with state.")]
        public Image checkBox;

        [Tooltip("The tick inside the box. Switched off until the task is done.")]
        public Image checkMark;

        [Tooltip("The task name.")]
        public TMP_Text label;

        [Tooltip("Optional - a panel behind the whole row, lit only while this is the current task.")]
        public Image rowHighlight;

        [Header("Colours")]
        public Color pendingBox = new Color32(0x6B, 0x7A, 0xA8, 0xFF);
        public Color currentBox = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        public Color doneBox = new Color32(0x3D, 0xD5, 0x83, 0xFF);

        public Color pendingText = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
        public Color currentText = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        public Color doneText = new Color32(0x6E, 0x85, 0xA8, 0xFF);

        public Color highlightOn = new Color32(0x2A, 0x3A, 0xB5, 0x80);
        public Color highlightOff = new Color32(0x00, 0x00, 0x00, 0x00);

        [Header("Text")]
        [Tooltip("Strike a completed task through. Needs Rich Text left ON on the label, which " +
                 "the builder does.")]
        public bool strikeThroughWhenDone = true;

        /// <summary>The label without any markup. Held separately because the strikethrough is
        /// applied by wrapping the text in tags - read the wrapped version back and you would
        /// wrap it again, and again, every refresh.</summary>
        private string plainLabel = "";

        public State Current_ { get; private set; } = State.Pending;

        // ==================================================================

        public void SetLabel(string text)
        {
            plainLabel = text ?? "";
            ApplyLabel();
        }

        public void SetState(State state)
        {
            Current_ = state;

            if (checkMark != null) checkMark.enabled = state == State.Done;

            if (checkBox != null)
            {
                checkBox.color = state == State.Done ? doneBox
                               : state == State.Current ? currentBox
                               : pendingBox;
            }

            if (label != null)
            {
                label.color = state == State.Done ? doneText
                            : state == State.Current ? currentText
                            : pendingText;

                label.fontStyle = state == State.Current ? FontStyles.Bold : FontStyles.Normal;
            }

            if (rowHighlight != null)
                rowHighlight.color = state == State.Current ? highlightOn : highlightOff;

            ApplyLabel();
        }

        private void ApplyLabel()
        {
            if (label == null) return;

            label.text = (strikeThroughWhenDone && Current_ == State.Done)
                ? "<s>" + plainLabel + "</s>"
                : plainLabel;
        }
    }
}
