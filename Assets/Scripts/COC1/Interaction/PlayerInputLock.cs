using UnityEngine;

/// <summary>
/// A counted "the player is not driving right now" flag, read by PlayerMovement and MouseLook.
///
/// WHY THIS EXISTS
/// Typing a web address into the browser station walked the player around: WASD are movement
/// axes and also letters, so "rufus.ie" pushed the character backwards on the 's' and strafed
/// on the 'a'. Unity's legacy Input axes do not care that a TMP_InputField has focus - they
/// read the keyboard directly, every frame, regardless of what the UI thinks it owns.
///
/// WHY COUNTED AND NOT A BOOL
/// Two things can want the player held still at once - a station panel and a cutscene, say -
/// and with a plain bool whichever finishes first would hand control back while the other is
/// still using it. Acquire/Release pairs nest correctly.
///
/// PlayerMovement already froze itself while a dialog was open; this covers the other case.
/// </summary>
public static class PlayerInputLock
{
    private static int count;

    /// <summary>True while anything is holding the player still.</summary>
    public static bool IsLocked { get { return count > 0; } }

    public static void Acquire()
    {
        count++;
    }

    public static void Release()
    {
        count = Mathf.Max(0, count - 1);
    }

    /// <summary>Drops every lock. For a hard reset - a scene change, a bail-out.</summary>
    public static void ReleaseAll()
    {
        count = 0;
    }

    /// <summary>
    /// A static survives Play mode entry in the Editor, so a lock held when the last session
    /// ended would still be held on the next one and the player would start frozen with no way
    /// to tell why. Clearing it as the game starts costs nothing and removes that entire
    /// category of "movement stopped working and I have no idea when".
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetOnPlay()
    {
        count = 0;
    }
}
