using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One row representing a single DiagnosticTestData. Put this on a prefab
/// and assign it to DiagnosticsView.rowPrefab.
///
/// Suggested row layout: [name + description] [progress bar] [result text].
/// failureHintText is optional and only shown while this row's last result
/// is a fail.
/// </summary>
public class DiagnosticTestRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text failureHintText;

    private DiagnosticTestData _data;

    public void Setup(DiagnosticTestData data)
    {
        _data = data;
        if (nameText != null) nameText.text = data.testName;
        if (descriptionText != null) descriptionText.text = data.description;
        SetPending();
    }

    public void SetPending()
    {
        if (progressBar != null) progressBar.value = 0f;
        if (resultText != null) resultText.text = "Pending";
        if (failureHintText != null) failureHintText.gameObject.SetActive(false);
    }

    public void SetRunning()
    {
        if (resultText != null) resultText.text = "Running...";
        if (failureHintText != null) failureHintText.gameObject.SetActive(false);
    }

    public void SetProgress(float t)
    {
        if (progressBar != null) progressBar.value = t;
    }

    public void SetResult(bool passed)
    {
        if (progressBar != null) progressBar.value = 1f;

        if (resultText != null)
            resultText.text = passed ? "Pass" : "Fail";

        if (failureHintText != null)
        {
            bool showHint = !passed && _data != null && !string.IsNullOrEmpty(_data.failureHint);
            failureHintText.gameObject.SetActive(showHint);
            if (showHint) failureHintText.text = _data.failureHint;
        }
    }
}
