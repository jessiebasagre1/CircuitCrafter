using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Makes every new build start with no saved data, so a build handed to someone else - or run on
/// your own machine after a playtest in the Editor - behaves exactly like a first install.
///
/// THERE IS NOTHING TO ATTACH. It runs before the first scene loads, which is before
/// ProfileManager, SaveManager and CompetencyLedger have woken, so they all start from an empty
/// folder rather than half-reading files that are about to vanish.
///
/// ---------------------------------------------------------------------------------------------
/// WHAT IS ACTUALLY HAPPENING, BECAUSE IT IS NOT WHAT IT LOOKS LIKE
///
/// No saved data is inside the build. Nothing this project writes ever goes into the build output.
/// What you are seeing is a SHARED FOLDER: Unity gives the Editor and a standalone player on the
/// same machine the same Application.persistentDataPath, derived from the Company Name and Product
/// Name in Player Settings. On Windows that is
///
///     C:\Users\&lt;you&gt;\AppData\LocalLow\&lt;Company Name&gt;\&lt;Product Name&gt;
///
/// So every profile, save, ledger and result you created while testing in the Editor is already
/// sitting there when the build starts, and the build reads them because as far as it is
/// concerned they are its own. On a phone this does not happen on a fresh install, but it does on
/// a device you have installed onto before - Android keeps that folder across reinstalls unless
/// the app opts out of backup.
///
/// This script removes that data once per build, so the first run of a new build is always clean.
/// ---------------------------------------------------------------------------------------------
///
/// HOW IT KNOWS A BUILD IS NEW
/// Application.buildGUID changes every time you build and is empty in the Editor. It is stored in
/// PlayerPrefs after a wipe, so the wipe happens on the FIRST run of each build and never again -
/// a player who quits and reopens the same build keeps their progress.
///
/// THE TRADE-OFF, STATED PLAINLY
/// A rebuild is a new build. If you ship an update to learners who already have progress, this
/// will clear it. That is the right behaviour for demos, defence and testing, and the wrong
/// behaviour for an app in real use - set WipeOnEveryNewBuild to false below before handing out a
/// build that people are meant to keep using across updates.
///
/// IT ONLY TOUCHES THIS PROJECT'S OWN FILES. The list is written out explicitly rather than
/// clearing the whole folder, so nothing Unity or a package keeps there is caught in the blast.
/// </summary>
public static class FreshBuildData
{
    // -----------------------------------------------------------------
    // The two switches. Edit these; there is no Inspector on a static class.
    // -----------------------------------------------------------------

    /// <summary>
    /// true  - the first run of each new build starts with no data (demos, testing, defence).
    /// false - nothing is ever wiped automatically (an app people keep using across updates).
    /// </summary>
    private const bool WipeOnEveryNewBuild = true;

    /// <summary>
    /// Whether to reset audio, controls and accessibility settings too. They live in PlayerPrefs
    /// rather than in the data folder, so they survive a file wipe unless this is on.
    /// </summary>
    private const bool AlsoResetSettings = true;

    private const string StampKey = "CircuitCrafter.BuildStamp";

    // -----------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CheckOnStartup()
    {
        if (!WipeOnEveryNewBuild) return;

        // NEVER IN THE EDITOR, and this is deliberate rather than cautious. Wiping here would
        // delete the profiles and ledgers you are in the middle of testing against, every time you
        // pressed Play. Use the menu item below when you want the Editor's data gone.
        if (Application.isEditor) return;

        string stamp = CurrentStamp();
        if (string.IsNullOrEmpty(stamp)) return;

        if (PlayerPrefs.GetString(StampKey, "") == stamp) return;

        int removed = WipeSavedData();

        if (AlsoResetSettings) PlayerPrefs.DeleteAll();

        // AFTER DeleteAll, not before - otherwise the stamp is wiped along with everything else
        // and the next launch believes it is a new build again, clearing the player's progress
        // every single time they open the game.
        PlayerPrefs.SetString(StampKey, stamp);
        PlayerPrefs.Save();

        Debug.Log($"[FreshBuildData] First run of this build - removed {removed} saved item(s). " +
                  "Starting fresh.");
    }

