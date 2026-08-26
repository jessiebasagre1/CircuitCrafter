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

    [Header("Step View — what to show while using a machine")]
    [Tooltip("The GameObject containing the 30 step panels AND the step HUD (score, progress, hint). Hidden while walking, shown while at a machine.")]
    public GameObject stepUIRoot;

    [Tooltip("The player's normal 3D camera.")]
    public Camera playerCamera;

    [Tooltip("Movement / look scripts to disable while the player is using a machine.")]
    public MonoBehaviour[] playerControlScripts;

    [Tooltip("Key that leaves the machine and returns to walking.")]
    public KeyCode exitKey = KeyCode.Escape;

    [Header("Mobile")]
    [Tooltip("Joystick, touch-look field, interact button — hidden while using a machine.")]
    public GameObject[] mobileControlsToHide;

    [Tooltip("Controls shown ONLY while using a machine, e.g. an on-screen Exit button.")]
    public GameObject[] mobileStationControls;

    [Header("Dialogs (optional — generated if left empty)")]
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

        if (InStepView && Input.GetKeyDown(exitKey))
            ExitStepView();
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
#if UNITY_ANDROID || UNITY_IOS
        // Nothing to lock on touch, and locking it in the Editor breaks on-screen controls.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
#else
        Cursor.lockState = inStepView ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = inStepView;
#endif
    }

    // ==================================================================
    // Waypoint + dialogs
    // ==================================================================

    private void PointWaypointAt(COC3MachineType m)
    {
        if (!useWaypoint || WaypointIndicator.Instance == null) return;

        var station = StationFor(m);
        if (station != null) WaypointIndicator.Instance.SetTarget(station.WaypointTarget);
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
            DialogManager.Instance.ShowDialog(seq);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RebuildClientStepSet();
    }
#endif
}