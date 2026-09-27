using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>Small helpers shared by the guidance scripts.</summary>
    public static class GuidanceUtil
    {
        private static MobileControlsVisibility touchSource;
        private static bool searched;
        private static int searchedScene = -1;

        /// <summary>
        /// Touch wording or keyboard wording? Asks MobileControlsVisibility first, exactly like
        /// PlatformPromptText does, so every prompt in the game agrees - including the Editor's
        /// touch preview, where Application.isMobilePlatform is always false.
        /// </summary>
        public static bool TouchWanted()
        {
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (!searched || searchedScene != scene || touchSource == null)
            {
                searched = true;
                searchedScene = scene;
                touchSource = Object.FindAnyObjectByType<MobileControlsVisibility>(FindObjectsInactive.Include);
            }

            if (touchSource != null) return touchSource.Wanted;
            return Application.isMobilePlatform;
        }

        /// <summary>A key press that is not someone typing into a field.</summary>
        public static bool KeyPressed(KeyCode key)
        {
            return key != KeyCode.None && Input.GetKeyDown(key) && !TypingGuard.IsTyping;
        }

        /// <summary>'- ' at the start of a line becomes a bullet. Keeps card text easy to write.</summary>
        public static string Bulletize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("- ")) lines[i] = "•  " + t.Substring(2);
            }
            return string.Join("\n", lines);
        }

        /// <summary>Everything a panel does to take the screen: stop the player, free the cursor.
        /// Returns whether the cursor was captured before, so Release can put it back.</summary>
        public static bool HoldPlayer()
        {
            bool wasCaptured = CursorControl.Captured;
            PlayerInputLock.Acquire();
            CursorControl.Free();
            return wasCaptured;
        }

        public static void ReleasePlayer(bool recapture)
        {
            PlayerInputLock.Release();
            if (recapture && !PlayerInputLock.IsLocked) CursorControl.Capture();
        }
    }
}
