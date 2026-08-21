using CSSSimulation.Interaction;
using UnityEngine;

// NOTE ON THE FULLY-QUALIFIED PlayerInteractor BELOW
//
// This project has TWO classes called PlayerInteractor:
//   COC1/Interaction/PlayerInteractor.cs - GLOBAL namespace, raycast + USB-drive system,
//                                          no Instance property
//   COC2/Interactor/PlayerInteractor.cs  - namespace CSSSimulation.Interaction, has Instance
//
// C# resolves a bare name against the enclosing namespace (the global one, for this file)
// BEFORE any using-directive. So an unqualified "PlayerInteractor" here binds to COC1's and
// fails with "'PlayerInteractor' does not contain a definition for 'Instance'" - even with
// the using above. A using-alias cannot fix it either: an alias that collides with a
// global-namespace type is itself an error (CS0576).
//
// Writing the full name is the one thing that unambiguously works, and it costs nothing.
// Interactable needs no such treatment - only COC2 defines that name.

/// <summary>Which physical machine a COC3 step is performed on.</summary>
public enum COC3MachineType
{
    Server,
    Client
}

/// <summary>
/// A computer in the world the player can walk up to and use.
///
/// ATTACH THIS TO: your Server PC object, and again to your Client PC object.
/// Each one needs a trigger Collider sized to the interaction range, and your player's
/// root object must be tagged "Player" - identical requirements to the crimping station,
/// because this extends the same Interactable base class your COC2 scene already uses.
///
/// It does not decide anything itself. Pressing E just asks COC3StationController whether
/// this is the machine the current step belongs to; the controller either opens the step
/// view or explains that the learner is at the wrong computer.
/// </summary>
public class COC3Workstation : Interactable
{
    [Header("Identity")]
    [Tooltip("Which machine this object represents. One workstation should be Server, the other Client.")]
    public COC3MachineType machine = COC3MachineType.Server;

    [Tooltip("Shown in dialogs, e.g. 'Server PC' or 'Client PC'.")]
    public string displayName = "Server PC";

    [Header("Close-up View")]
    [Tooltip("Optional camera framed on this machine's monitor. Swapped in while the player is using it. Leave empty to keep the player camera where it is.")]
    public Camera stationCamera;

    [Tooltip("Optional - where the waypoint arrow should point. Defaults to this object.")]
    public Transform waypointTarget;

    [Header("Prompts")]
    [Tooltip("Prompt shown when this IS the machine the current step needs.")]
    public string promptWhenReady = "Press E to use";

    [Tooltip("Prompt shown when the current step belongs to the OTHER machine.")]
    public string promptWhenLocked = "Not needed right now";

    /// <summary>Where the waypoint arrow should aim.</summary>
    public Transform WaypointTarget => waypointTarget != null ? waypointTarget : transform;

    private bool lastReadyState;

    private void Update()
    {
        // Keep the on-screen prompt honest as the steps advance. PlayerInteractor only
        // reads promptText when the player enters range, so when the required machine
        // changes while they are already standing here we have to push the refresh
        // ourselves - otherwise the prompt still says "Press E to use" on a machine that
        // will now turn them away.
        bool ready = IsRequiredNow();
        promptText = ready ? promptWhenReady : promptWhenLocked;

        if (ready != lastReadyState && IsPlayerInRange &&
            CSSSimulation.Interaction.PlayerInteractor.Instance != null)
        {
            CSSSimulation.Interaction.PlayerInteractor.Instance.SetNearbyInteractable(this);
        }

        lastReadyState = ready;
    }

    private bool IsRequiredNow()
    {
        var c = COC3StationController.Instance;
        return c == null || c.RequiredMachine == machine;
    }

    public override void Interact()
    {
        if (COC3StationController.Instance == null)
        {
            Debug.LogWarning("[COC3Workstation] No COC3StationController in the scene.", this);
            return;
        }

        COC3StationController.Instance.TryEnter(this);
    }
}