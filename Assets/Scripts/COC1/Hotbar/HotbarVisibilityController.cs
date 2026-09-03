using UnityEngine;
using CircuitCrafter.Repair;

/// <summary>
/// Single source of truth for hotbar visibility. Continuously derives the
/// correct visible state every frame from ground truth - "is the player
/// actually inside a station where parts can be handled, AND is a quiz not
/// currently covering the screen" - instead of relying on StationCameraSwitch
/// and QuizCameraController each independently calling SetActive() on the
/// same hotbar object, which can drift out of sync (e.g. a quiz forcing it
/// active while the player isn't actually in the station).
///
/// This replaces the need to list the hotbar in either StationCameraSwitch's
/// enableOnStationView/disableOnStationView arrays OR QuizCameraController's
/// enableOnQuiz/disableOnQuiz arrays - remove the hotbar entry from both of
/// those in the Inspector once this is set up, so only this script controls
/// it. Everything else in those arrays (waypoint arrow, OS install UI, quiz
/// panel, etc.) is unaffected and keeps working exactly as before.
///
/// THE SAME RULE NOW COVERS THE REPAIR SITE. A COC4 ticket sends the player
/// into this scene through a RepairStation rather than a StationCameraSwitch,
/// and the hotbar has to appear there for exactly the same reason: it is the
/// station where parts are handled. Adding it to that station's Enable On Enter
/// array would not work, and the reason is the whole point of this script - two
/// things writing the same flag, with this one winning every frame because it
/// runs in Update. So the station is listed HERE instead, as a second source of
/// truth for the same question, and there is still only one writer.
///
/// Attach this to a persistent object (NOT the hotbar itself - if it were on
/// the hotbar and disabled the hotbar, its own Update would stop running and
/// it could never re-enable it). A good place is the same object that holds
/// HotbarManager, or a small always-on "UI Coordinators" object in the scene.
/// </summary>
public class HotbarVisibilityController : MonoBehaviour
{
    [Tooltip("The PC assemble/disassemble station's StationCameraSwitch. Hotbar is only allowed to be visible while this reports IsUsingStationCamera == true. Leave empty in a scene that is only used for repair tickets.")]
    [SerializeField] private StationCameraSwitch assemblyStation;

    [Tooltip("The COC4 system unit station. The hotbar is visible while the learner is inside it, for the same reason it is visible at the assembly station - it is where parts are handled. Leave empty outside a repair site.\n\nIMPORTANT: do NOT also list the hotbar in that station's Enable On Enter array. This script wins every frame, and the array would silently do nothing.")]
    [SerializeField] private RepairStation systemUnitStation;

    [Tooltip("The hotbar's root GameObject (the panel/canvas that should show/hide) - not the same object this script is attached to.")]
    [SerializeField] private GameObject hotbarRoot;

    void Update()
    {
        if (hotbarRoot == null) return;

        // Either station counts. Both are "the player is somewhere parts can be
        // handled"; which one it is depends only on how they got into this scene.
        bool atAssembly = assemblyStation != null && assemblyStation.IsUsingStationCamera;
        bool atSystemUnit = systemUnitStation != null && systemUnitStation.IsInside;

        if (!atAssembly && !atSystemUnit && assemblyStation == null && systemUnitStation == null)
        {
            // Neither reference assigned: nothing can be derived, so leave the
            // hotbar alone rather than hiding it forever with no explanation.
            return;
        }

        bool quizOpen = QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen;
        bool shouldBeVisible = (atAssembly || atSystemUnit) && !quizOpen;

        if (hotbarRoot.activeSelf != shouldBeVisible)
            hotbarRoot.SetActive(shouldBeVisible);
    }
}