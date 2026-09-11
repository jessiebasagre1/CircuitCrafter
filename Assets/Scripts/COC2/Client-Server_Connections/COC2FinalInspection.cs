// COC2FinalInspection.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The clipboard at the end of COC2. The player walks up, presses E, and gets the inspection
    /// report for the whole job: every checkpoint, what it says, and whether the installation as a
    /// whole is signed off.
    ///
    /// WHY THIS IS NOT JUST A "YOU WIN" SCREEN
    ///
    /// COC2 finishes with an inspection, not a celebration - "final inspections are undertaken to
    /// ensure the configuration conforms with the job requirements". The point of the exercise is
    /// that the trainee can look at a finished installation and say what is and is not right about
    /// it. So this reads every checkpoint LIVE at the moment the player asks, prints the lot, and
    /// lets them stand there reading a report that has failures in it if their work has failures in
    /// it. Nothing here is remembered from earlier; if the player unplugs a cable after passing, the
    /// report says so the next time they read it.
    ///
    /// It deliberately records NOTHING to your assessment system by itself. Wire On Inspection
    /// Passed / On Inspection Failed to whatever COC2AssessmentManager exposes, so the recording
    /// stays in one place instead of being spread across every station in the scene.
    ///
    /// SETUP:
    ///   1. Empty GameObject where the report is read - a clipboard on a desk, the door out. Add
    ///      this and a trigger collider.
    ///   2. Fill Checks: one row per thing that has to be true, each with a Label and the object
    ///      that proves it.
    ///   3. Drag a TMP text into Report Text - a panel on the clipboard, or your dialog's body.
    ///   4. Assign Passed Dialog and Failed Dialog.
    /// </summary>
    [DefaultExecutionOrder(220)]
    public class COC2FinalInspection : Interactable
    {
        /// <summary>Where a checkpoint's answer comes from. Every one of these reads state that
        /// already exists somewhere else in the scene.</summary>
        public enum CheckKind
        {
            /// <summary>TaskManager says this id is complete.</summary>
            TaskComplete,
            /// <summary>A PatchCordBoard phase finished and passed.</summary>
            PhasePassed,
            /// <summary>A CableCarrier has both ends plugged in.</summary>
            CableRun,
            /// <summary>A DeviceConfigStation has been configured correctly.</summary>
            DeviceConfigured,
            /// <summary>A ConnectivityCheckStation has passed its ping.</summary>
            LinkVerified
        }

        [System.Serializable]
        public class Check
        {
            [Tooltip("The line as it appears on the report, e.g. 'Patch panel terminated to T568A'. " +
                     "Write it the way an inspector would - a statement that is true or false.")]
            public string label = "Checkpoint";

            public CheckKind kind = CheckKind.TaskComplete;

            [Tooltip("Kind = Task Complete.")]
            public string taskId = "";
            [Tooltip("Kind = Phase Passed.")]
            public PatchCordBoard phase;
            [Tooltip("Kind = Cable Run.")]
            public CableCarrier cable;
            [Tooltip("Kind = Device Configured.")]
            public DeviceConfigStation configStation;
            [Tooltip("Kind = Link Verified.")]
            public ConnectivityCheckStation linkStation;

            [Tooltip("Untick for a checkpoint that is worth reporting but must not block sign-off - " +
                     "an optional extra, or something you are still building.")]
            public bool requiredToPass = true;

            [Tooltip("Printed under the line when it fails. Say where to go and what to do.")]
            [TextArea(1, 3)]
            public string remedy = "";
        }

        [Header("The inspection")]
        [Tooltip("Every checkpoint, in the order they should be read. Group them the way the job " +
                 "was done - cabling, then configuration, then connectivity - so the report reads " +
                 "like a walk through the installation.")]
        public List<Check> checks = new List<Check>();

        [Header("Report")]
        [Tooltip("Where the report is written.")]
        public TMP_Text reportText;
        [Tooltip("Heading at the top of the report.")]
        public string reportTitle = "COC2 - FINAL INSPECTION";
        [Tooltip("Marks beside each line. Keep them short and monospaced-friendly.")]
        public string passMark = "[ PASS ]";
        public string failMark = "[ FAIL ]";
        [Tooltip("Optional panel switched on while the report is being read. Leave it INACTIVE " +
                 "in the scene.")]
        public GameObject panel;
        [Tooltip("Optional Close button object - hook its On Click to Close().")]
        public GameObject closeButton;

        [Header("Dialogs")]
        public DialogSequence passedDialog;
        public DialogSequence failedDialog;
        public bool passedDialogOnlyOnce = true;

        [Header("Task")]
        [Tooltip("Reported when the inspection passes. Leave blank to report nothing.")]
        public string taskId = "final_inspection";
        public bool autoRegisterTask = true;
        public int taskSteps = 1;

        [Header("Events")]
        [Tooltip("Raised every time the report is opened, pass or fail.")]
        public UnityEvent OnInspected;
        [Tooltip("Raised when every required checkpoint passes. Hook your assessment recording here.")]
        public UnityEvent OnInspectionPassed;
        [Tooltip("Raised when at least one required checkpoint fails.")]
        public UnityEvent OnInspectionFailed;

        [Header("Diagnostics")]
        public bool logReport = true;

        /// <summary>Result of the most recent read.</summary>
        public bool LastResultPassed { get; private set; }

        /// <summary>How many required checkpoints passed, out of how many, at the last read.</summary>
        public int PassedCount { get; private set; }
        public int RequiredCount { get; private set; }

        bool shownPassed;
        bool reported;

        void Start()
        {
            if (autoRegisterTask && !string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                TaskManager.Instance.RegisterTask(taskId, Mathf.Max(1, taskSteps));

            if (panel != null) panel.SetActive(false);
            ValidateSetup();
        }

        public override void Interact() { Inspect(); }

        /// <summary>Reads every checkpoint now and shows the report. Public so a button or a
        /// UnityEvent can trigger it too.</summary>
        public void Inspect()
        {
            string report = BuildReport();

            if (reportText != null) reportText.text = report;
            if (panel != null) panel.SetActive(true);
            if (logReport) Debug.LogWarning(report, this);

            OnInspected?.Invoke();

            if (LastResultPassed)
            {
                if (!reported && !string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                {
                    reported = true;
                    TaskManager.Instance.ReportProgress(taskId);
                }
                OnInspectionPassed?.Invoke();
                Say(passedDialog, passedDialogOnlyOnce && shownPassed);
                shownPassed = true;
            }
            else
            {
                OnInspectionFailed?.Invoke();
                Say(failedDialog, false);
            }
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        /// <summary>
        /// The report as text. Split out from Inspect so the same lines can go to a clipboard, a
        /// dialog, the Console and an assessment record without being rebuilt three different ways
        /// and drifting apart.
        /// </summary>
        public string BuildReport()
        {
            int passed = 0, required = 0;
            List<string> failedLines = new List<string>();

            string body = "";
            for (int i = 0; i < checks.Count; i++)
            {
                Check c = checks[i];
                if (c == null) continue;

                bool ok = Evaluate(c);
                if (c.requiredToPass)
                {
                    required++;
                    if (ok) passed++;
                }

                body += "\n" + (ok ? passMark : failMark) + "  " + c.label +
                        (c.requiredToPass ? "" : "   (advisory)");

                if (!ok && !string.IsNullOrEmpty(c.remedy))
                    body += "\n           " + c.remedy;

                if (!ok && c.requiredToPass) failedLines.Add(c.label);
            }

            PassedCount = passed;
            RequiredCount = required;
            LastResultPassed = required > 0 && passed == required;

            string head = reportTitle +
                          "\n" + new string('-', Mathf.Max(reportTitle.Length, 24)) +
                          "\n" + passed + " of " + required + " checkpoints met.\n";

            string foot = "\n\n" + (LastResultPassed
                ? "RESULT: The installation conforms to the job order. Signed off."
                : "RESULT: Not accepted. " + failedLines.Count + " item(s) to put right.");

            return head + body + foot;
        }

        bool Evaluate(Check c)
        {
            switch (c.kind)
            {
                case CheckKind.TaskComplete:
                    if (string.IsNullOrEmpty(c.taskId) || TaskManager.Instance == null) return false;
                    return TaskManager.Instance.IsTaskComplete(c.taskId);

                case CheckKind.PhasePassed:
                    return c.phase != null && c.phase.HasFinished && c.phase.LastResultPassed;

                case CheckKind.CableRun:
                    return c.cable != null && c.cable.IsComplete;

                case CheckKind.DeviceConfigured:
                    return c.configStation != null && c.configStation.IsConfigured;

                case CheckKind.LinkVerified:
                    return c.linkStation != null && c.linkStation.IsConnected;
            }
            return false;
        }

        void Say(DialogSequence sequence, bool alreadyShown)
        {
            if (sequence == null || alreadyShown) return;
            if (DialogManager.Instance == null || DialogManager.Instance.IsDialogOpen) return;
            DialogManager.Instance.ShowDialog(sequence);
        }

        /// <summary>Forgets the pass so the dialog plays again on a re-run.</summary>
        public void ResetStation()
        {
            shownPassed = false;
            reported = false;
            LastResultPassed = false;
        }

        // ---------------------------------------------------------------- self-reporting

        void ValidateSetup()
        {
            List<string> problems = new List<string>();

            if (checks.Count == 0)
                problems.Add("Checks is empty, so the report has nothing in it and can never pass.");

            if (reportText == null && panel == null)
                problems.Add("Neither Report Text nor Panel is assigned, so the player never sees " +
                             "the report. It will still go to the Console.");

            if (panel != null && panel.activeSelf)
                problems.Add("Panel '" + panel.name + "' is ACTIVE in the scene. It should start " +
                             "inactive.");

            int required = 0;
            for (int i = 0; i < checks.Count; i++)
            {
                Check c = checks[i];
                string where = "Check " + i + " ('" + (c != null ? c.label : "null") + "')";
                if (c == null) { problems.Add("Check " + i + " is empty."); continue; }
                if (c.requiredToPass) required++;

                switch (c.kind)
                {
                    case CheckKind.TaskComplete:
                        if (string.IsNullOrEmpty(c.taskId)) problems.Add(where + " has no Task Id.");
                        break;
                    case CheckKind.PhasePassed:
                        if (c.phase == null) problems.Add(where + " has no Phase.");
                        break;
                    case CheckKind.CableRun:
                        if (c.cable == null) problems.Add(where + " has no Cable.");
                        break;
                    case CheckKind.DeviceConfigured:
                        if (c.configStation == null) problems.Add(where + " has no Config Station.");
                        break;
                    case CheckKind.LinkVerified:
                        if (c.linkStation == null) problems.Add(where + " has no Link Station.");
                        break;
                }
            }

            if (checks.Count > 0 && required == 0)
                problems.Add("Every check is advisory, so the inspection can never pass. Tick " +
                             "Required To Pass on the ones that matter.");

            if (GetComponent<Collider>() == null)
                problems.Add("No Collider, so the player can never walk up to this.");

            if (problems.Count == 0)
            {
                Debug.LogWarning("[COC2FinalInspection] Ready. " + checks.Count + " checkpoint(s), " +
                                 required + " required to sign off.", this);
                return;
            }

            string msg = "[COC2FinalInspection] Setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>Prints the report right now, without the player walking over. Use it from the
        /// Inspector while you are building the checklist.</summary>
        [ContextMenu("Report inspection")]
        public void LogInspection() { Debug.LogWarning(BuildReport(), this); }
    }
}