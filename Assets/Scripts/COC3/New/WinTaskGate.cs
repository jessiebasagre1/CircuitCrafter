using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Carries what a window reported to whoever is listening, without either side holding a
    /// reference to the other. A step gate subscribes; a window's OK button publishes.
    /// </summary>
    public static class WinActionBus
    {
        /// <summary>actionId, and true when the learner got it right.</summary>
        public static event Action<string, bool> OnAction;

        /// <summary>
        /// Every action accepted so far this run.
        ///
        /// The event on its own cannot make the desktop remember anything: a window that was shut
        /// when the folder was created never heard about it, and a window opened for the first time
        /// at step 17 missed the lot. This is what a window consults when it opens.
        /// </summary>
        private static readonly HashSet<string> succeeded = new HashSet<string>();

        public static void Report(string actionId, bool success)
        {
            if (string.IsNullOrEmpty(actionId)) return;

            if (success) succeeded.Add(actionId);

            Debug.Log($"[WinActionBus] {actionId} -> {(success ? "OK" : "REJECTED")}");
            OnAction?.Invoke(actionId, success);
        }

        public static bool HasSucceeded(string actionId)
        {
            return !string.IsNullOrEmpty(actionId) && succeeded.Contains(actionId);
        }

        /// <summary>
        /// Wipes the record, so a replay of COC 3 starts on a desktop nothing has been done to.
        /// Called from WinDesktop.Awake; call it yourself too if you ever restart the module
        /// without reloading the scene.
        /// </summary>
        public static void Forget()
        {
            succeeded.Clear();
        }

        /// <summary>
        /// Statics survive a Play session when Enter Play Mode Options has domain reload switched
        /// off - a common setting for faster iteration, and one that would otherwise leave the
        /// second Play showing a folder created in the first. This runs before the scene loads
        /// either way.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            succeeded.Clear();
            OnAction = null;
        }
    }

    /// <summary>
    /// The OK button of a simulated Windows dialog: checks what the learner filled in, and either
    /// accepts it or shows a Windows-style error.
    ///
    /// ATTACH THIS TO: the window whose OK button it guards. Point `submitButton` at that OK.
    ///
    /// THIS IS WHERE THE MARKING MOVED TO
    /// The old COC 3 asked the learner to type an answer into a labelled box and compared strings.
    /// Now the marking lives on the control a technician would actually press, and the conditions
    /// are the ones Windows itself would impose: the right radio selected, the right values in the
    /// right boxes. Getting to this button at all means they navigated there correctly.
    ///
    /// A rejected attempt does NOT advance and does NOT punish silently - it shows the error a
    /// real dialog would show, and the learner stays in the window to correct it.
    /// </summary>
    public class WinTaskGate : MonoBehaviour
    {
        [Serializable]
        public class FieldRequirement
        {
            public TMP_InputField field;

            [TooltipAttribute("What this box must contain. Whitespace is trimmed and runs of backslashes " +
                     "are collapsed, so a UNC path is judged the way the learner can type it.")]
            public string expected = "";

            [TooltipAttribute("Windows is not case sensitive about host names, shares or user names. " +
                     "Leave off unless the value genuinely is.")]
            public bool caseSensitive = false;

            [TooltipAttribute("Named in the error message, e.g. \"IP address\".")]
            public string displayName = "value";
        }

        [HeaderAttribute("What this reports")]
        [TooltipAttribute("Matched against COC3StepGate's bindings, e.g. \"set_computer_name\".")]
        public string actionId = "action";

        [TooltipAttribute("The button that submits this dialog - its OK. The listener is added in Awake, " +
                 "so leave the button's own OnClick empty.")]
        public Button submitButton;

        [TooltipAttribute("Leave empty for a gate that always applies.\n\n" +
                 "Set to a stepId and this gate only answers while that step is on screen, so two " +
                 "gates can share one OK button and each check its own answer. That is how the New " +
                 "Folder dialog asks for 'Users' at step 12 and 'Redirected' at step 23 without " +
                 "being two different windows - which is what Windows does, and what a technician " +
                 "expects.\n\n" +
                 "Ignored entirely if there is no COC3StepGate in the scene.")]
        public string requiredStepId = "";

        [HeaderAttribute("Conditions - ALL must hold")]
        public FieldRequirement[] fields;

        [TooltipAttribute("Toggles that must be ON, e.g. 'Use the following IP address'.")]
        public WinToggle[] mustBeOn;

        [TooltipAttribute("Toggles that must be OFF.")]
        public WinToggle[] mustBeOff;

        [TooltipAttribute("Action ids that must ALREADY have been accepted, anywhere on the desktop.\n\n" +
                 "For a step whose answer is partly something done earlier in the same window - " +
                 "granting the group Print only counts once Everyone has been removed - and there " +
                 "is no toggle on screen that records it. Read from WinActionBus's memory.")]
        public string[] requiredActions;

        [TooltipAttribute("Shown when one of Required Actions has not happened yet.")]
        [TextAreaAttribute(2, 4)]
        public string missingActionMessage =
            "Something this depends on has not been done yet. Check the step instruction.";

        [HeaderAttribute("On success")]
        [TooltipAttribute("Closed when accepted - normally the window this gate is on, the way OK dismisses " +
                 "a property sheet.")]
        public WinWindow closeOnSuccess;

        [TooltipAttribute("Opened when accepted, as a child of Open On Success Parent.\n\n" +
                 "This is what makes a guarded 'Properties...' button work: Ethernet Properties " +
                 "only opens the TCP/IPv4 sheet if the learner selected the IPv4 row first, and " +
                 "shows the Windows error if they did not.")]
        public WinWindow openOnSuccess;

        [TooltipAttribute("The window Open On Success is opened FROM, so cancelling returns here.")]
        public WinWindow openOnSuccessParent;

        [TooltipAttribute("The same window, by id - a safety net for the reference above.\n\n" +
                 "An object reference that comes back empty is silent: the OK is accepted, the " +
                 "dialog closes, and the window that should have appeared simply does not, with " +
                 "nothing in the Console to say why. An id is text, so it survives anything that " +
                 "can drop a reference, and WinDesktop looks it up when it is needed.")]
        public string openOnSuccessId = "";

        public UnityEvent OnAccepted;

        [HeaderAttribute("On failure")]
        [TooltipAttribute("A small Windows-style error dialog. Left empty, only the Console reports it.")]
        public GameObject errorDialog;
        public TMP_Text errorText;

        [TooltipAttribute("Shown when a value is wrong. {field} is replaced with the box's display name.")]
        [TextAreaAttribute(2, 4)]
        public string wrongValueMessage =
            "The {field} you have entered is not valid for this network.\n\nCheck the job order and try again.";

        [TooltipAttribute("Shown when a required option has not been selected.")]
        [TextAreaAttribute(2, 4)]
        public string missingOptionMessage =
            "You must choose the correct option before this can be applied.";

        public UnityEvent OnRejected;

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (submitButton == null)
            {
                Debug.LogWarning($"[WinTaskGate] '{name}' has no Submit Button, so nothing can ever " +
                                 "be checked here.", this);
                return;
            }

            submitButton.onClick.RemoveListener(Submit);
            submitButton.onClick.AddListener(Submit);
        }

        /// <summary>Also public so a keyboard Enter or a test harness can drive it.</summary>
        public void Submit()
        {
            // Not my step - the other gate on this button will answer. Silent on purpose: showing
            // an error here would mean every shared dialog complained once per gate.
            if (!AppliesToCurrentStep()) return;

            string problem = FirstProblem();

            if (problem == null)
            {
                WinActionBus.Report(actionId, true);
                OnAccepted?.Invoke();

                if (errorDialog != null) errorDialog.SetActive(false);

                // Open BEFORE closing. Closing a window also closes its children, and if the new
                // window were opened first as a child of the one being dismissed it would be shut
                // again in the same frame.
                WinWindow opening = ResolveOpenOnSuccess();

                if (opening != null) opening.Open(openOnSuccessParent);
                if (closeOnSuccess != null) closeOnSuccess.Close();

                // ...and then raise it again, because Close() ends by bringing whatever opened the
                // dismissed window back to the front. That is right when OK simply dismisses a
                // dialog - you want to be looking at the sheet underneath. It is wrong here: New
                // Object - Group was opened from AD Users and Computers, so closing it lifted the
                // console over the STUDENTS Properties sheet that had just appeared. The sheet was
                // open the whole time, behind a window that looked like nothing had happened.
                if (opening != null && opening.IsOpen)
                    opening.transform.SetAsLastSibling();

                return;
            }

            WinActionBus.Report(actionId, false);
            OnRejected?.Invoke();

            // GUIDANCE: the problem text names what is wrong ("Check: IP address") - corrective, Story
            // only. In an assessment the error box says only that it was not accepted, and the detail
            // goes with the mistake the line above just recorded, for the review screen.
            bool explain = CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.CorrectiveFeedback);
            if (!explain) CircuitCrafter.Guidance.MistakeJournal.AttachCorrectionToLast(problem);

            if (errorText != null) errorText.text = explain ? problem : CircuitCrafter.Guidance.GuidancePolicy.NeutralRejection;
            if (errorDialog != null)
            {
                errorDialog.SetActive(true);
                errorDialog.transform.SetAsLastSibling();
            }
        }

        /// <summary>
        /// The window to open, by reference if there is one and by id if there is not.
        ///
        /// Says so out loud when neither works. A gate that was MEANT to open something and opens
        /// nothing used to be indistinguishable from a gate that was meant to just close - which is
        /// how New Object - Group came to accept STUDENTS and then sit there.
        /// </summary>
        private WinWindow ResolveOpenOnSuccess()
        {
            if (openOnSuccess != null) return openOnSuccess;
            if (string.IsNullOrEmpty(openOnSuccessId)) return null;

            var found = WinDesktop.Instance != null
                ? WinDesktop.Instance.Find(openOnSuccessId)
                : null;

            if (found == null)
                Debug.LogWarning($"[WinTaskGate] '{name}' should open '{openOnSuccessId}' but the " +
                                 "reference is empty and no window has that id. Nothing will open.",
                                 this);

            return found;
        }

        private bool AppliesToCurrentStep()
        {
            if (string.IsNullOrEmpty(requiredStepId)) return true;

            // No step gate in the scene means nothing is publishing a step. Scoping would switch
            // this gate off permanently, which is worse than ignoring the filter.
            string current = COC3StepGate.CurrentStepId;
            if (string.IsNullOrEmpty(current)) return true;

            return current == requiredStepId;
        }

        /// <summary>Null when everything checks out; otherwise the message to show.</summary>
        private string FirstProblem()
        {
            if (requiredActions != null)
                foreach (var id in requiredActions)
                    if (!string.IsNullOrEmpty(id) && !WinActionBus.HasSucceeded(id))
                        return missingActionMessage;

            foreach (var t in mustBeOn)
                if (t != null && !t.isOn) return missingOptionMessage;

            foreach (var t in mustBeOff)
                if (t != null && t.isOn) return missingOptionMessage;

            foreach (var r in fields)
            {
                if (r == null || r.field == null) continue;

                if (!Matches(r.field.text, r.expected, r.caseSensitive))
                    return wrongValueMessage.Replace("{field}", r.displayName);
            }

            return null;
        }

        private static bool Matches(string typed, string expected, bool caseSensitive)
        {
            typed = Normalize(typed);
            expected = Normalize(expected);

            return caseSensitive
                ? typed == expected
                : string.Equals(typed, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Trims, and collapses runs of backslashes to one - the same rule ScreenshotStep uses, and
        /// for the same reason: TextMeshPro treats "\\" as an escape and swallows one of the two,
        /// so a UNC path cannot be typed in full. Judging both sides collapsed keeps the step
        /// passable without accepting a genuinely different path.
        /// </summary>
        private static string Normalize(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.IndexOf('\\') < 0) return value;

            var sb = new System.Text.StringBuilder(value.Length);
            bool previousWasBackslash = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (c == '\\')
                {
                    if (previousWasBackslash) continue;
                    previousWasBackslash = true;
                }
                else previousWasBackslash = false;

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}