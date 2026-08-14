using UnityEngine;

/// <summary>
/// Single source of truth for hotbar visibility. Continuously derives the
/// correct visible state every frame from ground truth - "is the player
/// actually inside the PC assemble/disassemble station, AND is a quiz not
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
/// Attach this to a persistent object (NOT the hotbar itself - if it were on
/// the hotbar and disabled the hotbar, its own Update would stop running and
/// it could never re-enable it). A good place is the same object that holds
/// HotbarManager, or a small always-on "UI Coordinators" object in the scene.
/// </summary>
public class HotbarVisibilityController : MonoBehaviour
{
    [Tooltip("The PC assemble/disassemble station's StationCameraSwitch. Hotbar is only allowed to be visible while this reports IsUsingStationCamera == true.")]
    [SerializeField] private StationCameraSwitch assemblyStation;

    [Tooltip("The hotbar's root GameObject (the panel/canvas that should show/hide) - not the same object this script is attached to.")]
    [SerializeField] private GameObject hotbarRoot;

    void Update()
    {
        if (assemblyStation == null || hotbarRoot == null) return;

        bool quizOpen = QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen;
        bool shouldBeVisible = assemblyStation.IsUsingStationCamera && !quizOpen;

        if (hotbarRoot.activeSelf != shouldBeVisible)
            hotbarRoot.SetActive(shouldBeVisible);
    }
}