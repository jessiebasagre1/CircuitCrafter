using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI-facing counterpart to DiagnosticsTestManager. Spawns one
/// DiagnosticTestRowView per test, wires the Run button, and keeps rows in
/// sync with the manager's events. Subscribes to
/// DiagnosticsTestManager.Instance the same way OSInstallView subscribes to
/// OSInstallManager.Instance -- there's only one Diagnostics tool per scene.
/// </summary>
public class DiagnosticsView : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private Transform rowsContent;
    [SerializeField] private DiagnosticTestRowView rowPrefab;

    [Header("Controls")]
    [SerializeField] private Button runButton;
    [SerializeField] private TMP_Text overallResultText;

    private readonly Dictionary<DiagnosticTestData, DiagnosticTestRowView> _rows =
        new Dictionary<DiagnosticTestData, DiagnosticTestRowView>();

    private void Start()
    {
        if (DiagnosticsTestManager.Instance == null)
        {
            Debug.LogWarning("[DiagnosticsView] DiagnosticsTestManager.Instance is still null in Start(). " +
                "Is DiagnosticsTestManager actually present and enabled in this scene?");
            return;
        }

        DiagnosticsTestManager.Instance.OnTestStarted += HandleTestStarted;
        DiagnosticsTestManager.Instance.OnTestProgress += HandleTestProgress;
        DiagnosticsTestManager.Instance.OnTestResult += HandleTestResult;
        DiagnosticsTestManager.Instance.OnAllTestsFinished += HandleAllFinished;

        if (runButton != null)
            runButton.onClick.AddListener(HandleRunClicked);

        Populate();
    }

    private void OnDisable()
    {
        if (DiagnosticsTestManager.Instance == null) return;
        DiagnosticsTestManager.Instance.OnTestStarted -= HandleTestStarted;
        DiagnosticsTestManager.Instance.OnTestProgress -= HandleTestProgress;
        DiagnosticsTestManager.Instance.OnTestResult -= HandleTestResult;
        DiagnosticsTestManager.Instance.OnAllTestsFinished -= HandleAllFinished;
    }

    private void Populate()
    {
        foreach (Transform child in rowsContent)
            Destroy(child.gameObject);
        _rows.Clear();

        foreach (var test in DiagnosticsTestManager.Instance.Tests)
        {
            if (test == null) continue;
            var row = Instantiate(rowPrefab, rowsContent);
            row.Setup(test);

            var lastResult = DiagnosticsTestManager.Instance.GetLastResult(test);
            if (lastResult.HasValue) row.SetResult(lastResult.Value);

            _rows[test] = row;
        }
    }

    private void HandleRunClicked()
    {
        if (runButton != null) runButton.interactable = false;
        if (overallResultText != null) overallResultText.text = "Running diagnostics...";
        DiagnosticsTestManager.Instance.RunAllTests();
    }

    private void HandleTestStarted(DiagnosticTestData test)
    {
        if (_rows.TryGetValue(test, out var row)) row.SetRunning();
    }

    private void HandleTestProgress(DiagnosticTestData test, float t)
    {
        if (_rows.TryGetValue(test, out var row)) row.SetProgress(t);
    }

    private void HandleTestResult(DiagnosticTestData test, bool passed)
    {
        if (_rows.TryGetValue(test, out var row)) row.SetResult(passed);
    }

    private void HandleAllFinished(bool allPassed)
    {
        if (runButton != null) runButton.interactable = true;

        if (overallResultText != null)
            overallResultText.text = allPassed
                ? "All tests passed."
                : "Some tests failed - fix the issue above and run diagnostics again.";
    }
}
