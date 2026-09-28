using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
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
///
/// KEEPING START CLICKABLE (all on by default, nothing to wire)
///   Hold Player   - PlayerInputLock + CursorKeeper.Claim while the briefing is up. Freeing the
///                   cursor without claiming it lost to CursorKeeper every frame in COC 3, whose
///                   MouseLook was still on: the cursor stayed pinned to the centre.
///   Keep On Top   - the briefing is sorted above every other screen canvas.
///   Enter Starts  - Enter presses Start on PC.
///   Report Missed Clicks - a click on Start that does not arrive is explained in the Console.
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
    [Tooltip("GUIDANCE: assign this scene's Job Order and the briefing shows its ASSESSMENT reading - the " +
             "scenario, the required outcome and the specifications - instead of the Title and Objectives " +
             "below. Recommended: an objectives list written as steps ('Strip the jacket... Arrange the " +
             "wires...') tells the candidate the procedure being assessed.")]
    public CircuitCrafter.Guidance.JobOrderData jobOrder;

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

    [Header("Keeping Start clickable")]
    [Tooltip("Hold the player for as long as the briefing is up, the way the project's other panels " +
             "do: PlayerInputLock is held (no walking, no mouse look) and CursorKeeper is told the " +
             "cursor is claimed, so it does not lock it again underneath the briefing. Released when " +
             "Start is pressed.\n\n" +
             "LEAVE THIS ON. Without it, in a scene whose MouseLook is not in Freeze Until Start, " +
             "CursorKeeper locks the cursor every frame and the Start button cannot be reached.")]
    public bool holdPlayer = true;

    [Tooltip("While the briefing is up, draw it above every other screen canvas, so that no full-screen " +
             "panel on another canvas can sit in front of the Start button.")]
    public bool keepOnTop = true;

    [Tooltip("Enter also presses Start (PC).")]
    public bool enterStarts = true;

    [Tooltip("When a click lands on the Start button but does not reach it, say in the Console what " +
             "took it: a locked cursor, a panel in front, a raycaster that is off. Silent otherwise.")]
    public bool reportMissedClicks = true;

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

    private bool holding;
    private float readyAt = float.MaxValue;
    private Canvas ownCanvas;
    private int reportsLeft = 3;
    private readonly List<RaycastResult> hits = new List<RaycastResult>();

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
        if (!IsShowing) return;

        if (manageCursor)
        {
            CursorControl.Free();
            ListWithCursorArbiter();
        }

        // Again, now that every other canvas has run its own Awake - some raise their order there.
        BringToFront();

        // A short grace period, so an Enter still held from the menu does not skip the briefing.
        readyAt = Time.unscaledTime + 0.3f;
    }

    private void OnEnable()
    {
        if (IsShowing) Hold();
    }

    private void OnDisable()
    {
        // Never leave the player frozen behind a briefing that has gone away.
        Release();
    }

    private void Update()
    {
        if (!IsShowing) return;

        if (enterStarts && Time.unscaledTime >= readyAt &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            StartAssessment();
            return;
        }

        if (reportMissedClicks && Input.GetMouseButtonDown(0))
            ReportMissedClick(Input.mousePosition);
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

        if (titleText != null) titleText.text = jobOrder != null ? jobOrder.Title(true) : title;
        if (objectivesText != null)
            objectivesText.text = jobOrder != null ? jobOrder.BuildText(true, false) : BuildObjectives();

        if (rulesText != null)
        {
            rulesText.text = rules;
            rulesText.gameObject.SetActive(!string.IsNullOrEmpty(rules));
        }

        foreach (var go in hideUntilStart) if (go != null) go.SetActive(false);
        foreach (var mb in freezeUntilStart) if (mb != null) mb.enabled = false;

        if (manageCursor) CursorControl.Free();
        Hold();

        if (panel != null) panel.SetActive(true);
        BringToFront();

        IsShowing = true;
        readyAt = Time.unscaledTime + 0.5f;
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

        Release();
        if (manageCursor) CursorControl.Capture();

        // LAST. Enabling the manager runs its Start(), which starts the timer - so everything
        // the run needs must already be back on by the time this line executes, or the first
        // second is spent with the HUD still hidden and the player still frozen.
        EnableManagers();

        OnStarted?.Invoke();

        Debug.Log($"[AssessmentIntroPanel] {modeId} assessment started.");
    }

    // ==================================================================
    // Keeping Start clickable
    // ==================================================================

    /// <summary>
    /// Holds the player and claims the cursor from CursorKeeper for as long as the briefing is up.
    ///
    /// CursorKeeper runs after everything else in the frame and locks the cursor whenever a
    /// MouseLook is switched on and nothing has claimed the cursor. Freeing the cursor here without
    /// claiming it only lasts until the keeper's turn, so the cursor ends every frame locked and
    /// pinned to the centre of the screen, and the Start button can never be reached. Holding the
    /// player also stops the camera turning while the mouse moves over the briefing, even when
    /// MouseLook is missing from Freeze Until Start.
    /// </summary>
    private void Hold()
    {
        if (!holdPlayer || holding) return;
        holding = true;
        PlayerInputLock.Acquire();
        CursorKeeper.Claim();
    }

    private void Release()
    {
        if (!holding) return;
        holding = false;
        PlayerInputLock.Release();
        CursorKeeper.Release();
    }

    /// <summary>
    /// COC 3's CursorArbiter captures the cursor every frame unless something on its Free While
    /// Active list is showing. Put the briefing on that list, so that nobody has to remember to.
    /// </summary>
    private void ListWithCursorArbiter()
    {
        CursorArbiter arbiter = CursorArbiter.Instance;
        if (arbiter == null || panel == null) return;

        GameObject[] list = arbiter.freeWhileActive;
        if (list != null)
            foreach (GameObject go in list) if (go == panel) return;

        int count = list != null ? list.Length : 0;
        var grown = new GameObject[count + 1];
        if (count > 0) System.Array.Copy(list, grown, count);
        grown[count] = panel;
        arbiter.freeWhileActive = grown;
    }

    /// <summary>
    /// Sorts the briefing above every other screen canvas that is showing.
    ///
    /// The briefing gets its own Canvas with Override Sorting, plus a GraphicRaycaster, because
    /// the buttons on a nested canvas are clicked only through a raycaster on that same canvas.
    /// The briefing is hidden after Start, so none of this is left on screen afterwards.
    /// </summary>
    private void BringToFront()
    {
        if (!keepOnTop || panel == null || !panel.activeInHierarchy) return;

        int highest = HighestOtherSortingOrder();
        if (highest == int.MinValue) return;   // nothing else on screen to get above

        if (ownCanvas == null)
        {
            ownCanvas = panel.GetComponent<Canvas>();
            if (ownCanvas == null)
            {
                Canvas parent = panel.transform.parent != null
                    ? panel.transform.parent.GetComponentInParent<Canvas>() : null;
                ownCanvas = panel.AddComponent<Canvas>();
                if (parent != null) ownCanvas.sortingLayerID = parent.rootCanvas.sortingLayerID;
            }
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
        }

        if (!ownCanvas.isRootCanvas) ownCanvas.overrideSorting = true;

        int target = Mathf.Min(highest + 1, short.MaxValue);
        if (ownCanvas.sortingOrder < target) ownCanvas.sortingOrder = target;
    }

    private int HighestOtherSortingOrder()
    {
        int highest = int.MinValue;

        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.isActiveAndEnabled) continue;
            if (c.renderMode == RenderMode.WorldSpace) continue;
            if (!c.isRootCanvas && !c.overrideSorting) continue;          // drawn with its parent
            if (c.transform.IsChildOf(panel.transform)) continue;         // the briefing itself
            if (c.sortingOrder > highest) highest = c.sortingOrder;
        }

        return highest;
    }

    /// <summary>A click that should have pressed Start and did not: say what happened to it.</summary>
    private void ReportMissedClick(Vector2 position)
    {
        if (reportsLeft <= 0 || startButton == null) return;

        string problem = WhyStartMissed(position);
        if (problem == null) return;

        reportsLeft--;
        Debug.LogWarning("[AssessmentIntroPanel] A click did not reach the Start button: " + problem +
                         (reportsLeft == 0 ? "\n(Last report.)" : ""), this);
    }

    /// <summary>Null when the click reached Start, or was not aimed at it.</summary>
    private string WhyStartMissed(Vector2 position)
    {
        // A captured cursor reports the centre of the screen, whatever the mouse is over.
        if (Cursor.lockState == CursorLockMode.Locked)
            return "the cursor was LOCKED, so the click landed in the centre of the screen. Something " +
                   "locks it every frame after this panel frees it." + CursorLockCulprits();

        if (EventSystem.current == null)
            return "this scene has no EventSystem, so nothing on any canvas can be clicked. " +
                   "Add one: GameObject > UI > Event System.";

        if (!startButton.IsActive())
            return "the Start button is switched off.";

        if (!startButton.IsInteractable())
            return "the Start button is not interactable. Tick Interactable on it, and on any " +
                   "CanvasGroup above it.";

        foreach (CanvasGroup g in startButton.GetComponentsInParent<CanvasGroup>())
        {
            if (!g.blocksRaycasts)
                return "the CanvasGroup on '" + g.name + "' has Blocks Raycasts off, so the Start " +
                       "button cannot be hit. Tick it.";
            if (g.ignoreParentGroups) break;
        }

        var rect = (RectTransform)startButton.transform;
        Canvas canvas = startButton.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? (canvas.worldCamera != null ? canvas.worldCamera : Camera.main) : null;

        if (!RectTransformUtility.RectangleContainsScreenPoint(rect, position, cam))
            return null;   // aimed somewhere else on the screen - nothing to report

        var data = new PointerEventData(EventSystem.current) { position = position };
        hits.Clear();
        EventSystem.current.RaycastAll(data, hits);

        if (hits.Count == 0)
            return "nothing was hit at the Start button. The canvas holding the briefing needs a " +
                   "GraphicRaycaster, and the Start button's Image needs Raycast Target ticked.";

        GameObject front = hits[0].gameObject;
        if (front.transform.IsChildOf(startButton.transform)) return null;   // Start got it

        Canvas frontCanvas = front.GetComponentInParent<Canvas>();
        string where = frontCanvas != null
            ? " (canvas '" + frontCanvas.rootCanvas.name + "', sort order " + frontCanvas.sortingOrder + ")"
            : "";

        return "'" + PathOf(front.transform) + "'" + where + " is in front of it and took the click. " +
               "Hide it while the briefing is up (Hide Until Start), or untick Raycast Target on its Image.";
    }

    private string CursorLockCulprits()
    {
        var sb = new StringBuilder();

        MouseLook look = FindAnyObjectByType<MouseLook>();
        if (look != null && look.isActiveAndEnabled)
            sb.Append("\n  - MouseLook on '").Append(look.name)
              .Append("' is still switched on. Add it to Freeze Until Start.");

        if (!holdPlayer)
            sb.Append("\n  - Hold Player is off, so CursorKeeper locks the cursor while MouseLook is on. Tick it.");

        if (!manageCursor)
            sb.Append("\n  - Manage Cursor is off, so this panel does not free the cursor at all. Tick it.");

        if (sb.Length == 0)
            sb.Append("\n  - Another script sets Cursor.lockState every frame. If the scene has a " +
                      "CursorArbiter, tick its Log Changes to see its reason.");

        return sb.ToString();
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
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