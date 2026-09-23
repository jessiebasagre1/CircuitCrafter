using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The briefing screen shown before a graded run: what the candidate is being asked to do, how
/// it is scored, and a Start button that begins the clock.
///
/// ATTACH THIS TO: any always-active object in the scene. Not to the panel it shows - a panel
/// that is switched off cannot switch itself back on.
///
/// ONE COMPONENT, EVERY MODULE. Nothing here knows what COC1, COC2 or COC3 are. The objectives
/// are typed in the Inspector and the assessment manager is just "a MonoBehaviour to enable",
/// so the same script serves all four modules and any you add later.
///
/// HOW IT HOLDS THE ASSESSMENT BACK - AND WHY NO MANAGER NEEDED EDITING
/// Leave the assessment manager COMPONENT unticked in the Inspector. A disabled component still
/// gets Awake(), so its Instance is set and anything looking for it finds it - but Start() does
/// not run until the component is enabled for the first time, and Start() is where every
/// assessment manager calls BeginAssessment(). Update() does not run either, so the timer cannot
/// tick while the candidate is still reading.
///
/// Pressing Start ticks the component. Start() fires then, the run begins, and the manager is
/// none the wiser. That is the whole mechanism.
///
/// IN CAMPAIGN MODE it enables the manager immediately in Awake and switches itself off. The
/// manager's Start() still runs, still sees that no assessment is active, and still shuts itself
/// down the way it always did - so a campaign playthrough is completely unaffected.
/// </summary>
public class AssessmentIntroPanel : MonoBehaviour
{
    [Header("Which module")]
    [Tooltip("Must match the Mode Id on the menu and on this scene's assessment manager - " +
             "\"COC1\", \"COC2\", \"COC3\", \"COC4\". Nothing shows unless an assessment for " +
             "THIS id is running.")]
    public string modeId = "COC2";

    // ------------------------------------------------------------------

    [Header("Panel")]
    [Tooltip("The briefing UI. Switched on at the start of a graded run, off when Start is pressed.")]
    public GameObject panel;

    public TMP_Text titleText;
    public TMP_Text objectivesText;
    public TMP_Text rulesText;

    [Tooltip("Wire nothing to its OnClick - this adds the listener itself. Wiring it twice runs " +
             "the assessment start twice.")]
    public Button startButton;

    // ------------------------------------------------------------------

    [Header("Text")]
    public string title = "COC 2 - Cable Assembly and Testing";

    [Tooltip("One line per objective. A bullet is added to each, so type the text only.")]
    [TextArea(1, 3)]
    public string[] objectives =
    {
        "Collect the crimping tool from the workbench.",
        "Strip the jacket without nicking the conductors.",
        "Arrange all eight wires to the standard for the cable you were asked to build.",
        "Crimp both ends. A crimped RJ45 cannot be reopened - get it right first time.",
        "Test the cable. It must pass all eight conductors.",
        "Build and pass BOTH a straight-through and a crossover cable."
    };

    [Tooltip("Shown under the objectives. Say how it is scored and what a mistake costs. Left " +
             "blank, the rules block is hidden.")]
    [TextArea(2, 6)]
    public string rules =
        "You are timed from the moment you press Start.\n" +
        "Every cable that fails the tester is recorded, with the fault that caused it.\n" +
        "A run finished with no failures earns a bonus.";

    [Tooltip("Prefix put in front of each objective line.")]
    public string bullet = "•  ";

    // ------------------------------------------------------------------

    [Header("What Start switches on")]
    [Tooltip("The assessment manager for this scene. LEAVE ITS COMPONENT UNTICKED in the " +
             "Inspector - this ticks it when Start is pressed, which is what runs its Start() " +
             "and begins the run.")]
    public MonoBehaviour[] beginOnStart;

    [Tooltip("Hidden while the briefing is up, shown when Start is pressed - the HUD, the mobile " +
             "controls, anything that should not be visible behind a briefing.")]
    public GameObject[] hideUntilStart;

    [Tooltip("Switched off while the briefing is up - PlayerMovement and MouseLook, so the " +
             "candidate cannot wander off mid-briefing.")]
    public MonoBehaviour[] freezeUntilStart;

    [Tooltip("Free the cursor while the briefing is up so the Start button is clickable, and " +
             "capture it again on Start. No-op on touch platforms.")]
    public bool manageCursor = true;

    // ------------------------------------------------------------------

    [Header("Suppressed for the whole graded run")]
    [Tooltip("Switched off in Awake, before any Start() in the scene runs. This is where the " +
             "INTRO DIALOG trigger goes, and the quiz, and the autosaver.\n\n" +
             "Not the same as the manager's Disable In Assessment list: that one only runs when " +
             "Start is pressed, by which time an intro dialog has already fired over the briefing. " +
             "Anything that acts at scene load belongs here instead.")]
    public GameObject[] disableForAssessment;

    [Tooltip("Set DialogManager.SuppressTutorials the moment the briefing appears, rather than " +
             "waiting for the assessment manager. IntroDialogTrigger fires from Start(), and every " +
             "Start() in the scene runs after every Awake() - so setting it here beats it with no " +
             "ordering luck involved.")]
    public bool suppressDialogsDuringBriefing = true;

    [Header("Events")]
    public UnityEvent OnBriefingShown;
    public UnityEvent OnStarted;

    // ------------------------------------------------------------------

    /// <summary>True while the briefing is on screen.</summary>
    public bool IsShowing { get; private set; }

    /// <summary>True once Start has been pressed. Never resets - the briefing is shown once.</summary>
    public bool HasStarted { get; private set; }

