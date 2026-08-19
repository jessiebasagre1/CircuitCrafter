using System;
using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Simulates typing a "ping" command and seeing replies scroll in, like a real
/// Command Prompt. Whether the ping "succeeds" is decided by whether the
/// learner's earlier network configuration step was correct - it does not
/// do any real networking, this is purely a visual simulation.
/// </summary>
public class PingSimulation : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The text area that displays the simulated command-prompt output.")]
    public TMP_Text pingOutputText;

    [Tooltip("Optional: a 'Run Ping' button to hide while the simulation is playing.")]
    public GameObject pingButton;

    [Header("Timing")]
    [Tooltip("Delay in seconds between each line appearing, for a typing effect.")]
    public float delayBetweenLines = 0.4f;

    [Tooltip("How many 'Reply from...' lines to show.")]
    public int numberOfReplies = 4;

    /// <summary>
    /// Starts the simulated ping. Pass in whether the previous network config
    /// step was correct, and a callback that receives the final success/fail result.
    /// </summary>
    public void RunPing(bool configWasCorrect, Action<bool> onComplete)
    {
        StartCoroutine(PingRoutine(configWasCorrect, onComplete));
    }

    private IEnumerator PingRoutine(bool configWasCorrect, Action<bool> onComplete)
    {
        if (pingButton != null) pingButton.SetActive(false);
        if (pingOutputText != null) pingOutputText.text = "Pinging target...\n";

        yield return new WaitForSeconds(delayBetweenLines);

        for (int i = 0; i < numberOfReplies; i++)
        {
            if (pingOutputText != null)
            {
                pingOutputText.text += configWasCorrect
                    ? "Reply from target: bytes=32 time<1ms TTL=64\n"
                    : "Request timed out.\n";
            }
            yield return new WaitForSeconds(delayBetweenLines);
        }

        if (pingOutputText != null)
        {
            pingOutputText.text += configWasCorrect
                ? "\nPing successful. Connection verified."
                : "\nPing failed. Review your network configuration.";
        }

        if (pingButton != null) pingButton.SetActive(true);
        onComplete?.Invoke(configWasCorrect);
    }
}