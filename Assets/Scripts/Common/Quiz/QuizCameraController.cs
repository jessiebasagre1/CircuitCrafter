using UnityEngine;

/// <summary>
/// Dedicated camera for the quiz that runs after the PC build is complete. This doesn't run
/// the quiz itself — it only owns the camera switch. Whatever starts your quiz (a QuizManager,
/// a UI button, a station trigger, etc.) should call ActivateQuizCamera() the moment the quiz
/// begins, and NotifyQuizFinished() once the last question has been answered; this then
/// switches back to whatever was actually active before the quiz started.
///
/// IMPORTANT: unlike a simple "toggle and reverse" pattern, this snapshots each object's real
/// active/inactive state right before the quiz starts, and restores exactly that afterward.
/// This matters because quizzes don't always fire from the same context — e.g. the Bootable
/// Flashdrive quiz fires while the player is inside the USB Creator desktop UI, not the normal
/// walking/assembly view, so the hotbar was never active to begin with. A naive
/// "always re-enable disableOnQuiz on finish" approach would incorrectly force the hotbar
/// (and anything else in that list) back on even in contexts where it was never shown.
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

    // Snapshots of actual pre-quiz state, so NotifyQuizFinished() restores
    // reality instead of assuming a fixed "normal gameplay" state.
    private bool playerCameraWasActive;
    private bool[] enableOnQuizPriorState;
    private bool[] disableOnQuizPriorState;

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

        // Snapshot real state before touching anything.
        playerCameraWasActive = playerCamera != null && playerCamera.gameObject.activeSelf;
        enableOnQuizPriorState = SnapshotActiveStates(enableOnQuiz);
        disableOnQuizPriorState = SnapshotActiveStates(disableOnQuiz);

        if (playerCamera != null) playerCamera.gameObject.SetActive(false);
        if (quizCamera != null) quizCamera.gameObject.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetActiveAll(enableOnQuiz, true);
        SetActiveAll(disableOnQuiz, false);
    }

    /// <summary>Call once the last question has been answered / the quiz is finished (or exited
    /// early, if that's allowed) — restores whatever was actually active before the quiz
    /// started, rather than assuming a fixed "back to normal gameplay" state.</summary>
    public void NotifyQuizFinished()
    {
        if (!IsQuizCameraActive) return;
        IsQuizCameraActive = false;

        if (quizCamera != null) quizCamera.gameObject.SetActive(false);
        if (playerCamera != null) playerCamera.gameObject.SetActive(playerCameraWasActive);

        // Only restore the locked-cursor gameplay state if the player camera
        // is actually what we're returning to. If it wasn't active before
        // the quiz (e.g. player was in the USB Creator desktop UI instead),
        // leave the cursor free — whatever UI they were in needs it visible.
        if (playerCameraWasActive)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        RestoreActiveStates(enableOnQuiz, enableOnQuizPriorState);
        RestoreActiveStates(disableOnQuiz, disableOnQuizPriorState);
    }

    private bool[] SnapshotActiveStates(GameObject[] objs)
    {
        if (objs == null) return null;
        bool[] states = new bool[objs.Length];
        for (int i = 0; i < objs.Length; i++)
            states[i] = objs[i] != null && objs[i].activeSelf;
        return states;
    }

    private void RestoreActiveStates(GameObject[] objs, bool[] priorStates)
    {
        if (objs == null || priorStates == null) return;
        for (int i = 0; i < objs.Length; i++)
        {
            if (objs[i] != null && i < priorStates.Length)
                objs[i].SetActive(priorStates[i]);
        }
    }

    private void SetActiveAll(GameObject[] objs, bool active)
    {
        if (objs == null) return;
        foreach (var obj in objs)
            if (obj != null) obj.SetActive(active);
    }
}