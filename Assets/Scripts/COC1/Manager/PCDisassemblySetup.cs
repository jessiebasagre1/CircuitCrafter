using UnityEngine;

/// <summary>
/// Worked example for the PC-building scenario:
/// - Clicking the motherboard shows a "how to disassemble" tutorial (via InteractableObject).
/// - Removing every PCPart reports progress to TaskManager (TaskId).
/// - Reinstalling every part reports progress to TaskManager (AssembleTaskId).
/// - When all parts are removed/reinstalled, a TaskDialogTrigger shows the matching popup.
/// - Once BOTH disassemble and assemble are complete, the waypoint arrow is redirected to
///   the Create Bootable Flashdrive station (see bootableUsbTarget below), the system unit
///   relocates beside it, and the camera snaps straight into the quiz camera (see
///   quizCameraController below)  no separate quiz station/E-press needed.
/// </summary>
public class PCDisassemblySetup : MonoBehaviour
{
    /// <summary>The one in this scene, so a CableConnector can report in without holding a
    /// reference. Same pattern TaskManager and DialogManager use here.</summary>
    public static PCDisassemblySetup Instance { get; private set; }

    [SerializeField] private int totalParts = 6;

    [Header("Cables")]
    [Tooltip("ON: re-plugging a cable counts toward assemble_pc, so the module is not finished - " +
             "and the quiz does not fire - with cables still hanging loose.\n\n" +
             "A PC with every part seated and the PSU leads unplugged is not an assembled PC, and a " +
             "learner who is asked the assembly quiz at that point has been told their work is done " +
             "when it is not.\n\n" +
             "OFF restores the old behaviour, where only parts counted.")]
    [SerializeField] private bool cablesCountTowardAssembly = true;
    public const string TaskId = "disassemble_pc";
    public const string AssembleTaskId = "assemble_pc";

    [Tooltip("Must exactly match this scene's modeId as configured in MainMenuController's " +
             "Game Modes list (e.g. \"COC1\"). Claimed as SaveManager.CurrentGameMode at Start() " +
             "if nothing has set it yet -- covers entering this scene directly (e.g. pressing " +
             "Play on it in the Editor) instead of via the Main Menu, so a checkpoint fired later " +
             "this session doesn't write gameMode: null and break Continue.")]
    [SerializeField] private string gameModeId = "COC1";

    [Header("Waypoint Routing")]
    [Tooltip("Transform of the Create Bootable Flashdrive station (e.g. its StationCameraSwitch " +
             "trigger zone). Once both disassemble_pc and assemble_pc are complete, the waypoint " +
             "arrow is pointed here. Leave empty to skip this hand-off.")]
    [SerializeField] private Transform bootableUsbTarget;

    [Tooltip("This PC's own StationCameraSwitch (the one gated with disassemble_pc/assemble_pc " +
             "in its Gated Task Ids). Once both tasks complete, if the player is still standing " +
             "inside this station's camera view, it's forced back to the player camera right " +
             "away  the station is locked out from here on (see StationCameraSwitch's gate), " +
             "so they shouldn't be left staring at a station they can no longer re-enter.")]
    [SerializeField] private StationCameraSwitch pcStationCameraSwitch;

    [Header("Quiz")]
    [Tooltip("Once both tasks are complete, the camera automatically snaps into the quiz camera " +
             "via QuizCameraController.ActivateQuizCamera()  no station/E-press needed. Leave " +
             "empty to skip this hand-off (e.g. if you trigger the quiz some other way).")]
    [SerializeField] private QuizCameraController quizCameraController;

    [Header("System Unit Relocation")]
    [Tooltip("Root transform of the assembled PC/system unit  the case with all its parts, " +
             "as a single object/hierarchy. Once both tasks are complete, this is moved next to " +
             "the Bootable Flashdrive Creator station so its USB port can be used to plug in the " +
             "freshly-created bootable drive. Leave empty to skip relocation.")]
    [SerializeField] private Transform systemUnitRoot;
    [Tooltip("Marker Transform placed exactly where the system unit should end up, beside the " +
             "Bootable Flashdrive Creator station. Position AND rotation are copied from this " +
             "marker onto systemUnitRoot  it doesn't need to be a real/visible object, an empty " +
             "GameObject positioned by hand in the Scene view works fine.")]
    [SerializeField] private Transform systemUnitDestination;

