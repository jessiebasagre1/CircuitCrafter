using UnityEngine;

/// <summary>
/// Keeps the cursor locked and hidden whenever the first-person view is the one on screen.
///
/// THERE IS NOTHING TO ATTACH AND NOTHING TO WIRE. Put this file anywhere in the project; it
/// creates itself before the first scene and survives every scene load.
///
/// WHY A KEEPER RATHER THAN FIXING EACH CULPRIT
/// Cursor.lockState is one global that eleven different scripts write to in this project - the
/// stations, the pause menu, the quiz camera, the tutor, the story-complete panel, the main menu.
/// Several of them SNAPSHOT it on open and restore it on close, which is correct on its own and
/// wrong in combination: if the state changes while the snapshot is held, the restore puts back a
/// value that stopped being true. That is exactly how exiting a station left the learner walking
/// around with a free cursor and no mouse look.
///
/// Chasing every one of those is a long tail with no end. Stating the rule once, in one place, and
/// enforcing it every frame ends the whole category. AudioListenerKeeper already does this for the
/// AudioListener, for the same reason and with the same shape.
///
/// IT ONLY EVER LOCKS, NEVER FREES
/// When it decides the cursor should be locked it locks it. Every other time it does nothing at all
/// and leaves whatever state it finds. So nothing here can take the cursor away from a panel that
/// legitimately wants it - the worst it can do is fail to lock, which is visible and reportable,
/// rather than fight another script, which would not be.
///
/// WHEN IT STAYS OUT OF THE WAY
///   - MouseLook is off, so the learner is not in first-person control (a station close-up, the
///     quiz camera, the main menu)
///   - the pause menu is open
///   - PlayerInputLock is held - the settings panel, COC 4's panels, the story-complete screen
///   - something called CursorKeeper.Claim() - COC 2's DeviceConfigStation and
///     ConnectivityCheckStation, and AiTutorChat, all do
///   - touch controls are actually on screen, so capturing would pin the pointer to the centre
///
/// A DIALOG OR A FOCUSED TEXT FIELD IS NOT ON THAT LIST, deliberately. Neither needs a free cursor
/// while the learner is walking, dialogs advance on Space, and treating them as exceptions left a
/// standing excuse for the exact free-cursor state this exists to prevent.
///
/// ANY PANEL THAT FREES THE CURSOR ITSELF MUST CLAIM IT. Otherwise this takes it straight back on
/// the next frame and the panel's fields cannot be clicked - which is what happened to COC 2's
/// network configuration.
/// </summary>
[DefaultExecutionOrder(2000)]   // after everything, so it sees the frame's final state
public class CursorKeeper : MonoBehaviour
{
    private static CursorKeeper instance;
    private static int claims;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        // A static survives entering Play mode in the Editor, so a claim held when the last session
        // ended would still be held now and the cursor would never lock, with nothing to show why.
        // PlayerInputLock clears itself the same way for the same reason.
        claims = 0;

        if (instance != null) return;

        var go = new GameObject("~CursorKeeper");

        // DontSave, not HideAndDontSave. HideInHierarchy would keep it out of the way, but it also
        // puts "Report cursor state" out of reach - there would be no object to right-click. A
        // diagnostic you cannot reach is not a diagnostic.
        go.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<CursorKeeper>();

