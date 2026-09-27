using System.Collections.Generic;
using UnityEngine;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Which tutorials THIS learner has already seen, so each one appears once - the first time a
    /// feature or procedure comes up - and not on every visit.
    ///
    /// Kept per learner profile, so on a shared lab PC the second student still gets every tutorial
    /// the first one dismissed. Stored in PlayerPrefs rather than the save file on purpose: the
    /// Story save is wiped by "start fresh", and having the same pop-ups return every time a learner
    /// restarts a COC is exactly the repetition this is meant to prevent.
    ///
    /// Reset it from the Settings screen with a button wired to TutorialProgress.ResetForActiveProfile
    /// (see GuidanceResetButton), or CircuitCrafter > Guidance > Reset Seen Tutorials in the Editor.
    /// </summary>
    public static class TutorialProgress
    {
        private const string Prefix = "cc.tutorial.";

        private static string cachedProfile;
        private static HashSet<string> seen;

        private static string ProfileId =>
            ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfileId : "default";

        private static string IndexKey(string profile) => Prefix + profile + ".index";

        private static HashSet<string> Seen
        {
            get
            {
                string profile = ProfileId;
                if (seen == null || cachedProfile != profile)
                {
                    cachedProfile = profile;
                    seen = new HashSet<string>();
                    string raw = PlayerPrefs.GetString(IndexKey(profile), "");
                    foreach (string id in raw.Split('|'))
                        if (!string.IsNullOrEmpty(id)) seen.Add(id);
                }
                return seen;
            }
        }

        public static bool HasSeen(string tutorialId)
        {
            return !string.IsNullOrEmpty(tutorialId) && Seen.Contains(tutorialId);
        }

        public static void MarkSeen(string tutorialId)
        {
            if (string.IsNullOrEmpty(tutorialId)) return;
            if (!Seen.Add(tutorialId)) return;

            PlayerPrefs.SetString(IndexKey(cachedProfile), string.Join("|", seen));
            PlayerPrefs.Save();
        }

        /// <summary>Every tutorial shows again for the current learner.</summary>
        public static void ResetForActiveProfile()
        {
            string profile = ProfileId;
            PlayerPrefs.DeleteKey(IndexKey(profile));
            PlayerPrefs.Save();
            seen = null;
            Debug.Log($"[TutorialProgress] Tutorials reset for profile '{profile}'.");
        }
    }
}
