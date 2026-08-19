using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Shows a short success/failure message on screen (e.g. a green "Correct!"
/// or a red "Try again" popup) and automatically hides it after a delay.
/// </summary>
public class FeedbackManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The panel/GameObject that contains the feedback text. This gets shown/hidden.")]
    public GameObject feedbackPanel;

    [Tooltip("The text element inside the feedback panel.")]
    public TMP_Text feedbackText;

    [Header("Colors")]
    public Color successColor = Color.green;
    public Color failColor = Color.red;

    [Header("Timing")]
    [Tooltip("How long the feedback message stays on screen before auto-hiding.")]
    public float displayDuration = 2f;

    private Coroutine hideRoutine;

    /// <summary>Shows a green success message.</summary>
    public void ShowSuccess(string message)
    {
        Show(message, successColor);
    }

    /// <summary>Shows a red failure/try-again message.</summary>
    public void ShowFailure(string message)
    {
        Show(message, failColor);
    }

    private void Show(string message, Color color)
    {
        if (feedbackPanel == null || feedbackText == null) return;

        if (hideRoutine != null) StopCoroutine(hideRoutine);

        feedbackText.text = message;
        feedbackText.color = color;
        feedbackPanel.SetActive(true);

        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(displayDuration);
        feedbackPanel.SetActive(false);
    }
}