    [Tooltip("Every CableConnector belonging to the system unit that could be UNPLUGGED when " +
             "relocation happens  e.g. the PSU's cables (see PSUPartItem/CableGatedPartItem's " +
             "own Cables array  assign the same connectors here too). If still unplugged once " +
             "both tasks complete (nothing requires cables to be reconnected to finish " +
             "assemble_pc), each one is auto-plugged back into its own Start Socket so it " +
             "properly travels with the case. A connector with no Start Socket assigned falls " +
             "back to being carried along by raw position math instead (see CarryCablesAlong).")]
    [SerializeField] private CableConnector[] cablesToRelocate;

    [Tooltip("If true, cablesToRelocate above is treated as a starting point only. Every " +
             "CableConnector in the scene whose startSocket (or current pluggedSocket) is a " +
             "descendant of systemUnitRoot is automatically added too, so forgetting to drag a " +
             "newly-added cable into the array above can no longer leave it behind on relocation. " +
             "Leave OFF if you want full manual control over exactly which cables move.")]
    [SerializeField] private bool autoDiscoverCables = true;

    [Tooltip("Objects that should simply be hidden once the system unit relocates, instead of " +
             "being carried along  e.g. RAM retention locks, which don't need to stay visible " +
             "once the case is closed up and moved to the Bootable Flashdrive station.")]
    [SerializeField] private GameObject[] objectsToHideOnRelocate;

    private bool hasRelocatedSystemUnit;

    // Cables the learner has UNPLUGGED at least once. Each one adds a step to assemble_pc - you
    // took it out, you put it back.
    private readonly System.Collections.Generic.HashSet<CableConnector> cablesToRestore =
        new System.Collections.Generic.HashSet<CableConnector>();

    // Cables that have since been re-plugged and already reported their step, so a learner who
    // unplugs and re-plugs the same lead five times does not finish the assembly five times over.
    private readonly System.Collections.Generic.HashSet<CableConnector> cablesRestored =
        new System.Collections.Generic.HashSet<CableConnector>();

    private void Awake()
    {
        // Awake, not Start: a cable cannot be unplugged before the first frame, but CableConnector's
        // own Start plugs the start-plugged ones in, and that call reaches Instance. Assigning here
        // means it is never null when it does.
        if (Instance != null && Instance != this)
            Debug.LogWarning("[PCDisassemblySetup] A second one on '" + name + "'. The first keeps " +
                             "Instance, so cables will report to that one.", this);
        else
            Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.EnsureGameMode(gameModeId);

        TaskManager.Instance.RegisterTask(TaskId, totalParts);
        TaskManager.Instance.RegisterTask(AssembleTaskId, totalParts);

        TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

        // Covers the case where a save was loaded with both tasks already
        // complete  RestoreState() re-fires OnTaskCompleted per completed
        // task, but only after this Start() has subscribed if load happens
        // later; this catches the case where it happened earlier too.
        TryRouteWaypointToBootableUsb();
    }

