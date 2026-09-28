using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TUTORIAL / GUIDED MODE for COC 3.
///
/// ATTACH THIS TO: the same GameObject as your COC3Manager. One component for the whole
/// activity - NOT one per panel.
///
/// TWO SEPARATE SURFACES, on purpose:
///
///   INTRO DIALOGS  ->  your existing DialogManager / DialogPanel.
///       Story and teaching, played automatically when a step opens. Typewriter effect,
///       tap or Space to advance, blocks input until read - exactly like COC1.
///
///   HINTS          ->  a panel of your own, with a Close button.
///       Reference material the learner opens when stuck. It should NOT block the screen,
///       should NOT type itself out one character at a time, and should stay up while
///       they type the answer. That is a different job from a dialog, so it gets a
///       different panel.
///
/// HOW IT KNOWS WHICH STEP IS ACTIVE:
/// COC3Manager keeps currentStepIndex private and raises no events, so rather than edit a
/// script that already works, this one watches which panel is switched on. Show()
/// activates exactly one step panel at a time, so "the active one" IS the current step.
/// </summary>
public class COC3Tutorial : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave empty to use the COC3Manager on this same GameObject.")]
    public COC3Manager manager;

    // ------------------------------------------------------------------
    [Header("INTRO DIALOGS � uses your existing DialogManager")]
    [Tooltip("Index-matched to COC3Manager.steps. Played through DialogManager when the step opens. Leave an entry EMPTY for steps that need no popup � nothing is auto-generated here, because the instruction text is already on the panel.")]
    public DialogSequence[] stepIntroDialogs;

    [Tooltip("Untick to keep hints but stop the per-step popups.")]
    public bool playIntroDialogs = true;

    // ------------------------------------------------------------------
    [Header("HINT PANEL � your own panel, not the dialog box")]
    [Tooltip("The hint panel GameObject. Must start INACTIVE.")]
    public GameObject hintPanel;

    [Tooltip("The TMP_Text inside the hint panel that shows the hint body.")]
    public TMP_Text hintText;

    [Tooltip("Optional heading inside the hint panel, e.g. 'Hint' or the step name.")]
    public TMP_Text hintTitleText;

    [Tooltip("The Close (X) button ON the hint panel. Wired automatically at runtime � you do not need to set its OnClick.")]
    public Button hintCloseButton;

    [Tooltip("The Hint button in your HUD that OPENS the panel. Also wired automatically.")]
    public Button hintButton;

    [Tooltip("Optional per-step hint text. Index-matched to the steps. Leave an entry empty and a hint is generated from that step's own answer data.")]
    public DialogSequence[] stepHintDialogs;

    // ------------------------------------------------------------------
    [Header("Hint Behaviour")]
    [Tooltip("Master switch. Untick for a graded assessment run � no dialogs, no hints, no highlights.")]
    public bool tutorialMode = true;

    [Tooltip("Open the hint automatically after this many seconds stuck on one step. 0 = only when the button is pressed.")]
    public float autoHintAfterSeconds = 20f;

    [Tooltip("Generated hints give the answer outright. Untick for a nudge only ('This step is asking for the Computer Name').")]
    public bool revealAnswerInHint = true;

    [Tooltip("Space or Escape also closes the hint panel, as well as the Close button.")]
    public bool closeHintOnSpace = true;

    [Tooltip("Move the hint panel to the front each time it opens, so it can never be painted over by a step panel. Only works if the hint panel is a SIBLING of the step panels � normally a direct child of the Canvas.")]
    public bool bringHintToFront = true;

    [Tooltip("Heading shown above generated hints.")]
    public string hintTitle = "Hint";

    // ------------------------------------------------------------------
    [Header("Assessment")]
    [Tooltip("Open the hint by itself after Auto Hint After Seconds during an ASSESSMENT as well. Off by " +
             "default: in an assessment a hint appears only when the learner asks for it, and every step " +
             "where one was opened is noted in the assessment report.\n\n" +
             "Hints are allowed in the assessment by unticking Disable Hints on COC3AssessmentManager.")]
    public bool autoHintInAssessment = false;

    /// <summary>Set by COC3AssessmentManager when its Disable Hints is unticked: the learner may open
    /// hints during this assessment.</summary>
    public bool HintsAllowedInAssessment
    {
        get { return hintsAllowedInAssessment; }
        set
        {
            hintsAllowedInAssessment = value;
            // A step may already be open - its panel can come up before the assessment starts - so
            // the Hint button is re-evaluated now rather than at the next step.
            if (hintButton != null) hintButton.gameObject.SetActive(TutorialOn && activeStep != null);
        }
    }
    private bool hintsAllowedInAssessment;

    [Header("Hotspot Highlight")]
    [Tooltip("Pulse the transparent hotspot buttons so the learner can see where to click.")]
    public bool highlightHotspots = true;

    public Color highlightColor = new Color(1f, 0.92f, 0.23f, 1f);

    [Tooltip("How strong the pulse gets at its brightest. 0 = invisible, 1 = solid.")]
    [Range(0f, 1f)] public float highlightMaxAlpha = 0.5f;

    [Tooltip("Pulses per second.")]
    public float pulseSpeed = 1.5f;

    // --- runtime state -------------------------------------------------
    private ScreenshotStep activeStep;
    private int activeIndex = -1;
    private float timeOnStep;
    private bool hintShownForThisStep;

    /// <summary>An intro dialog we want to play but cannot yet, because another one is
    /// still open. Without this, a step's intro fired on the same frame as the scene's
    /// IntroDialogTrigger would wipe that intro out - ShowDialog() clears the queue.</summary>
    private DialogSequence pendingIntro;

    private readonly List<Graphic> pulsing = new List<Graphic>();
    private readonly List<Color> originalColors = new List<Color>();

    // ==================================================================

    private void Awake()
    {
        if (manager == null) manager = GetComponent<COC3Manager>();

        // Wired in code rather than the Inspector so there is nothing to forget, and
        // nothing to wire twice. AddListener is additive, so we clear ours first in case
        // this runs again (domain reload with fast enter-play-mode, for instance).
        if (hintCloseButton != null)
        {
            hintCloseButton.onClick.RemoveListener(CloseHint);
            hintCloseButton.onClick.AddListener(CloseHint);
        }

        if (hintButton != null)
        {
            hintButton.onClick.RemoveListener(ShowHint);
            hintButton.onClick.AddListener(ShowHint);
        }

        if (hintPanel != null) hintPanel.SetActive(false);
    }

    private void Update()
    {
        if (manager == null || manager.steps == null) return;

        ScreenshotStep current = FindActiveStep();

        if (current != activeStep)
        {
            OnStepChanged(current);
            return;                     // let the new step start on a clean frame
        }

        bool dialogOpen = DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;

        // Keyboard shortcut to dismiss the hint. Only while the hint is actually up, so
        // it never competes with DialogManager's own Space handling.
        if (closeHintOnSpace && IsHintOpen && !dialogOpen && DismissPressed())
        {
            CloseHint();
            return;
        }

        // An intro dialog we held back goes now that the way is clear.
        if (pendingIntro != null && !dialogOpen)
        {
            var seq = pendingIntro;
            pendingIntro = null;
            DialogManager.Instance.ShowDialog(seq);
            return;
        }

        if (activeStep == null) return;

        // --- stall timer (paused while an intro dialog is being read) ---
        bool usingMachine = COC3StationController.Instance == null || COC3StationController.Instance.InStepView;

        if (!dialogOpen && !IsHintOpen && usingMachine)
        {
            timeOnStep += Time.deltaTime;
            if (TutorialOn && !hintShownForThisStep &&
                autoHintAfterSeconds > 0f && timeOnStep >= autoHintAfterSeconds &&
                (!AssessmentMode.IsActive || autoHintInAssessment))
            {
                ShowHint();
            }
        }

        // --- hotspot pulse ---
        if (pulsing.Count > 0)
        {
            float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            var c = highlightColor;
            c.a = t * highlightMaxAlpha;

            for (int i = 0; i < pulsing.Count; i++)
                if (pulsing[i] != null) pulsing[i].color = c;
        }
    }

    public bool IsHintOpen => hintPanel != null && hintPanel.activeSelf;

    private static bool DismissPressed() =>
        Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape);

    /// <summary>The one step panel currently switched on. Null between steps and once the
    /// completion screen is up.</summary>
    private ScreenshotStep FindActiveStep()
    {
        for (int i = 0; i < manager.steps.Count; i++)
        {
            var s = manager.steps[i];
            if (s != null && s.gameObject.activeInHierarchy) return s;
        }
        return null;
    }

    private void OnStepChanged(ScreenshotStep newStep)
    {
        RestoreHotspots();
        CloseHint();                    // never carry one step's answer into the next

        activeStep = newStep;
        activeIndex = newStep == null ? -1 : manager.steps.IndexOf(newStep);
        timeOnStep = 0f;
        hintShownForThisStep = false;
        pendingIntro = null;

        // A dead Hint button on the completion screen just invites confusion.
        if (hintButton != null)
            hintButton.gameObject.SetActive(TutorialOn && newStep != null);

        if (newStep == null || !TutorialOn) return;

        if (highlightHotspots &&
            CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.ObjectHighlights))
            BeginHotspotPulse(newStep);

        if (playIntroDialogs)
        {
            var intro = Get(stepIntroDialogs, activeIndex);
            if (intro != null) PlayIntro(intro);
        }
    }

    /// <summary>Plays an intro dialog through DialogManager now, or holds it until the
    /// dialog currently on screen closes.</summary>
    private void PlayIntro(DialogSequence seq)
    {
        if (DialogManager.Instance == null)
        {
            Debug.LogWarning("[COC3Tutorial] No DialogManager in this scene - add the Managers object " +
                             "with DialogManager on it, as in COC1. Hints will still work.", this);
            return;
        }

        if (DialogManager.Instance.IsDialogOpen) pendingIntro = seq;
        else DialogManager.Instance.ShowDialog(seq);
    }

    // ==================================================================
    // Hint panel
    // ==================================================================

    /// <summary>Opens the hint panel. Wired to your Hint button automatically.</summary>
    /// <summary>GUIDANCE: the Inspector switch AND GuidancePolicy. Hints, auto-hints, the hint button
    /// and the hotspot pulse are all withheld in an assessment even if nothing called
    /// SetTutorialMode(false) - COC3AssessmentManager only does when its Disable Hints is ticked.</summary>
    private bool TutorialOn =>
        tutorialMode &&
        (CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.Hints) ||
         // The COC 3 assessment with Disable Hints unticked. Hotspot pulses and intro dialogs stay off
         // there all the same - they have their own checks (ObjectHighlights, DialogManager).
         (HintsAllowedInAssessment && AssessmentMode.IsActive));

    public void ShowHint()
    {
        if (!TutorialOn || activeStep == null || activeStep.stepData == null) return;

        // If the walk-up station system is in use, a hint only makes sense while the
        // learner is actually sitting at a machine - the step UI is hidden otherwise.
        if (COC3StationController.Instance != null && !COC3StationController.Instance.InStepView) return;

        // An intro dialog blocks the screen with its tap-catcher, so a hint opened
        // underneath it would be unreachable. Let the learner finish reading first.
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

        if (hintPanel == null || hintText == null)
        {
            Debug.LogWarning("[COC3Tutorial] Hint Panel / Hint Text are not assigned - nothing to show.", this);
            return;
        }

        // Noted once per step in an assessment, so the report shows where help was used.
        if (!hintShownForThisStep && AssessmentMode.IsActive && COC3AssessmentManager.Instance != null)
            COC3AssessmentManager.Instance.NoteHintUsed();

        hintShownForThisStep = true;

        var authored = Get(stepHintDialogs, activeIndex);

        // The AUTHORED branch goes through ForDisplay; the generated one does NOT, because
        // BuildGeneratedHint has already escaped the expected value itself. Escaping twice would
        // double the backslashes again and show four where two belong.
        //
        // The authored branch was missing this entirely, so a hand-written hint containing a UNC
        // path lost a backslash on the way to the screen and told the learner to type an answer
        // the validator then rejected. Only two steps in COC 3 carry backslashes - 19
        // (the SharedDocs path) and 25 (the Redirected path) - which is why it read as a
        // one-step content error rather than a rendering bug.
        hintText.text = authored != null
            ? ForDisplay(Flatten(authored))
            : BuildGeneratedHint(activeStep.stepData);

        if (hintTitleText != null)
            hintTitleText.text = (authored != null && authored.lines.Count > 0 &&
                                  !string.IsNullOrEmpty(authored.lines[0].speakerName))
                ? authored.lines[0].speakerName
                : hintTitle;

        hintPanel.SetActive(true);

        // Unity UI paints siblings in Hierarchy order, so a panel sitting ABOVE the step
        // panels is drawn UNDERNEATH them - it activates correctly and stays invisible,
        // which reads exactly like a broken button. Pushing it to last sibling each time
        // it opens guarantees it is on top of everything it shares a parent with.
        if (bringHintToFront) hintPanel.transform.SetAsLastSibling();
    }

    /// <summary>Closes the hint panel. Wired to your Close button automatically.</summary>
    public void CloseHint()
    {
        if (hintPanel != null) hintPanel.SetActive(false);
    }

    /// <summary>Open if closed, close if open - handy if you want ONE button for both.</summary>
    public void ToggleHint()
    {
        if (IsHintOpen) CloseHint();
        else ShowHint();
    }

    /// <summary>Replays the current step's intro dialog - handy on a "Repeat" button.</summary>
    public void ReplayIntro()
    {
        if (!tutorialMode || activeIndex < 0) return;

        var intro = Get(stepIntroDialogs, activeIndex);
        if (intro != null) PlayIntro(intro);
    }

    /// <summary>Wire a UI Toggle's OnValueChanged here to switch between the guided run and
    /// the graded run without needing a second scene.</summary>
    public void SetTutorialMode(bool on)
    {
        tutorialMode = on;

        if (!on)
        {
            RestoreHotspots();
            CloseHint();
            pendingIntro = null;
            if (hintButton != null) hintButton.gameObject.SetActive(false);
        }
        else if (activeStep != null)
        {
            if (hintButton != null) hintButton.gameObject.SetActive(TutorialOn);
            // GUIDANCE: the pulse is a highlight - withheld in an assessment, as in OnStepChanged.
            if (highlightHotspots &&
                CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.ObjectHighlights))
                BeginHotspotPulse(activeStep);
        }
    }

    // ==================================================================
    // Hotspot highlight
    // ==================================================================

    private void BeginHotspotPulse(ScreenshotStep step)
    {
        if (step.stepData == null) return;
        if (step.stepData.stepType != StepType.ButtonInteraction) return;
        if (step.interactionButtons == null) return;

        foreach (var btn in step.interactionButtons)
        {
            if (btn == null) continue;

            // targetGraphic is the hotspot's own Image. Fall back to any Image on the
            // object in case the button was wired up by hand.
            Graphic g = btn.targetGraphic != null ? btn.targetGraphic : btn.GetComponent<Image>();
            if (g == null) continue;

            pulsing.Add(g);
            originalColors.Add(g.color);
        }
    }

    /// <summary>Puts every pulsed hotspot back exactly as it was - normally fully
    /// transparent, which is how they must look outside tutorial mode.</summary>
    private void RestoreHotspots()
    {
        for (int i = 0; i < pulsing.Count; i++)
            if (pulsing[i] != null) pulsing[i].color = originalColors[i];

        pulsing.Clear();
        originalColors.Clear();
    }

    // ==================================================================
    // Hint text
    // ==================================================================

    private static DialogSequence Get(DialogSequence[] array, int index)
    {
        if (array == null || index < 0 || index >= array.Length) return null;
        return array[index];
    }

    /// <summary>Turns an authored multi-line DialogSequence into one block of text, since
    /// the hint panel shows everything at once rather than one line at a time.</summary>
    private static string Flatten(DialogSequence seq)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < seq.lines.Count; i++)
        {
            if (i > 0) sb.Append("\n\n");
            sb.Append(seq.lines[i].text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds the hint from the step's own data, so you only author hints for the steps
    /// that deserve a hand-written explanation - and generated ones stay correct
    /// automatically when you change an expected answer in COC3StepBlueprint.
    /// </summary>
    private string BuildGeneratedHint(ScreenshotStepData data)
    {
        switch (data.stepType)
        {
            case StepType.Informational:
                return "Read the instruction on the panel, then tap NEXT to continue.";

            case StepType.ButtonInteraction:
                return "Look for the highlighted area on the screenshot and tap it.\n\n" +
                       "It marks the exact control a technician would use at this point.";

            case StepType.PingTest:
                return "Tap RUN PING and watch the replies come back.\n\n" +
                       "Four replies means the client can reach the server. " +
                       "'Request timed out' means the network configuration needs another look.";

            case StepType.TextInput:
                return revealAnswerInHint
                    ? $"Type this into the <b>{data.textFieldLabel}</b> box:\n\n" +
                      $"<b>{ForDisplay(data.textFieldExpectedValue)}</b>"
                    : $"This step is asking for the <b>{data.textFieldLabel}</b>. " +
                      "Check the job order for the exact value.";

            case StepType.NetworkConfigInput:
                return revealAnswerInHint
                    ? $"IP Address:  <b>{data.expectedIP}</b>\n" +
                      $"Subnet Mask:  <b>{data.expectedSubnetMask}</b>\n" +
                      $"Default Gateway:  <b>{data.expectedGateway}</b>"
                    : "All three boxes must be correct before this step will accept it.\n\n" +
                      "Remember: a server uses a static address, and the gateway is the router.";

            default:
                return "Follow the instruction on the panel.";
        }
    }

    /// <summary>
    /// Makes a value safe to SHOW in a TextMeshPro label.
    ///
    /// TMP parses escape sequences in the string you assign to it, and one of them is
    /// "\\" meaning a single backslash. So a UNC path like
    ///     \\SERVER01\SharedDocs
    /// renders as
    ///     \SERVER01\SharedDocs
    /// - the hint quietly loses a backslash and shows the learner an answer the validator
    /// will reject. Doubling every backslash before display cancels TMP's un-escaping.
    /// Display only - the stored expected value is never touched.
    /// </summary>
    private static string ForDisplay(string value)
    {
        return string.IsNullOrEmpty(value) ? value : value.Replace("\\", "\\\\");
    }

#if UNITY_EDITOR
    /// <summary>Keeps the two arrays the same length as the steps list, so the Inspector
    /// slots always line up with the steps they belong to.</summary>
    private void OnValidate()
    {
        if (manager == null) manager = GetComponent<COC3Manager>();
        if (manager == null || manager.steps == null) return;

        int n = manager.steps.Count;
        if (stepIntroDialogs == null || stepIntroDialogs.Length != n)
            System.Array.Resize(ref stepIntroDialogs, n);
        if (stepHintDialogs == null || stepHintDialogs.Length != n)
            System.Array.Resize(ref stepHintDialogs, n);
    }
#endif
}