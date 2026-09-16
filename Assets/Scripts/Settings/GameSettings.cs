using System;
using UnityEngine;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// Every player-adjustable setting in CircuitCrafter, in one place, saved to
    /// PlayerPrefs and readable from anywhere.
    ///
    /// NOT A MonoBehaviour, and deliberately. Settings have to be readable before
    /// any scene has finished loading, they must survive every scene change, and
    /// there must be exactly one of them. A static gives all three for free; a
    /// singleton component would need a prefab in every scene, a DontDestroyOnLoad,
    /// and a duplicate guard - three things to get wrong for no benefit.
    ///
    /// SAVED TO PlayerPrefs, NOT TO savegame.json. A setting belongs to the device
    /// and the person using it, not to a learner's progress: a trainee who picks up
    /// a different tablet should not inherit someone else's sensitivity, and
    /// reloading an old save should not undo a change made five minutes ago. This
    /// is the same reasoning that keeps AssessmentMode and RepairTicketMode out of
    /// the save file, applied to the other end of the lifetime scale.
    ///
    /// SENSITIVITIES ARE STORED 0-1. The real values are wildly different units -
    /// mouse look is degrees-per-second around 100, touch look is degrees-per-pixel
    /// around 0.15, camera pan is metres-per-pixel around 0.002. A slider bound
    /// directly to any of those is unusable, and a slider bound to all three is
    /// impossible. So the store keeps a 0-1 dial and each Map* method below turns it
    /// into that setting's real range. It also means the ranges can be retuned later
    /// without invalidating everyone's saved preferences.
    /// </summary>
    public static class GameSettings
    {
        // ==================================================================
        // Keys
        // ==================================================================

        private const string KeyMouseLook = "cc.mouseLook01";
        private const string KeyTouchLook = "cc.touchLook01";
        private const string KeyInvertLookY = "cc.invertLookY";
        private const string KeyStationPan = "cc.stationPan01";
        private const string KeyStationZoom = "cc.stationZoom01";
        private const string KeyInvertStationDrag = "cc.invertStationDrag";
        private const string KeyMasterVolume = "cc.masterVolume01";
        private const string KeyMusicVolume = "cc.musicVolume01";
        private const string KeySfxVolume = "cc.sfxVolume01";
        private const string KeyUiScale = "cc.uiScale";
        private const string KeyReduceMotion = "cc.reduceMotion";
        private const string KeyPerformanceMode = "cc.performanceMode";
        private const string KeyFullscreen = "cc.fullscreen";
        private const string KeyMigrated = "cc.migratedCameraZoomPrefs";

        // The keys CameraZoom has been writing since before this existed. Read
        // once, so nobody's existing preferences are silently reset the first time
        // they open the new panel. See Migrate().
        private const string LegacyPanSpeed = "CameraZoom.PanSpeed";
        private const string LegacyZoomSpeed = "CameraZoom.ZoomSpeed";
        private const string LegacyInvertX = "CameraZoom.InvertX";

        // ==================================================================
        // Defaults
        // ==================================================================

        public const float DefaultMouseLook01 = 0.5f;
        public const float DefaultTouchLook01 = 0.5f;
        public const float DefaultStationPan01 = 0.35f;
        public const float DefaultStationZoom01 = 0.35f;
        public const float DefaultMasterVolume01 = 0.8f;

        /// <summary>Music sits UNDER the interface, not in front of it - anything a learner has
        /// to turn down before they can think is too loud.</summary>
        public const float DefaultMusicVolume01 = 0.7f;

        /// <summary>Interaction sounds are feedback, so they start close to full: a click the
        /// learner cannot hear is a click that did not confirm anything.</summary>
        public const float DefaultSfxVolume01 = 0.9f;
        public const float DefaultUiScale = 1f;

        // Real-world ranges each 0-1 dial maps onto. Centred on the values the
        // game already shipped with, so a default slider feels exactly like the
        // game did before there was a slider.
        private const float MinMouseLook = 30f, MaxMouseLook = 220f;      // MouseLook.mouseSensitivity, was 100
        private const float MinTouchLook = 0.05f, MaxTouchLook = 0.35f;   // TouchLookField.touchLookSensitivity, was 0.15
        private const float MinUiScale = 0.85f, MaxUiScale = 1.6f;

        // ==================================================================
        // Values
        // ==================================================================

        private static bool loaded;

        private static float mouseLook01;
        private static float touchLook01;
        private static bool invertLookY;
        private static float stationPan01;
        private static float stationZoom01;
        private static bool invertStationDrag;
        private static float masterVolume01;
        private static float musicVolume01;
        private static float sfxVolume01;
        private static float uiScale;
        private static bool reduceMotion;
        private static bool performanceMode;
        private static bool fullscreen;

        /// <summary>Raised whenever anything changes. SettingsApplier listens and
        /// pushes the new values into whatever is in the current scene.</summary>
        public static event Action OnChanged;

        // ==================================================================
        // Properties
        // ==================================================================

        /// <summary>PC look speed, 0-1. Only shown on desktop.</summary>
        public static float MouseLook01
        {
            get { EnsureLoaded(); return mouseLook01; }
            set { EnsureLoaded(); Set(ref mouseLook01, Mathf.Clamp01(value), KeyMouseLook); }
        }

        /// <summary>Touch look speed, 0-1. Only shown on mobile.
        ///
        /// Kept separate from MouseLook01 rather than shared, and it is worth being
        /// clear why: the two drive different units - degrees per second of mouse
        /// travel versus degrees per pixel of finger drag - so one dial cannot be
        /// right for both. Sharing it would mean a learner who tunes the game on a
        /// classroom PC finds it unusable on their phone, which is the exact
        /// situation a sensitivity setting exists to prevent. Only one of the two
        /// is ever visible, so nobody is asked to set a value they cannot feel.</summary>
        public static float TouchLook01
        {
            get { EnsureLoaded(); return touchLook01; }
            set { EnsureLoaded(); Set(ref touchLook01, Mathf.Clamp01(value), KeyTouchLook); }
        }

        public static bool InvertLookY
        {
            get { EnsureLoaded(); return invertLookY; }
            set { EnsureLoaded(); Set(ref invertLookY, value, KeyInvertLookY); }
        }

        /// <summary>Drag speed inside a station view - the assembly bench and the
        /// laid-open system unit.</summary>
        public static float StationPan01
        {
            get { EnsureLoaded(); return stationPan01; }
            set { EnsureLoaded(); Set(ref stationPan01, Mathf.Clamp01(value), KeyStationPan); }
        }

        public static float StationZoom01
        {
            get { EnsureLoaded(); return stationZoom01; }
            set { EnsureLoaded(); Set(ref stationZoom01, Mathf.Clamp01(value), KeyStationZoom); }
        }

        public static bool InvertStationDrag
        {
            get { EnsureLoaded(); return invertStationDrag; }
            set { EnsureLoaded(); Set(ref invertStationDrag, value, KeyInvertStationDrag); }
        }

        /// <summary>
        /// The one that scales EVERYTHING, through AudioListener.volume. Music and SFX below are
        /// multiplied on top of it, so this is the dial for "quieter overall" and those two are
        /// for "less of that, more of this".
        /// </summary>
        public static float MasterVolume01
        {
            get { EnsureLoaded(); return masterVolume01; }
            set { EnsureLoaded(); Set(ref masterVolume01, Mathf.Clamp01(value), KeyMasterVolume); }
        }

        /// <summary>Background music only. Read by MenuMusic; does not touch sound effects.</summary>
        public static float MusicVolume01
        {
            get { EnsureLoaded(); return musicVolume01; }
            set { EnsureLoaded(); Set(ref musicVolume01, Mathf.Clamp01(value), KeyMusicVolume); }
        }

        /// <summary>Interaction sounds only - parts, hotbar, completion jingles. Read by
        /// SfxPlayer; does not touch music.</summary>
        public static float SfxVolume01
        {
            get { EnsureLoaded(); return sfxVolume01; }
            set { EnsureLoaded(); Set(ref sfxVolume01, Mathf.Clamp01(value), KeySfxVolume); }
        }

        /// <summary>Multiplier on the whole interface, 0.85 to 1.6.
        ///
        /// The single most useful setting in a classroom build, and the one a
        /// desktop-first game usually forgets: the same scene is read on a phone
        /// held at arm's length and on a projector at the back of a room, and the
        /// size that suits one is unreadable on the other.
        ///
        /// It scales the Canvas rather than font sizes, deliberately. Growing text
        /// inside a fixed box makes it overflow or clip; growing the whole canvas
        /// keeps every layout exactly as it was authored, just bigger.</summary>
        /// <summary>
        /// PINNED TO 1. The Interface Size control was removed from the settings panel.
        ///
        /// The property is kept rather than deleted because SettingsApplier and anything else that
        /// reads it keeps compiling, and because the feature can be brought back by restoring the
        /// two lines below - the maths, the exclusion marker and the per-canvas bookkeeping all
        /// still work.
        ///
        /// The GETTER IGNORES THE SAVED VALUE, and that is the part that matters. A learner who had
        /// already set 1.6 would otherwise stay at 1.6 forever with no control left to change it -
        /// the setting removed from the screen but not from their game. Returning the constant means
        /// everyone lands back at the authored layout the moment they update.
        /// </summary>
        public static float UiScale
        {
            get { return DefaultUiScale; }
            set { /* no-op: the control is gone, so nothing may change it */ }
        }

        /// <summary>Skips the station entry animations - the system unit tipping
        /// over, mainly. Charming the first time and slow by the fifth ticket, and
        /// genuinely uncomfortable for a few people, so it is a setting rather than
        /// a decision made for everybody.</summary>
        public static bool ReduceMotion
        {
            get { EnsureLoaded(); return reduceMotion; }
            set { EnsureLoaded(); Set(ref reduceMotion, value, KeyReduceMotion); }
        }

        /// <summary>Lower quality and a 30fps cap. On the mid-range Android tablets
        /// a TESDA lab actually owns, this is the difference between a warm device
        /// at 22fps and a cool one at a steady 30.</summary>
        public static bool PerformanceMode
        {
            get { EnsureLoaded(); return performanceMode; }
            set { EnsureLoaded(); Set(ref performanceMode, value, KeyPerformanceMode); }
        }

        /// <summary>Desktop only.</summary>
        public static bool Fullscreen
        {
            get { EnsureLoaded(); return fullscreen; }
            set { EnsureLoaded(); Set(ref fullscreen, value, KeyFullscreen); }
        }

        // ==================================================================
        // Mapped values - what the game actually uses
        // ==================================================================

        public static float MouseLookSensitivity => Mathf.Lerp(MinMouseLook, MaxMouseLook, MouseLook01);
        public static float TouchLookSensitivity => Mathf.Lerp(MinTouchLook, MaxTouchLook, TouchLook01);

        /// <summary>Multiplier on a station animation's duration: 1 normally, 0 when
        /// Reduce Motion is on.</summary>
        public static float MotionScale => ReduceMotion ? 0f : 1f;

        // ==================================================================
        // Load / save
        // ==================================================================

        /// <summary>Loading is lazy rather than done in a RuntimeInitializeOnLoad
        /// hook, because the first read can happen during another object's Awake
        /// and initialisation order between the two would be undefined. Every getter
        /// calls this; it costs one bool check after the first time.</summary>
        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            Migrate();

            mouseLook01 = PlayerPrefs.GetFloat(KeyMouseLook, DefaultMouseLook01);
            touchLook01 = PlayerPrefs.GetFloat(KeyTouchLook, DefaultTouchLook01);
            invertLookY = PlayerPrefs.GetInt(KeyInvertLookY, 0) == 1;
            stationPan01 = PlayerPrefs.GetFloat(KeyStationPan, DefaultStationPan01);
            stationZoom01 = PlayerPrefs.GetFloat(KeyStationZoom, DefaultStationZoom01);
            invertStationDrag = PlayerPrefs.GetInt(KeyInvertStationDrag, 0) == 1;
            masterVolume01 = PlayerPrefs.GetFloat(KeyMasterVolume, DefaultMasterVolume01);
            musicVolume01 = PlayerPrefs.GetFloat(KeyMusicVolume, DefaultMusicVolume01);
            sfxVolume01 = PlayerPrefs.GetFloat(KeySfxVolume, DefaultSfxVolume01);
            uiScale = PlayerPrefs.GetFloat(KeyUiScale, DefaultUiScale);
            reduceMotion = PlayerPrefs.GetInt(KeyReduceMotion, 0) == 1;
            performanceMode = PlayerPrefs.GetInt(KeyPerformanceMode, Application.isMobilePlatform ? 1 : 0) == 1;
            fullscreen = PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;
        }

        /// <summary>CameraZoom has been saving pan and zoom under its own keys since
        /// before this class existed. Anyone who has already tuned those would
        /// otherwise find them silently reset the first time they open the new
        /// panel - a small betrayal, and an avoidable one. Runs once.</summary>
        private static void Migrate()
        {
            if (PlayerPrefs.GetInt(KeyMigrated, 0) == 1) return;
            PlayerPrefs.SetInt(KeyMigrated, 1);

            // The legacy values are raw speeds, and only CameraZoom knows the range
            // they sit in, so they cannot be converted to a 0-1 dial from here.
            // What CAN be carried across honestly is the invert toggle, which is
            // the same thing in both worlds.
            if (PlayerPrefs.HasKey(LegacyInvertX))
                PlayerPrefs.SetInt(KeyInvertStationDrag, PlayerPrefs.GetInt(LegacyInvertX, 0));

            if (PlayerPrefs.HasKey(LegacyPanSpeed) || PlayerPrefs.HasKey(LegacyZoomSpeed))
            {
                Debug.Log("[GameSettings] Found older CameraZoom preferences. The invert setting has been carried over; " +
                          "pan and zoom speed start from the new defaults, because the old values were stored as raw " +
                          "speeds with no record of the range they came from.");
            }

            PlayerPrefs.Save();
        }

        private static void Set(ref float field, float value, string key)
        {
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }

        private static void Set(ref bool field, bool value, string key)
        {
            if (field == value) return;
            field = value;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }

        /// <summary>Back to factory. Clears the stored values rather than writing
        /// the defaults over them, so a later change to a default reaches anyone who
        /// has reset since.</summary>
        public static void ResetToDefaults()
        {
            foreach (string key in new[]
            {
                KeyMouseLook, KeyTouchLook, KeyInvertLookY, KeyStationPan, KeyStationZoom,
                KeyInvertStationDrag, KeyMasterVolume, KeyMusicVolume, KeySfxVolume,
                KeyUiScale, KeyReduceMotion,
                KeyPerformanceMode, KeyFullscreen,
            })
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
            loaded = false;
            EnsureLoaded();

            Debug.Log("[GameSettings] Reset to defaults.");
            OnChanged?.Invoke();
        }

        /// <summary>A static survives entering Play mode in the Editor, so values
        /// edited in one session would linger into the next without ever being
        /// re-read from disk. Clearing the flag forces one honest reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnPlay()
        {
            loaded = false;
        }
    }
}