        // ONE LINE AT STARTUP, ALWAYS.
        //
        // Learned the hard way on WaypointAutoWake: a component that only speaks when it has a
        // complaint is indistinguishable from a component that is not in the project. "No
        // [CursorKeeper] in the Console" has to mean something definite, and without this it means
        // either "not installed" or "installed and content" - which are opposite conclusions
        // leading to opposite next steps.
        Debug.Log("[CursorKeeper] Installed and watching. It will re-lock the cursor whenever the " +
                  "first-person camera is the live view and nothing is claiming the cursor, and will " +
                  "say so the first few times it has to.");
    }

    /// <summary>
    /// "I need the cursor - leave it alone." Pair every Claim with a Release.
    ///
    /// Counted, not a bool, because two things can want the cursor at once - a panel and a tutor
    /// window - and with a bool whichever closed first would hand it back while the other was still
    /// using it. PlayerInputLock is counted for the same reason.
    /// </summary>
    public static void Claim() { claims++; }

    public static void Release() { claims = Mathf.Max(0, claims - 1); }

    /// <summary>How many claims are outstanding. For diagnostics.</summary>
    public static int ClaimCount => claims;

    /// <summary>Drops every claim. For a scene change or a bail-out.</summary>
    public static void ReleaseAll() { claims = 0; }

    // ==================================================================

    [Tooltip("Console line the first few times the cursor has to be corrected, naming what state it " +
             "was found in. Leave on while chasing a cursor bug; it goes quiet by itself.")]
    private const int MaxCorrectionsToReport = 5;

    private int correctionsReported;
    private Camera[] cameraBuffer = new Camera[8];

    private void LateUpdate()
    {
        // NO #if HERE, DELIBERATELY - AND THIS WAS THE BUG IN THE FIRST VERSION.
        //
        // The first version wrapped this whole method in #if UNITY_ANDROID || UNITY_IOS ... return.
        // UNITY_ANDROID is defined in the EDITOR whenever the build target is Android, which this
        // project's is - so the entire keeper compiled down to "return" and never ran a line. That
        // is why there was not one [CursorKeeper] message in the Console: not missing, not quiet,
        // compiled out.
        //
        // The concern behind that guard is real - capturing the cursor while touch controls are on
        // screen pins Input.mousePosition to the centre and makes the joystick unreachable - but it
        // is a question about what is on screen right now, which a compile-time symbol cannot
        // answer. CursorControl.CaptureBlocked asks it at runtime.
        if (CursorControl.CaptureBlocked) return;

        if (!ShouldBeLocked()) { WatchForAStuckCursor(); return; }

        heldFreeFor = 0f;

        if (Cursor.lockState == CursorLockMode.Locked && !Cursor.visible) return;   // already right

        if (correctionsReported < MaxCorrectionsToReport)
        {
            correctionsReported++;
            Debug.Log($"[CursorKeeper] Re-locked the cursor - it was {Cursor.lockState}, " +
                      $"visible={Cursor.visible}, while the first-person camera was the live view and " +
                      "nothing was claiming it. Something freed it and did not put it back." +
                      (correctionsReported == MaxCorrectionsToReport
                          ? "  (Last time this will be reported.)" : ""));
        }

        // Through CursorControl, so Captured stays in step with the rest of the project - anything
        // that reads CursorControl.Captured would otherwise be told the cursor is free while this
        // has it locked.
        CursorControl.Capture();
    }

    private float heldFreeFor;

    /// <summary>
    /// Names whoever is keeping the cursor free, once it has gone on long enough to be a bug.
    ///
    /// THIS IS THE PART THAT ENDS THE GUESSING. A free cursor while walking around always looks the
    /// same on screen, whatever caused it - a dialog nobody dismissed, a panel that took a
    /// PlayerInputLock and never released it, a field still holding focus after its panel was
    /// switched off, a claim never paired with a release. Every one of those is a different fix, and
    /// none of them is visible from the outside.
    ///
    /// The keeper already computes all of it every frame in order to decide whether to stand down.
    /// Printing that, once, after a few seconds of standing down while the first-person view is live
    /// turns "the cursor unlocked again" into a line naming the cause.
    ///
    /// A few seconds, not immediately: reading a dialog legitimately takes longer than that is not
    /// true of anything else here, and a dialog the learner is still reading is not a fault.
    /// </summary>
    private void WatchForAStuckCursor()
    {
        // Only interesting while the learner is meant to be walking around with a captured cursor.
        if (!FirstPersonViewIsLive() || Cursor.lockState == CursorLockMode.Locked)
        {
            heldFreeFor = 0f;
            return;
        }

        heldFreeFor += Time.unscaledDeltaTime;   // unscaled: a paused game still counts

        if (heldFreeFor < 2f) return;

        // Repeating, not once-per-session. A single line is easy to miss in a Console this busy, and
        // repeating also shows whether the hold is momentary or permanent - which is the difference
        // between a panel that is slow to release and one that never does.
        heldFreeFor = 0f;

        Debug.LogWarning("[CursorKeeper] The cursor has been free for 2s while the first-person view " +
                         "is live. Something is holding it. Whichever of these reads True is the one " +
                         "to chase:" +
                         (claims > 0
                             ? "\n    NOTE: a Claim is outstanding. If no panel is on screen, some " +
                               "Claim() was never paired with a Release() - that holds the cursor " +
                               "free for the rest of the run."
                             : "") +
                         $"\n    Claims held       : {claims}" +
                         $"\n    PlayerInputLock   : {PlayerInputLock.IsLocked}" +
                         $"\n    Pause menu open   : {PauseIsOpen()}" +
                         "\n  (for information - these no longer excuse a free cursor:)" +
                         $"\n    Dialog open       : {(DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)}" +
                         $"\n    Typing in a field : {TypingGuard.IsTyping}  (focused: {TypingGuard.FocusedFieldName()})" +
                         "\n\nAll False would mean nothing here is holding it - in which case the " +
                         "keeper would already have re-locked it, so that combination cannot appear. " +
                         "Repeats every 2s while it lasts.",
                         this);
    }

    private bool ShouldBeLocked()
    {
        if (claims > 0) return false;
        if (!FirstPersonViewIsLive()) return false;

        // Anything holding the player still is showing them something to read or click.
        if (PlayerInputLock.IsLocked) return false;

        // DIALOGS AND TEXT FIELDS ARE NOT EXCEPTIONS, AND TREATING THEM AS ONE WAS A MISTAKE.
        //
        // The rule asked for is "first-person camera live means cursor locked", and neither of these
        // needs a free cursor to work while the learner is walking around. Dialogs advance on Space
        // on desktop. A text field that still holds focus after its panel was switched off is a
        // leftover, not a reason to hand the cursor over - and it was a standing excuse for exactly
        // the state being complained about.
        //
        // Only things that put CLICKABLE BUTTONS in front of the learner remain: the pause menu,
        // anything holding a PlayerInputLock (settings, COC 4's panels, the story-complete screen),
        // and an explicit Claim.
        if (PauseIsOpen()) return false;

        return true;
    }

    private MouseLook look;

    /// <summary>
    /// Whether the learner is actually in first-person control right now.
    ///
    /// IT ASKS FOR MouseLook, NOT JUST FOR A CAMERA, AND THAT MATTERS.
    ///
    /// An earlier version tested only "is the object tagged MainCamera the live view". Every scene
    /// has a camera tagged MainCamera - including the MAIN MENU - so that version would have locked
    /// the cursor on the menu and left the learner unable to click Play. A camera says what is being
    /// drawn; it says nothing about whether anyone is driving.
    ///
    /// MouseLook is the thing that means first-person control, and it answers the whole question by
    /// itself:
    ///   - the main menu has no MouseLook at all
    ///   - a station view disables it (it is in COC3StationController's Player Control Scripts)
    ///   - a quiz or cutscene camera likewise leaves it off
    /// so "an active, enabled MouseLook" is exactly the state the cursor should be captured in.
    ///
    /// The camera depth check is kept underneath it for the arrangement where a close-up camera is
    /// drawn on top without anything disabling MouseLook.
    /// </summary>
    private bool FirstPersonViewIsLive()
    {
        // Re-found when it goes away: the reference does not survive a scene change, and a stale one
        // would answer for a scene that is no longer loaded.
        if (look == null) look = FindAnyObjectByType<MouseLook>(FindObjectsInactive.Exclude);
        if (look == null || !look.isActiveAndEnabled) return false;

        Camera main = Camera.main;
        if (main == null) return false;

        // GetAllCameras into a reused buffer rather than Camera.allCameras, which allocates a new
        // array every call - and this runs every frame.
        int count = Camera.allCamerasCount;
        if (cameraBuffer.Length < count) cameraBuffer = new Camera[count];
        Camera.GetAllCameras(cameraBuffer);

        for (int i = 0; i < count; i++)
        {
            Camera c = cameraBuffer[i];
            if (c == null || c == main) continue;
            if (c.isActiveAndEnabled && c.depth > main.depth) return false;
        }

        return true;
    }

    private PauseMenuController pause;

    private bool PauseIsOpen()
    {
        // Any pause menu at all - a scene holding two (one carried over on a DontDestroyOnLoad root,
        // say) would otherwise be read through whichever one was found first.
        if (PauseMenuController.AnyPaused) return true;

        // Cached, but re-found when it goes away - the reference does not survive a scene change,
        // and a stale one would report "not paused" forever in the new scene.
        if (pause == null)
            pause = FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Exclude);

        return pause != null && pause.IsPaused;
    }

    /// <summary>Why the cursor is, or is not, being held locked right now.</summary>
    [ContextMenu("Report cursor state")]
    public void ReportCursorState()
    {
        Debug.Log("[CursorKeeper] " +
                  $"\n    Cursor now            : {Cursor.lockState}, visible={Cursor.visible}" +
                  $"\n    First-person control  : {FirstPersonViewIsLive()}  (MouseLook = {(look != null ? (look.isActiveAndEnabled ? "active" : "present but OFF") : "none in scene")}, Camera.main = {(Camera.main != null ? Camera.main.name : "NONE")})" +
                  $"\n    Claims held           : {claims}" +
                  $"\n    PlayerInputLock       : {PlayerInputLock.IsLocked}" +
                  $"\n    Dialog open           : {(DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)}" +
                  $"\n    Typing in a field     : {TypingGuard.IsTyping}  ({TypingGuard.FocusedFieldName()})" +
                  $"\n    Pause menu open       : {PauseIsOpen()}" +
                  $"\n    ---- verdict          : {(ShouldBeLocked() ? "should be LOCKED" : "leaving it alone")}", this);
    }
}