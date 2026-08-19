using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// THE CENTRAL BRAIN of the simulation.
///
/// It owns the ordered list of ScreenshotStep panels, shows/hides them one at
/// a time, asks each step to validate itself, feeds results into the Score,
/// Progress, and Feedback managers, and builds the final report screen.
///
/// Everything is driven by public methods you call from UI Button OnClick()
/// events in the Inspector - you should not need to edit this script just to
/// add/remove/reorder steps (see the README for how to do that).
/// </summary>
public class COC3Manager : MonoBehaviour
{
    [Header("Steps (drag step panels here, IN ORDER)")]
    [Tooltip("Every ScreenshotStep panel in your Hierarchy, in the order they should be completed.")]
    public List<ScreenshotStep> steps = new List<ScreenshotStep>();

    [Header("Sub-Managers (drag the matching GameObjects here)")]
    public COC3ProgressManager progressManager;
    public COC3ScoreManager scoreManager;
    public FeedbackManager feedbackManager;
    public PingSimulation pingSimulation;

    [Header("Completion / Report Screen")]
    [Tooltip("The panel shown once all steps are finished.")]
    public GameObject completionScreen;

    [Tooltip("Text element on the completion screen that displays the final report.")]
    public TMP_Text reportText;

    private int currentStepIndex = 0;
    private bool lastNetworkConfigCorrect = true; // used so PingTest steps know if the config before them was right
    private readonly List<string> stepLog = new List<string>();

    private void Start()
    {
        StartActivity();
    }

    /// <summary>Begins (or restarts) the whole activity from Step 1.</summary>
    public void StartActivity()
    {
        currentStepIndex = 0;
        lastNetworkConfigCorrect = true;
        stepLog.Clear();

        if (scoreManager != null) scoreManager.ResetScore();
        if (progressManager != null) progressManager.ResetProgress();
        if (completionScreen != null) completionScreen.SetActive(false);

        foreach (var step in steps)
        {
            step.Setup();
            step.Hide();
        }

        ShowCurrentStep();
    }

    private void ShowCurrentStep()
    {
        if (currentStepIndex >= steps.Count)
        {
            ShowCompletionScreen();
            return;
        }

        ScreenshotStep step = steps[currentStepIndex];
        step.Setup();
        step.Show();

        if (progressManager != null)
            progressManager.UpdateProgress(currentStepIndex + 1, steps.Count);
    }

    /// <summary>
    /// Call this from the "Submit / Check / Next" button on a step's UI.
    /// It figures out the right validation path automatically based on StepType.
    /// </summary>
    public void SubmitCurrentStep()
    {
        if (currentStepIndex >= steps.Count) return;
        ScreenshotStep step = steps[currentStepIndex];

        // PingTest steps run an animated coroutine instead of validating instantly.
        if (step.stepData.stepType == StepType.PingTest && pingSimulation != null)
        {
            pingSimulation.RunPing(lastNetworkConfigCorrect, OnPingComplete);
            return;
        }

        bool success = step.Validate(out string message);
        HandleStepResult(step, success, message);
    }

    private void OnPingComplete(bool success)
    {
        ScreenshotStep step = steps[currentStepIndex];
        string message = success ? "Ping successful!" : "Ping failed - review your configuration.";
        HandleStepResult(step, success, message);
    }

    private void HandleStepResult(ScreenshotStep step, bool success, string message)
    {
        // Remember whether the config step passed, so a later PingTest step can react to it.
        if (step.stepData.stepType == StepType.NetworkConfigInput)
            lastNetworkConfigCorrect = success;

        if (success)
        {
            if (scoreManager != null) scoreManager.AddScore(step.stepData.pointsForCorrect);
            if (feedbackManager != null) feedbackManager.ShowSuccess(string.IsNullOrEmpty(message) ? "Correct!" : message);

            stepLog.Add($"Step {currentStepIndex + 1} ({step.stepData.stepId}): PASSED");
            AdvanceToNextStep();
        }
        else
        {
            if (scoreManager != null) scoreManager.AddMistake(step.stepData.pointsPenaltyForMistake);
            if (feedbackManager != null) feedbackManager.ShowFailure(string.IsNullOrEmpty(message) ? "Try again." : message);

            stepLog.Add($"Step {currentStepIndex + 1} ({step.stepData.stepId}): RETRY");
            // We intentionally do NOT advance - the learner stays on this step to try again.
        }
    }

    private void AdvanceToNextStep()
    {
        steps[currentStepIndex].Hide();
        currentStepIndex++;
        ShowCurrentStep();
    }

    /// <summary>
    /// For Informational or ButtonInteraction steps where you just want a plain
    /// "Next" button with no validation, wire it to this instead of SubmitCurrentStep().
    /// </summary>
    public void SkipToNextStep()
    {
        stepLog.Add($"Step {currentStepIndex + 1} ({steps[currentStepIndex].stepData.stepId}): VIEWED");
        AdvanceToNextStep();
    }

    private void ShowCompletionScreen()
    {
        if (completionScreen != null) completionScreen.SetActive(true);
        if (reportText != null) reportText.text = BuildReport();
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== COC 3 Simulation Report ===");
        sb.AppendLine($"Final Score: {(scoreManager != null ? scoreManager.Score : 0)}");
        sb.AppendLine($"Total Mistakes: {(scoreManager != null ? scoreManager.Mistakes : 0)}");
        sb.AppendLine($"Steps Completed: {steps.Count}");
        sb.AppendLine();
        sb.AppendLine("Step-by-step log:");
        foreach (string line in stepLog)
            sb.AppendLine("  " + line);

        return sb.ToString();
    }

    /// <summary>Wire this to a "Reset / Retry" button anywhere in the activity.</summary>
    public void RestartActivity()
    {
        StartActivity();
    }
}