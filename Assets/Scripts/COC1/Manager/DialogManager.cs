using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CircuitCrafter.Guidance;

/// <summary>
/// Central manager that displays tutorial/dialog popups.
/// Anything in the game (interaction, task completion, etc.) can call
/// DialogManager.Instance.ShowDialog(sequence) to pop up a tutorial box.
/// Put this on a persistent "Managers" GameObject in your scene.
/// </summary>
public class DialogManager : MonoBehaviour
{
    public static DialogManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject dialogPanel;
    [SerializeField] private TMP_Text speakerNameText;
    [SerializeField] private TMP_Text dialogBodyText;

    [Header("Settings")]
    [SerializeField] private float typewriterSpeed = 0.02f;
    [SerializeField] private bool pauseGameWhileOpen = false;

    [Header("Typing sound")]
    [Tooltip("The blip played as the line types itself out. Leave the clip empty for a silent " +
             "typewriter - nothing breaks.\n\n" +
             "Fill in ALTERNATES with two or three variants: a typing sound repeats more than any " +
             "other sound in the game, and one sample on loop is the fastest way to make a dialog " +
             "box annoying.\n\n" +
             "It plays through SfxPlayer, so the learner's SFX Volume and Master Volume both " +
             "apply. A scene with no SfxPlayer is simply silent.")]
    [SerializeField] private SfxEntry typingSound = new SfxEntry();

    [Tooltip("Play the blip every Nth character rather than on every one.\n\n" +
             "At the default typewriter speed a blip per character is about 50 a second, which " +
             "stops being typing and becomes a buzz. 2 or 3 sounds like someone talking.")]
    [Min(1)]
    [SerializeField] private int typingSoundEveryNChars = 2;

    [Tooltip("Do not blip on spaces and line breaks. Leave ON - the gaps between words are " +
             "what make the rhythm sound like speech rather than a machine.")]
    [SerializeField] private bool typingSoundSkipWhitespace = true;

    [Header("Auto-size to text")]
    [Tooltip("The dialog panel's RectTransform. Assign to have the box grow and shrink to fit each line. Leave empty to keep a fixed size, exactly as before.")]
    [SerializeField] private RectTransform panelRect;

    [Tooltip("Extra height added on top of the measured text - room for the speaker name, padding and the 'press space' prompt.")]
    [SerializeField] private float verticalPadding = 70f;

    [Tooltip("The box never shrinks below this, so a one-word line does not produce a sliver.")]
    [SerializeField] private float minPanelHeight = 130f;

    [Tooltip("Safety ceiling for a very long line.")]
    [SerializeField] private float maxPanelHeight = 600f;

    /// <summary>Characters revealed since the last blip. Reset per line.</summary>
    private int charsSinceTypingBlip;

    private readonly Queue<DialogLine> queuedLines = new Queue<DialogLine>();
    private Coroutine typingRoutine;
    private int openedFrame = -1;
    private int closedFrame = -1;

    /// <summary>Invoked when the currently-open sequence finishes (all lines advanced through).</summary>
    private Action currentOnComplete;

    /// <summary>True while a tutorial popup is showing. Other scripts (movement,
    /// interaction, drag-and-drop) should check this and ignore input while true.</summary>
    public bool IsDialogOpen { get; private set; }

    /// <summary>
    /// Silences every tutorial popup in the project. Set by COC1AssessmentManager while an
    /// assessment is running, and cleared when it ends.
    ///
    /// It is deliberately blunt: an assessment has to suppress dialogs scattered across dozens
    /// of PCPartItem inspectors (removalDialog, installDialog, outOfOrderDialog), which no
    /// central switch could otherwise reach. Rather than adding an "am I in assessment mode"
    /// check to every one of those call sites, ShowDialog itself returns early.
    ///
    /// The assessment's OWN dialogs get through by passing ignoreSuppression: true - see the
    /// three-argument ShowDialog below. Nothing else in the project uses that overload, so
    /// nothing else can accidentally bypass the suppression.
    ///
    /// Static because the flag has to survive being read by objects that may be created or
    /// destroyed at any point during the scene, and because there is only ever one dialog
    /// system in play.
    /// </summary>
    public static bool SuppressTutorials;

