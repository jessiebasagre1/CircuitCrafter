using UnityEngine;
using CSSSimulation.Networking;

/// <summary>
/// Points COC2's arrow at whichever machine COC3's current step needs.
///
/// ATTACH THIS TO: the same object as TaskWaypointArrow in the COC3 scene.
///
/// WHY COC3 CANNOT JUST USE A STEP LIST
/// TaskWaypointArrow walks an ORDERED list and points at the first unfinished entry. That is right
/// for COC1 and COC2, where the learner crosses the room in a fixed order and each place is visited
/// once. COC3 does not work like that: its thirty-odd steps alternate between two machines - server,
/// server, client, server, client - and which machine a step belongs to is already decided by
/// COC3StationController.clientStepNumbers. Writing that out as thirty steps would be thirty places
/// for the two lists to disagree, and the first time you inserted a step in the middle they would.
///
/// So COC3 uses ONE step, and this moves it. The arrow keeps everything it is good at - the world
/// marker, the off-screen screen arrow, the label, the distance, the suppression while a station is
/// open - and the question of WHERE stays with the component that already answers it.
///
/// TaskWaypointArrow is not modified, and does not know COC3 exists.
/// </summary>
[DefaultExecutionOrder(250)]      // before the arrow's own 300, so it reads a target set this frame
public class COC3WaypointBridge : MonoBehaviour
{
    [Header("Wiring - both found automatically if left empty")]
    [SerializeField] private TaskWaypointArrow arrow;
    [SerializeField] private COC3StationController controller;

    [Header("Which step to drive")]
    [Tooltip("Index into the arrow's Steps list. COC3 needs exactly ONE step, so leave this at 0 " +
             "unless you have added others.")]
    [Min(0)]
    [SerializeField] private int stepIndex = 0;

    [Header("Labels")]
    [Tooltip("Shown while the current step belongs to the server.")]
    [SerializeField] private string serverLabel = "Go to the server PC";

    [Tooltip("Shown while the current step belongs to the client.")]
    [SerializeField] private string clientLabel = "Go to the client PC";

    [Header("Behaviour")]
    [Tooltip("Hide the arrow while the learner is inside a machine's step view. Leave on - a marker " +
             "hovering over a PC across the room while they are looking at its screen is noise.")]
    [SerializeField] private bool hideWhileUsingAMachine = true;

    private bool warned;

    // ==================================================================

    private void Start()
    {
        if (arrow == null) arrow = TaskWaypointArrow.Instance;
        if (controller == null) controller = COC3StationController.Instance;

        if (arrow == null || controller == null)
        {
            Debug.LogWarning("[COC3WaypointBridge] " +
                             (arrow == null ? "No TaskWaypointArrow" : "No COC3StationController") +
                             " found, so the arrow will not follow the required machine.", this);
            enabled = false;
            return;
        }

        if (arrow.steps == null || arrow.steps.Count <= stepIndex)
        {
            Debug.LogWarning($"[COC3WaypointBridge] The arrow has no step {stepIndex}. Add one step " +
                             "to its Steps list - see this component's setup notes.", this);
            enabled = false;
            return;
        }

        // Said once, out loud, because two waypoints in one scene is a confusing thing to debug by
        // eye: they sit in the same place and only one of them responds to anything.
        if (controller.useWaypoint)
            Debug.LogWarning("[COC3WaypointBridge] COC3StationController.Use Waypoint is still ON, so " +
                             "the old WaypointIndicator is being driven as well and you will have two " +
                             "markers. Untick it.", controller);
    }

    private void Update()
    {
        if (arrow == null || controller == null) return;

        var step = arrow.steps[stepIndex];
        if (step == null) return;

        bool client = controller.RequiredMachine == COC3MachineType.Client;
        COC3Workstation station = client ? controller.clientStation : controller.serverStation;

        if (station == null)
        {
            if (!warned)
            {
                warned = true;
                Debug.LogWarning($"[COC3WaypointBridge] COC3StationController has no " +
                                 $"{(client ? "Client" : "Server")} Station assigned, so there is " +
                                 "nothing to point at.", controller);
            }
            return;
        }

        // WaypointTarget falls back to the machine's own transform when no marker child is set, so
        // this is never null once the station exists.
        step.target = station.WaypointTarget;
        step.label = client ? clientLabel : serverLabel;

        if (hideWhileUsingAMachine) arrow.SetSuppressed(controller.InStepView);
    }
}
