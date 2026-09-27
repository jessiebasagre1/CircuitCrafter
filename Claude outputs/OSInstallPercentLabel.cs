using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// Makes the percentage on the install screens count, instead of sitting at whatever number was
/// typed into the stage asset.
///
/// ATTACH THIS TO: the same object as OS Install View. Nothing to wire - it finds the stage label
/// that view is already using.
///
/// ---------------------------------------------------------------------------------------------
/// WHY IT IS STUCK AT 45%
///
/// The number is not being calculated. OSInstallView shows the stage's text exactly as written:
///
///     stageLabel.text = stage.displayText;
///
/// and the OSInstallStageData asset for that stage has "45%" typed into displayText. It is a
/// caption, not a readout, so it says 45 forever.
///
/// The progress itself is already there and already correct - OSInstallManager raises
/// OnStageProgress from 0 to 1 through every timed stage, and that is what fills the bar under the
/// text. This component puts that same number into the words.
/// ---------------------------------------------------------------------------------------------
///
/// WHAT IT DOES TO THE TEXT
///   "Getting devices ready 45%"   ->  the 45 is replaced, so it counts 1% ... 100%
///   "Getting devices ready {percent}"  ->  the token is replaced, if you prefer writing it that way
///   text with no number at all    ->  left alone, unless Append If Missing is ticked
///
/// So the existing assets work untouched: any NN% already written into displayText becomes the
/// live figure. Nothing has to be retyped, and a stage with no percentage in it - "Select your
/// region", say - is not given one it never asked for.
///
/// IT WRITES IN LateUpdate RATHER THAN FROM THE EVENT, on purpose. OSInstallView subscribes to
/// OnStageChanged too, and the order two subscribers are called in is the order they happened to
/// subscribe - not something to depend on. Writing after everything else has run each frame means
/// this always has the last word, whichever way that race falls.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class OSInstallPercentLabel : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The label the install screen writes its stage text into. Leave empty to borrow the " +
             "one OS Install View is already using, which is what you normally want.")]
    [SerializeField] private TMP_Text stageLabel;

    [Header("Behaviour")]
    [Tooltip("Never show 0%. A progress readout that opens on zero reads as stalled, and the first " +
             "frame of a stage is not really nothing - the work has started.")]
    [SerializeField] private bool startAtOne = true;

    [Tooltip("ON: a stage whose text has no percentage in it gets one appended anyway. OFF " +
             "(recommended): only stages that already show a number get a live one.")]
    [SerializeField] private bool appendIfMissing;

    [Tooltip("What to append when the above is on.")]
    [SerializeField] private string appendFormat = "  {percent}%";

    [Header("Diagnostics")]
    [SerializeField] private bool logStages;

    private OSInstallManager manager;

    private string template = "";
    private float progress;
    private bool hasStage;
    private string lastWritten;

    // "45%", "7 %", "100%" - the number and its sign, so only the digits are replaced and the
    // wording around them survives exactly as the asset has it.
    private static readonly Regex Percentage = new Regex(@"\d+\s*%");

    // ==================================================================

    private void OnEnable() => EnsureSubscribed();

    private void Start() => EnsureSubscribed();

    /// <summary>
    /// Finds the label and the manager and subscribes - retrying on later frames until it works.
    ///
    /// THIS IS NOT BELT AND BRACES, IT IS THE ONLY WAY IT EVER RUNS.
    /// OSInstallView.Start() deactivates the install panel, and this component lives on that same
    /// panel at a later execution order - so its own Start is never reached at all. A one-shot
    /// subscription in Start would silently never happen, and the percentage would stay at 45 with
    /// nothing in the Console to explain it. This project has already lost an evening to exactly
    /// that pattern with a waypoint arrow.
    ///
    /// OnEnable covers the moment the panel is switched back on for the first stage; the retry
    /// from LateUpdate covers everything else, including a manager that wakes up later.
    /// </summary>
    private void EnsureSubscribed()
    {
        if (manager != null) return;

        if (stageLabel == null) stageLabel = BorrowLabelFromView();
        if (stageLabel == null) return;

        OSInstallManager found = OSInstallManager.Instance;
        if (found == null) found = FindAnyObjectByType<OSInstallManager>(FindObjectsInactive.Include);
        if (found == null) return;

        manager = found;

        manager.OnStageChanged += HandleStageChanged;
        manager.OnStageProgress += HandleProgress;
        manager.OnInstallComplete += HandleComplete;

        // Unconditional: "installed and silent" must never be mistakable for "not in the project".
        Debug.Log("[OSInstallPercentLabel] Watching the install - percentages in the stage text " +
                  "will now count.", this);
    }

    private void OnDestroy()
    {
        if (manager == null) return;

        manager.OnStageChanged -= HandleStageChanged;
        manager.OnStageProgress -= HandleProgress;
        manager.OnInstallComplete -= HandleComplete;
    }

    private void LateUpdate()
    {
        // Keeps trying until the panel has been switched on and the manager exists. Costs a null
        // check per frame and removes the one failure mode that leaves no trace.
        if (manager == null) EnsureSubscribed();

        if (!hasStage || stageLabel == null) return;

        string text = Compose();

        // Only when it actually changed: assigning TMP_Text.text re-parses and rebuilds the mesh,
        // and doing that every frame for a string that has not moved is wasted work on a phone.
        if (text == lastWritten) return;

        lastWritten = text;
        stageLabel.text = text;
    }

    // ==================================================================

    private void HandleStageChanged(OSInstallStageData stage)
    {
        template = stage != null && stage.displayText != null ? stage.displayText : "";
        progress = 0f;
        hasStage = true;
        lastWritten = null;

        if (logStages)
        {
            Debug.Log($"[OSInstallPercentLabel] Stage '{(stage != null ? stage.stage.ToString() : "?")}': " +
                      (HasPercentage(template)
                          ? "counting the percentage in its text."
                          : appendIfMissing
                              ? "no percentage in its text - appending one."
                              : "no percentage in its text - left as written."), this);
        }
    }

    private void HandleProgress(float t) => progress = Mathf.Clamp01(t);

    private void HandleComplete()
    {
        progress = 1f;
        lastWritten = null;
    }

    // ==================================================================

    private string Compose()
    {
        int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), startAtOne ? 1 : 0, 100);

        // The explicit token first, so an asset written as "{percent}" is honoured exactly and
        // never also matched by the digits rule below.
        if (template.Contains("{percent}"))
            return template.Replace("{percent}", percent.ToString());

        if (HasPercentage(template))
            return Percentage.Replace(template, percent + "%", 1);

        if (appendIfMissing)
            return template + appendFormat.Replace("{percent}", percent.ToString());

        return template;
    }

    private static bool HasPercentage(string text)
    {
        return !string.IsNullOrEmpty(text) && Percentage.IsMatch(text);
    }

    /// <summary>
    /// Takes the label straight off OSInstallView, so the two can never be pointed at different
    /// objects - a mismatch there would show a counting percentage on a label nobody can see while
    /// the visible one stayed at 45.
    ///
    /// Reflection because that field is [SerializeField] private and belongs to the learner's own
    /// script; a display convenience is not worth editing it.
    /// </summary>
    private TMP_Text BorrowLabelFromView()
    {
        var view = GetComponent<OSInstallView>();
        if (view == null) view = FindAnyObjectByType<OSInstallView>(FindObjectsInactive.Include);
        if (view == null) return null;

        FieldInfo field = typeof(OSInstallView).GetField(
            "stageLabel", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field != null) return field.GetValue(view) as TMP_Text;

        Debug.LogWarning("[OSInstallPercentLabel] OSInstallView has no field called 'stageLabel' " +
                         "any more - assign Stage Label on this component by hand.", this);
        return null;
    }

    // ==================================================================

    [ContextMenu("Report")]
    public void Report()
    {
        Debug.Log("[OSInstallPercentLabel] REPORT\n" +
                  $"  Label       : {(stageLabel != null ? stageLabel.name : "none")}\n" +
                  $"  Stage text  : {(string.IsNullOrEmpty(template) ? "(no stage yet)" : template)}\n" +
                  $"  Has a number: {HasPercentage(template)}\n" +
                  $"  Progress    : {progress * 100f:0}%\n" +
                  $"  Showing     : {(hasStage ? Compose() : "(nothing yet)")}", this);
    }
}
