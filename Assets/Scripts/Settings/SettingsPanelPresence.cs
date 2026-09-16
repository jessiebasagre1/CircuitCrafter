using UnityEngine;
using UnityEngine.SceneManagement;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// Makes one settings panel work in EVERY scene, not just the main menu.
    ///
    /// ATTACH THIS TO: the root of your settings panel prefab, on the same GameObject as
    /// SettingsPanel (or a parent of it).
    ///
    /// IT DOES THREE THINGS THE PANEL ITSELF SHOULD NOT HAVE TO KNOW ABOUT:
    ///
    ///   1. RELEASES THE CURSOR AND HOLDS THE PLAYER STILL while the panel is open. In the main
    ///      menu this is a no-op, because the cursor is already free. In COC1-COC4 it is the
    ///      difference between a usable settings screen and one where the camera spins wildly
    ///      the moment you move the mouse towards a slider, because MouseLook is still reading
    ///      the mouse underneath the panel.
    ///
    ///   2. PUTS THE CURSOR BACK THE WAY IT FOUND IT. It remembers whether the cursor was
    ///      captured BEFORE the panel opened and only re-captures if it was. Without that, the
    ///      shared prefab would capture the cursor on closing the panel in the MAIN MENU, where
    ///      nothing else ever captures it, leaving the menu unclickable.
    ///
    ///   3. OPTIONALLY SPAWNS ITSELF in any scene that has no settings panel - see the
    ///      bootstrapper at the bottom. That is opt-in: it only happens if the prefab is sitting
    ///      in a Resources folder.
    ///
    /// IT DOES NOT TOUCH THE PANEL'S OWN BEHAVIOUR. Open, Close and TogglePanel are still the
    /// panel's; this only reacts to them.
    /// </summary>
    [DisallowMultipleComponent]
    public class SettingsPanelPresence : MonoBehaviour
    {
        /// <summary>Resources path the bootstrapper looks for. No file extension, no leading slash.</summary>
        public const string ResourcePath = "SettingsPanel";

        [Header("Panel")]
        [Tooltip("The SettingsPanel this guards. Found on this object or in its children if left empty.")]
        [SerializeField] private SettingsPanel panel;

        [Header("While the panel is open")]
        [Tooltip("Release the mouse cursor so the learner can actually click the sliders.\n\n" +
                 "Routed through CursorControl, so a phone - where there is no cursor and grabbing " +
                 "one pins every tap to the screen centre - is left alone.")]
        [SerializeField] private bool freeCursor = true;

        [Tooltip("Hold the player still, so WASD does not walk the character around behind the " +
                 "panel and the mouse does not turn the camera. Counted, so it nests correctly " +
                 "with a station or dialog that is already holding a lock.")]
        [SerializeField] private bool lockPlayerInput = true;

        [Tooltip("Freeze the game with Time.timeScale = 0.\n\n" +
                 "Leave OFF unless you want it: COC1's cable ropes and several tweens run on " +
                 "scaled time and will simply stop, which is fine, but an assessment TIMER that " +
                 "runs on scaled time would also stop, and pausing it is a way to cheat.")]
        [SerializeField] private bool pauseGame = false;

        [Header("Hotkey")]
        [Tooltip("Optional key to open and close the panel. Leave as None to rely on the gear " +
                 "button in the prefab.\n\n" +
                 "ESCAPE IS A BAD CHOICE HERE: COC2 and COC3 already use Escape to leave a " +
                 "station, so the same press would back out of a station AND open settings.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.None;

        [Tooltip("Ignore the hotkey while a dialog or station already holds an input lock, so the " +
                 "settings screen cannot be opened on top of a tutorial popup.")]
        [SerializeField] private bool ignoreHotkeyWhileLocked = true;

        [Header("Diagnostics")]
        [SerializeField] private bool logTransitions = false;

        private bool wasOpen;
        private bool holdingInputLock;
        private bool cursorWasCaptured;
        private float savedTimeScale = 1f;

        // ==================================================================

        private void Awake()
        {
            if (panel == null) panel = GetComponentInChildren<SettingsPanel>(includeInactive: true);

            if (panel == null)
                Debug.LogWarning("[SettingsPanelPresence] No SettingsPanel found on this object or " +
                                 "its children, so nothing will be guarded.", this);
        }

        private void Update()
        {
            if (panel == null) return;

            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            {
                bool blocked = ignoreHotkeyWhileLocked && !panel.IsOpen && PlayerInputLock.IsLocked;
                if (!blocked) panel.TogglePanel();
            }

            // Polled rather than event-driven, because Open/Close/TogglePanel are all public and
            // can be called from a button, a pause menu or another script. Watching the result
            // catches every route; subscribing to one of them would miss the others.
            bool open = panel.IsOpen;
            if (open == wasOpen) return;

            wasOpen = open;
            if (open) HandleOpened();
            else HandleClosed();
        }

        private void HandleOpened()
        {
            cursorWasCaptured = CursorControl.Captured;

            if (freeCursor && cursorWasCaptured) CursorControl.Free();

            if (lockPlayerInput && !holdingInputLock)
            {
                PlayerInputLock.Acquire();
                holdingInputLock = true;
            }

            if (pauseGame)
            {
                savedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            if (logTransitions)
                Debug.Log($"[SettingsPanelPresence] Opened. Cursor was " +
                          $"{(cursorWasCaptured ? "captured" : "free")}, input lock " +
                          $"{(holdingInputLock ? "taken" : "not taken")}.", this);
        }

        private void HandleClosed()
        {
            if (pauseGame) Time.timeScale = savedTimeScale;

            if (holdingInputLock)
            {
                PlayerInputLock.Release();
                holdingInputLock = false;
            }

            // ONLY if it was captured before. Re-capturing unconditionally would grab the cursor
            // in the main menu, where nothing ever captures it, and leave the menu unclickable.
            if (freeCursor && cursorWasCaptured && !PlayerInputLock.IsLocked)
                CursorControl.Capture();

            if (logTransitions)
                Debug.Log("[SettingsPanelPresence] Closed, state restored.", this);
        }

        private void OnDisable()
        {
            // A scene change with the panel still open would otherwise leak the lock, and the
            // learner arrives in the next scene unable to move with nothing on screen to blame.
            if (holdingInputLock)
            {
                PlayerInputLock.Release();
                holdingInputLock = false;
            }

            if (pauseGame && wasOpen) Time.timeScale = savedTimeScale;
            wasOpen = false;
        }

        // ==================================================================
        // Optional auto-spawn
        // ==================================================================

        /// <summary>
        /// Drops the settings prefab into any scene that does not already have one.
        ///
        /// OPT-IN BY CONSTRUCTION: it loads "SettingsPanel" from a Resources folder, so if you
        /// have not put the prefab in one, this does nothing at all and you place the prefab in
        /// each scene by hand instead. Both approaches work; this one just saves you remembering.
        ///
        /// A scene that ALREADY contains a settings panel is left alone, so a scene where you
        /// want the panel positioned or configured differently simply wins.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallBootstrapper()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;

            // AfterSceneLoad runs once the FIRST scene is already up, and sceneLoaded will not
            // fire again for it - so that one has to be handled here.
            EnsurePanelExists();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;
            EnsurePanelExists();
        }

        private static void EnsurePanelExists()
        {
#if UNITY_2023_1_OR_NEWER
            // FindObjectsInactive.Include matters: a settings panel is normally authored with its
            // root active but its contents switched off, and some scenes switch the whole thing
            // off until needed. Missing one and spawning a second is worse than not spawning.
            var existing = Object.FindAnyObjectByType<SettingsPanel>(FindObjectsInactive.Include);
#else
            var existing = Object.FindObjectOfType<SettingsPanel>(true);
#endif
            if (existing != null) return;

            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null) return;     // not using the Resources route - nothing to do

            var spawned = Object.Instantiate(prefab);
            spawned.name = prefab.name;     // without this every scene gets a "(Clone)" suffix

            Debug.Log($"[SettingsPanelPresence] No settings panel in '{SceneManager.GetActiveScene().name}' " +
                      $"- spawned one from Resources/{ResourcePath}.", spawned);
        }
    }
}
