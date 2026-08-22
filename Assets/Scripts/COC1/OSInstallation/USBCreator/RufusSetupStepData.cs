using UnityEngine;

/// <summary>
/// One screen in the "get Rufus onto this PC" sequence: open the browser, find the official
/// site, download the installer, run it, get through UAC, and start Rufus.
///
/// Each step is a BACKGROUND SPRITE plus one live control placed on top of it. You supply the
/// sprite (a screenshot of the browser, the Rufus site, the UAC prompt); the manager and view
/// supply the interaction and the marking.
///
/// -------------------------------------------------------------------------------------
/// COORDINATES ARE NORMALISED, NOT PIXELS.
/// Every Rect below is 0-1 across the panel, measured from the TOP-LEFT corner with y going
/// DOWN - so (0.5, 0.5, 0.2, 0.06) is a control 20% wide and 6% tall, starting halfway across
/// and halfway down.
///
/// Normalised rather than pixels because the background sprite stretches to fill the panel:
/// pixel coordinates would drift the moment the panel or the screen resolution changed, and
/// the hotspot would stop lining up with the thing it is sitting on.
///
/// The values shipped in the blueprint are SENSIBLE GUESSES. They cannot be right, because
/// they were written without seeing your screenshots. Expect to nudge every one of them in
/// the Inspector while looking at the Scene view - that is the intended workflow, not a sign
/// something is broken.
/// -------------------------------------------------------------------------------------
/// </summary>
[CreateAssetMenu(fileName = "NewRufusSetupStep", menuName = "PCSim/Rufus Setup Step")]
public class RufusSetupStepData : ScriptableObject
{
    public enum StepKind
    {
        /// <summary>Learner clicks a region of the screenshot - a link, a button, a file.</summary>
        ClickHotspot,

        /// <summary>Learner types a web address and presses Go.</summary>
        TypeUrl,

        /// <summary>Nothing to do - a progress bar runs and the sequence moves on. Downloading,
        /// extracting, starting up.</summary>
        Timed
    }

    [Tooltip("Short id used in the Console and in mistake reports, e.g. \"download_link\".")]
    public string stepId = "step";

    [Tooltip("Name of a screen drawn in Unity UI to show for this step, e.g. \"RufusSite\". Must " +
             "match a child of the view's Screens root. When this is set the Background sprite " +
             "below is ignored entirely - built by Tools > COC 1 > Rufus Setup Builder.")]
    public string drawnScreen = "";

    [Tooltip("A screenshot to show instead, for a step with no drawn screen. Stretched to fill " +
             "the panel, so match the panel's aspect ratio or it will look squashed. Ignored " +
             "whenever Drawn Screen above is set.")]
    public Sprite background;

    [TextArea(2, 4)]
    [Tooltip("The instruction strip along the bottom of the panel - what the learner is being asked to do.")]
    public string instruction;

    public StepKind kind = StepKind.ClickHotspot;

    // ------------------------------------------------------------------
    [Header("Click Hotspot")]
    [Tooltip("The region the learner has to click. Normalised 0-1 from the TOP-LEFT.")]
    public Rect hotspot = new Rect(0.4f, 0.45f, 0.2f, 0.07f);

    [Tooltip("Text drawn on the hotspot. Leave EMPTY to make it invisible so the learner has to " +
             "find the real button in your screenshot - which is the whole point of the exercise.")]
    public string hotspotLabel = "";

    [Tooltip("Draws a thin highlight outline around the hotspot. Useful while you are lining the " +
             "rects up, and worth leaving on for the first few steps of a beginner build.")]
    public bool showHotspotOutline = true;

    [Header("Decoy (optional) - a wrong button on the same screen")]
    [Tooltip("A second clickable region that is the WRONG answer, e.g. \"No\" on the UAC prompt. " +
             "Clicking it is recorded as a mistake and the learner stays on this step. " +
             "Leave width or height at 0 for a step with no decoy.")]
    public Rect decoyHotspot = new Rect(0f, 0f, 0f, 0f);

    public string decoyLabel = "";

    // ------------------------------------------------------------------
    [Header("Type URL")]
    [Tooltip("Where the address bar sits on your browser screenshot. Normalised 0-1 from the TOP-LEFT.")]
    public Rect inputRect = new Rect(0.08f, 0.06f, 0.72f, 0.05f);

    [Tooltip("Where the Go / arrow button sits.")]
    public Rect goRect = new Rect(0.82f, 0.06f, 0.08f, 0.05f);

    [Tooltip("The address that counts as correct. Compared loosely: case, https://, www. and a " +
             "trailing slash are all ignored, so the learner is marked on the DOMAIN, not on typing " +
             "a URL perfectly.")]
    public string correctUrl = "rufus.ie";

    [Tooltip("Other addresses also accepted, e.g. a full download path. Same loose comparison.")]
    public string[] alsoAccept;

    public string urlPlaceholder = "Search or type a web address";

    // ------------------------------------------------------------------
    [Header("Timed")]
    public float duration = 3f;

    [Tooltip("Where the progress bar sits on this screenshot.")]
    public Rect progressRect = new Rect(0.3f, 0.55f, 0.4f, 0.04f);

    public string progressLabel = "Downloading...";

    // ------------------------------------------------------------------
    [Header("Teaching")]
    [Tooltip("Shown when this step opens - the CSS NC II point behind it.")]
    public DialogSequence educationalDialog;

    [Tooltip("Shown when the learner types the wrong address or clicks the decoy.")]
    public DialogSequence wrongDialog;

    [TextArea(1, 3)]
    [Tooltip("Appears under the instruction after a wrong answer. A nudge, not the answer.")]
    public string hint;
}