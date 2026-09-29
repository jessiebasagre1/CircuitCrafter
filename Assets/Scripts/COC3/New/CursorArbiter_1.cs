using UnityEngine;

/// <summary>
/// Decides, once per frame, whether the mouse cursor should be free or captured - and hides the
/// world interaction prompt at the same time.
///
/// ATTACH THIS TO: the always-active Managers object, one per gameplay scene.
///
/// WHY IT HAD TO EXIST
/// Fourteen scripts in this project write Cursor.lockState directly - the station controllers in
/// COC 1, 2 and 3, the pause menu, IntroDialogTrigger, QuizCameraController, StoryCompletePanel,
/// COC4AssessmentRun. Each is correct on its own. Together they are a race: whoever writes last
/// wins, and nobody re-asserts anything afterwards.
///
/// That is the bug where leaving a COC 3 station leaves the cursor on screen, and the one where a
/// quiz opens with the cursor still locked. QuizManager leaves the station on the way to showing
/// the quiz, COC3StationController locks the cursor as it stands the learner up, and without a
/// QuizCameraController in the scene nothing frees it again.
///
/// Chasing which script did it is the wrong fix, because the next feature adds a fifteenth writer.
/// This does not try to be another writer. It runs in LateUpdate, after everything else has had
/// its say, reads the REAL cursor state, and corrects it. One authority, asked every frame:
///
///     Is anything on screen that needs a pointer?   free it.
///     No?                                           capture it.
///
/// Because it reads Cursor.lockState rather than trusting a flag, it heals whatever any other
/// script did without any of them being modified.
///
/// BUILT-IN "FREE" CONDITIONS (no list to fill in): the pause menu, a PlayerInputLock (settings,
/// briefing, report), a CursorKeeper claim, a station step view, a dialog, a quiz. Without the
/// pause-menu check this captured the cursor every frame while the menu was open, so the first
/// click on Resume, Retry, Settings or Main Menu locked the cursor instead of pressing the button.
/// </summary>
[DefaultExecutionOrder(1000)]
public class CursorArbiter : MonoBehaviour
{
    public static CursorArbiter Instance { get; private set; }

    [HeaderAttribute("Free the cursor while any of these is on screen")]
    [TooltipAttribute("Panels that need a pointer: the pause menu, the QUIZ VIEW, the assessment " +
             "intro, the report, the completion screen.\n\n" +
             "Anything switched on that the learner has to click belongs here.")]
    public GameObject[] freeWhileActive;

    [TooltipAttribute("Components that mean a pointer is needed while they are enabled. Usually empty.")]
    public Behaviour[] freeWhileEnabled;

    [HeaderAttribute("Built-in conditions")]
    [TooltipAttribute("Left empty, the scene's COC3StationController is found automatically. Assign it " +
             "to be certain - a null here silently drops the step-view condition.")]
    public COC3StationController station;

    [TooltipAttribute("Left empty, the scene's QuizManager is found automatically.")]
    public QuizManager quizManager;

    [TooltipAttribute("Free while a COC 3 station's step view is open - the learner is sitting at " +
             "the PC and using its screen.")]
    public bool freeInStepView = true;

    [TooltipAttribute("Free while a dialog is being read, so its tap-catcher and Next can be clicked.")]
    public bool freeDuringDialog = true;

    [TooltipAttribute("Free while a quiz is on screen.")]
    public bool freeDuringQuiz = true;

    [TooltipAttribute("Free while the pause menu is open (Escape or the Menu button), so Resume, Retry, " +
             "Settings and Main Menu can be clicked. Works without listing the pause panel above.")]
    public bool freeWhilePaused = true;

    [TooltipAttribute("Free while something is holding the player still with PlayerInputLock - the " +
             "settings panel, the assessment briefing, the assessment report. Anything that stops " +
             "the learner walking is showing them something to read or click.")]
    public bool freeWhilePlayerHeld = true;

