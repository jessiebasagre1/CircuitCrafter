using UnityEngine;

/// <summary>
/// Lets one script say "I have already dealt with Escape this frame", so a second one does not
/// deal with it as well.
///
/// THE PROBLEM IT SOLVES
/// Input.GetKeyDown(KeyCode.Escape) is TRUE for every script that asks, on the same frame. In COC 3
/// two of them ask: COC3StationController, whose Exit Key is Escape, and PauseMenuController, which
/// opens the pause menu on Escape from any scene. One keypress, two responses.
///
/// Both orderings are wrong, which is why this cannot be fixed by tidying either script alone:
///
///   station first  - the learner stands up, the cursor is re-locked, and then the pause menu
///                    opens on top of it. Unexpected, but survivable.
///
///   pause first    - the pause menu opens and SNAPSHOTS the cursor as it is RIGHT NOW, which in a
///                    step view is unlocked and visible. Then the station exits and locks the
///                    cursor, so the pause menu is on screen with a cursor that cannot be moved onto
///                    its buttons. Press Escape again and the menu restores its snapshot - unlocked
///                    and visible - and now the learner is walking around with a free cursor and no
///                    mouse look. That is the "cursor unlocks when I exit a station" bug exactly.
///
/// Unity does not define the Update order of two scripts that both sit at execution order 0, so
/// which of the two happens is not something the scenes can be relied on to settle.
///
/// HOW THE ORDER IS MADE CERTAIN
/// Not with [DefaultExecutionOrder], which changes Awake and Start ordering too and has a habit of
/// breaking things at a distance. Instead PauseMenuController reads Escape in LateUpdate, which
/// Unity always runs after every Update. GetKeyDown is still true there, so nothing is lost - and by
/// then any station that wanted Escape has had its Update and has claimed it here.
///
/// It is a plain frame number, not a queue: no allocation, no registration, nothing to clean up, and
/// it cannot leak a claim into the next frame even if the claiming script is destroyed mid-frame.
/// </summary>
public static class EscapeKeyClaim
{
    private static int claimedOnFrame = -1;

    /// <summary>Call this the moment your script acts on Escape.</summary>
    public static void Claim() { claimedOnFrame = Time.frameCount; }

    /// <summary>True when something earlier in this frame already acted on Escape.</summary>
    public static bool AlreadyClaimed => claimedOnFrame == Time.frameCount;
}