    private void OnDisable()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
    }

    // ==================================================================
    // Cables as assembly steps
    // ==================================================================

    /// <summary>
    /// The learner pulled a cable out. That raises what assemble_pc asks for by one.
    ///
    /// COUNTED ON UNPLUG RATHER THAN UP FRONT, and that is what makes this safe.
    ///
    /// Registering "parts + every cable in the scene" at Start looks simpler and has a failure that
    /// cannot be recovered from: a cable the learner never unplugs never reports, so the task sits
    /// one step short forever with nothing on screen to say which step. Growing the requirement as
    /// they actually disassemble means it always matches what they have taken apart - and a cable
    /// they never touched is still correctly plugged in, so there is nothing to ask of them.
    /// </summary>
    public static void NotifyCableUnplugged(CableConnector cable)
    {
        if (Instance != null) Instance.HandleCableUnplugged(cable);
    }

    /// <summary>The learner put a cable back. Reports one step of assemble_pc, once per cable.</summary>
    public static void NotifyCablePlugged(CableConnector cable)
    {
        if (Instance != null) Instance.HandleCablePlugged(cable);
    }

    private void HandleCableUnplugged(CableConnector cable)
    {
        if (!cablesCountTowardAssembly || cable == null || TaskManager.Instance == null) return;

        if (!cablesToRestore.Add(cable)) return;      // already counted

        // Re-registering an existing task RAISES its required count and re-derives whether it is
        // complete - so an assemble_pc that had finished under the old count correctly reopens.
        // That matters here: a learner can seat the last part, see the task complete, and only then
        // pull a PSU lead. The requirement going up puts the module honestly back to unfinished.
        TaskManager.Instance.RegisterTask(AssembleTaskId, totalParts + cablesToRestore.Count);

        Debug.Log($"[PCDisassemblySetup] '{cable.name}' unplugged - assemble_pc now needs " +
                  $"{totalParts + cablesToRestore.Count} step(s) ({totalParts} part(s) plus " +
                  $"{cablesToRestore.Count} cable(s)).", this);
    }

    private void HandleCablePlugged(CableConnector cable)
    {
        if (!cablesCountTowardAssembly || cable == null || TaskManager.Instance == null) return;

        // Not in cablesToRestore means this is the start-plugged state being set up, not the learner
        // reconnecting something - so there is nothing to credit. No flag, no timing guard: a cable
        // that was never taken out cannot be put back.
        if (!cablesToRestore.Contains(cable)) return;

        if (!cablesRestored.Add(cable)) return;       // already credited

        TaskManager.Instance.ReportProgress(AssembleTaskId);

        Debug.Log($"[PCDisassemblySetup] '{cable.name}' reconnected - {cablesRestored.Count} of " +
                  $"{cablesToRestore.Count} cable(s) back in.", this);
    }

    private void HandleTaskCompleted(string completedTaskId)
    {
        if (completedTaskId == TaskId || completedTaskId == AssembleTaskId)
            TryRouteWaypointToBootableUsb();
    }

    private void TryRouteWaypointToBootableUsb()
    {
        bool bothComplete = TaskManager.Instance.IsTaskComplete(TaskId)
            && TaskManager.Instance.IsTaskComplete(AssembleTaskId);

        if (!bothComplete) return;

        if (bootableUsbTarget != null && WaypointIndicator.Instance != null)
            WaypointIndicator.Instance.SetTarget(bootableUsbTarget);

        if (pcStationCameraSwitch != null)
            pcStationCameraSwitch.ForceReturnToPlayerCamera();

        if (quizCameraController != null)
            quizCameraController.ActivateQuizCamera();

        RelocateSystemUnit();
    }

    /// <summary>Moves the assembled system unit to sit beside the Bootable Flashdrive Creator
    /// station. Guarded by hasRelocatedSystemUnit so re-firing OnTaskCompleted (e.g. once per
    /// task while restoring a save where both are already complete) doesn't repeat the move
    /// with stale data  it's a plain instant teleport, so repeating it would be harmless anyway,
    /// but the guard keeps the Debug.Log below from spamming twice per load.</summary>
    private void RelocateSystemUnit()
    {
        if (hasRelocatedSystemUnit) return;
        if (systemUnitRoot == null || systemUnitDestination == null) return;

        hasRelocatedSystemUnit = true;

        CableConnector[] cables = autoDiscoverCables ? BuildFullCableList() : cablesToRelocate;

        Vector3 oldPos = systemUnitRoot.position;
        Quaternion oldRot = systemUnitRoot.rotation;

        // Do this BEFORE moving the case: a cable that gets properly re-plugged becomes a
        // child of its socket (which lives under systemUnitRoot), so it then moves for free
        // along with everything else below  no separate position math needed for it at all.
        ReattachLooseCables(cables);

        systemUnitRoot.SetPositionAndRotation(systemUnitDestination.position, systemUnitDestination.rotation);

        Vector3 deltaPos = systemUnitRoot.position - oldPos;
        Quaternion deltaRot = systemUnitRoot.rotation * Quaternion.Inverse(oldRot);
        CarryCablesAlong(cables, oldPos, deltaPos, deltaRot);
        HideRelocateExcludedObjects();

        Debug.Log($"[PCDisassemblySetup] System unit relocated beside the Bootable Flashdrive Creator station.", systemUnitRoot);
    }

    /// <summary>
    /// Combines the manually-assigned cablesToRelocate array with every CableConnector in the
    /// scene that actually belongs to this system unit  either its startSocket or its current
    /// pluggedSocket is a descendant of systemUnitRoot. Catches cables an unplugged
    /// (transform.SetParent(null) in CableConnector.Unplug orphans them completely, so hierarchy
    /// search alone can't find them) and cables someone simply forgot to drag into the array.
    /// </summary>
    private CableConnector[] BuildFullCableList()
    {
        var set = new System.Collections.Generic.HashSet<CableConnector>();

        if (cablesToRelocate != null)
            foreach (var c in cablesToRelocate)
                if (c != null) set.Add(c);

        foreach (var cable in FindObjectsByType<CableConnector>(FindObjectsInactive.Include))
        {
            bool startsHere = cable.startSocket != null && cable.startSocket.transform.IsChildOf(systemUnitRoot);
            bool pluggedHere = cable.isPlugged && cable.pluggedSocket != null && cable.pluggedSocket.transform.IsChildOf(systemUnitRoot);

            if (startsHere || pluggedHere)
                set.Add(cable);
        }

        return new System.Collections.Generic.List<CableConnector>(set).ToArray();
    }

    /// <summary>
    /// The disassemble/assemble tasks only track PCPartItems (CPU, RAM, PSU, etc)  a cable
    /// being unplugged doesn't block "assemble_pc" from completing, and nothing else in the
    /// project ever plugs it back in. So by the time both tasks are done, a cable can easily
    /// still be sitting unplugged wherever it was dropped. Fix that here: snap every unplugged
    /// cable back into its own startSocket (its designated home port, independent of whether it
    /// happened to start the scene already plugged in), so it's a properly attached,
    /// socket-parented part of the case again  same as if the player had plugged it back in by hand.
    /// </summary>
    private void ReattachLooseCables(CableConnector[] cables)
    {
        if (cables == null) return;

        foreach (var cable in cables)
        {
            if (cable == null || cable.isPlugged) continue; // already attached  moves for free

            if (cable.startSocket == null)
            {
                Debug.LogWarning($"[PCDisassemblySetup] '{cable.name}' is unplugged and has no " +
                    "Start Socket assigned on its CableConnector, so it can't be auto-reconnected " +
                    "before the system unit relocates. Assign its home PortSocket there, or it'll " +
                    "only be carried along loosely (falling back to CarryCablesAlong).", cable);
                continue;
            }

            cable.Plug(cable.startSocket);
        }
    }

    /// <summary>
    /// Fallback only  runs after ReattachLooseCables, so this now only ever touches a cable
    /// that's STILL unplugged (no Start Socket was assigned for it to snap back into). Keeps it
    /// visually attached to the case regardless: rotated/translated by the same delta the case
    /// just underwent, pivoting around the case's old position, so it keeps the same relative
    /// spot next to the case instead of staying behind at the old location. Also rebuilds every
    /// listed cable's rope (plugged or not) so none of them visibly stretch across the room for
    /// a moment while Verlet integration catches up to the anchors' new positions.
    /// </summary>
    private void CarryCablesAlong(CableConnector[] cables, Vector3 pivot, Vector3 deltaPos, Quaternion deltaRot)
    {
        if (cables == null) return;

        foreach (var cable in cables)
        {
            if (cable == null) continue;

            if (!cable.transform.IsChildOf(systemUnitRoot))
            {
                Vector3 offsetFromPivot = cable.transform.position - pivot;
                cable.transform.position = pivot + deltaPos + deltaRot * offsetFromPivot;
                cable.transform.rotation = deltaRot * cable.transform.rotation;
            }

            // Snap the rope's simulated points straight to the new anchor positions instead of
            // letting Verlet integration visibly stretch/settle over the next few physics steps.
            if (cable.rope != null)
                cable.rope.ForceRebuild();
        }
    }

    /// <summary>
    /// Simply hides objects that don't need to travel with the case  e.g. RAM retention locks,
    /// which live outside systemUnitRoot's Hierarchy branch and don't need to be seen once the
    /// case is closed up and relocated. Much simpler than trying to carry them along with
    /// position/rotation math.
    /// </summary>
    private void HideRelocateExcludedObjects()
    {
        if (objectsToHideOnRelocate == null) return;

        foreach (var obj in objectsToHideOnRelocate)
        {
            if (obj != null) obj.SetActive(false);
        }
    }
}