    /// <summary>
    /// A value that changes with every build. buildGUID is exactly that and costs nothing;
    /// Application.version is the fallback for the rare platform that leaves it empty, which at
    /// least still resets when you bump the version in Player Settings.
    /// </summary>
    private static string CurrentStamp()
    {
        string guid = Application.buildGUID;
        return !string.IsNullOrEmpty(guid) ? guid : Application.version;
    }

    /// <summary>
    /// Deletes this project's saved data. Returns how many items went, so the log can say
    /// something true rather than just "done".
    ///
    /// EVERY PATH HERE IS ONE THIS PROJECT WRITES, and they are listed rather than discovered:
    ///   profiles.json          ProfileManager's index of learners
    ///   savegame*.json         SaveManager, one per profile (and the pre-profile file)
    ///   ledger_*.json          CompetencyLedger, one per profile
    ///   Results/, Exports/     ResultsRepository - run evidence and CSV exports
    ///   Tickets/               TicketSessionState - COC4 ticket progress
    /// </summary>
    private static int WipeSavedData()
    {
        string root = Application.persistentDataPath;
        int removed = 0;

        removed += DeleteFile(Path.Combine(root, "profiles.json"));

        removed += DeleteMatching(root, "savegame*.json");
        removed += DeleteMatching(root, "ledger_*.json");

        removed += DeleteFolder(Path.Combine(root, "Results"));
        removed += DeleteFolder(Path.Combine(root, "Exports"));
        removed += DeleteFolder(Path.Combine(root, "Tickets"));

        return removed;
    }

    private static int DeleteMatching(string folder, string pattern)
    {
        int removed = 0;

        try
        {
            if (!Directory.Exists(folder)) return 0;

            foreach (string path in Directory.GetFiles(folder, pattern))
                removed += DeleteFile(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FreshBuildData] Couldn't list '{pattern}': {e.Message}");
        }

        return removed;
    }

    private static int DeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            File.Delete(path);
            return 1;
        }
        catch (Exception e)
        {
            // Swallowed, never thrown. A file that refuses to go is a stale save; a startup
            // exception is a game that will not open at all, which is far worse.
            Debug.LogWarning($"[FreshBuildData] Couldn't delete '{Path.GetFileName(path)}': {e.Message}");
            return 0;
        }
    }

    private static int DeleteFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            Directory.Delete(path, recursive: true);
            return 1;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FreshBuildData] Couldn't delete folder '{Path.GetFileName(path)}': {e.Message}");
            return 0;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Clears the same data in the Editor, on demand.
    ///
    /// THIS IS THE ONE YOU WILL ACTUALLY USE DAY TO DAY. The Editor and a Windows build share one
    /// data folder, so "the build has my old data in it" is usually solved by running this before
    /// building - the build then starts clean even on the machine you developed it on.
    ///
    /// It asks first, because it deletes every profile and every competency record on this machine
    /// and there is no undo.
    /// </summary>
    [UnityEditor.MenuItem("CircuitCrafter/Wipe Saved Data Now", false, 100)]
    private static void WipeFromMenu()
    {
        bool confirmed = UnityEditor.EditorUtility.DisplayDialog(
            "Wipe saved data",
            "Delete every profile, save, competency ledger, result and COC4 ticket record in\n\n" +
            Application.persistentDataPath +
            "\n\nThis is the folder the Editor AND a Windows build both use. It cannot be undone.",
            "Delete everything", "Cancel");

        if (!confirmed) return;

        int removed = WipeSavedData();

        if (AlsoResetSettings) PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        Debug.Log($"[FreshBuildData] Removed {removed} saved item(s) from {Application.persistentDataPath}. " +
                  "The next Play session, and the next build run on this machine, will start fresh.");
    }

    /// <summary>Opens the data folder, for when you would rather look before deleting.</summary>
    [UnityEditor.MenuItem("CircuitCrafter/Show Saved Data Folder", false, 101)]
    private static void ShowFolder()
    {
        Directory.CreateDirectory(Application.persistentDataPath);
        UnityEditor.EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
#endif
}
