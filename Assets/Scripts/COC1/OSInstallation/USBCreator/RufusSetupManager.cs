using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the "download and install Rufus" sequence that now comes BEFORE the Rufus window.
///
/// ATTACH THIS TO: any always-active object in the COC 1 scene - the Managers object is ideal.
/// It must NOT live on the browser panel, because that panel spends most of its life switched
/// off, and a manager that is off cannot register its task or answer IsComplete.
///
/// WHY THIS EXISTS
/// A learner who is handed a working copy of Rufus has skipped the part of the job that
/// actually goes wrong in a shop: finding the real vendor site rather than a look-alike,
/// recognising a UAC prompt, and not clicking through installers blind. Typing the wrong
/// domain is exactly how a bench PC picks up bundled adware, so that one is graded.
///
/// SAME SHAPE AS THE REST OF THE PROJECT
/// Manager holds the state and the rules, view holds the Canvas references, content lives in
/// ScriptableObjects - identical to OSInstallManager / OSInstallView / OSInstallStageData.
/// Completion is reported to TaskManager, so TaskCheckpointAutosaver checkpoints it, a
/// QuizSetData with this taskId auto-fires after it, and a TaskGate can lock the Rufus icon
/// behind it without any of those systems knowing this class exists.
/// </summary>
public class RufusSetupManager : MonoBehaviour
{
    public static RufusSetupManager Instance { get; private set; }

    /// <summary>Type this exact string into the Rufus desktop icon's Task Gate so the icon stays
    /// locked until the learner has actually been through the download and install.</summary>
    public const string TaskId = "install_rufus";

    [Tooltip("The screens, in order. Built by Tools > COC 1 > Rufus Setup Builder.")]
    [SerializeField] private List<RufusSetupStepData> steps = new List<RufusSetupStepData>();

    [Header("Panels")]
    [Tooltip("The browser panel. Switched off when the sequence finishes.")]
    [SerializeField] private GameObject browserPanel;

    [Tooltip("Re-shown when the sequence finishes - normally the reference PC's desktop.")]
    [SerializeField] private GameObject desktopPanel;

    [Header("Dialogs")]
    [Tooltip("Shown once the whole sequence is done, e.g. \"Rufus is installed. Open it from the desktop.\"")]
    [SerializeField] private DialogSequence completeDialog;

    public IReadOnlyList<RufusSetupStepData> Steps => steps;
    public int CurrentIndex { get; private set; } = -1;
    public bool IsRunning { get; private set; }

    public RufusSetupStepData CurrentStep
    {
        get { return (CurrentIndex >= 0 && CurrentIndex < steps.Count) ? steps[CurrentIndex] : null; }
    }

    /// <summary>Checked live against TaskManager rather than cached, so it is automatically
    /// right after a save reload - same approach as ChecklistToolManager.AllItemsComplete.</summary>
    public bool IsComplete
    {
        get { return TaskManager.Instance != null && TaskManager.Instance.IsTaskComplete(TaskId); }
    }

    // UI hooks - mirrors OSInstallManager's event pattern
    public event Action<RufusSetupStepData> OnStepChanged;
    public event Action<float> OnStepProgress;              // 0-1 within a Timed step
    public event Action<RufusSetupStepData, string> OnWrongAnswer;
    public event Action OnSequenceComplete;
    public event Action OnAlreadyComplete;

    private Coroutine _timed;
    private Coroutine _beginWait;

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
            TaskManager.Instance.RegisterTask(TaskId, 1);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==================================================================
    // Sequence
    // ==================================================================

    /// <summary>Called by RufusSetupView when the browser panel opens.</summary>
    public void Begin()
    {
        if (IsComplete)
        {
            // Reopening the browser after Rufus is installed is harmless curiosity, not a
            // reason to make them do it all again.
            IsRunning = false;
            CurrentIndex = -1;
            OnAlreadyComplete?.Invoke();
            return;
        }

        if (steps.Count == 0)
        {
            Debug.LogWarning("[RufusSetupManager] No steps assigned - run Tools > COC 1 > Rufus Setup Builder.", this);
            return;
        }

        if (_beginWait != null) StopCoroutine(_beginWait);
        _beginWait = StartCoroutine(BeginWhenScreenIsFree());
    }

    /// <summary>
    /// Waits for whatever is already on screen before opening step one.
    ///
    /// Two things kept jumping the queue. The intro dialog runs at scene load, and if the
    /// browser panel happens to be active at that moment this sequence fired its first
    /// teaching popup straight over the top of it. And the quiz that auto-fires when assembly
    /// completes was arriving second, because this had already grabbed the screen.
    ///
    /// A quiz or a dialog owns the screen until it is finished. This waits its turn.
    /// </summary>
    private IEnumerator BeginWhenScreenIsFree()
    {
        while (IsScreenBusy())
            yield return null;

        _beginWait = null;

        IsRunning = true;
        CurrentIndex = -1;
        Advance();
    }

    private static bool IsScreenBusy()
    {
        if (QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen) return true;
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return true;
        return false;
    }