    [TooltipAttribute("Free while a panel has claimed the cursor through CursorKeeper.Claim() - the AI " +
             "tutor, the assessment briefing. The same rule CursorKeeper follows, so the two never " +
             "disagree about who owns the cursor.")]
    public bool freeWhileClaimed = true;

    [HeaderAttribute("World prompt")]
    [TooltipAttribute("Hide 'Press E to enter station' whenever the cursor is free.\n\n" +
             "The prompt belongs to walking around. While a station, a dialog or a quiz is up, the " +
             "key it names is either swallowed or would do something the learner is not looking at.\n\n" +
             "Uses PlayerInteractor's own SuppressPromptThisFrame, so nothing in it is modified.")]
    public bool suppressWorldPrompt = true;

    [HeaderAttribute("Diagnostics")]
    [TooltipAttribute("Log every change, with the reason. Worth one playthrough if the cursor " +
             "misbehaves - it names what is holding it open.")]
    public bool logChanges;

    private string lastReason = "";

    // ------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;

        if (station == null) station = FindAnyObjectByType<COC3StationController>();
        if (quizManager == null) quizManager = FindAnyObjectByType<QuizManager>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// The reason the cursor should be free, or null when it should be captured.
    ///
    /// Public so WorldPromptSuppressor can ask the same question rather than keeping a second
    /// copy of these conditions that would drift from this one.
    /// </summary>
    public string WhyFree()
    {
        // The pause menu first: it sits on top of everything else, including a step view.
        if (freeWhilePaused && PauseMenuController.AnyPaused)
            return "pause menu";

        if (freeWhilePlayerHeld && PlayerInputLock.IsLocked)
            return "player held (PlayerInputLock)";

        if (freeWhileClaimed && CursorKeeper.ClaimCount > 0)
            return "cursor claimed (CursorKeeper)";

        if (freeInStepView && CurrentStation() != null && CurrentStation().InStepView)
            return "station step view";

        if (freeDuringDialog && DialogManager.Instance != null &&
            DialogManager.Instance.IsDialogOpen)
            return "dialog open";

        if (freeDuringQuiz && CurrentQuiz() != null && CurrentQuiz().IsQuizOpen)
            return "quiz open";

        if (freeWhileActive != null)
            foreach (var go in freeWhileActive)
                if (go != null && go.activeInHierarchy) return go.name;

        if (freeWhileEnabled != null)
            foreach (var b in freeWhileEnabled)
                if (b != null && b.isActiveAndEnabled) return b.GetType().Name;

        return null;
    }

    public bool ShouldFreeCursor()
    {
        return WhyFree() != null;
    }

    // The singletons are assigned in their own Awake, which may run after this one. Re-checking
    // costs nothing and removes an ordering assumption that would fail silently.
    private COC3StationController CurrentStation()
    {
        if (station == null) station = COC3StationController.Instance;
        return station;
    }

    private QuizManager CurrentQuiz()
    {
        if (quizManager == null) quizManager = QuizManager.Instance;
        return quizManager;
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// LateUpdate, not Update. Everything that opens a panel or leaves a station does it during
    /// the frame; running after them is what makes this the last word rather than one more voice.
    /// </summary>
    private void LateUpdate()
    {
        string reason = WhyFree();
        bool free = reason != null;

        if (logChanges && reason != lastReason)
        {
            Debug.Log("[CursorArbiter] " + (free ? "free - " + reason : "captured"), this);
            lastReason = reason;
        }

        if (free)
        {
            // Read the REAL state rather than CursorControl.Captured: another script may have
            // changed it directly, which is the whole reason this component exists.
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                CursorControl.Free();

            return;
        }

        // On a touch device, or with the on-screen joystick up, capture is refused - and the
        // cursor must be left visible rather than half-locked.
        if (CursorControl.CaptureBlocked)
        {
            if (!Cursor.visible) CursorControl.Free();
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
            CursorControl.Capture();
    }
}