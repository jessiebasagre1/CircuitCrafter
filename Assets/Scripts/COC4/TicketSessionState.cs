using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CircuitCrafter.Assessment;

/// <summary>
/// Every COC4 ticket's status, kept in memory AND written to the active learner's own file so a
/// student can close the game, come back tomorrow, and pick the assignment sheet up where they left
/// it.
///
/// A NOTE ON THE HISTORY OF THIS FILE, because it used to say the opposite.
/// This was deliberately memory-only, and twice had persistence removed, on the reasoning that
/// entering COC4 should hand the learner a fresh set of Open tickets like a new assignment sheet.
/// That reasoning was sound for a module you dip into to practise. It is the wrong call for one a
/// class works through over several sessions: a student who repairs three machines on Monday and
/// finds all three Open again on Tuesday has had their work thrown away, and no amount of
/// tidiness is worth that. Harold asked for it to persist, so it persists - and the deliberate
/// wipe now lives in ResetForActiveProfile(), where it has to be asked for by name.
///
/// IT IS KEYED TO THE LEARNER, NOT THE MACHINE. One lab PC is used by a whole class, so a single
/// shared file would show every student the previous one's finished tickets - and, worse, would let
/// them close a ticket somebody else repaired. The file lives under the active profile, beside that
/// learner's assessment results, and switching profiles reloads it.
///
/// EVERY READ AND WRITE IS WRAPPED. A disk failure must never take the run down: the learner has
/// already done the work in front of them, and losing the record is bad but losing the session is
/// worse. Failures are logged and swallowed, exactly as ResultsRepository does it.
/// </summary>
public static class TicketSessionState
{
    private const string TicketsFolder = "Tickets";

    private static readonly Dictionary<string, TicketStatus> statusById = new Dictionary<string, TicketStatus>();

    /// <summary>Which profile the dictionary above currently holds. Null means nothing is loaded
    /// yet. This is what makes switching learners mid-session safe.</summary>
    private static string loadedProfileId;

    // ==================================================================
    // Public API - unchanged, so nothing that calls this needs touching
    // ==================================================================

    /// <summary>Returns TicketStatus.Open for any ticket id this learner has not recorded.</summary>
    public static TicketStatus Get(string ticketId)
    {
        if (string.IsNullOrEmpty(ticketId)) return TicketStatus.Open;

        EnsureLoaded();
        return statusById.TryGetValue(ticketId, out var status) ? status : TicketStatus.Open;
    }

    public static void Set(string ticketId, TicketStatus status)
    {
        if (string.IsNullOrEmpty(ticketId)) return;

        EnsureLoaded();
        statusById[ticketId] = status;

        // Written through on every change rather than at some later checkpoint. A ticket is
        // finished perhaps a dozen times in a whole module, so the cost is nothing - and the
        // alternative is choosing a moment to flush, which is the same as choosing which crash
        // loses the learner's work.
        Save();

        Debug.Log($"[TicketSessionState] Set '{ticketId}' = {status}. " +
                  $"({statusById.Count} ticket(s) recorded for profile '{loadedProfileId}'.)");
    }

    /// <summary>
    /// Forgets what is in memory. The next read loads the learner's file again, so this NO LONGER
    /// throws progress away.
    ///
    /// MenuNavigator calls this every time the Main Menu appears, and that call is now harmless by
    /// design rather than by your remembering to untick a box. Leaving Clear Ticket Progress On
    /// Start ticked simply drops the cache and reloads it; unticking it skips even that. Neither
    /// can cost a learner a finished ticket, which is the property worth having when the setting
    /// lives in a different scene from the thing it affects.
    /// </summary>
    public static void Clear()
    {
        int had = statusById.Count;
        statusById.Clear();
        loadedProfileId = null;
        Debug.Log($"[TicketSessionState] Clear() - dropped {had} cached ticket(s). " +
                  "The learner's saved progress is untouched and reloads on the next read.");
    }

