using UnityEngine;

/// <summary>
/// Dedicated camera for the quiz that runs after the PC build is complete. This doesn't run
/// the quiz itself  it only owns the camera switch. Whatever starts your quiz (a QuizManager,
/// a UI button, a station trigger, etc.) should call ActivateQuizCamera() the moment the quiz
/// begins, and NotifyQuizFinished() once the last question has been answered; this then
/// switches back to the player's main camera automatically.
///
/// Mirrors StationCameraSwitch's enable/disable-array pattern so quiz UI and the player's
/// normal HUD/hotbar/waypoint arrow can be toggled the same way stations already do.
/// </summary>
public class QuizCameraController : MonoBehaviour
{
    public static QuizCameraController Instance { get; private set; }

    [Header("Cameras")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Camera quizCamera;

    [Header("UI/Objects to toggle when the quiz camera is active")]
    [SerializeField] private GameObject[] enableOnQuiz;   // e.g. quiz question panel
    [SerializeField] private GameObject[] disableOnQuiz;  // e.g. hotbar, waypoint arrow

    public bool IsQuizCameraActive { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>Call the moment the quiz starts.</summary>
    public void ActivateQuizCamera()
    {
        if (IsQuizCameraActive) return;
        IsQuizCameraActive = true;

        if (playerCamera != null) playerCamera.gameObject.SetActive(false);
        if (quizCamera != null) quizCamera.gameObject.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetActiveAll(enableOnQuiz, true);
        SetActiveAll(disableOnQuiz, false);
    }

    /// <summary>Call once the last question has been answered / the quiz is finished (or exited
    /// early, if that's allowed)  switches back to the player's main camera.</summary>
    public void NotifyQuizFinished()
    {
        if (!IsQuizCameraActive) return;
        IsQuizCameraActive = false;

        if (quizCamera != null) quizCamera.gameObject.SetActive(false);
        if (playerCamera != null) playerCamera.gameObject.SetActive(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SetActiveAll(enableOnQuiz, false);
        SetActiveAll(disableOnQuiz, true);
    }

    private void SetActiveAll(GameObject[] objs, bool active)
    {
        if (objs == null) return;
        foreach (var obj in objs)
            if (obj != null) obj.SetActive(active);
    }
}