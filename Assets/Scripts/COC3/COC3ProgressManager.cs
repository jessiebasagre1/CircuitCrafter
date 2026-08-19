using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Updates the on-screen progress bar (Slider) and "Step X of Y" text.
/// Keeping this separate from COC3Manager means you can swap out how progress
/// is displayed (e.g. dots instead of a bar) without touching the core logic.
/// </summary>
public class COC3ProgressManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("A UI Slider used as a progress bar. Optional - leave empty if unused.")]
    public Slider progressBar;

    [Tooltip("A text element showing e.g. 'Step 4 of 10'. Optional - leave empty if unused.")]
    public TMP_Text stepIndicatorText;

    /// <summary>Call this whenever the active step changes.</summary>
    public void UpdateProgress(int currentStepNumber, int totalSteps)
    {
        if (progressBar != null)
        {
            progressBar.maxValue = totalSteps;
            progressBar.value = currentStepNumber;
        }

        if (stepIndicatorText != null)
        {
            stepIndicatorText.text = $"Step {currentStepNumber} of {totalSteps}";
        }
    }

    /// <summary>Resets the progress display back to empty, used when restarting.</summary>
    public void ResetProgress()
    {
        if (progressBar != null) progressBar.value = 0;
        if (stepIndicatorText != null) stepIndicatorText.text = string.Empty;
    }
}