    /// <summary>
    /// Dialog assets that are still shown while tutorials are suppressed - i.e. during an assessment.
    /// For messages that belong to the job rather than to the teaching: "you need the crimping tool
    /// first", "the link is verified". Filled per scene (COC2AssessmentDialogs) and emptied again when
    /// that scene unloads. Anything not listed here is withheld exactly as before.
    /// </summary>
    public static readonly HashSet<DialogSequence> AllowedWhileSuppressed = new HashSet<DialogSequence>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[DialogManager] Duplicate detected on '{name}' - " +
                $"an instance already exists on '{Instance.name}'. Destroying this one.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log($"[DialogManager] Instance set to '{name}'.", this);
        dialogPanel.SetActive(false);
    }

    private void Update()
    {
        if (!IsDialogOpen) return;

        // Ignore input on the exact frame the dialog opened, so the same click/key
        // that triggered the tutorial (e.g. clicking the motherboard) doesn't also
        // immediately skip the first line.
        if (Time.frameCount == openedFrame) return;

        // Keyboard skip (PC). Touch/mouse-tap skip is handled by RequestAdvance()
        // below, called from DialogTapAdvance via the UI EventSystem - see that
        // script's comment for why it's not read here as raw Input polling too.
        if (Input.GetKeyDown(KeyCode.Space))
            AdvanceDialog();
    }

    /// <summary>Called when the player taps/clicks anywhere on screen to skip -
    /// wired up via DialogTapAdvance on a full-screen child of dialogPanel, so it
    /// reaches here through the UI EventSystem rather than raw Input polling.</summary>
    public void RequestAdvance()
    {
        if (!IsDialogOpen) return;
        if (Time.frameCount == openedFrame) return;
        AdvanceDialog();
    }

    /// <summary>Call this from anywhere to show a tutorial popup.</summary>
    public void ShowDialog(DialogSequence sequence)
    {
        ShowDialog(sequence, null, false);
    }

    /// <summary>Show a tutorial popup and invoke a callback once the player finishes it
    /// (e.g. to spawn a waypoint arrow after the sequence closes).</summary>
    public void ShowDialog(DialogSequence sequence, Action onComplete)
    {
        ShowDialog(sequence, onComplete, false);
    }

    /// <summary>
    /// The real implementation. ignoreSuppression is for dialogs that are part of the
    /// assessment itself rather than the tutorial layer - the stage hand-off messages, and
    /// anything else that must still be seen while SuppressTutorials is on.
    /// </summary>
    public void ShowDialog(DialogSequence sequence, Action onComplete, bool ignoreSuppression)
    {
        // GUIDANCE: tutorial dialogs are also withheld whenever GuidancePolicy says so - i.e. in ANY
        // assessment, including COC4's repair sites, which have no assessment manager of their own
        // to set SuppressTutorials. The assessment's own messages still pass ignoreSuppression.
        bool withheld = SuppressTutorials || !GuidancePolicy.Allows(GuidanceKind.ProcedureTutorial);

        if (withheld && !ignoreSuppression && (sequence == null || !AllowedWhileSuppressed.Contains(sequence)))
        {
            // Fire the callback anyway - a caller waiting on it must not stall just because
            // the popup itself was silenced.
            onComplete?.Invoke();
            return;
        }

        if (sequence == null || sequence.lines.Count == 0)
        {
            // Nothing to show  fire the callback immediately so callers don't stall.
            onComplete?.Invoke();
            return;
        }

        queuedLines.Clear();
        foreach (var line in sequence.lines)
            queuedLines.Enqueue(line);

        currentOnComplete = onComplete;

        IsDialogOpen = true;
        openedFrame = Time.frameCount;
        dialogPanel.SetActive(true);

        if (pauseGameWhileOpen) Time.timeScale = 0f;

        AdvanceDialog();
    }

    /// <summary>
    /// For a dialog that EXPLAINS A MISTAKE - "that's the wrong ISO, use the one on the job order".
    ///
    /// Story Mode: shown straight away, exactly like the old ShowDialog(seq, cb, true) calls.
    /// Assessment: NOT shown - revealing the correct procedure right after a mistake would turn the
    /// exam into a lesson. Its text is kept with the mistake just recorded (MistakeJournal) and shown
    /// on the review screen once the run is over. The callback still fires, so nothing waits on it.
    ///
    /// Call it on the line AFTER the mistake is reported, in the same frame - that is how the text
    /// finds the right mistake.
    /// </summary>
    public void ShowCorrectiveDialog(DialogSequence sequence, Action onComplete = null)
    {
        if (!GuidancePolicy.Allows(GuidanceKind.CorrectiveFeedback))
        {
            if (sequence != null)
            {
                var text = new System.Text.StringBuilder();
                foreach (var line in sequence.lines)
                {
                    if (line == null || string.IsNullOrEmpty(line.text)) continue;
                    if (text.Length > 0) text.Append(' ');
                    text.Append(line.text.Trim());
                }
                MistakeJournal.AttachCorrectionToLast(text.ToString());
            }

            if (GuidancePolicy.IsAssessment && GuidancePolicy.Settings.showNeutralRejectionToast)
                CoachFeedback.Info(GuidancePolicy.NeutralRejection);

            onComplete?.Invoke();
            return;
        }

        ShowDialog(sequence, onComplete, true);
    }

    /// <summary>Called by pressing any key/button. Also called automatically to show the first line.</summary>
    private void AdvanceDialog()
    {
        if (typingRoutine != null)
        {
            // Text is still typing out - pressing a key skips straight to the full
            // line instead of moving on immediately.
            StopCoroutine(typingRoutine);
            typingRoutine = null;

            // The full line is already in the component - only the reveal was partial.
            dialogBodyText.text = lastFullLine;
            dialogBodyText.maxVisibleCharacters = int.MaxValue;
            return;
        }

        if (queuedLines.Count == 0)
        {
            CloseDialog();
            return;
        }

        DialogLine line = queuedLines.Dequeue();
        speakerNameText.text = line.speakerName;

        typingRoutine = StartCoroutine(TypeText(line.text));
    }

    private string lastFullLine;

    /// <summary>
    /// Types the line out one character at a time WITHOUT changing the text itself.
    ///
    /// It used to append a character per frame-step to dialogBodyText.text. That works, but
    /// it means the text's measured size changes on every single character - so anything
    /// sizing itself from the text (a ContentSizeFitter, or the auto-size below) grows the
    /// box letter by letter and jumps every time a word wraps. It also allocates a new
    /// string per character.
    ///
    /// Setting the whole line immediately and revealing it with maxVisibleCharacters gives
    /// the identical effect, with the layout settled from the first frame. That is what
    /// makes auto-sizing possible at all.
    /// </summary>
    private IEnumerator TypeText(string text)
    {
        lastFullLine = text;
        charsSinceTypingBlip = 0;

        dialogBodyText.text = text;
        dialogBodyText.maxVisibleCharacters = 0;

        // Measure and resize NOW, against the complete line, before a single character shows.
        ApplyAutoSize(text);

        // ForceMeshUpdate so textInfo.characterCount is right this frame rather than next.
        dialogBodyText.ForceMeshUpdate();
        int total = dialogBodyText.textInfo.characterCount;

        for (int shown = 0; shown <= total; shown++)
        {
            dialogBodyText.maxVisibleCharacters = shown;

            // shown == 0 reveals nothing, so there is no character to sound yet.
            if (shown > 0) PlayTypingBlip(shown - 1);

            // Written as if/else rather than a conditional expression on purpose:
            // WaitForSecondsRealtime and WaitForSeconds share no common base, so the ternary
            // only compiles under C# 9's target-typing. This works on any version.
            if (pauseGameWhileOpen)
                yield return new WaitForSecondsRealtime(typewriterSpeed);
            else
                yield return new WaitForSeconds(typewriterSpeed);
        }

        typingRoutine = null;
    }

    /// <summary>
    /// Sounds the character that was just revealed.
    ///
    /// NOT ONE BLIP PER CHARACTER. At the default 0.02s that would be fifty a second, which is
    /// not typing, it is a tone. Every second or third character, with the spaces skipped, gives
    /// the uneven rhythm that reads as someone speaking.
    ///
    /// Whitespace is skipped rather than counted, so the gap between words is a real gap - that
    /// pause is most of what makes the effect work.
    ///
    /// The retrigger guard is bypassed on purpose: it exists to stop ONE action being heard
    /// twice, and this is the opposite case - a sound deliberately repeating faster than the
    /// guard's window.
    /// </summary>
    private void PlayTypingBlip(int characterIndex)
    {
        if (typingSound == null || !typingSound.HasSound) return;

        var info = dialogBodyText.textInfo;
        if (info == null || characterIndex >= info.characterInfo.Length) return;

        char c = info.characterInfo[characterIndex].character;
        if (typingSoundSkipWhitespace && char.IsWhiteSpace(c)) return;

        charsSinceTypingBlip++;
        if (charsSinceTypingBlip < typingSoundEveryNChars) return;

        charsSinceTypingBlip = 0;
        SfxPlayer.PlayCustom(typingSound, bypassRetriggerGuard: true);
    }

    /// <summary>
    /// Sizes the panel to the line it is about to show.
    ///
    /// GetPreferredValues measures the text as if it were laid out at the given width, which
    /// is what lets this run before anything is visible. Assigning panelRect is optional -
    /// leave it empty and the dialog behaves exactly as it always has.
    ///
    /// The panel needs a FIXED vertical anchor (anchored to the bottom, say) rather than a
    /// stretched one, or sizeDelta.y is an offset from the parent's edges and changing it
    /// will not do what you expect.
    /// </summary>
    private void ApplyAutoSize(string fullText)
    {
        if (panelRect == null || dialogBodyText == null) return;

        float wrapWidth = dialogBodyText.rectTransform.rect.width;
        if (wrapWidth <= 1f) return;   // not laid out yet - better to leave it alone than guess

        Vector2 preferred = dialogBodyText.GetPreferredValues(fullText, wrapWidth, 0f);
        float target = Mathf.Clamp(preferred.y + verticalPadding, minPanelHeight, maxPanelHeight);

        panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, target);
    }

    private void CloseDialog()
    {
        IsDialogOpen = false;
        closedFrame = Time.frameCount;
        dialogPanel.SetActive(false);
        if (pauseGameWhileOpen) Time.timeScale = 1f;

        // Fire and clear the completion callback for this sequence.
        Action callback = currentOnComplete;
        currentOnComplete = null;
        callback?.Invoke();
    }

    /// <summary>True only on the exact frame the dialog just closed  lets other
    /// scripts ignore the same key/click press that closed it.</summary>
    public bool WasDialogClosedThisFrame => closedFrame == Time.frameCount;
}