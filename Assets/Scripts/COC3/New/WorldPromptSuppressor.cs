using UnityEngine;

/// <summary>
/// Hides "Press E to enter station" whenever the cursor is free.
///
/// ATTACH THIS TO: the same Managers object as CursorArbiter.
///
/// THE BUG IT FIXES
/// COC3StationController hides the mobile controls and the walking camera when the learner sits
/// down, but not the world prompt - so "Press E to enter station" stays across the bottom of the
/// screen while they are already sitting at the machine, on top of the desktop.
///
/// PlayerInteractor already has the mechanism: SuppressPromptThisFrame(), which the COC 2 stations
/// call. COC 3 simply never called it. This does, and uses CursorArbiter's own answer to decide
/// when - so the prompt is hidden in exactly the situations the cursor is free, and there is no
/// second list of conditions to drift out of step with the first.
///
/// WHY ITS OWN COMPONENT, AND WHY EXECUTION ORDER -200
/// PlayerInteractor reads the flag in its Update and CLEARS it after reading. Setting it from
/// CursorArbiter, which runs at order 1000, would set it after the read - so the prompt would
/// always be one frame behind and flash on every entry. Running before PlayerInteractor removes
/// the lag entirely.
///
/// THE FULLY-QUALIFIED NAME IS DELIBERATE. This project has two classes called PlayerInteractor -
/// COC 1's in the global namespace and COC 2's in CSSSimulation.Interaction. A bare name binds to
/// the global one, which has no such method. COC3Workstation carries the same note for the same
/// reason.
/// </summary>
[DefaultExecutionOrder(-200)]
public class WorldPromptSuppressor : MonoBehaviour
{
    [TooltipAttribute("Left empty, the CursorArbiter on this object is used.")]
    public CursorArbiter arbiter;

    [TooltipAttribute("Hide the prompt while the cursor is free - a station, a dialog, a quiz, or " +
             "any panel listed on the arbiter.")]
    public bool followCursorArbiter = true;

    [TooltipAttribute("Always hide it, whatever the arbiter says. For testing.")]
    public bool alwaysSuppress;

    private void Awake()
    {
        if (arbiter == null) arbiter = GetComponent<CursorArbiter>();
        if (arbiter == null) arbiter = CursorArbiter.Instance;
    }

    private void Update()
    {
        if (alwaysSuppress)
        {
            CSSSimulation.Interaction.PlayerInteractor.SuppressPromptThisFrame();
            return;
        }

        if (!followCursorArbiter) return;

        if (arbiter == null) arbiter = CursorArbiter.Instance;
        if (arbiter == null) return;

        if (arbiter.ShouldFreeCursor())
            CSSSimulation.Interaction.PlayerInteractor.SuppressPromptThisFrame();
    }
}
