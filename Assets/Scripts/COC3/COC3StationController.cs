using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns COC 3 from a menu of 30 panels into a job you walk through.
///
/// ATTACH THIS TO: the same GameObject as your COC3Manager.
///
/// WHAT IT DOES
///   - Decides which machine each step belongs to (Server or Client).
///   - Refuses to open the step UI at the wrong computer, and says why.
///   - Swaps to a close-up camera and disables player movement while the learner is
///     using a machine, then hands control back when they step away - the same pattern
///     CrimpingViewController uses for the crimping station.
///   - Points the waypoint arrow at the machine the current step needs.
///   - Plays a dialog telling the learner where to go whenever that changes.
///
/// WHY THE STEP PANELS DO NOT MOVE
/// The 30 panels stay exactly where they are, under one Canvas. This script simply hides
/// that whole UI root while the player is walking around, and shows it when they sit down
/// at the right machine. COC3Manager keeps running underneath, unaware and unchanged -
/// it still owns step order, validation and scoring.
///
/// (That means a step panel is often active while its parent UI root is switched off.
/// Harmless with the lazy-CanvasGroup version of ScreenshotStep; it would throw a
/// NullReferenceException with the original.)
/// </summary>
public class COC3StationController : MonoBehaviour
{
    public static COC3StationController Instance { get; private set; }

    [Header("References")]
    [Tooltip("Leave empty to use the COC3Manager on this same GameObject.")]
    public COC3Manager manager;

    [Tooltip("The COC3Workstation on your Server PC object.")]
    public COC3Workstation serverStation;

    [Tooltip("The COC3Workstation on your Client PC object.")]
    public COC3Workstation clientStation;

    [Header("Which steps happen on the CLIENT")]
    [Tooltip("Step NUMBERS (1-based, as the learner sees them) performed on the client PC. Every other step is on the server. Defaults are the five test steps: 15 TestClientAccess, 18 TestPrintFromClient, 22 TestRedirectionClient, 25 ConnectFromClient, 26 VerifyRemoteAccess.")]
    public int[] clientStepNumbers = { 15, 18, 22, 25, 26 };

    [Header("Step View � what to show while using a machine")]
    [Tooltip("The GameObject containing the 30 step panels AND the step HUD (score, progress, hint). Hidden while walking, shown while at a machine.")]
    public GameObject stepUIRoot;

    [Tooltip("The player's normal 3D camera.")]
    public Camera playerCamera;

    [Tooltip("Movement / look scripts to disable while the player is using a machine.")]
    public MonoBehaviour[] playerControlScripts;

    [Tooltip("Key that leaves the machine and returns to walking.")]
    public KeyCode exitKey = KeyCode.Escape;

    [Header("Mobile")]
    [Tooltip("Joystick, touch-look field, interact button � hidden while using a machine.")]
    public GameObject[] mobileControlsToHide;

    [Tooltip("Controls shown ONLY while using a machine, e.g. an on-screen Exit button.")]
    public GameObject[] mobileStationControls;

    [Header("Dialogs (optional � generated if left empty)")]
    [Tooltip("Played when the next step moves to the SERVER.")]
    public DialogSequence goToServerDialog;

    [Tooltip("Played when the next step moves to the CLIENT.")]
    public DialogSequence goToClientDialog;

    [Tooltip("Played when the learner presses E on the machine the current step does NOT need.")]
    public DialogSequence wrongMachineDialog;

    [Tooltip("Speaker name used on generated dialogs.")]
    public string speakerName = "Instructor";

    [Header("Waypoint")]
    [Tooltip("Point the COC1 waypoint arrow at whichever machine the current step needs.")]
    public bool useWaypoint = true;

    [Tooltip("Hide the arrow while the player is actually using a machine.")]
    public bool hideWaypointWhileUsing = true;

    // --- runtime state -------------------------------------------------
    public bool InStepView { get; private set; }
    public COC3Workstation CurrentStation { get; private set; }
    public COC3MachineType RequiredMachine { get; private set; } = COC3MachineType.Server;

    private ScreenshotStep activeStep;
    private int activeIndex = -1;
    private bool activityFinished;
    private bool waypointComplaintMade;

