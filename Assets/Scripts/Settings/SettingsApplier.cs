using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// Pushes the values in GameSettings into whatever happens to be in the
    /// current scene, and keeps doing it.
    ///
    /// ATTACH THIS TO: one object in your FIRST scene - the main menu. It survives
    /// scene loads and there is only ever one, so it does not want adding to every
    /// scene; a duplicate stands itself down.
    ///
    /// WHY A SEPARATE APPLIER AND NOT PROPERTIES THAT APPLY THEMSELVES
    /// The things being configured are scene objects, and they do not exist yet
    /// when the setting is changed. MouseLook lives in COC1; change the sensitivity
    /// from the main menu and there is nothing to write it to. Change it from the
    /// pause menu inside COC1 and there is - but the COC3 scene loaded afterwards
    /// gets a fresh MouseLook that has never heard of it.
    ///
    /// So applying happens at the three moments it can matter: when this starts,
    /// whenever a setting changes, and whenever a scene finishes loading. That last
    /// one is what makes a setting stick across the whole game rather than only in
    /// the scene it was set in.
    ///
    /// IT WRITES, IT NEVER READS BACK. GameSettings is the only source of truth.
    /// Anything that also writes to these fields - the older CameraSettingsUI, say -
    /// will be overwritten on the next scene load, which is why the guide says to
    /// retire it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SettingsApplier : MonoBehaviour
    {
        public static SettingsApplier Instance { get; private set; }

        [Tooltip("Log what gets applied and to how many objects. Useful once, when a setting appears to do nothing.")]
        [SerializeField] private bool verbose;

        /// <summary>Each Canvas scaler's authored reference resolution, remembered
        /// the first time it is seen. Interface scale divides this, and dividing a
        /// value that has already been divided is how a UI ends up growing every
        /// time a scene loads.</summary>
        [Header("Interface size")]
        [Tooltip("Canvases the Interface Size setting must NOT resize. World Space canvases are skipped already - this is for anything else, such as a menu whose layout was tuned by hand and must stay exactly as authored.")]
        [SerializeField] private List<Canvas> excludedCanvases = new List<Canvas>();

        private readonly Dictionary<CanvasScaler, Vector2> baseReferenceResolutions =
            new Dictionary<CanvasScaler, Vector2>();

        // ==================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy(this), not Destroy(gameObject) - this component often
                // shares an object with other managers, and taking the whole
                // object out would silently kill them too.
                Debug.Log($"[SettingsApplier] A second applier on '{name}' stood down - the one from the first scene is still active.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            GameSettings.OnChanged += Apply;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            GameSettings.OnChanged -= Apply;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Start()
        {
            Apply();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The new scene's objects have all had Awake and OnEnable by now, so
            // there is something to write to.
            baseReferenceResolutions.Clear();
            Apply();
        }

        // ==================================================================

        /// <summary>Also safe to call by hand - from a Settings panel's Close
        /// button, say, though the OnChanged event already covers that.</summary>
        public void Apply()
        {
            ApplyLook();
            ApplyStationCamera();
            ApplyAudio();
            ApplyInterfaceScale();
            ApplyPerformance();
            ApplyScreen();

            if (verbose)
                Debug.Log($"[SettingsApplier] Applied. Look {GameSettings.MouseLookSensitivity:0}, " +
                          $"touch {GameSettings.TouchLookSensitivity:0.00}, UI {GameSettings.UiScale:0.00}, " +
                          $"volume {GameSettings.MasterVolume01:0.00}, reduceMotion {GameSettings.ReduceMotion}.", this);
        }

        private void ApplyLook()
        {
            foreach (var look in FindAll<MouseLook>())
            {
                if (look == null) continue;
                look.mouseSensitivity = GameSettings.MouseLookSensitivity;
                look.invertLookY = GameSettings.InvertLookY;
            }

            foreach (var field in FindAll<TouchLookField>())
            {
                if (field == null) continue;
                field.touchLookSensitivity = GameSettings.TouchLookSensitivity;
            }
        }

        private void ApplyStationCamera()
        {
            foreach (var zoom in FindAll<CameraZoom>())
            {
                if (zoom == null) continue;

                zoom.SetPanSensitivity01(GameSettings.StationPan01);
                zoom.SetZoomSensitivity01(GameSettings.StationZoom01);
                zoom.invertPanX = GameSettings.InvertStationDrag;
                zoom.invertPanY = GameSettings.InvertStationDrag;
            }
        }

        private void ApplyAudio()
        {
            // One line, and it covers every AudioSource in the game at once.
            // Nothing plays sound yet - there is not an AudioSource in the project -
            // so this does nothing today and everything the day the first clip is
            // added, which is the right time for a volume slider to already exist.
            AudioListener.volume = GameSettings.MasterVolume01;
        }

        private void ApplyInterfaceScale()
        {
            float scale = GameSettings.UiScale;

            foreach (var scaler in FindAll<CanvasScaler>())
            {
                if (scaler == null) continue;
                if (IsExcluded(scaler)) continue;

                if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    if (!baseReferenceResolutions.TryGetValue(scaler, out Vector2 authored))
                    {
                        authored = scaler.referenceResolution;
                        baseReferenceResolutions[scaler] = authored;
                    }

                    // Smaller reference resolution = bigger UI. Dividing the
                    // AUTHORED value, never the current one, is what keeps this
                    // idempotent across repeated applies.
                    scaler.referenceResolution = authored / Mathf.Max(0.1f, scale);
                }
                else if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
                {
                    scaler.scaleFactor = scale;
                }
            }
        }

        /// <summary>
        /// True for a canvas the interface-size setting must not touch.
        ///
        /// WHY AN OPT-OUT EXISTS AT ALL
        /// Interface Size is meant to be global - that is the point of it - and
        /// every canvas moving together is correct. But some canvases are not
        /// interface: a world-space canvas standing in for a monitor screen inside
        /// the 3D scene is part of the set, and scaling it because a learner wanted
        /// bigger menu text would resize a computer monitor in the world. World
        /// Space canvases are therefore skipped automatically, and Excluded
        /// Canvases is there for anything else you want left alone.
        ///
        /// EXCLUDED CANVASES ONLY REACHES THIS SCENE
        /// That list holds object references, and Unity cannot hold a reference
        /// from one scene into another. This applier lives in the Main Menu, so
        /// the list can name menu canvases and nothing else - a canvas in COC2
        /// or COC3 cannot be dragged into it, and the field just refuses the
        /// drop with no explanation.
        ///
        /// An IgnoreInterfaceScale component on the canvas itself covers that
        /// case: it travels with the canvas into whatever scene it lives in.
        /// </summary>
        private bool IsExcluded(CanvasScaler scaler)
        {
            var canvas = scaler.GetComponent<Canvas>();

            // A canvas standing in the 3D world is scenery, not interface.
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace) return true;

            // The cross-scene opt-out. Checked before the list because it is the
            // one that works from any scene.
            var marker = scaler.GetComponent<IgnoreInterfaceScale>();
            if (marker != null)
            {
                if (verbose)
                    Debug.Log($"[SettingsApplier] Leaving '{scaler.name}' at its authored size - " +
                              $"IgnoreInterfaceScale: {marker.Reason}", scaler);
                return true;
            }

            if (excludedCanvases == null) return false;

            foreach (var excluded in excludedCanvases)
            {
                if (excluded != null && excluded == canvas) return true;
            }
            return false;
        }

        private void ApplyPerformance()
        {
            if (GameSettings.PerformanceMode)
            {
                QualitySettings.SetQualityLevel(0, true);
                Application.targetFrameRate = 30;
            }
            else
            {
                QualitySettings.SetQualityLevel(Mathf.Max(0, QualitySettings.names.Length - 1), true);
                Application.targetFrameRate = 60;
            }
        }

        private void ApplyScreen()
        {
            // Desktop only. Calling this on a phone does nothing useful and on some
            // Android versions logs a warning every time.
            if (Application.isMobilePlatform) return;
            if (Screen.fullScreen != GameSettings.Fullscreen)
                Screen.fullScreen = GameSettings.Fullscreen;
        }

        // ==================================================================

        /// <summary>Inactive objects included: a station camera's MouseLook is
        /// switched off most of the time, and it still has to have the right
        /// sensitivity the moment it comes on.</summary>
        private static T[] FindAll<T>() where T : Object
        {
            return FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }
}