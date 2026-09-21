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

    private bool _populated;

    // Same reason as ChecklistToolView: Start() runs once, but OnDisable unsubscribes on every
    // close, so after the window was closed once the progress bars stopped moving. Subscribing in
    // OnEnable keeps the two paired.
    private void OnEnable()
    {
        Subscribe();

        // The first open is handled by Start(). Every later open catches up on anything that
        // happened while the window was closed.
        if (_populated) RefreshRows();
    }

    private void Start()
    {
        if (DiagnosticsTestManager.Instance == null)
        {
            Debug.LogWarning("[DiagnosticsView] DiagnosticsTestManager.Instance is still null in Start(). " +
                "Is DiagnosticsTestManager actually present and enabled in this scene?");
            return;
        }

        Subscribe();   // does nothing new if OnEnable already did

        if (runButton != null)
            runButton.onClick.AddListener(HandleRunClicked);

        Populate();
        _populated = true;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        var manager = DiagnosticsTestManager.Instance;
        if (manager == null) return;

        // Unsubscribe first so calling this twice can never double up the handlers.
        Unsubscribe();
        manager.OnTestStarted += HandleTestStarted;
        manager.OnTestProgress += HandleTestProgress;
        manager.OnTestResult += HandleTestResult;
        manager.OnAllTestsFinished += HandleAllFinished;
    }

    private void Unsubscribe()
    {
        var manager = DiagnosticsTestManager.Instance;
        if (manager == null) return;

        manager.OnTestStarted -= HandleTestStarted;
        manager.OnTestProgress -= HandleTestProgress;
        manager.OnTestResult -= HandleTestResult;
        manager.OnAllTestsFinished -= HandleAllFinished;
    }

    /// <summary>Brings the rows, the Run button and the overall message back in line with the
    /// manager after the window has been closed and reopened.</summary>
    private void RefreshRows()
    {
        var manager = DiagnosticsTestManager.Instance;
        if (manager == null) return;

        bool anyResult = false;
        foreach (var pair in _rows)
        {
            if (pair.Value == null) continue;

            bool? last = manager.GetLastResult(pair.Key);
            if (last.HasValue)
            {
                pair.Value.SetResult(last.Value);
                anyResult = true;
            }
            else
            {
                pair.Value.SetPending();
            }
        }

        // The Run button is switched off when a run starts and back on by HandleAllFinished. If
        // that finished while this window was closed, nothing would ever switch it back on.
        if (runButton != null) runButton.interactable = !manager.IsRunning;

        if (overallResultText == null) return;

        if (manager.IsRunning)
            overallResultText.text = "Running diagnostics...";
        else if (anyResult)
            overallResultText.text = manager.AllTestsPassed
                ? "All tests passed."
                : "Some tests failed - fix the issue above and run diagnostics again.";
        // Never run: leave the "Not tested yet." text that is already on the panel.
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