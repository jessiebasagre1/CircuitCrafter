using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Captures and restores game progress for a single ongoing save (no player
/// profiles). Also tracks which COC modes have been completed, which
/// MainMenuController uses to decide which mode buttons are unlocked.
///
/// Persists across scene loads (DontDestroyOnLoad) since the main menu and
/// gameplay scenes are separate — SelectMode remembers what to do and acts
/// on it the moment the next scene finishes loading.
///
/// Confirmed against the real project scripts:
///   - CableConnector.isPlugged is a plain public bool field, and Plug(socket)
///     / Unplug() are the real methods — cables don't report task progress at
///     all (only PCPartItem parts count toward disassemble_pc/assemble_pc), so
///     restore just calls these directly to snap the connector's transform/
///     parent back correctly.
///   - The RAM retention clip class is RAMLock (not RAMLatch) — confirmed
///     to have both Open() and Close().
///   - Scenes always start with every PCPartItem already installed in its
///     HomeSocket. Loading a save therefore only ever REMOVES parts marked
///     as not-installed — it can't re-insert a part into a socket it doesn't
///     already start in, since HomeSocket is lost once removed.
///   - Parts restored as "removed" must also be placed into a HotbarSlot —
///     PCPartItem.Pickup()/PickupInstant() only detach from the socket and
///     hide the 3D object, they know nothing about the hotbar UI. Without
///     this, a part correctly ends up out of its socket on Continue but its
///     icon never appears in any hotbar slot.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Header("Scene References (for restoring things that need object lookups)")]
    [Tooltip("Same list you assigned on BootableUSBCreator — used to look up an ISOImageData by name when restoring a drive's written image.")]
    [SerializeField] private ISOImageData[] knownIsoImages;

    public string CurrentGameMode { get; private set; }

    private List<string> completedModes = new List<string>();

    private bool subscribedToProfiles;

    private bool pendingLoadExisting;
    private bool pendingNewGame;

    /// <summary>True while LoadSave() is actively restoring state. RestoreState()
    /// on ISaveable objects (e.g. TaskManager) re-fires events like OnTaskCompleted
    /// so systems can react to a loaded save — but listeners such as
    /// TaskCheckpointAutosaver react to those same events by calling
    /// SaveCheckpoint(), which would otherwise overwrite savegame.json mid-restore
    /// with only-partially-restored data (e.g. a task later in the same
    /// RestoreGenericSaveables loop that hasn't been re-applied yet). SaveCheckpoint()
    /// checks this and no-ops while it's true.</summary>
    public bool IsRestoringSave { get; private set; }

    /// <summary>Fires only when SaveCheckpoint(showProgressToast: true) is called —
    /// i.e. at a real milestone (task completed, USB created, OS install finished),
    /// not on every minor autosave. UI (see SaveToast) subscribes to this to show a
    /// brief "Progress Saved" confirmation to the player.</summary>
    public event Action OnProgressSaved;

    [Header("Profiles")]
    [Tooltip("Moves an old single-profile savegame.json into whichever profile is active the " +
             "first time this build runs, then deletes it, so existing progress is not lost when " +
             "saves become per-profile. Untick only if you want the old file left alone.")]
    [SerializeField] private bool migrateLegacySave = true;

    /// <summary>
    /// Where this profile's save lives.
    ///
    /// THIS IS THE FIX FOR "EVERY PROFILE SHARES ONE STORY PROGRESS".
    /// It used to be a single savegame.json in persistentDataPath, for the whole device. Every
    /// other piece of learner state in this project is already per profile - the competency ledger
    /// (CompetencyLedger.LedgerPathFor), the run results (ResultsRepository), COC4's tickets
    /// (TicketSessionState) - but the save file, which is what holds completedModes and therefore
    /// every story-mode unlock, was not. So finishing COC1 on one profile unlocked COC2 for
    /// everyone on the device, including a learner who had never played.
    ///
    /// The naming scheme comes from ProfileManager.SaveFileNameFor rather than a string literal,
    /// because that method already existed for exactly this purpose and two definitions of a file
    /// name is how the two halves quietly stop agreeing.
    ///
    /// No ProfileManager in the scene falls back to the old path, so a build without the profile
    /// system behaves exactly as it did before.
    /// </summary>
    private string SavePath
    {
        get
        {
            var profiles = CircuitCrafter.Assessment.ProfileManager.Instance;
            if (profiles == null) return LegacySavePath;

            return Path.Combine(Application.persistentDataPath,
                                CircuitCrafter.Assessment.ProfileManager.SaveFileNameFor(profiles.ActiveProfileId));
        }
    }

    private string LegacySavePath => Path.Combine(Application.persistentDataPath, "savegame.json");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Load completedModes AND the last-known gameMode up front (doesn't
        // need any scene objects to exist), so the main menu can check
        // unlocks immediately.
        //
        // Preloading gameMode here matters even outside the normal
        // MainMenu -> SelectMode() flow: CurrentGameMode is otherwise ONLY
        // ever set inside SelectMode(). If a gameplay scene is entered
        // without going through SelectMode first (e.g. pressing Play
        // directly on the COC1 scene in the Editor while iterating),
        // CurrentGameMode stays null for the whole session — and every
        // SaveCheckpoint() call (task completion, Pause Menu "Main Menu",
        // etc.) would silently write gameMode: null into savegame.json,
        // clobbering whatever valid mode was saved there before. That
        // leaves HasSaveFile() true but GetSavedGameMode() empty, so
        // Continue logs "no saved game mode found" even though a checkpoint
        // clearly just got written. Seeding CurrentGameMode from disk here
        // prevents that overwrite.
        MigrateLegacySaveIfNeeded();
        ReloadFromDisk();

        // A profile switch has to be treated exactly like starting the game as that learner: the
        // cached completedModes below is what every unlock check reads, and without this it would
        // still hold the PREVIOUS profile's unlocks for the rest of the session - the menu would
        // show COC2 unlocked for someone who has never played COC1, which is the bug this whole
        // change is about, just moved from disk into memory.
        TrySubscribeToProfiles();
    }

    private void Start()
    {
        // AGAIN IN Start, AND THIS IS NOT BELT AND BRACES.
        // Awake order between two DontDestroyOnLoad managers is not defined, so ProfileManager's
        // own Awake may not have run when ours did - Instance would still be null, the subscription
        // would never happen, and switching profiles would silently keep the previous learner's
        // unlocks for the whole session. By Start every Awake has run, so this is the first moment
        // the subscription can be relied on. ReloadFromDisk again for the same reason: the active
        // profile may only have been resolved after our Awake read the wrong file.
        if (TrySubscribeToProfiles()) ReloadFromDisk();
    }

    private bool TrySubscribeToProfiles()
    {
        if (subscribedToProfiles) return false;

        var profiles = CircuitCrafter.Assessment.ProfileManager.Instance;
        if (profiles == null) return false;

        profiles.OnActiveProfileChanged += HandleProfileChanged;
        subscribedToProfiles = true;
        return true;
    }

    private void OnDestroy()
    {
        if (!subscribedToProfiles) return;

        var profiles = CircuitCrafter.Assessment.ProfileManager.Instance;
        if (profiles != null) profiles.OnActiveProfileChanged -= HandleProfileChanged;

        subscribedToProfiles = false;
    }

    private void HandleProfileChanged(CircuitCrafter.Assessment.LearnerProfile profile)
    {
        ReloadFromDisk();

        Debug.Log($"[SaveManager] Profile changed to '{(profile != null ? profile.Label : "none")}' - " +
                  $"reloaded their own save. {completedModes.Count} completed mode(s), " +
                  $"game mode '{(string.IsNullOrEmpty(CurrentGameMode) ? "none" : CurrentGameMode)}'.");
    }

    /// <summary>
    /// Re-reads the cached fields for whichever profile is active now.
    ///
    /// CLEARS FIRST, ALWAYS. Reading into the existing values would leave the previous profile's
    /// unlocks in place whenever the new profile has no save file yet - which is precisely the
    /// case that matters, a learner who has not played.
    /// </summary>
    private void ReloadFromDisk()
    {
        completedModes = new List<string>();
        CurrentGameMode = null;

        string path = SavePath;
        if (!File.Exists(path)) return;

        try
        {
            var data = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(path));
            if (data?.completedModes != null) completedModes = data.completedModes;
            if (!string.IsNullOrEmpty(data?.gameMode)) CurrentGameMode = data.gameMode;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] Couldn't pre-read save data: {e.Message}");
        }
    }

    /// <summary>
    /// Moves a pre-profile savegame.json into the active profile, once.
    ///
    /// IT DELETES THE OLD FILE, AND THAT IS WHAT KEEPS IT HONEST. If the legacy file survived, the
    /// next profile to start without a save of its own would inherit the same progress and the
    /// original bug would be back in a new disguise. Moving it means exactly one profile can ever
    /// receive it - the one active on first run, which is the last profile used before the update
    /// and so the most likely owner.
    /// </summary>
    private void MigrateLegacySaveIfNeeded()
    {
        if (!migrateLegacySave) return;

        string legacy = LegacySavePath;
        string target = SavePath;

        if (target == legacy) return;              // no ProfileManager - nothing to move
        if (!File.Exists(legacy)) return;
        if (File.Exists(target)) return;           // this profile already has its own save

        try
        {
            File.Move(legacy, target);

            Debug.Log($"[SaveManager] Moved the old shared save into this profile: " +
                      $"{Path.GetFileName(target)}. Saves are per profile from now on, so other " +
                      "profiles start with no story progress, as they should.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] Couldn't move the old save file: {e.Message}. " +
                             "Story progress stays where it was; nothing is lost.");
        }
    }

    private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (pendingLoadExisting)
        {
            pendingLoadExisting = false;
            LoadSave();
        }
        else if (pendingNewGame)
        {
            pendingNewGame = false;
            SaveCheckpoint();
        }
    }



    // ---------------------------------------------------------------
    // Main menu entry points
    // ---------------------------------------------------------------

    /// <summary>Reads just the gameMode field from the existing save file,
    /// without needing scene objects — used by MainMenuController to decide
    /// whether picking a mode should Continue or start fresh.</summary>
    public string GetSavedGameMode()
    {
        if (!File.Exists(SavePath)) return null;
        try
        {
            var data = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(SavePath));
            return data?.gameMode;
        }
        catch { return null; }
    }

    public string GetSavedProgressLabel()
    {
        if (!File.Exists(SavePath)) return null;
        try
        {
            var data = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(SavePath));
            return data?.progressLabel;
        }
        catch { return null; }
    }

    public bool IsModeCompleted(string modeId) => completedModes.Contains(modeId);

    /// <summary>How many COCs this learner has finished. This - not whether a save FILE exists - is
    /// what "there is something to continue" means now.
    ///
    /// A save file exists the moment anyone plays for a second, because every checkpoint writes
    /// one. Under the always-fresh rule that file holds nothing a learner would recognise as
    /// progress; the only thing in it worth returning to is which COCs they have unlocked.</summary>
    public int CompletedModeCount => completedModes.Count;

    /// <summary>True when at least one COC has been completed, so at least one BEYOND the first is
    /// unlocked and the mode picker has something Continue can usefully show.</summary>
    public bool HasUnlockedProgress => completedModes.Count > 0;

    /// <summary>Sets CurrentGameMode to modeId, but ONLY if it isn't already
    /// set — never overwrites a value SelectMode() already assigned this
    /// session. Call this once near the start of each gameplay scene (e.g.
    /// PCDisassemblySetup.Start()) so CurrentGameMode is correct even when
    /// the scene is entered without going through the Main Menu first (e.g.
    /// pressing Play directly on the scene in the Editor while iterating).
    /// Without this, any SaveCheckpoint() fired during such a session (task
    /// completion, Pause Menu, etc.) writes gameMode: null into
    /// savegame.json — and since SaveManager is DontDestroyOnLoad,
    /// Awake()'s own disk preload only ever runs once per app launch, so it
    /// can't catch this mid-session.</summary>
    public void EnsureGameMode(string modeId)
    {
        if (string.IsNullOrEmpty(modeId)) return;
        if (string.IsNullOrEmpty(CurrentGameMode))
        {
            CurrentGameMode = modeId;
            Debug.Log($"[SaveManager] CurrentGameMode was unset — claimed as '{modeId}' by the active scene.");
        }
    }

    /// <summary>Call this from whatever signifies a mode is finished (e.g.
    /// OSInstallManager.OnInstallComplete for COC1) — unlocks the next mode
    /// and saves immediately.</summary>
    public void MarkModeComplete(string modeId)
    {
        if (!completedModes.Contains(modeId))
            completedModes.Add(modeId);
        SaveCheckpoint();
    }

    /// <summary>True if the scene about to load (or that just loaded) is being
    /// resumed from an existing save, as opposed to started fresh. Scene
    /// scripts that should only run their one-time "first time entering this
    /// level" behaviour (e.g. IntroDialogTrigger's intro popup) — and NOT
    /// repeat it every time the player hits Continue — should check this in
    /// their own Start().</summary>
    public bool IsContinuingSave { get; private set; }

    /// <summary>
    /// The two ways Story Mode progress can work. ONE setting decides both halves of it - what
    /// picking a COC does, and when Continue appears - because they are the same decision.
    ///
    /// Split across two switches they can be set to contradict each other, and the contradiction is
    /// invisible until a learner meets it: a Continue button offering to resume a COC that starts
    /// fresh the moment they pick it. Anything with a coherent and an incoherent setting should not
    /// be able to be set to the incoherent one.
    ///
    /// Serialised by NUMBER, so append new values, never insert.
    /// </summary>
    public enum StoryProgressMode
    {
        /// <summary>THE ORIGINAL BEHAVIOUR. Picking a COC resumes wherever that learner stopped -
        /// parts still seated, tasks still ticked - and Continue appears as soon as a save file
        /// exists, which is a few seconds into the first session.
        ///
        /// Right for a game somebody plays at home over several evenings.</summary>
        ResumeSavedProgress = 0,

        /// <summary>THE CLASSROOM BEHAVIOUR. Picking a COC always starts it from the beginning,
        /// from either button. Only which COCs have been COMPLETED survives, so the unlock chain
        /// still works, and Continue appears only once at least one COC has been earned.
        ///
        /// Right for a lesson: a CSS procedure is performed start to finish, and a learner resuming
        /// halfway through an assembly has practised the second half twice and the first half
        /// never.</summary>
        AlwaysStartCocFresh = 1,
    }

    [Header("Story mode")]
    [Tooltip("How Story Mode progress behaves. This one setting drives BOTH what picking a COC does " +
             "and when the Continue button is shown - see the enum's own notes for each option.")]
    [SerializeField] private StoryProgressMode progressMode = StoryProgressMode.AlwaysStartCocFresh;

    /// <summary>Which progress rule this build uses. Read it rather than keeping a second copy of
    /// the decision anywhere else.</summary>
    public StoryProgressMode ProgressMode => progressMode;

    /// <summary>
    /// Whether the Continue button has anything worth offering.
    ///
    /// The question is genuinely different per mode, which is why it lives here beside the setting
    /// rather than in the menu: resuming asks "is there a saved run", starting fresh asks "has
    /// anything been unlocked". Same button, two different meanings of the same word.
    /// </summary>
    public bool ShouldShowContinue =>
        progressMode == StoryProgressMode.ResumeSavedProgress
            ? HasSaveFile()
            : HasUnlockedProgress;

    /// <summary>Call this from a mode button on the main menu.</summary>
    public void SelectMode(string modeId, string sceneName)
    {
        CurrentGameMode = modeId;

        // WHICH COCs ARE UNLOCKED SURVIVES; HOW FAR INTO ONE THEY GOT DOES NOT.
        //
        // completedModes is not touched here - it is the record the unlock chain reads, and it is
        // written by MarkModeCompleted when a learner finishes a COC. Everything else in the save
        // is per-attempt working state, and ClearProgressKeepingUnlocks drops it so the fresh run
        // cannot inherit a half-assembled PC from the last one.
        if (progressMode == StoryProgressMode.AlwaysStartCocFresh)
        {
            ClearProgressKeepingUnlocks();

            pendingNewGame = true;
            IsContinuingSave = false;
        }
        else if (GetSavedGameMode() == modeId)
        {
            pendingLoadExisting = true;
            IsContinuingSave = true;
        }
        else
        {
            pendingNewGame = true;
            IsContinuingSave = false;
        }

        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// Throws away the working state of whatever attempt came before, and keeps the list of
    /// completed COCs.
    ///
    /// DELIBERATELY NOT DeleteSave(). That one clears completedModes as well, which would relock
    /// every COC the learner had earned - the exact opposite of what Continue is now for. The
    /// difference between the two is the whole point of this feature, so they are separate methods
    /// with names that say which is which.
    /// </summary>
    public void ClearProgressKeepingUnlocks()
    {
        var kept = new List<string>(completedModes);

        if (File.Exists(SavePath)) File.Delete(SavePath);

        completedModes = kept;

        // Written straight back out, so the unlock record survives even if the learner closes the
        // game during the COC they just started. Deleting the file and waiting for the next
        // checkpoint would put every earned unlock one crash away from being lost.
        SaveCheckpoint();

        Debug.Log($"[SaveManager] Story progress reset for a fresh run. " +
                  $"{completedModes.Count} completed COC(s) kept: {string.Join(", ", completedModes)}");
    }

    public bool HasSaveFile() => File.Exists(SavePath);

    /// <summary>
    /// The destructive wipe: every COC relocked, every task cleared, and COC4's tickets reset.
    ///
    /// COC4's ticket progress lives in its OWN per-profile file, not in savegame.json, so deleting
    /// this one never touched it. That is exactly right for Continue - a learner returning to the
    /// hub should find the jobs they closed still closed - and exactly wrong here, where New Game
    /// means start the whole course over and a "new" COC4 that opens with three tickets already
    /// resolved is not a new anything.
    ///
    /// Done HERE rather than in the New Game button, because all four callers of this method are
    /// wipe-everything actions - New Game and three Reset Progress buttons - and a fifth added
    /// later would have to remember. The one place that already means "erase it all" is the place
    /// that should know what "all" includes.
    /// </summary>
    public void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
        completedModes.Clear();

        TicketSessionState.ResetForActiveProfile();
    }

    /// <summary>
    /// What the Reset Progress buttons run: DeleteSave() - story progress, COC unlocks, COC4
    /// tickets - PLUS the competency record, so the competency screen goes back to Not Attempted
    /// and the written-test (quiz) results are cleared with it - PLUS every stored run result for
    /// this learner, so History and the exported CSVs don't go on describing runs the competency
    /// screen no longer has any record of.
    ///
    /// NOT folded into DeleteSave(), on purpose. New Game also calls DeleteSave(), and starting a
    /// fresh story run is no reason to erase what a learner earned in ASSESSMENT mode. Two methods
    /// with names that say which is which - the same reasoning as ClearProgressKeepingUnlocks.
    ///
    /// REVERSES THE ORIGINAL DESIGN HERE ON PURPOSE. This used to leave the stored result
    /// JSON/CSV alone, treating them as an audit trail separate from "progress" - which is the
    /// right call for a permanent record you never want a stray reset to destroy, but wrong for
    /// a training game where "Reset Progress" is explicitly meant to hand the next student (or
    /// the same one, starting over) a genuinely clean slate. If this project ever needs BOTH - a
    /// reset that clears standing but keeps the evidence, for record-keeping - that's a reason
    /// for a second, differently-named method, not for reverting this one.
    /// </summary>
    public void ResetAllProgress()
    {
        DeleteSave();
        ResetCompetencyRecord();
        ResetCompetencyHistory();
    }

    /// <summary>
    /// Deletes every stored run result (JSON evidence + CSV rows) for the active profile, across
    /// all four modules. Separate from ResetCompetencyRecord() rather than folded into it, so
    /// that method's own comment - about what IT touches - stays accurate on its own.
    /// </summary>
    private static void ResetCompetencyHistory()
    {
        string profileId = CircuitCrafter.Assessment.ProfileManager.Instance != null
            ? CircuitCrafter.Assessment.ProfileManager.Instance.ActiveProfileId
            : "default";

        CircuitCrafter.Assessment.ResultsRepository.DeleteAll(profileId);
    }

    private static void ResetCompetencyRecord()
    {
        // Quizzes answered earlier this session must not count toward the next graded run either.
        CircuitCrafter.Assessment.KnowledgeCheck.Begin("");

        var ledger = CircuitCrafter.Assessment.CompetencyLedger.Instance;
        if (ledger != null)
        {
            // Clears the copy in memory, writes the file, and tells the competency screen and
            // badges to redraw - all in one call.
            ledger.ResetActiveLedger();
            return;
        }

        // No ledger in this scene. Delete the learner's ledger file instead, so the next scene
        // that loads one starts empty.
        string profileId = CircuitCrafter.Assessment.ProfileManager.Instance != null
            ? CircuitCrafter.Assessment.ProfileManager.Instance.ActiveProfileId
            : "default";

        try
        {
            string path = CircuitCrafter.Assessment.CompetencyLedger.LedgerPathFor(profileId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] Couldn't clear the competency record: {e.Message}");
        }
    }

    // ---------------------------------------------------------------
    // Save
    // ---------------------------------------------------------------

    /// <summary>Call this at any checkpoint (task completed, stage advanced,
    /// USB created) — also called automatically by OSInstallManager,
    /// BootableUSBCreator, and TaskManager at their own checkpoints.</summary>
    public void SaveCheckpoint(bool showProgressToast = false)
    {
        if (IsRestoringSave)
        {
            // A load is currently in progress and re-firing events (e.g.
            // TaskManager.RestoreState() re-invoking OnTaskCompleted) that some
            // listener is reacting to by checkpointing. Writing now would save
            // half-restored state and clobber the real file. Once LoadSave()
            // finishes, everything is already on disk correctly anyway, so this
            // checkpoint isn't needed.
            Debug.Log("[SaveManager] Ignored SaveCheckpoint() call during LoadSave() restore.");
            return;
        }

        // An assessment is a separate, recorded run - it must not overwrite the learner's
        // Story progress (a half-built PC from an exam would greet them on Continue). Its evidence is
        // saved by ResultsRepository instead.
        if (AssessmentMode.IsActive)
        {
            Debug.Log("[SaveManager] Ignored SaveCheckpoint() during an assessment - Story progress is untouched.");
            return;
        }

        var data = new GameSaveData
        {
            savedAtUtc = DateTime.UtcNow.ToString("o"),
            gameMode = CurrentGameMode,
            completedModes = new List<string>(completedModes),
            progressLabel = BuildProgressLabel()
        };

        CaptureParts(data);
        CaptureCables(data);
        CaptureRamLatches(data);
        CaptureUsbDrives(data);
        CaptureOSInstall(data);
        CaptureGenericSaveables(data);

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SavePath, json);

        Debug.Log($"[SaveManager] Saved checkpoint to {SavePath}");

        if (showProgressToast)
            OnProgressSaved?.Invoke();
    }

    private string BuildProgressLabel()
    {
        if (OSInstallManager.Instance != null && OSInstallManager.Instance.CurrentStageIndex >= 0)
            return OSInstallManager.Instance.CurrentStage.ToString();
        return "Just started";
    }

    private void CaptureParts(GameSaveData data)
    {
        // includeInactive — removed parts get SetActive(false) after their
        // shrink animation (see PCPartItem.ShrinkThenDisable), so a normal
        // FindObjectsByType would miss them entirely.
        var allParts = FindObjectsByType<PCPartItem>(FindObjectsInactive.Include);

        // Dedupe by partId — RestoreParts() only ever tracks ONE object per
        // partId (its own byId dictionary keeps just the first match), so if
        // two PCPartItem objects in the scene accidentally share a partId,
        // writing an entry for both here produces two conflicting PartEntry
        // rows for the same restored object. Whichever entry happened to be
        // last in the list would then silently win on load — e.g. flipping
        // an untouched, still-installed CPU into the hotbar. Keeping this in
        // sync with RestoreParts' "first one wins" rule avoids that.
        var seenIds = new HashSet<string>();
        foreach (var part in allParts)
        {
            if (string.IsNullOrEmpty(part.partId)) continue;

            if (!seenIds.Add(part.partId))
            {
                Debug.LogWarning($"[SaveManager] Duplicate partId '{part.partId}' found on '{part.name}' — " +
                                  $"only the first object with this partId is saved/restored. Check for a " +
                                  $"leftover/duplicate PCPartItem using this same partId.", part);
                continue;
            }

            data.parts.Add(new PartEntry
            {
                partId = part.partId,
                isInstalled = part.HomeSocket != null
            });
        }
    }

    private void CaptureCables(GameSaveData data)
    {
        var allCables = FindObjectsByType<CableConnector>(FindObjectsInactive.Include);

        // Dedupe by name — same reasoning as CaptureParts()'s partId dedupe above.
        // RestoreCables() only ever tracks ONE object per cable name (its own
        // cablesByName dictionary keeps just the first match), so two CableConnector
        // objects sharing a name here would write two conflicting CableEntry rows for
        // the same restored object — last one in the list silently wins on load.
        var seenNames = new HashSet<string>();
        foreach (var cable in allCables)
        {
            if (!seenNames.Add(cable.name))
            {
                Debug.LogWarning($"[SaveManager] Duplicate cable name '{cable.name}' found — " +
                                  $"only the first object with this name is saved/restored. Check for a " +
                                  $"leftover/duplicate CableConnector using this same name.", cable);
                continue;
            }

            data.cables.Add(new CableEntry
            {
                cableName = cable.name,
                isPlugged = cable.isPlugged,
                socketName = cable.pluggedSocket != null ? cable.pluggedSocket.name : ""
            });
        }
    }

    private void CaptureRamLatches(GameSaveData data)
    {
        var allLatches = FindObjectsByType<RAMLock>(FindObjectsInactive.Include);
        foreach (var latch in allLatches)
        {
            data.ramLatches.Add(new RamLatchEntry
            {
                latchName = latch.name,
                isOpen = latch.IsOpen
            });
        }
    }

    private void CaptureUsbDrives(GameSaveData data)
    {
        foreach (var drive in USBDriveItem.AllDrives)
        {
            if (drive == null) continue;

            data.usbDrives.Add(new UsbDriveEntry
            {
                driveName = drive.name,
                isBootable = drive.IsBootable,
                writtenImageName = drive.WrittenImage != null ? drive.WrittenImage.displayName : "",
                isInsertedInPort = drive.IsInsertedInPort
            });
        }
    }

    private void CaptureOSInstall(GameSaveData data)
    {
        data.osInstall.stageIndex = OSInstallManager.Instance != null
            ? OSInstallManager.Instance.CurrentStageIndex
            : -1;
    }

    /// <summary>Finds every MonoBehaviour in the scene implementing ISaveable
    /// and captures its state — lets future systems (TaskManager, etc.)
    /// participate in saving with zero changes needed here.</summary>
    private void CaptureGenericSaveables(GameSaveData data)
    {
        var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
        foreach (var behaviour in allBehaviours)
        {
            if (behaviour is ISaveable saveable)
            {
                data.genericEntries.Add(new GenericEntry
                {
                    key = saveable.SaveKey,
                    json = saveable.CaptureState()
                });
            }
        }
    }

    // ---------------------------------------------------------------
    // Load
    // ---------------------------------------------------------------

    public void LoadSave()
    {
        if (!HasSaveFile())
        {
            Debug.LogWarning("[SaveManager] No save file found at " + SavePath);
            return;
        }

        string json = File.ReadAllText(SavePath);
        var data = JsonUtility.FromJson<GameSaveData>(json);

        CurrentGameMode = data.gameMode;
        completedModes = data.completedModes ?? new List<string>();

        IsRestoringSave = true;
        try
        {
            // EACH STEP IS ISOLATED, ON PURPOSE.
            //
            // These used to run as one straight sequence, which meant the FIRST one to throw
            // silently cancelled every step after it. RestoreParts threw a NullReferenceException
            // on every COC2 load (no HotbarManager in that scene), so RestoreCables,
            // RestoreRamLatches, RestoreUsbDrives, RestoreOSInstall and - the one that actually
            // matters outside COC1 - RestoreGenericSaveables never ran at all. Continue looked like
            // it worked; the module simply came back with none of its own saved state.
            //
            // A step that fails now says so and the rest still run. The console still gets the full
            // exception, so nothing is hidden - it just stops being fatal to everything downstream.
            RunRestoreStep("parts", () => RestoreParts(data));
            RunRestoreStep("cables", () => RestoreCables(data));
            RunRestoreStep("RAM latches", () => RestoreRamLatches(data));
            RunRestoreStep("USB drives", () => RestoreUsbDrives(data));
            RunRestoreStep("OS install", () => RestoreOSInstall(data));
            RunRestoreStep("saveables", () => RestoreGenericSaveables(data));
        }
        finally
        {
            // Always clear this, even if a Restore step throws, so the save
            // system doesn't get permanently stuck refusing to checkpoint.
            IsRestoringSave = false;
        }

        Debug.Log($"[SaveManager] Loaded save from {SavePath} (saved at {data.savedAtUtc})");
    }

    /// <summary>
    /// Runs one restore step and reports it if it throws, instead of letting it cancel the steps
    /// that come after it. See the comment in LoadSave.
    /// </summary>
    private void RunRestoreStep(string what, System.Action step)
    {
        try
        {
            step();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveManager] Restoring {what} failed, so that part of the save was " +
                           "skipped. The remaining steps still ran. Details below.");
            Debug.LogException(e, this);
        }
    }

    private void RestoreParts(GameSaveData data)
    {
        if (data.parts == null || data.parts.Count == 0) return;

        var allParts = FindObjectsByType<PCPartItem>(FindObjectsInactive.Include);
        var byId = new Dictionary<string, PCPartItem>();
        foreach (var part in allParts)
        {
            if (!string.IsNullOrEmpty(part.partId) && !byId.ContainsKey(part.partId))
                byId.Add(part.partId, part);
        }

        foreach (var entry in data.parts)
        {
            if (!byId.TryGetValue(entry.partId, out var part)) continue;

            bool currentlyInstalled = part.HomeSocket != null;

            if (entry.isInstalled && !currentlyInstalled)
            {
                Debug.LogWarning($"[SaveManager] '{entry.partId}' was saved as installed but starts removed in this scene — can't auto re-insert without a socket reference.");
            }
            else if (!entry.isInstalled && currentlyInstalled)
            {
                // Detach from the socket and hide instantly (no shrink tween needed on load).
                part.PickupInstant();

                // PickupInstant() only detaches/hides the 3D object — it has no
                // knowledge of the hotbar UI, so we have to place it into a slot
                // ourselves, exactly like DragController does on a live drop.
                //
                // Only COC1 HAS a hotbar. This line used to dereference
                // HotbarManager.Instance unguarded, so loading a save that recorded a removed part
                // into any other module threw a NullReferenceException here - which, before the
                // isolation added in LoadSave, cancelled every remaining restore step.
                if (HotbarManager.Instance == null)
                {
                    Debug.Log($"[SaveManager] '{entry.partId}' was saved as removed, but this scene " +
                              "has no hotbar to put it in. Detached it and moved on.", this);
                    continue;
                }

                var slot = HotbarManager.Instance.GetFirstEmptySlot();
                if (slot != null)
                    slot.SetItem(part);
                else
                    Debug.LogWarning($"[SaveManager] '{entry.partId}' restored as removed, but the hotbar is full — it has nowhere to appear.");
            }
        }
    }

    private void RestoreCables(GameSaveData data)
    {
        var allCables = FindObjectsByType<CableConnector>(FindObjectsInactive.Include);
        var cablesByName = new Dictionary<string, CableConnector>();
        foreach (var cable in allCables)
        {
            if (!cablesByName.ContainsKey(cable.name))
                cablesByName.Add(cable.name, cable);
        }

        var allSockets = FindObjectsByType<PortSocket>(FindObjectsInactive.Include);
        var socketsByName = new Dictionary<string, PortSocket>();
        foreach (var socket in allSockets)
        {
            if (!socketsByName.ContainsKey(socket.name))
                socketsByName.Add(socket.name, socket);
        }

        foreach (var entry in data.cables)
        {
            if (!cablesByName.TryGetValue(entry.cableName, out var cable)) continue;

            if (entry.isPlugged && !string.IsNullOrEmpty(entry.socketName)
                && socketsByName.TryGetValue(entry.socketName, out var socket))
            {
                cable.Plug(socket);
            }
            else if (!entry.isPlugged && cable.isPlugged)
            {
                cable.Unplug();
            }
        }
    }

    private void RestoreRamLatches(GameSaveData data)
    {
        var allLatches = FindObjectsByType<RAMLock>(FindObjectsInactive.Include);
        var byName = new Dictionary<string, RAMLock>();
        foreach (var latch in allLatches)
        {
            if (!byName.ContainsKey(latch.name))
                byName.Add(latch.name, latch);
        }

        foreach (var entry in data.ramLatches)
        {
            if (!byName.TryGetValue(entry.latchName, out var latch)) continue;

            if (entry.isOpen) latch.Open();
            else latch.Close();
        }
    }

    private void RestoreUsbDrives(GameSaveData data)
    {
        var byName = new Dictionary<string, USBDriveItem>();
        foreach (var drive in USBDriveItem.AllDrives)
        {
            if (drive != null && !byName.ContainsKey(drive.name))
                byName.Add(drive.name, drive);
        }

        foreach (var entry in data.usbDrives)
        {
            if (!byName.TryGetValue(entry.driveName, out var drive)) continue;

            if (entry.isBootable)
            {
                ISOImageData image = FindIsoByName(entry.writtenImageName);
                drive.MarkAsBootable(image);
            }
            else
            {
                drive.ClearBootableState();
            }

            USBPort targetPort = FindPortHolding(drive) ?? FindFirstEmptyPort();
            if (entry.isInsertedInPort && targetPort != null)
                targetPort.PlugIn(drive);
            else if (!entry.isInsertedInPort && targetPort != null && targetPort.CurrentDrive == drive)
                targetPort.Unplug();
        }
    }

    private ISOImageData FindIsoByName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName) || knownIsoImages == null) return null;
        foreach (var iso in knownIsoImages)
        {
            if (iso != null && iso.displayName == displayName) return iso;
        }
        return null;
    }

    private USBPort FindPortHolding(USBDriveItem drive)
    {
        foreach (var port in USBPort.AllPorts)
        {
            if (port != null && port.CurrentDrive == drive) return port;
        }
        return null;
    }

    private USBPort FindFirstEmptyPort()
    {
        foreach (var port in USBPort.AllPorts)
        {
            if (port != null && port.CurrentDrive == null) return port;
        }
        return null;
    }

    private void RestoreOSInstall(GameSaveData data)
    {
        if (OSInstallManager.Instance == null) return;
        if (data.osInstall.stageIndex >= 0)
            OSInstallManager.Instance.RestoreToStage(data.osInstall.stageIndex);
    }

    private void RestoreGenericSaveables(GameSaveData data)
    {
        var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
        var saveablesByKey = new Dictionary<string, ISaveable>();
        foreach (var behaviour in allBehaviours)
        {
            if (behaviour is ISaveable saveable && !saveablesByKey.ContainsKey(saveable.SaveKey))
                saveablesByKey.Add(saveable.SaveKey, saveable);
        }

    }
}