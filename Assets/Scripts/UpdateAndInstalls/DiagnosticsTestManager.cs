using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives step 4, "Test the computer": runs a short list of scripted
/// diagnostic checks in sequence, same timed-progress pattern as
/// OSInstallManager.RunTimedStage. Each test's pass/fail is derived from
/// whether its prerequisite TaskManager tasks are complete (see
/// DiagnosticTestData) -- so a skipped driver or update earlier genuinely
/// shows up as a failed test here, and the player can go fix it and re-run.
/// </summary>
public class DiagnosticsTestManager : MonoBehaviour
{
    public static DiagnosticsTestManager Instance { get; private set; }

    [Tooltip("Reported to TaskManager once per test that finishes RUNNING (pass or fail both count as 'ran'), required = tests.Count. This only means the tech tested the machine, not that everything passed -- gate step 5's submit button on AllTestsPassed too, not just this task.")]
    [SerializeField] private string groupTaskId = SystemConfigTaskIds.TestComputer;

    [SerializeField] private List<DiagnosticTestData> tests = new List<DiagnosticTestData>();

    public IReadOnlyList<DiagnosticTestData> Tests => tests;
    public bool IsRunning { get; private set; }

    /// <summary>True once every test in the most recent full run passed. False
    /// before the first run, or if any test in the last run failed.</summary>
    public bool AllTestsPassed { get; private set; }

    // UI hooks -- mirrors OSInstallManager's event pattern
    public event Action<DiagnosticTestData> OnTestStarted;
    public event Action<DiagnosticTestData, float> OnTestProgress; // 0-1
    public event Action<DiagnosticTestData, bool> OnTestResult; // bool = passed
    public event Action<bool> OnAllTestsFinished; // bool = every test passed this run

    private readonly Dictionary<DiagnosticTestData, bool> _lastResults = new Dictionary<DiagnosticTestData, bool>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.RegisterTask(groupTaskId, Mathf.Max(tests.Count, 1));
    }

    /// <summary>The result from the last time this specific test was run, or
    /// null if it hasn't been run yet this session.</summary>
    public bool? GetLastResult(DiagnosticTestData test)
    {
        return _lastResults.TryGetValue(test, out bool passed) ? passed : (bool?)null;
    }

    /// <summary>Call from the "Run Diagnostics" button. Re-runnable any number
    /// of times (e.g. after the player goes back and installs a missing
    /// driver) -- TaskManager.ReportProgress on an already-complete task is a
    /// harmless no-op, so re-running never double-counts.</summary>
    public void RunAllTests()
    {
        if (IsRunning) return;
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        IsRunning = true;
        bool allPassed = true;

        foreach (var test in tests)
        {
            if (test == null) continue;

            OnTestStarted?.Invoke(test);

            float t = 0f;
            float duration = Mathf.Max(test.duration, 0.01f);
            while (t < duration)
            {
                t += Time.deltaTime;
                OnTestProgress?.Invoke(test, Mathf.Clamp01(t / duration));
                yield return null;
            }
            OnTestProgress?.Invoke(test, 1f);

            bool passed = Evaluate(test);
            _lastResults[test] = passed;
            if (!passed) allPassed = false;

            OnTestResult?.Invoke(test, passed);

            if (TaskManager.Instance != null)
                TaskManager.Instance.ReportProgress(groupTaskId);
        }

        AllTestsPassed = allPassed;
        IsRunning = false;
        OnAllTestsFinished?.Invoke(allPassed);
    }

    private bool Evaluate(DiagnosticTestData test)
    {
        if (test.prerequisiteTaskIds == null || test.prerequisiteTaskIds.Length == 0)
            return true;

        if (TaskManager.Instance == null) return false;

        foreach (var id in test.prerequisiteTaskIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (!TaskManager.Instance.IsTaskComplete(id)) return false;
        }
        return true;
    }
}