    private void Advance()
    {
        StopTimed();

        CurrentIndex++;

        if (CurrentIndex >= steps.Count)
        {
            Complete();
            return;
        }

        var step = CurrentStep;
        if (step == null)
        {
            // An empty slot in the list would otherwise stall the whole sequence silently.
            Debug.LogWarning("[RufusSetupManager] Step " + CurrentIndex + " is an empty slot - skipping.", this);
            Advance();
            return;
        }

        OnStepChanged?.Invoke(step);

        if (step.educationalDialog != null && DialogManager.Instance != null)
            DialogManager.Instance.ShowDialog(step.educationalDialog);

        if (step.kind == RufusSetupStepData.StepKind.Timed)
            _timed = StartCoroutine(RunTimed(step));
    }

    private IEnumerator RunTimed(RufusSetupStepData step)
    {
        float t = 0f;
        float duration = Mathf.Max(step.duration, 0.01f);

        while (t < duration)
        {
            t += Time.deltaTime;
            OnStepProgress?.Invoke(Mathf.Clamp01(t / duration));
            yield return null;
        }

        OnStepProgress?.Invoke(1f);
        _timed = null;
        Advance();
    }

    private void StopTimed()
    {
        if (_timed != null)
        {
            StopCoroutine(_timed);
            _timed = null;
        }
    }

    /// <summary>Called when the panel closes, so a pending Begin does not fire into a panel
    /// nobody is looking at any more.</summary>
    public void CancelPendingBegin()
    {
        if (_beginWait != null)
        {
            StopCoroutine(_beginWait);
            _beginWait = null;
        }
    }

    private void Complete()
    {
        IsRunning = false;
        StopTimed();

        if (TaskManager.Instance != null)
            TaskManager.Instance.ReportProgress(TaskId);

        OnSequenceComplete?.Invoke();

        if (completeDialog != null && DialogManager.Instance != null)
            DialogManager.Instance.ShowDialog(completeDialog);

        // Closing the panel here rather than in the view: the desktop coming back is what makes
        // DesktopIconButton re-run its OnEnable and notice the Rufus icon has just unlocked.
        if (browserPanel != null) browserPanel.SetActive(false);
        if (desktopPanel != null) desktopPanel.SetActive(true);

        Debug.Log("[RufusSetupManager] Rufus acquired. '" + TaskId + "' reported complete.");
    }

    // ==================================================================
    // Answers
    // ==================================================================

    /// <summary>Called by the view when the learner clicks the correct hotspot.</summary>
    public void SubmitHotspot()
    {
        if (!IsRunning) return;

        var step = CurrentStep;
        if (step == null || step.kind != RufusSetupStepData.StepKind.ClickHotspot) return;

        Advance();
    }

    /// <summary>Called by the view when the learner clicks the decoy - the wrong button on a
    /// screen that has two, e.g. "No" on the UAC prompt.</summary>
    public void SubmitDecoy()
    {
        if (!IsRunning) return;

        var step = CurrentStep;

        // Same guard as SubmitHotspot. Without it the decoy could register a mistake on a step
        // that is not a hotspot step at all - it was an inconsistency between the two, not a
        // deliberate difference.
        if (step == null || step.kind != RufusSetupStepData.StepKind.ClickHotspot) return;

        Reject(step, "clicked '" + (string.IsNullOrEmpty(step.decoyLabel) ? "the wrong button" : step.decoyLabel) + "'");
    }

    /// <summary>Called by the view when the learner presses Go on the address bar.</summary>
    public void SubmitUrl(string typed)
    {
        if (!IsRunning) return;

        var step = CurrentStep;
        if (step == null || step.kind != RufusSetupStepData.StepKind.TypeUrl) return;

        if (Matches(typed, step))
        {
            Advance();
            return;
        }

        Reject(step, "typed '" + typed + "' instead of the official site '" + step.correctUrl + "'");
    }

    private void Reject(RufusSetupStepData step, string what)
    {
        COC1AssessmentManager.ReportMistake("Rufus setup - " + step.stepId + ": " + what + ".", step);

        // ignoreSuppression: corrective feedback, not a tutorial, so it still shows while an
        // assessment has tutorial dialogs switched off.
        if (step.wrongDialog != null && DialogManager.Instance != null)
            DialogManager.Instance.ShowDialog(step.wrongDialog, null, true);

        OnWrongAnswer?.Invoke(step, what);
    }

    /// <summary>
    /// Deliberately forgiving about URL formatting and strict about the domain. The lesson is
    /// "go to the vendor's real site", not "type a URL without a typo" - marking someone wrong
    /// for writing https:// would teach nothing except that the game is fussy.
    /// </summary>
    private static bool Matches(string typed, RufusSetupStepData step)
    {
        string t = Normalise(typed);
        if (string.IsNullOrEmpty(t)) return false;

        if (t == Normalise(step.correctUrl)) return true;

        if (step.alsoAccept != null)
        {
            foreach (var alt in step.alsoAccept)
                if (!string.IsNullOrEmpty(alt) && t == Normalise(alt)) return true;
        }

        return false;
    }

    private static string Normalise(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";

        string s = url.Trim().ToLowerInvariant();

        if (s.StartsWith("https://")) s = s.Substring(8);
        else if (s.StartsWith("http://")) s = s.Substring(7);

        if (s.StartsWith("www.")) s = s.Substring(4);

        while (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);

        return s;
    }
}