    // ==================================================================

    private void Awake()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartAssessment);
            startButton.onClick.AddListener(StartAssessment);
        }

        if (!AssessmentMode.IsAssessing(modeId))
        {
            // Campaign run. Hand everything straight back and get out of the way.
            if (panel != null) panel.SetActive(false);
            EnableManagers();
            enabled = false;
            return;
        }

        ShowBriefing();
    }

    // ==================================================================

    /// <summary>
    /// The cursor is re-freed here as well as in Awake, and Start() is the reason: several view
    /// controllers capture the cursor from their OWN Awake, and Awake order between components is
    /// undefined. Whatever they did, this runs after all of it.
    /// </summary>
    private void Start()
    {
        if (IsShowing && manageCursor) CursorControl.Free();
    }

    /// <summary>
    /// Holds the cursor free for as long as the briefing is up.
    ///
    /// Belt and braces, and earned: five separate scripts in this project write Cursor.lockState,
    /// some of them every frame. A single Free() call at startup is a bet on nobody else touching
    /// it afterwards, and that bet has already been lost twice. This only runs while the briefing
    /// is on screen and does nothing at all on touch platforms.
    /// </summary>
    private void LateUpdate()
    {
        if (!IsShowing) return;

        // The world "Press E to ..." prompt belongs to PlayerInteractor, which keeps running every
        // frame regardless of this panel - Freeze Until Start stops the player from WALKING away,
        // but it does not stop the prompt for whatever they were already standing next to from
        // showing on top of (or behind) the briefing. This is the same per-frame request
        // StationPromptHider and every station-view controller already use, so it costs nothing to
        // ask again here and there is nothing to undo when the briefing closes.
        //
        // Fully qualified on purpose - this project has two classes named PlayerInteractor, and an
        // unqualified reference resolves to the wrong one (COC1's, which has no such method).
        CSSSimulation.Interaction.PlayerInteractor.SuppressPromptThisFrame();

        if (!manageCursor) return;

        if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
            CursorControl.Free();
    }

    private void ShowBriefing()
    {
        WarnIfManagersAlreadyRunning();

        // FIRST, before anything else can react to the scene loading. Every Start() in the scene
        // runs after every Awake(), and this is called from Awake - so IntroDialogTrigger.Start()
        // finds suppression already on and its ShowDialog becomes a no-op.
        if (suppressDialogsDuringBriefing) DialogManager.SuppressTutorials = true;

        foreach (var go in disableForAssessment) if (go != null) go.SetActive(false);

        if (titleText != null) titleText.text = title;
        if (objectivesText != null) objectivesText.text = BuildObjectives();

        if (rulesText != null)
        {
            rulesText.text = rules;
            rulesText.gameObject.SetActive(!string.IsNullOrEmpty(rules));
        }

        foreach (var go in hideUntilStart) if (go != null) go.SetActive(false);
        foreach (var mb in freezeUntilStart) if (mb != null) mb.enabled = false;

        if (manageCursor) CursorControl.Free();

        if (panel != null) panel.SetActive(true);

        IsShowing = true;
        OnBriefingShown?.Invoke();

        Debug.Log($"[AssessmentIntroPanel] Briefing shown for {modeId}. " +
                  "The assessment manager stays disabled until Start is pressed.");
    }

    /// <summary>Wired to the Start button in Awake. Also callable from anywhere.</summary>
    public void StartAssessment()
    {
        if (HasStarted) return;
        HasStarted = true;
        IsShowing = false;

        if (panel != null) panel.SetActive(false);

        foreach (var go in hideUntilStart) if (go != null) go.SetActive(true);
        foreach (var mb in freezeUntilStart) if (mb != null) mb.enabled = true;

        if (manageCursor) CursorControl.Capture();

        // LAST. Enabling the manager runs its Start(), which starts the timer - so everything
        // the run needs must already be back on by the time this line executes, or the first
        // second is spent with the HUD still hidden and the player still frozen.
        EnableManagers();

        OnStarted?.Invoke();

        Debug.Log($"[AssessmentIntroPanel] {modeId} assessment started.");
    }

    // ==================================================================

    private void EnableManagers()
    {
        foreach (var mb in beginOnStart)
        {
            if (mb == null) continue;
            mb.enabled = true;
        }
    }

    /// <summary>
    /// A manager left ticked runs its Start() during scene load, before this ever draws the
    /// briefing - so the clock is already going while the candidate reads. It looks fine and
    /// quietly costs them time, which is the worst kind of bug in a graded exercise.
    /// </summary>
    private void WarnIfManagersAlreadyRunning()
    {
        foreach (var mb in beginOnStart)
        {
            if (mb == null || !mb.enabled) continue;

            Debug.LogError($"[AssessmentIntroPanel] '{mb.GetType().Name}' on '{mb.name}' is ENABLED. " +
                           "Its Start() has already run, so the assessment began before the briefing " +
                           "was shown and the timer is counting. Untick that component in the " +
                           "Inspector - this panel ticks it when Start is pressed.", mb);
        }

        if (beginOnStart == null || beginOnStart.Length == 0)
            Debug.LogWarning("[AssessmentIntroPanel] Begin On Start is empty, so pressing Start " +
                             "will show the scene but begin nothing.", this);
    }

    private string BuildObjectives()
    {
        if (objectives == null || objectives.Length == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < objectives.Length; i++)
        {
            if (string.IsNullOrEmpty(objectives[i])) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(bullet).Append(objectives[i]);
        }

        return sb.ToString();
    }
}