    /// <summary>Frame on which the learner last left a step view, so the same keypress cannot sit
    /// them straight back down. -1 means never.</summary>
    private int leftOnFrame = -1;

    /// <summary>Reusable asset-less sequences for the generated dialogs, so we are not
    /// allocating a ScriptableObject every time the learner changes machines.</summary>
    private DialogSequence generatedGoTo;
    private DialogSequence generatedWrong;

    private readonly HashSet<int> clientSteps = new HashSet<int>();

    // ==================================================================

    private void Awake()
    {
        Instance = this;
        if (manager == null) manager = GetComponent<COC3Manager>();

        generatedGoTo = MakeRuntimeSequence();
        generatedWrong = MakeRuntimeSequence();

        RebuildClientStepSet();
        SetStepViewActive(false, null);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (generatedGoTo != null) Destroy(generatedGoTo);
        if (generatedWrong != null) Destroy(generatedWrong);
    }

    private static DialogSequence MakeRuntimeSequence()
    {
        var s = ScriptableObject.CreateInstance<DialogSequence>();
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    private void Start()
    {
        // Start, not Awake. Instance is set in WaypointIndicator's own Awake, and script execution
        // order between two plain MonoBehaviours is not defined - asking in Awake would sometimes
        // report a missing arrow that is perfectly fine.
        if (useWaypoint && WaypointIndicator.Instance == null)
            ComplainAboutWaypointOnce(DescribeMissingIndicator());

        COC3WaypointBridge bridge = FindBridge();

        // Both at once is two markers hovering over the same PC, and only one of them responds to
        // anything - a genuinely baffling thing to debug by eye.
        if (useWaypoint && bridge != null)
            Debug.LogWarning("[COC3StationController] Use Waypoint is ON and there is also a " +
                             "COC3WaypointBridge in this scene. Both will point at the same machine " +
                             "and you will see two arrows. Untick Use Waypoint - the bridge is the " +
                             "newer of the two.", this);

        // THE ONE THAT ACTUALLY COST A DAY.
        //
        // The bridge and TaskWaypointArrow both do their registering in Awake, and AWAKE DOES NOT
        // RUN ON AN INACTIVE GAMEOBJECT. Leave the manager object switched off in the Hierarchy and
        // neither one ever starts: no arrow, no label, no validation, and - the part that makes it
        // so hard to find - NOT ONE LINE IN THE CONSOLE, because the components that would have
        // complained are the components that never ran.
        //
        // This controller is active, so it is the one thing in the scene that can still say it.
        //
        // The manager object is meant to stay ON for the whole module. Nothing is gained by hiding
        // it: TaskWaypointArrow already switches the arrow MODEL off by itself whenever there is
        // nothing to point at or a station view is open, and that model is a separate object at the
        // scene root. Switching the manager off instead does not hide the arrow earlier - it stops
        // the thing that does the hiding.
        // FOUR SEPARATE TICKBOXES, ALL OF WHICH LOOK THE SAME FROM OUTSIDE.
        //
        // An earlier version of this check only asked whether the GameObject was active, and that
        // was not enough. A DISABLED COMPONENT on an ACTIVE object behaves almost - but not quite -
        // like an inactive object: Awake still runs, so the arrow registers its Instance and looks
        // present to everything that asks; Start and LateUpdate do not, so it never validates
        // itself, never points at anything, and never prints a word. From the Console the two are
        // indistinguishable, and the check said nothing about the second one.
        //
        // So report the state of all four boxes at once and let the reader see which is wrong,
        // rather than testing them one at a time across several Play sessions.
        if (bridge != null) ReportWaypointObjectIfBroken(bridge);
    }

    private void ReportWaypointObjectIfBroken(COC3WaypointBridge bridge)
    {
        var arrow = bridge.GetComponent<CSSSimulation.Networking.TaskWaypointArrow>();

        bool objectOn = bridge.gameObject.activeInHierarchy;
        bool bridgeOn = bridge.enabled;
        bool arrowHere = arrow != null;
        bool arrowOn = arrowHere && arrow.enabled;

        if (objectOn && bridgeOn && arrowHere && arrowOn) return;   // nothing to say

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[COC3StationController] The waypoint arrow cannot run. State of " +
                      $"'{PathOf(bridge.transform)}':");
        sb.AppendLine($"    GameObject active        : {(objectOn ? "yes" : "NO   <- tick the box beside the object's name")}");
        sb.AppendLine($"    COC3WaypointBridge on    : {(bridgeOn ? "yes" : "NO   <- tick the box beside the component")}");
        sb.AppendLine($"    TaskWaypointArrow here   : {(arrowHere ? "yes" : "NO   <- the bridge expects it on this same object")}");
        if (arrowHere)
            sb.AppendLine($"    TaskWaypointArrow on     : {(arrowOn ? "yes" : "NO   <- tick the box beside the component")}");

        if (!objectOn)
        {
            // Name the ancestor that is actually switched off. "It looks ticked to me" is almost
            // always a parent three levels up, and hunting for it by hand is tedious.
            Transform culprit = bridge.transform;
            while (culprit != null && culprit.gameObject.activeSelf) culprit = culprit.parent;
            if (culprit != null && culprit != bridge.transform)
                sb.AppendLine($"    The object switched off is actually '{PathOf(culprit)}', above it.");
        }
        else if (!arrowOn && arrowHere)
        {
            sb.AppendLine("    NOTE: with the object active but the component unticked, Awake still");
            sb.AppendLine("    runs - so the arrow registers and looks present - while Start and");
            sb.AppendLine("    LateUpdate do not. That is why there is no setup line from it and why");
            sb.AppendLine("    'Report waypoints' says the component has never run.");
        }

        Debug.LogError(sb.ToString(), bridge.gameObject);
    }

    /// <summary>Whether the newer TaskWaypointArrow route is set up, which makes this component's
    /// own waypoint the one that is meant to be switched off.</summary>
    private bool HasWaypointBridge() { return FindBridge() != null; }

    /// <summary>Inactive included, deliberately - an inactive bridge is the failure this is looking
    /// for, not a reason to report that there is no bridge.</summary>
    private COC3WaypointBridge FindBridge()
    {
        return FindAnyObjectByType<COC3WaypointBridge>(FindObjectsInactive.Include);
    }

    private void RebuildClientStepSet()
    {
        clientSteps.Clear();
        if (clientStepNumbers == null) return;
        foreach (int n in clientStepNumbers) clientSteps.Add(n);
    }

    private void Update()
    {
        if (manager == null || manager.steps == null) return;

        ScreenshotStep current = FindActiveStep();
        int index = current == null ? -1 : manager.steps.IndexOf(current);

        if (index != activeIndex || current != activeStep)
        {
            OnStepChanged(current, index);
            return;
        }

        // TypingGuard is what makes a LETTER safe as the exit key.
        //
        // Every step in COC 3 is a Windows dialog with fields in it, and the learner types SERVER01,
        // css.local, Administrator. Input.GetKeyDown reads the physical keyboard and knows nothing
        // about the text field receiving the same keystroke, so without this the E in SERVER01 would
        // stand them up mid-word - losing the dialog they were halfway through filling in.
        //
        // Costs one component lookup, and only on a frame where the exit key was actually pressed.
        if (InStepView && Input.GetKeyDown(exitKey) && !TypingGuard.IsTyping)
        {
            ExitStepView();

            // Escape is shared with PauseMenuController, which would otherwise open the pause menu
            // on the same press. Claiming it here is what stops that - PauseMenuController asks in
            // LateUpdate, after this, and stands aside when the key is already spoken for.
            //
            // Only for Escape: a scene that sets Exit Key to something else has no conflict to
            // resolve, and claiming a key nobody shares would be a lie.
            if (exitKey == KeyCode.Escape) EscapeKeyClaim.Claim();
        }
    }

    private ScreenshotStep FindActiveStep()
    {
        for (int i = 0; i < manager.steps.Count; i++)
        {
            var s = manager.steps[i];
            if (s != null && s.gameObject.activeInHierarchy) return s;
        }
        return null;
    }

    // ==================================================================
    // Step routing
    // ==================================================================

    private void OnStepChanged(ScreenshotStep newStep, int index)
    {
        activeStep = newStep;
        activeIndex = index;

        // newStep == null means the completion screen is up. Keep the learner seated so
        // they can read the report rather than kicking them out to walk around.
        if (newStep == null)
        {
            activityFinished = true;
            ClearWaypoint();
            return;
        }

        activityFinished = false;

        COC3MachineType needed = MachineForStep(index);
        bool machineChanged = needed != RequiredMachine;
        RequiredMachine = needed;

        if (InStepView && CurrentStation != null && CurrentStation.machine != needed)
        {
            // The work has moved to the other computer - stand the learner up and send
            // them over. This is the moment the whole system exists for.
            ExitStepView();
            Announce(needed, machineChangedFromUse: true);
            return;
        }

        if (!InStepView)
        {
            PointWaypointAt(needed);
            if (machineChanged) Announce(needed, machineChangedFromUse: false);
        }
    }

    /// <summary>Server unless the step's 1-based number is listed in clientStepNumbers.</summary>
    public COC3MachineType MachineForStep(int zeroBasedIndex)
    {
        int stepNumber = zeroBasedIndex + 1;
        return clientSteps.Contains(stepNumber) ? COC3MachineType.Client : COC3MachineType.Server;
    }

    private COC3Workstation StationFor(COC3MachineType m) =>
        m == COC3MachineType.Server ? serverStation : clientStation;

    // ==================================================================
    // Entering / leaving a machine
    // ==================================================================

    /// <summary>Called by COC3Workstation when the player presses E on it.</summary>
    public void TryEnter(COC3Workstation station)
    {
        if (station == null || InStepView) return;

        // THE SAME PRESS MUST NOT PUT THEM STRAIGHT BACK IN.
        //
        // This only became possible when Exit Key moved to E, because E is also the key
        // PlayerInteractor uses to interact - and the learner is standing at the monitor, well
        // inside its trigger, at the exact moment they leave it. So one press is seen twice: this
        // controller's Update stands them up, and PlayerInteractor's Update, on the same frame, sees
        // an interactable in range and sits them back down. The screen flickers and nothing appears
        // to happen.
        //
        // Which of the two runs first is not defined for two scripts at execution order 0, so the
        // guard is a frame number rather than an ordering assumption: whichever order they run in,
        // the re-entry is refused for the rest of that frame. GetKeyDown is true for one frame only,
        // so nothing legitimate is ever blocked - the learner's next press works normally.
        if (leftOnFrame == Time.frameCount) return;

        if (!activityFinished && station.machine != RequiredMachine)
        {
            ShowWrongMachine(station);
            return;
        }

        EnterStepView(station);
    }

    public void EnterStepView(COC3Workstation station)
    {
        if (InStepView) return;
        SetStepViewActive(true, station);
    }

    /// <summary>Wire an on-screen Exit button here for mobile.</summary>
    public void ExitStepView()
    {
        if (!InStepView) return;

        // Stamped before anything else, so it is set no matter which path called this - the exit
        // key, the on-screen mobile button, or a step moving to the other machine.
        leftOnFrame = Time.frameCount;

        SetStepViewActive(false, null);

        if (!activityFinished) PointWaypointAt(RequiredMachine);
    }

    private void SetStepViewActive(bool active, COC3Workstation station)
    {
        InStepView = active;
        CurrentStation = active ? station : null;

        // Cameras. The station camera is optional - without one the player camera simply
        // stays put, which is fine if your PCs are framed head-on already.
        if (playerCamera != null) playerCamera.gameObject.SetActive(!active);

        if (station != null && station.stationCamera != null)
            station.stationCamera.gameObject.SetActive(active);
        else if (!active && playerCamera != null)
            playerCamera.gameObject.SetActive(true);

        // Make sure we never leave the OTHER station's camera running.
        if (serverStation != null && serverStation.stationCamera != null && serverStation != station)
            serverStation.stationCamera.gameObject.SetActive(false);
        if (clientStation != null && clientStation.stationCamera != null && clientStation != station)
            clientStation.stationCamera.gameObject.SetActive(false);

        foreach (var script in playerControlScripts)
            if (script != null) script.enabled = !active;

        if (stepUIRoot != null) stepUIRoot.SetActive(active);

        foreach (var obj in mobileControlsToHide)
            if (obj != null) obj.SetActive(!active);

        foreach (var obj in mobileStationControls)
            if (obj != null) obj.SetActive(active);

        if (useWaypoint && hideWaypointWhileUsing && active) ClearWaypoint();

        ApplyCursorMode(active);
    }

    /// <summary>
    /// Free the cursor inside a step view, capture it again for walking - on desktop only.
    ///
    /// WHY THE PLATFORM GUARD
    /// This method is called from Awake() with active=false, so on a mobile target it used to
    /// lock the cursor the instant the scene opened. On a phone that is harmless, because
    /// Cursor.lockState does nothing there. In the EDITOR it is not harmless: the Editor is a
    /// desktop application whatever the build target, so the lock takes effect - and a locked
    /// cursor makes Input.mousePosition stop tracking and report the screen centre.
    ///
    /// The EventSystem feeds that position to the raycaster, so EVERY UI click lands dead
    /// centre of the screen. A virtual joystick in the corner can never be reached, which
    /// reads as "the joystick is broken" rather than "the pointer is pinned". Opening the
    /// pause menu appears to fix it only because that unlocks the cursor.
    ///
    /// MouseLook.Start() already guards its own lock exactly this way. This brings the two
    /// into line, so switching the build target to Android is enough to test the touch
    /// controls with a mouse.
    /// </summary>
    private static void ApplyCursorMode(bool inStepView)
    {
        // THIS IS WHY THE CURSOR UNLOCKED ON EVERY EXIT.
        //
        // The old body was wrapped in #if UNITY_ANDROID || UNITY_IOS, and UNITY_ANDROID is defined
        // in the EDITOR whenever the build target is Android - which this project's is. So the
        // branch that ran was not the desktop one. It was:
        //
        //     Cursor.lockState = CursorLockMode.None;
        //     Cursor.visible = true;
        //
        // UNCONDITIONALLY, ignoring inStepView entirely. Leaving a station therefore freed the
        // cursor instead of capturing it, every single time, by both the exit key and the automatic
        // machine change. Nothing "broke" it afterwards - it was never locked in the first place.
        //
        // THE GUARD WAS NOT WRONG, IT WAS ASKING THE WRONG QUESTION. What it wanted to avoid is real:
        // capturing the cursor while on-screen touch controls are up pins Input.mousePosition to the
        // screen centre, so the joystick can never be reached. But that is a question about what is
        // on screen RIGHT NOW, and a compile-time platform symbol cannot answer it - an Android build
        // target being selected says nothing about whether this session is a phone or a mouse.
        //
        // CursorControl asks it at runtime instead: SuspendCapture, Application.isMobilePlatform, or
        // a VirtualJoystick actually visible. In the Editor with an Android target and the touch
        // canvas hidden, all three are false and the cursor captures properly. On a real device
        // isMobilePlatform is true and it is left alone. Same code, right answer in both places -
        // and it is this project's own helper, already used by IntroDialogTrigger and
        // SettingsPanelPresence.
        CursorControl.SetCaptured(!inStepView);
    }

    // ==================================================================
    // Waypoint + dialogs
    // ==================================================================

    private void PointWaypointAt(COC3MachineType m)
    {
        // Every one of these was a silent return. "No arrow in COC 3" is the same three words
        // whether the indicator is missing, switched off, or pointed at a station that was never
        // assigned - and none of them printed anything, so there was nothing to go on.
        // Unticked is the CORRECT setting when COC3WaypointBridge is driving TaskWaypointArrow
        // instead, so that case is not a fault and says nothing. Nagging about a box the setup
        // guide told you to untick is how a useful warning becomes one people learn to ignore.
        if (!useWaypoint)
        {
            if (!HasWaypointBridge())
                ComplainAboutWaypointOnce("Use Waypoint is unticked on this component, and there is " +
                                          "no COC3WaypointBridge in the scene to drive the newer " +
                                          "TaskWaypointArrow instead. Nothing is pointing anywhere.");
            return;
        }

        if (WaypointIndicator.Instance == null)
        {
            ComplainAboutWaypointOnce(DescribeMissingIndicator());
            return;
        }

        var station = StationFor(m);
        if (station == null)
        {
            ComplainAboutWaypointOnce($"The {(m == COC3MachineType.Server ? "Server" : "Client")} " +
                                      "Station field is empty, so there is no machine to point at.");
            return;
        }

        WaypointIndicator.Instance.SetTarget(station.WaypointTarget);
    }

    /// <summary>
    /// Why there is no WaypointIndicator.Instance - which is a different question from whether
    /// there is a WaypointIndicator.
    ///
    /// Instance is set in Awake, and AWAKE DOES NOT RUN ON AN INACTIVE GAMEOBJECT. An arrow left
    /// switched off in the scene - or parented under an empty that is switched off - therefore
    /// never registers, and nothing ever switches it on, because the only thing that would is
    /// reached through the Instance it never set. It cannot recover on its own, and from the
    /// outside it looks exactly like no arrow being in the scene at all.
    ///
    /// So this looks for one INCLUDING inactive objects and says which of the two it is.
    /// </summary>
    private string DescribeMissingIndicator()
    {
        var found = FindObjectsByType<WaypointIndicator>(FindObjectsInactive.Include,
                                                         FindObjectsSortMode.None);

        if (found == null || found.Length == 0)
            return "There is no WaypointIndicator in this scene. Copy the arrow object from COC 1 " +
                   "(a GameObject with a SpriteRenderer and a WaypointIndicator component) into COC 3.";

        var sb = new System.Text.StringBuilder();
        sb.Append($"There {(found.Length == 1 ? "is a WaypointIndicator" : $"are {found.Length} WaypointIndicators")} " +
                  "in this scene, but none of them registered:");

        foreach (var w in found)
        {
            if (w == null) continue;
            sb.Append($"\n    '{PathOf(w.transform)}' - ");
            sb.Append(!w.gameObject.activeInHierarchy
                ? "its GameObject is INACTIVE, so its Awake never ran and it never set Instance. " +
                  "Switch it on in the Hierarchy (and check no parent above it is switched off) - " +
                  "it hides itself on the first frame anyway."
                : !w.enabled
                    ? "the component itself is unticked."
                    : "active and enabled, which should have worked - check the Console for an " +
                      "earlier error in its Awake.");
        }

        return sb.ToString();
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    /// <summary>Once per run. This is called from step changes, so left unguarded it would be one
    /// line per frame.</summary>
    private void ComplainAboutWaypointOnce(string reason)
    {
        if (waypointComplaintMade) return;
        waypointComplaintMade = true;

        Debug.LogWarning("[COC3StationController] The waypoint arrow is not being shown.\n  " +
                         reason +
                         "\n\nRight-click this component > Report waypoint for the full picture.", this);
    }

    /// <summary>
    /// The whole waypoint chain in one message: the switch, the indicator, the two stations, and
    /// what is being pointed at right now.
    /// </summary>
    [ContextMenu("Report waypoint")]
    public void ReportWaypoint()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[COC3StationController] Waypoint report");
        COC3WaypointBridge bridgeComp = FindBridge();
        bool bridge = bridgeComp != null;

        sb.AppendLine($"  Route                   : {(bridge ? "COC3WaypointBridge -> TaskWaypointArrow (the newer one). Right-click THAT component for its own report." : "this component -> WaypointIndicator (the COC1 sprite arrow)")}");

        if (bridge)
            sb.AppendLine($"  Bridge object           : '{PathOf(bridgeComp.transform)}' - " +
                          (bridgeComp.gameObject.activeInHierarchy
                              ? "active"
                              : "INACTIVE  <- nothing on it has run its Awake. This alone is enough to explain no arrow."));
        sb.AppendLine($"  Use Waypoint            : {(useWaypoint ? (bridge ? "ON  <- untick it, the bridge is driving the other arrow" : "ON") : (bridge ? "OFF  (correct - the bridge is driving the other arrow)" : "OFF  <- nothing will ever be pointed at"))}");
        sb.AppendLine($"  Hide While Using        : {hideWaypointWhileUsing}");
        sb.AppendLine($"  WaypointIndicator       : {(WaypointIndicator.Instance != null ? "registered, '" + PathOf(WaypointIndicator.Instance.transform) + "'" : "NOT REGISTERED")}");

        if (WaypointIndicator.Instance == null)
            sb.AppendLine("      " + DescribeMissingIndicator().Replace("\n", "\n      "));

        sb.AppendLine($"  Server Station          : {(serverStation != null ? serverStation.name + "  -> points at '" + serverStation.WaypointTarget.name + "'" : "NOT ASSIGNED")}");
        sb.AppendLine($"  Client Station          : {(clientStation != null ? clientStation.name + "  -> points at '" + clientStation.WaypointTarget.name + "'" : "NOT ASSIGNED")}");
        sb.AppendLine($"  Required machine now    : {RequiredMachine}");
        sb.AppendLine($"  In step view            : {InStepView}{(InStepView && hideWaypointWhileUsing ? "   <- the arrow is hidden ON PURPOSE while you are sitting at a machine" : "")}");
        sb.AppendLine($"  Activity finished       : {activityFinished}{(activityFinished ? "   <- all steps done, so the arrow is cleared" : "")}");
        sb.AppendLine($"  Main camera             : {(Camera.main != null ? Camera.main.name : "NONE - nothing is tagged MainCamera, and the indicator needs one to billboard")}");

        Debug.LogWarning(sb.ToString(), this);
    }

    private void ClearWaypoint()
    {
        if (useWaypoint && WaypointIndicator.Instance != null)
            WaypointIndicator.Instance.ClearTarget();
    }

    private void Announce(COC3MachineType m, bool machineChangedFromUse)
    {
        PointWaypointAt(m);

        var authored = m == COC3MachineType.Server ? goToServerDialog : goToClientDialog;
        if (authored != null) { Play(authored); return; }

        var station = StationFor(m);
        string where = station != null ? station.displayName
                                       : (m == COC3MachineType.Server ? "Server PC" : "Client PC");

        string body = machineChangedFromUse
            ? $"That part is done here. The next step is performed on the <b>{where}</b> - " +
              $"walk over and press E to continue."
            : $"This step is performed on the <b>{where}</b>. Follow the arrow and press E when you get there.";

        SetLine(generatedGoTo, body);
        Play(generatedGoTo);
    }

    private void ShowWrongMachine(COC3Workstation station)
    {
        if (wrongMachineDialog != null) { Play(wrongMachineDialog); return; }

        var needed = StationFor(RequiredMachine);
        string neededName = needed != null ? needed.displayName
                                           : (RequiredMachine == COC3MachineType.Server ? "Server PC" : "Client PC");

        SetLine(generatedWrong,
            $"Nothing to do on the {station.displayName} yet.\n\n" +
            $"The current step is performed on the <b>{neededName}</b>.");

        Play(generatedWrong);
    }

    private void SetLine(DialogSequence seq, string body)
    {
        seq.lines.Clear();
        seq.lines.Add(new DialogLine { speakerName = speakerName, text = body });
    }

    private void Play(DialogSequence seq)
    {
        if (DialogManager.Instance == null)
        {
            Debug.LogWarning("[COC3StationController] No DialogManager in this scene - " +
                             "the learner will get no guidance about which computer to use.", this);
            return;
        }

        // Never stack a second ShowDialog on top of one already open - it clears the
        // queue and would swallow whatever was mid-read.
        if (!DialogManager.Instance.IsDialogOpen)
            DialogManager.Instance.ShowDialog(seq, RestoreCursorAfterDialog);
    }

    /// <summary>
    /// Puts the cursor back the way this controller wants it, once the "go to the other machine"
    /// dialog has been dismissed.
    ///
    /// WHY THE AUTOMATIC EXIT NEEDED THIS AND THE MANUAL ONE DID NOT
    /// Leaving by pressing the exit key ends at SetStepViewActive, which locks the cursor, and
    /// nothing runs afterwards. The automatic exit - the one that happens when the next step moves
    /// to the other machine - does the same and then opens a dialog. Whatever that dialog's panel
    /// does to the cursor so its Continue can be clicked happens AFTER the lock, so the lock is the
    /// state that gets overwritten rather than the one that survives.
    ///
    /// Re-applying on the dialog's completion callback is the ordering that actually holds: last
    /// word, after the panel has finished with it.
    ///
    /// CursorKeeper would also catch this within a frame, but only where it is installed and only
    /// once nothing else claims the cursor. This makes the module correct on its own.
    /// </summary>
    private void RestoreCursorAfterDialog()
    {
        ApplyCursorMode(InStepView);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RebuildClientStepSet();
    }
#endif
}