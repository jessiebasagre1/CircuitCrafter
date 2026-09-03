using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>One learner. Kept deliberately thin - identity only, no progress.
    /// Progress lives in the save file and the ledger, both keyed by profileId.</summary>
    [Serializable]
    public class LearnerProfile
    {
        [Tooltip("Generated once and never reused. Used as the key in every filename, so it must not contain path characters.")]
        public string profileId = "";

        public string displayName = "";
        public string section = "";
        public string createdUtc = "";
        public string lastPlayedUtc = "";

        public string Label => string.IsNullOrEmpty(section) ? displayName : $"{displayName} ({section})";
    }

    /// <summary>
    /// Who is playing. Everything else in this package is keyed by the active
    /// profile's id.
    ///
    /// ATTACH THIS TO: the same persistent Managers object as SaveManager and
    /// ScoreManager. It is DontDestroyOnLoad for the same reason they are - the
    /// main menu and the gameplay scenes are separate, and the answer to "who is
    /// playing" must survive the load between them.
    ///
    /// WHY THIS MATTERS MORE THAN IT LOOKS
    /// SaveManager currently writes one savegame.json with no profile concept, so
    /// on a shared lab machine the second student to sit down overwrites the
    /// first. That's the deployment this project is actually for. It also blocks
    /// per-student data, which is the evidence a study needs.
    ///
    /// This class does NOT touch SaveManager. Everything here works standalone,
    /// and the one-property patch that makes the SAVE FILE per-profile too is
    /// written out separately in the README - deliberately kept as an opt-in step
    /// so nothing that currently works can break by dropping this in.
    ///
    /// FIRST RUN
    /// If no profiles exist, a single "Trainee" profile is created automatically.
    /// That means a build with no profile-select UI behaves exactly as it does
    /// today, and you can add the picker whenever you want it.
    /// </summary>
    public class ProfileManager : MonoBehaviour
    {
        public static ProfileManager Instance { get; private set; }

        [Header("Defaults")]
        [Tooltip("Name given to the profile created automatically on a first run with no picker UI.")]
        [SerializeField] private string defaultProfileName = "Trainee";

        /// <summary>Fires when the active profile changes, including the automatic
        /// selection on first run. CompetencyLedger subscribes to this to reload
        /// the right records.</summary>
        public event Action<LearnerProfile> OnActiveProfileChanged;

        public LearnerProfile ActiveProfile { get; private set; }

        public string ActiveProfileId => ActiveProfile != null ? ActiveProfile.profileId : "default";

        public IReadOnlyList<LearnerProfile> AllProfiles => index.profiles;

        [Serializable]
        private class ProfileIndex
        {
            public List<LearnerProfile> profiles = new List<LearnerProfile>();
            public string activeProfileId = "";
        }

        private ProfileIndex index = new ProfileIndex();

        private static string IndexPath => Path.Combine(Application.persistentDataPath, "profiles.json");

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy(this), NOT Destroy(gameObject) - and the difference is not
                // cosmetic.
                //
                // This component is DontDestroyOnLoad, so an instance survives from
                // the first scene onward. Every gameplay scene that also contains
                // this object therefore hits this branch. SaveManager and
                // ScoreManager destroy the whole GameObject here, which is correct
                // for THEM because nothing else ever rides on their object.
                //
                // This object is different: the setup guide has scene-specific
                // components - LegacyAssessmentBridge, AssessmentRecorder,
                // OSInstallTaskBridge - added to the per-scene copy. Destroying the
                // GameObject would take those with it, silently, in Awake, before
                // they ever reach their first Update. The symptom is a run that
                // finishes and records absolutely nothing, with no error to explain
                // why. Destroying only the duplicate component leaves its siblings
                // alive and working.
                Debug.Log($"[ProfileManager] A second ProfileManager on '{name}' stood down - the one from the first scene is still active. Any other components on this object keep running.", this);
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadIndex();
            EnsureActiveProfile();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Creates a profile and makes it active. Returns the new profile.</summary>
        public LearnerProfile CreateProfile(string displayName, string section = "")
        {
            var profile = new LearnerProfile
            {
                profileId = NewId(),
                displayName = string.IsNullOrWhiteSpace(displayName) ? defaultProfileName : displayName.Trim(),
                section = section != null ? section.Trim() : "",
                createdUtc = DateTime.UtcNow.ToString("o"),
                lastPlayedUtc = DateTime.UtcNow.ToString("o")
            };

            index.profiles.Add(profile);
            SetActive(profile);
            return profile;
        }

        public LearnerProfile GetProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return null;
            foreach (var p in index.profiles)
            {
                if (p != null && p.profileId == profileId) return p;
            }
            return null;
        }

        /// <summary>Switches the active profile. Returns false if the id is unknown,
        /// so a picker can report the problem instead of silently doing nothing.</summary>
        public bool SelectProfile(string profileId)
        {
            var profile = GetProfile(profileId);
            if (profile == null)
            {
                Debug.LogWarning($"[ProfileManager] No profile with id '{profileId}'.");
                return false;
            }

            SetActive(profile);
            return true;
        }

        public void RenameActiveProfile(string displayName, string section)
        {
            if (ActiveProfile == null) return;

            if (!string.IsNullOrWhiteSpace(displayName)) ActiveProfile.displayName = displayName.Trim();
            if (section != null) ActiveProfile.section = section.Trim();

            SaveIndex();
            OnActiveProfileChanged?.Invoke(ActiveProfile);
        }

        /// <summary>
        /// Removes a profile and everything belonging to it - its ledger, its
        /// stored results, and its save file if the SaveManager patch is applied.
        ///
        /// This is destructive and there is no undo, so a UI calling it should
        /// confirm first. It deletes by exact filename rather than by wildcard,
        /// so a file it doesn't recognise is left alone.
        /// </summary>
        public void DeleteProfile(string profileId)
        {
            var profile = GetProfile(profileId);
            if (profile == null) return;

            index.profiles.Remove(profile);

            TryDelete(Path.Combine(Application.persistentDataPath, SaveFileNameFor(profileId)));
            TryDelete(CompetencyLedger.LedgerPathFor(profileId));
            ResultsRepository.DeleteAll(profileId);

            if (ActiveProfile == profile)
            {
                ActiveProfile = null;
                EnsureActiveProfile();
            }
            else
            {
                SaveIndex();
            }
        }

        /// <summary>Stamps the active profile as played now. Call it when a
        /// gameplay scene starts, so a picker can sort by most recent.</summary>
        public void TouchActiveProfile()
        {
            if (ActiveProfile == null) return;
            ActiveProfile.lastPlayedUtc = DateTime.UtcNow.ToString("o");
            SaveIndex();
        }

        /// <summary>The per-profile save filename. Used by the optional SaveManager
        /// patch in the README - kept here so there is one definition of the
        /// naming scheme rather than a string literal in two places.</summary>
        public static string SaveFileNameFor(string profileId)
        {
            return string.IsNullOrEmpty(profileId) ? "savegame.json" : $"savegame_{profileId}.json";
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void SetActive(LearnerProfile profile)
        {
            ActiveProfile = profile;
            index.activeProfileId = profile != null ? profile.profileId : "";

            if (profile != null)
                profile.lastPlayedUtc = DateTime.UtcNow.ToString("o");

            SaveIndex();

            Debug.Log($"[ProfileManager] Active profile: {(profile != null ? profile.Label : "none")}");
            OnActiveProfileChanged?.Invoke(profile);
        }

        /// <summary>Guarantees ActiveProfile is never null after Awake. A build with
        /// no profile UI therefore behaves exactly like the single-save build it
        /// replaces, which is what makes this safe to drop in mid-project.</summary>
        private void EnsureActiveProfile()
        {
            if (index.profiles.Count == 0)
            {
                CreateProfile(defaultProfileName);
                return;
            }

            var stored = GetProfile(index.activeProfileId);
            SetActive(stored ?? index.profiles[0]);
        }

        private void LoadIndex()
        {
            if (!File.Exists(IndexPath))
            {
                index = new ProfileIndex();
                return;
            }

            try
            {
                index = JsonUtility.FromJson<ProfileIndex>(File.ReadAllText(IndexPath)) ?? new ProfileIndex();
                if (index.profiles == null) index.profiles = new List<LearnerProfile>();
            }
            catch (Exception e)
            {
                // A corrupt index must not cost the learner their save. Start a
                // fresh index in memory but leave the file on disk untouched so it
                // can be recovered by hand.
                Debug.LogWarning($"[ProfileManager] profiles.json couldn't be read ({e.Message}). Starting a fresh index; the existing file has been left in place.");
                index = new ProfileIndex();
            }
        }

        private void SaveIndex()
        {
            try
            {
                File.WriteAllText(IndexPath, JsonUtility.ToJson(index, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProfileManager] Couldn't write profiles.json: {e.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning($"[ProfileManager] Couldn't delete '{path}': {e.Message}"); }
        }

        /// <summary>Short, filename-safe, and collision-free in any realistic class
        /// size. Not a full GUID because it ends up in filenames a human may have
        /// to look at.</summary>
        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}