    /// <summary>
    /// The real wipe: forgets everything AND deletes the active learner's file, so COC4 genuinely
    /// starts over with a fresh assignment sheet.
    ///
    /// Deliberately not called from anywhere. Wire it to an instructor's "Start a new assignment
    /// sheet" button if you want one - it should take a press and a person, not a scene load.
    /// </summary>
    public static void ResetForActiveProfile()
    {
        string profileId = ActiveProfileId;

        statusById.Clear();
        loadedProfileId = profileId;

        try
        {
            string path = PathFor(profileId);
            if (File.Exists(path)) File.Delete(path);
            Debug.Log($"[TicketSessionState] Reset every ticket for profile '{profileId}'.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[TicketSessionState] Could not delete the ticket file for " +
                             $"'{profileId}': {e.Message}");
        }
    }

    /// <summary>Where this learner's tickets are stored. Useful in a bug report.</summary>
    public static string SavePathForActiveProfile() => PathFor(ActiveProfileId);

    // ==================================================================
    // Loading and saving
    // ==================================================================

    private static string ActiveProfileId =>
        ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfileId : "default";

    private static string PathFor(string profileId)
    {
        string safe = string.IsNullOrEmpty(profileId) ? "default" : profileId;
        return Path.Combine(Application.persistentDataPath, TicketsFolder, safe + ".json");
    }

    /// <summary>
    /// Loads the active learner's file if it is not already the one in memory.
    ///
    /// Checked on EVERY read rather than once at startup, because the active profile can change
    /// while the game is running - a student finishes, hands the machine over, and the next one
    /// picks their name. Loading once would show the second student the first one's tickets, and
    /// the first write would then save them under the second student's name.
    /// </summary>
    private static void EnsureLoaded()
    {
        string profileId = ActiveProfileId;
        if (loadedProfileId == profileId) return;

        statusById.Clear();
        loadedProfileId = profileId;

        try
        {
            string path = PathFor(profileId);
            if (!File.Exists(path))
            {
                Debug.Log($"[TicketSessionState] No saved tickets for profile '{profileId}' yet - " +
                          "starting with every ticket Open.");
                return;
            }

            var saved = JsonUtility.FromJson<SavedTickets>(File.ReadAllText(path));
            if (saved == null || saved.entries == null) return;

            foreach (var entry in saved.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.ticketId)) continue;
                statusById[entry.ticketId] = entry.status;
            }

            Debug.Log($"[TicketSessionState] Loaded {statusById.Count} ticket(s) for profile '{profileId}'.");
        }
        catch (System.Exception e)
        {
            // Left empty rather than half-filled. A partly-read file would show some tickets
            // Resolved and silently reopen the rest, which looks like the game losing work at
            // random - far harder to report than a clean start with a warning in the Console.
            statusById.Clear();
            Debug.LogWarning($"[TicketSessionState] Could not read the ticket file for " +
                             $"'{profileId}', so every ticket starts Open: {e.Message}");
        }
    }

    private static void Save()
    {
        string profileId = loadedProfileId ?? ActiveProfileId;

        try
        {
            string path = PathFor(profileId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var saved = new SavedTickets();
            foreach (var pair in statusById)
                saved.entries.Add(new SavedTicket { ticketId = pair.Key, status = pair.Value });

            File.WriteAllText(path, JsonUtility.ToJson(saved, true));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[TicketSessionState] Could not save tickets for '{profileId}': {e.Message}");
        }
    }

    // ==================================================================

    /// <summary>JsonUtility cannot serialise a Dictionary, so the pairs go to disk as a list.</summary>
    [System.Serializable]
    private class SavedTickets
    {
        public List<SavedTicket> entries = new List<SavedTicket>();
    }

    [System.Serializable]
    private class SavedTicket
    {
        public string ticketId;

        // Serialised by NAME, not by the enum's number, because JsonUtility writes an enum as its
        // integer - so reordering TicketStatus would silently turn every saved Resolved ticket into
        // something else. Appending to that enum is still the rule; this just means a mistake there
        // cannot reach into files already written.
        public string statusName = TicketStatus.Open.ToString();

        public TicketStatus status
        {
            get
            {
                return System.Enum.TryParse(statusName, out TicketStatus parsed)
                    ? parsed
                    : TicketStatus.Open;
            }
            set { statusName = value.ToString(); }
        }
    }
}