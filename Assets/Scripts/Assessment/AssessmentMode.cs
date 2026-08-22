using UnityEngine;

/// <summary>
/// A one-flag record of "the player is in Assessment Mode right now", set by the Main Menu
/// just before it loads a gameplay scene and read by that scene when it starts.
///
/// WHY A STATIC AND NOT THE SAVE FILE
/// Assessment Mode is a property of THIS PLAY SESSION, not of the player's saved progress -
/// it must not be written to savegame.json, must not affect campaign unlocks, and must be
/// forgotten the moment they return to the menu. A static survives LoadScene (which is the
/// only thing it needs to survive) and dies with the application, which is exactly the
/// lifetime we want.
///
/// Nothing has to reference this: a scene with no assessment components behaves exactly as
/// it always has, whatever the flag says.
/// </summary>
public static class AssessmentMode
{
    /// <summary>True while an assessment run is in progress.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>Which module is being assessed, e.g. "COC1".</summary>
    public static string ModeId { get; private set; }

    /// <summary>Called by AssessmentMenuController immediately before loading the scene.</summary>
    public static void Begin(string modeId)
    {
        IsActive = true;
        ModeId = modeId;
        Debug.Log($"[AssessmentMode] Started for {modeId}. Campaign saving and tutorials are suppressed.");
    }

    /// <summary>Called when returning to the Main Menu, or when the run finishes.</summary>
    public static void End()
    {
        if (!IsActive) return;
        Debug.Log($"[AssessmentMode] Ended for {ModeId}.");
        IsActive = false;
        ModeId = null;
    }

    /// <summary>True if an assessment for this specific module is running.</summary>
    public static bool IsAssessing(string modeId)
    {
        return IsActive && ModeId == modeId;
    }
}
