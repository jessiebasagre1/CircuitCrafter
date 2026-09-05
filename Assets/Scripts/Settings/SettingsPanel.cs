using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// The Settings screen: a category list down the left, one page of controls at
    /// a time on the right, and a floating bubble that explains whichever setting
    /// the learner asked about.
    ///
    /// ATTACH THIS TO: the settings panel object on your Canvas. Menu:
    /// CircuitCrafter > Build Settings Panel builds and wires the whole thing.
    ///
    /// WHY CATEGORIES AND NOT ONE LONG COLUMN
    /// Eleven controls in a single list is a wall: everything has equal weight, so
    /// nothing is findable, and on a phone half of it is below the fold. Four
    /// named groups, one visible at a time, means the learner reads four words and
    /// then four rows.
    ///
    /// THE HELP BUBBLE OPENS BESIDE THE QUESTION MARK THAT SUMMONED IT.
    /// Three things keep that from going wrong, and all three are worth knowing
    /// about because each is a real failure people hit:
    ///
    ///   Its HEIGHT comes from the text, measured before it is drawn, so a one-line
    ///   tip is not a half-empty box and a long one is not clipped.
    ///
    ///   It FLIPS to the left of the button when there is not room on the right,
    ///   rather than being clamped - a clamped bubble ends up sitting on top of the
    ///   very control it is explaining.
    ///
    ///   All the positioning happens in the PANEL'S OWN LOCAL SPACE, never in screen
    ///   pixels. The panel, the button and the bubble all scale together, so a
    ///   bubble that fits at Interface Size 1.0 still fits at 1.6.
    ///
    /// EVERY FIELD IS OPTIONAL. A pause-menu copy can show one category and leave
    /// the rest empty, using the same component.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        /// <summary>One tab and the page it shows.</summary>
        [System.Serializable]
        public class Category
        {
            [Tooltip("Only for reading in the Inspector - the button's own label is what the learner sees.")]
            public string name;

            public Button tabButton;

            [Tooltip("The page of rows shown when this tab is selected.")]
            public GameObject page;
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button resetButton;

        [Header("Categories")]
        [SerializeField] private List<Category> categories = new List<Category>();

        [SerializeField] private Color tabIdleColor = new Color(0.16f, 0.19f, 0.18f, 1f);
        [SerializeField] private Color tabActiveColor = new Color(0.13f, 0.31f, 0.26f, 1f);
        [SerializeField] private Color tabIdleText = new Color(0.72f, 0.77f, 0.74f, 1f);
        [SerializeField] private Color tabActiveText = new Color(0.62f, 0.93f, 0.78f, 1f);

        [Header("Help bubble")]
        [Tooltip("The floating bubble. Must be a child of this panel, and the LAST child, so it draws over the rows.")]
        [SerializeField] private RectTransform helpBubble;

        [SerializeField] private TMP_Text helpTitle;
        [SerializeField] private TMP_Text helpBody;
        [SerializeField] private Button helpCloseButton;

        [Tooltip("How wide the bubble is. The height is worked out from the text, so a short tip gets a short bubble.")]
        [Min(160f)]
        [SerializeField] private float bubbleWidth = 340f;

        [Tooltip("Gap between the question mark and the bubble.")]
        [SerializeField] private float bubbleGap = 10f;

        [Tooltip("How close the bubble may come to the edge of the panel before it is pushed back inside.")]
        [SerializeField] private float bubbleEdgePadding = 12f;

        [Tooltip("Padding inside the bubble, used when measuring the text.")]
        [SerializeField] private Vector2 bubblePadding = new Vector2(18f, 16f);

        [Header("Controls - look")]
        [Tooltip("The whole row object, hidden on mobile.")]
        [SerializeField] private GameObject mouseLookRow;
        [SerializeField] private Slider mouseLookSlider;
        [SerializeField] private TMP_Text mouseLookValue;

        [Tooltip("The whole row object, hidden on desktop.")]
        [SerializeField] private GameObject touchLookRow;
        [SerializeField] private Slider touchLookSlider;
        [SerializeField] private TMP_Text touchLookValue;

        [SerializeField] private Toggle invertLookYToggle;

        [Header("Controls - station camera")]
        [SerializeField] private Slider stationPanSlider;
        [SerializeField] private TMP_Text stationPanValue;
        [SerializeField] private Slider stationZoomSlider;
        [SerializeField] private TMP_Text stationZoomValue;
        [SerializeField] private Toggle invertStationDragToggle;

        [Header("Display")]
        [SerializeField] private Slider uiScaleSlider;
        [SerializeField] private TMP_Text uiScaleValue;
        [SerializeField] private Toggle reduceMotionToggle;
        [SerializeField] private Toggle performanceModeToggle;

        [Tooltip("The whole row object, hidden on mobile.")]
        [SerializeField] private GameObject fullscreenRow;
        [SerializeField] private Toggle fullscreenToggle;

        [Header("Audio")]
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private TMP_Text masterVolumeValue;

        [Header("Testing")]
        [Tooltip("Shows every row in the Editor regardless of platform, so the mobile-only controls can be checked without a build. Has no effect in a real build.")]
        [SerializeField] private bool showAllRowsInEditor = true;

        private int activeCategory = -1;
        private string currentHelpId;
        private bool initialised;
        private bool openRequested;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (resetButton != null) resetButton.onClick.AddListener(ResetAll);
            if (helpCloseButton != null) helpCloseButton.onClick.AddListener(ClearHelp);

            for (int i = 0; i < categories.Count; i++)
            {
                // Captured into a local - a loop variable used directly inside the
                // closure would leave every tab pointing at the last index.
                int index = i;
                if (categories[i] != null && categories[i].tabButton != null)
                    categories[i].tabButton.onClick.AddListener(() => ShowCategory(index));
            }

            WireSlider(mouseLookSlider, v => GameSettings.MouseLook01 = v);
            WireSlider(touchLookSlider, v => GameSettings.TouchLook01 = v);
            WireSlider(stationPanSlider, v => GameSettings.StationPan01 = v);
            WireSlider(stationZoomSlider, v => GameSettings.StationZoom01 = v);
            WireSlider(masterVolumeSlider, v => GameSettings.MasterVolume01 = v);
            WireSlider(uiScaleSlider, v => GameSettings.UiScale = v);

            WireToggle(invertLookYToggle, v => GameSettings.InvertLookY = v);
            WireToggle(invertStationDragToggle, v => GameSettings.InvertStationDrag = v);
            WireToggle(reduceMotionToggle, v => GameSettings.ReduceMotion = v);
            WireToggle(performanceModeToggle, v => GameSettings.PerformanceMode = v);
            WireToggle(fullscreenToggle, v => GameSettings.Fullscreen = v);
        }

        private void Start()
        {
            Initialise();
        }

        /// <summary>
        /// The one-time setup, called from Start OR from the first Open - whichever
        /// happens first.
        ///
        /// WHY IT CANNOT SIMPLY LIVE IN Start
        /// If this panel's object starts INACTIVE in the scene, Unity never runs
        /// Start at all. The menu's Settings button still reaches Open(), because a
        /// UnityEvent invokes its target whether the object is active or not - so
        /// Open() activates the object, Unity finally runs Start in that same frame,
        /// and Start's last line hides the panel again. The learner sees nothing,
        /// clicks a second time, and now it works.
        ///
        /// That is the "I have to click Settings twice" bug, and it is entirely mine:
        /// hiding unconditionally in Start assumed Start always ran before anyone
        /// asked for the panel. The openRequested flag is what breaks that
        /// assumption - Start will not close a panel that has just been asked to open.
        ///
        /// The hide still cannot move up into Awake, for the original reason:
        /// deactivating Panel Root during Awake stops Unity ever calling Start, and
        /// the controls would be wired but never populated.
        /// </summary>
        private void Initialise()
        {
            if (initialised) return;
            initialised = true;

            ApplyPlatformVisibility();
            Refresh();
            ShowCategory(0);
            ClearHelp();

            if (!openRequested && panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnEnable() => GameSettings.OnChanged += Refresh;
        private void OnDisable() => GameSettings.OnChanged -= Refresh;

        // ==================================================================
        // Open / close
        // ==================================================================

        public void Open()
        {
            // Set BEFORE Initialise, so that if this is the call that finally makes
            // Unity run Start, Start knows not to hide what it is being asked to show.
            openRequested = true;
            Initialise();

            ApplyPlatformVisibility();
            Refresh();
            if (activeCategory < 0) ShowCategory(0);
            ClearHelp();
            if (panelRoot != null) panelRoot.SetActive(true);
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void TogglePanel()
        {
            if (panelRoot == null) return;
            if (panelRoot.activeSelf) Close();
            else Open();
        }

        public void ResetAll()
        {
            GameSettings.ResetToDefaults();
            Refresh();
        }

        // ==================================================================
        // Categories
        // ==================================================================

        /// <summary>Wired to each tab button. Also safe to call from elsewhere - a
        /// pause menu could open the screen straight onto Controls.</summary>
        public void ShowCategory(int index)
        {
            if (categories.Count == 0) return;

            activeCategory = Mathf.Clamp(index, 0, categories.Count - 1);

            for (int i = 0; i < categories.Count; i++)
            {
                var category = categories[i];
                if (category == null) continue;

                bool selected = i == activeCategory;

                if (category.page != null) category.page.SetActive(selected);
                PaintTab(category.tabButton, selected);
            }

            // The old tip belongs to a setting that is no longer on screen, and a
            // help box describing something the learner cannot see is worse than an
            // empty one.
            ClearHelp();
        }

        private void PaintTab(Button tab, bool selected)
        {
            if (tab == null) return;

            var background = tab.targetGraphic as Graphic;
            if (background != null) background.color = selected ? tabActiveColor : tabIdleColor;

            var label = tab.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = selected ? tabActiveText : tabIdleText;
        }

        // ==================================================================
        // Help bubble
        // ==================================================================

        /// <summary>
        /// Opens the bubble beside the question mark that was pressed.
        ///
        /// PRESSING THE SAME ONE AGAIN CLOSES IT. Without that, a bubble opened by
        /// mistake has no obvious way out on a touch screen except hunting for the
        /// small x, and the button the learner instinctively presses again is the
        /// one they just pressed.
        /// </summary>
        public void ShowHelp(string settingId, RectTransform anchor)
        {
            if (helpBubble == null) return;

            if (helpBubble.gameObject.activeSelf && settingId == currentHelpId)
            {
                ClearHelp();
                return;
            }

            currentHelpId = settingId;

            SettingsHelp.Entry entry = SettingsHelp.Get(settingId);
            if (helpTitle != null) helpTitle.text = entry.title;
            if (helpBody != null) helpBody.text = entry.body;

            helpBubble.gameObject.SetActive(true);

            // Last sibling, every time. A bubble is only useful if it is on top of
            // the rows it explains, and anything added to the panel later would
            // otherwise draw over it.
            helpBubble.SetAsLastSibling();

            SizeBubbleToText(entry.body);
            PositionBubbleBeside(anchor);
        }

        /// <summary>Overload for calling from a UnityEvent, which cannot pass the
        /// button. Opens the bubble in the middle of the panel.</summary>
        public void ShowHelp(string settingId)
        {
            ShowHelp(settingId, null);
        }

        public void ClearHelp()
        {
            currentHelpId = null;
            if (helpBubble != null) helpBubble.gameObject.SetActive(false);
        }

        /// <summary>Height comes from the text rather than being fixed, so a
        /// one-line tip does not sit in a half-empty box and a long one is not
        /// clipped. TMP will measure a string at a given width without it having to
        /// be laid out first, which is what makes this possible before the bubble is
        /// ever drawn.</summary>
        private void SizeBubbleToText(string body)
        {
            if (helpBubble == null) return;

            float innerWidth = bubbleWidth - bubblePadding.x * 2f;
            float bodyHeight = 60f;

            if (helpBody != null)
                bodyHeight = helpBody.GetPreferredValues(body, innerWidth, 0f).y;

            float titleHeight = helpTitle != null ? 24f : 0f;
            float height = bubblePadding.y * 2f + titleHeight + bodyHeight;

            helpBubble.sizeDelta = new Vector2(bubbleWidth, height);
        }

        /// <summary>
        /// Puts the bubble to the right of the button, then pushes it back inside
        /// the panel if that would hang it over an edge.
        ///
        /// ALL THE MATHS IS DONE IN THE PANEL'S OWN LOCAL SPACE, which is what makes
        /// it survive Interface Size. The panel, the button and the bubble all scale
        /// together, so a bubble that fits at 1.0x still fits at 1.6x - no screen
        /// pixels are involved anywhere.
        /// </summary>
        private void PositionBubbleBeside(RectTransform anchor)
        {
            if (helpBubble == null) return;

            var panel = transform as RectTransform;
            if (panel == null) return;

            // Pivot on the left edge, vertically centred: the bubble grows to the
            // right of wherever it is placed, which is the direction it opens in.
            helpBubble.anchorMin = new Vector2(0.5f, 0.5f);
            helpBubble.anchorMax = new Vector2(0.5f, 0.5f);
            helpBubble.pivot = new Vector2(0f, 0.5f);

            float halfPanelWidth = panel.rect.width * 0.5f;
            float halfPanelHeight = panel.rect.height * 0.5f;
            float width = helpBubble.sizeDelta.x;
            float height = helpBubble.sizeDelta.y;

            if (anchor == null)
            {
                helpBubble.anchoredPosition = new Vector2(-width * 0.5f, 0f);
                return;
            }

            Vector3 worldCentre = anchor.TransformPoint(anchor.rect.center);
            Vector3 localPoint = panel.InverseTransformPoint(worldCentre);
            var local = new Vector2(localPoint.x, localPoint.y);

            float halfAnchorWidth = anchor.rect.width * 0.5f;

            float x = local.x + halfAnchorWidth + bubbleGap;

            // Not enough room on the right? Open to the left instead. Flipping is
            // better than clamping here: a clamped bubble would sit ON TOP of the
            // control it is explaining.
            if (x + width > halfPanelWidth - bubbleEdgePadding)
                x = local.x - halfAnchorWidth - bubbleGap - width;

            float minX = -halfPanelWidth + bubbleEdgePadding;
            float maxX = halfPanelWidth - bubbleEdgePadding - width;
            x = maxX >= minX ? Mathf.Clamp(x, minX, maxX) : minX;

            float y = local.y;
            float minY = -halfPanelHeight + bubbleEdgePadding + height * 0.5f;
            float maxY = halfPanelHeight - bubbleEdgePadding - height * 0.5f;
            y = maxY >= minY ? Mathf.Clamp(y, minY, maxY) : 0f;

            helpBubble.anchoredPosition = new Vector2(x, y);
        }

        // ==================================================================
        // Populating
        // ==================================================================

        /// <summary>Pushes the stored values into the controls WITHOUT firing their
        /// listeners. Notifying here would write the value straight back to
        /// GameSettings, raise OnChanged, and call this again - and on a slider
        /// whose range does not exactly match the stored value, that loop does not
        /// settle where it started.</summary>
        private void Refresh()
        {
            SetSlider(mouseLookSlider, GameSettings.MouseLook01, mouseLookValue, Percent);
            SetSlider(touchLookSlider, GameSettings.TouchLook01, touchLookValue, Percent);
            SetSlider(stationPanSlider, GameSettings.StationPan01, stationPanValue, Percent);
            SetSlider(stationZoomSlider, GameSettings.StationZoom01, stationZoomValue, Percent);
            SetSlider(masterVolumeSlider, GameSettings.MasterVolume01, masterVolumeValue, Percent);

            if (uiScaleSlider != null)
            {
                uiScaleSlider.minValue = 0.85f;
                uiScaleSlider.maxValue = 1.6f;
                uiScaleSlider.SetValueWithoutNotify(GameSettings.UiScale);
            }
            if (uiScaleValue != null) uiScaleValue.text = $"{GameSettings.UiScale:0.00}x";

            SetToggle(invertLookYToggle, GameSettings.InvertLookY);
            SetToggle(invertStationDragToggle, GameSettings.InvertStationDrag);
            SetToggle(reduceMotionToggle, GameSettings.ReduceMotion);
            SetToggle(performanceModeToggle, GameSettings.PerformanceMode);
            SetToggle(fullscreenToggle, GameSettings.Fullscreen);
        }

        private void ApplyPlatformVisibility()
        {
            bool mobile = Application.isMobilePlatform;

#if UNITY_EDITOR
            if (showAllRowsInEditor)
            {
                Show(mouseLookRow, true);
                Show(touchLookRow, true);
                Show(fullscreenRow, true);
                return;
            }
#endif

            Show(mouseLookRow, !mobile);
            Show(touchLookRow, mobile);
            Show(fullscreenRow, !mobile);
        }

        // ==================================================================
        // Small helpers
        // ==================================================================

        private static void Show(GameObject row, bool visible)
        {
            if (row != null && row.activeSelf != visible) row.SetActive(visible);
        }

        private void WireSlider(Slider slider, System.Action<float> write)
        {
            if (slider == null) return;
            slider.onValueChanged.AddListener(value =>
            {
                write(value);
                Refresh();
            });
        }

        private void WireToggle(Toggle toggle, System.Action<bool> write)
        {
            if (toggle == null) return;
            toggle.onValueChanged.AddListener(value => write(value));
        }

        private static void SetSlider(Slider slider, float value01, TMP_Text label,
                                      System.Func<float, string> format)
        {
            if (slider != null)
            {
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.SetValueWithoutNotify(value01);
            }
            if (label != null) label.text = format(value01);
        }

        private static void SetToggle(Toggle toggle, bool value)
        {
            if (toggle != null) toggle.SetIsOnWithoutNotify(value);
        }

        private static string Percent(float value01)
        {
            return Mathf.RoundToInt(value01 * 100f) + "%";
        }
    }
}