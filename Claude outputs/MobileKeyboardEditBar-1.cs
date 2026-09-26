using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The wide editing bar that sits on top of the on-screen keyboard while the learner is typing:
/// the field being edited is repeated full-width just above the keys, so what is being typed is
/// always in plain sight no matter where the real field sits on the screen.
///
/// ONE FILE, NOTHING TO ATTACH, NOTHING TO WIRE, NO PREFAB. It installs itself at startup and
/// works with every input field in the game - COC2's device config, COC3's hostname and IP boxes,
/// the AI tutor, the profile name box, and anything added later. It references no other script of
/// mine, so this file alone is the whole feature and deleting it is the whole removal.
///
/// IT IS A MIRROR, NOT THE REAL FIELD MOVED. The real field keeps focus and keeps receiving every
/// keystroke; this only ever READS from it. So it cannot be undone by a Layout Group, cannot push
/// the top of a panel off screen, and does not care how the screen behind it was built.
///
/// ---------------------------------------------------------------------------------------------
/// THE FOUR MISTAKES THE EARLIER VERSION MADE, AND WHAT IS DIFFERENT HERE
///
/// 1. IT LEAKED AN OBJECT INTO THE EDITOR THAT OUTLIVED THE SCRIPT ITSELF.
///    The old version set hideFlags = HideFlags.DontSave and then called DontDestroyOnLoad. When
///    Play mode ends, Unity destroys objects that belong to a scene; an object marked DontSave and
///    moved out of every scene belongs to none, so the cleanup skipped it and it survived into the
///    Editor session. Deleting the script killed the component, but the GameObject, Canvas, Images
///    and TMP labels are native - they carried on drawing a bar that nothing owned any more, in
///    every scene, with no way to find it.
///    THIS VERSION SETS NO hideFlags AT ALL. A plain object in the DontDestroyOnLoad scene is
///    cleaned up when Play mode ends, like everything else. It also appears in the Hierarchy under
///    "DontDestroyOnLoad" where it can be selected - and if a stray one ever does appear, the
///    Editor menu item at the bottom of this file removes it.
///
/// 2. IT BUILT ITS UI ON A PC, WHERE IT CAN NEVER BE USED.
///    The canvas was created in Awake whatever the platform. Now nothing is built until the bar is
///    genuinely needed, so on desktop this costs one empty GameObject and nothing is ever drawn.
///
/// 3. ITS PLATFORM CHECK WAS BELOW THE THING THAT LIES.
///    The check lived inside the keyboard measurement, under a reading of
///    TouchScreenKeyboard.visible. With an Android build target the Unity EDITOR creates a stand-in
///    touch keyboard as soon as a TMP_InputField activates, sets visible to true, leaves it true,
///    and reports an area of zero - so the bar appeared on a PC and stayed. The platform test is
///    now the first line of LateUpdate, before anything else is read.
///
/// 4. IT TRUSTED TouchScreenKeyboard.visible ON ITS OWN.
///    That flag sticks at true on several Android builds after the back button closes the keyboard.
///    The bar now also requires a field to actually have focus, so a stuck flag cannot show it, and
///    Trust Reported Area Only is there for a device with the problem.
///
/// NO #if UNITY_ANDROID ANYWHERE. That symbol is true in the Editor whenever the build target is
/// Android, so it would have prevented none of the above. Every gate here is a runtime fact.
/// ---------------------------------------------------------------------------------------------
///
/// TO KEEP SETTINGS BETWEEN SESSIONS, put this component on a persistent object yourself - the
/// Managers object, beside CursorKeeper. Install() stands down when it finds one, and the values
/// are then saved with the scene like any other component.
/// </summary>
[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public class MobileKeyboardEditBar : MonoBehaviour
{
    private const string ObjectName = "MobileKeyboardEditBar";

    /// <summary>Which edge a bar narrower than the screen sits against.</summary>
    public enum BarAlignment { Left, Centre, Right }

    [Header("Behaviour")]
    [Tooltip("Shows a Done button that closes the keyboard.")]
    [SerializeField] private bool showDoneButton = true;

    [Tooltip("ON (recommended): the bar only appears when the keyboard would actually cover the " +
             "field being typed into. A field near the top of the screen is already visible, and " +
             "covering it with a bar solves nothing.")]
    [SerializeField] private bool onlyWhenCovered = true;

    [Tooltip("How close to the keyboard a field may get before it counts as covered, in pixels.")]
    [SerializeField] private float coveredMargin = 16f;

    [Header("Placement")]
    [Tooltip("Clear space in PIXELS between the top of the keyboard and the bottom of the bar. " +
             "0 rests the bar on the keys.")]
    [SerializeField] private float gapAboveKeyboard;

    [Tooltip("The bar's width as a fraction of screen width. 1 is the full width of the screen.")]
    [Range(0.25f, 1f)]
    [SerializeField] private float barWidthFraction = 1f;

    [Tooltip("Pixels taken off the width. With Centre alignment it comes off both ends; with Left " +
             "or Right it becomes the gap to that edge.")]
    [SerializeField] private float sideMargin;

    [Tooltip("Which edge a bar narrower than the screen sits against. No effect at full width.")]
    [SerializeField] private BarAlignment alignment = BarAlignment.Centre;

    [Tooltip("The bar's height as a fraction of screen height, before the limits below.")]
    [Range(0.04f, 0.3f)]
    [SerializeField] private float barHeightFraction = 0.085f;

    [SerializeField] private float minBarHeight = 96f;
    [SerializeField] private float maxBarHeight = 200f;

    [Tooltip("ON: pin the bar to the TOP of the screen instead of on top of the keyboard - the " +
             "escape hatch for a device that misreports its keyboard height. Gap Above Keyboard " +
             "then reads as a gap below the top edge.")]
    [SerializeField] private bool pinToTopOfScreen;

    [Header("Keyboard measurement")]
    [Tooltip("ON: believe ONLY the keyboard size the device reports, and show nothing when it " +
             "reports none. Tick this if the bar lingers after the back button closes the " +
             "keyboard. The cost is a device that never reports a size: there the bar stops " +
             "appearing at all.")]
    [SerializeField] private bool trustReportedAreaOnly;

    [Tooltip("How much of the screen to assume the keyboard covers when the device does not " +
             "report its size.")]
    [Range(0.2f, 0.7f)]
    [SerializeField] private float fallbackKeyboardFraction = 0.45f;

    [Tooltip("The smallest keyboard height to believe, as a fraction of screen height. A reported " +
             "height below this is treated as wrong and the fallback above is used instead.\n\n" +
             "THIS IS WHAT STOPS THE KEYBOARD OVERLAPPING THE BAR. A real phone keyboard is " +
             "roughly 35-45% of the screen; Android often reports far less - only the part of the " +
             "window it resized, or the suggestion strip alone - and a bar placed on that number " +
             "sits inside the keys. 0.30 accepts any plausible keyboard and rejects the obviously " +
             "wrong readings. Raise it if the keys still reach the bar on your device.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float minimumKeyboardFraction = 0.30f;

    [Header("Testing and diagnostics")]
    [Tooltip("ON: in the Editor, pretend a keyboard covers the bottom of the screen whenever a " +
             "field has focus, so the bar can be checked in Play mode without building to a " +
             "phone. NO effect on a real device.")]
    [SerializeField] private bool simulateInEditor;

    [Tooltip("Logs one line each time the bar appears and disappears, with the keyboard state " +
             "behind the decision.")]
    [SerializeField] private bool logShowHide = true;

    [Header("Look")]
    [SerializeField] private Color barColour = new Color32(0x1C, 0x22, 0x33, 0xFF);
    [SerializeField] private Color boxColour = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    [SerializeField] private Color boxEdgeColour = new Color32(0x46, 0xC8, 0xF5, 0xFF);
    [SerializeField] private Color valueColour = new Color32(0x14, 0x1A, 0x28, 0xFF);
    [SerializeField] private Color labelColour = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
    [SerializeField] private Color doneColour = new Color32(0x46, 0xC8, 0xF5, 0xFF);
    [SerializeField] private Color doneInkColour = new Color32(0x0B, 0x16, 0x2E, 0xFF);

    private static MobileKeyboardEditBar instance;

    private RectTransform bar;
    private RectTransform edge;
    private RectTransform box;
    private TMP_Text labelText;
    private TMP_Text valueText;
    private RectTransform doneRect;

    private TMP_InputField boundTmp;
    private InputField boundLegacy;

    private GameObject decidedFor;
    private bool decision;

    private float builtHeight = -1f;
    private bool builtDoneButton;

    // ==================================================================
    // Install
    // ==================================================================

    /// <summary>
    /// Creates the holder object - unless one was placed in a scene by hand, which wins.
    /// AfterSceneLoad runs after every Awake in the first scene, so a placed copy has already
    /// claimed the slot by the time this looks.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        // Every scene load, not only the first. RuntimeInitializeOnLoadMethod fires once per play
        // session, so a single check here would be the only check there ever is - and if the
        // instance is lost later (a copy placed in a scene that then unloads, for example) the
        // feature would disappear for the rest of the session with nothing to say why.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        EnsureInstance();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { EnsureInstance(); }

    private static void EnsureInstance()
    {
        if (instance != null) return;

        // A copy placed in a scene by hand wins, and carries whatever was tuned on it in the
        // Inspector. Include inactive: a Managers object that starts disabled is still the one
        // that should be used, not a reason to build a second.
        var placed = FindAnyObjectByType<MobileKeyboardEditBar>(FindObjectsInactive.Include);
        if (placed != null) { instance = placed; return; }

        // NO hideFlags. See mistake 1 in the class comment - HideFlags.DontSave is precisely what
        // let the old object outlive Play mode, the script, and every attempt to find it. A plain
        // object in the DontDestroyOnLoad scene is destroyed when Play mode ends, and is visible
        // and selectable in the Hierarchy meanwhile.
        var go = new GameObject(ObjectName);

        instance = go.AddComponent<MobileKeyboardEditBar>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        // A duplicate removes its own COMPONENT, never its GameObject - destroying the object would
        // take the Managers object with it if that is where this was placed.
        if (instance != null && instance != this) { Destroy(this); return; }

        instance = this;

        // THIS IS WHAT WAS MISSING, AND IT MATTERED MOST FOR THE ONE SETUP WORTH RECOMMENDING.
        // Install calls DontDestroyOnLoad on the object it creates, but a copy placed in a scene by
        // hand got no such treatment - so it was destroyed at the first scene change, and because
        // RuntimeInitializeOnLoadMethod had already run and would not run again, nothing replaced
        // it. Tuning the values on a scene object, which is the only way to carry them into a
        // build, would have made the bar work on the menu and then silently stop.
        //
        // Only for a root object: DontDestroyOnLoad refuses a child and logs its own complaint.
        // A child of a Managers object that is already persistent is carried by its parent anyway,
        // which is exactly the arrangement being recommended.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        // Unconditional, so "installed and silent" can never be mistaken for "not in the project".
        Debug.Log("[MobileKeyboardEditBar] Installed. On a phone, typing into any field will show " +
                  "it full-width above the keyboard. Nothing is drawn on desktop.");
    }

    // ==================================================================
    // Per frame
    // ==================================================================

    private void LateUpdate()
    {
        // FIRST LINE, BEFORE ANYTHING ELSE IS READ. There is no on-screen keyboard on a desktop,
        // so there is nothing here to solve and nothing may be drawn - including in the Editor,
        // which is a desktop however the build target is set. Application.isMobilePlatform is a
        // runtime fact and reads false in the Editor; #if UNITY_ANDROID would not.
        if (!Application.isMobilePlatform && !simulateInEditor)
        {
            Forget();
            Hide();
            return;
        }

        // LateUpdate so layout groups and content fitters have settled.
        if (!BindFocusedField())
        {
            // No focus means no bar, whatever TouchScreenKeyboard.visible claims. This is what
            // stops a flag stuck at true on Android from showing a bar over nothing.
            Forget();
            Hide();
            return;
        }

        float covered = KeyboardCoverage();
        if (covered <= 0f) { Forget(); Hide(); return; }

        if (!WantedForThisField(covered)) { Hide(); return; }

        EnsureBuilt();
        Layout();
        Place(covered);
        Mirror();
        Show();
    }

    // ==================================================================
    // Decisions
    // ==================================================================

    /// <summary>Binds whichever field has focus. False when nobody is typing.</summary>
    private bool BindFocusedField()
    {
        boundTmp = null;
        boundLegacy = null;

        EventSystem es = EventSystem.current;
        if (es == null) return false;

        GameObject selected = es.currentSelectedGameObject;
        if (selected == null) return false;

        // Selected AND isFocused: a field the learner has tapped away from stays the selected
        // object for a frame, and treating that as typing would outlive the keyboard.
        TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
        if (tmp != null && tmp.isFocused) { boundTmp = tmp; return true; }

        InputField legacy = selected.GetComponent<InputField>();
        if (legacy != null && legacy.isFocused) { boundLegacy = legacy; return true; }

        return false;
    }

    /// <summary>Pixels of the screen the keyboard covers from the bottom, or 0 for no keyboard.</summary>
    private float KeyboardCoverage()
    {
        if (simulateInEditor && !Application.isMobilePlatform)
            return Screen.height * fallbackKeyboardFraction;

        if (!TouchScreenKeyboard.visible) return 0f;

        float height = TouchScreenKeyboard.area.height;

        // TOO SMALL COUNTS AS WRONG, NOT JUST ZERO.
        // The old test only caught a height of 0 or one taller than the screen. Android routinely
        // reports something in between and still wrong - on API 30+ the soft keyboard no longer
        // resizes the window by default, so Unity has little real to measure and hands back the
        // strip it does know about rather than the whole keyboard. Trusting that number puts the
        // bar a few hundred pixels too low, which is to say inside the keys: the bar appears,
        // correctly positioned against a keyboard height that is not the keyboard's height.
        //
        // Anything below minimumKeyboardFraction of the screen cannot be a phone keyboard, so it
        // is treated exactly like a missing reading.
        bool tooSmall = height < Screen.height * minimumKeyboardFraction;

        if (height <= 1f || tooSmall || height >= Screen.height)
        {
            // Which reading is right depends on the device: "up, but the size is not known yet"
            // during the open animation, or "not up at all" on the builds where visible sticks
            // after the back button. Guessing keeps it working on the first; Trust Reported Area
            // Only is for the second.
            if (trustReportedAreaOnly) return 0f;

            height = Screen.height * fallbackKeyboardFraction;
        }

        return height;
    }

    /// <summary>
    /// Whether the bar is wanted for the field being typed into.
    ///
    /// DECIDED ONCE PER FIELD, THEN HELD, so a layout settling a few pixels cannot make the bar
    /// flicker in the middle of a word.
    /// </summary>
    private bool WantedForThisField(float covered)
    {
        GameObject field = boundTmp != null ? boundTmp.gameObject
                                            : (boundLegacy != null ? boundLegacy.gameObject : null);

        if (field != decidedFor)
        {
            decidedFor = field;
            decision = !onlyWhenCovered || IsFieldCovered(covered);
        }

        return decision;
    }

    /// <summary>
    /// True when the keyboard reaches the field's lowest edge.
    ///
    /// Measured from world corners, not anchoredPosition: a field inside a scaled canvas, a scroll
    /// view or a rotated parent has an anchored position that says nothing about where it lands on
    /// the glass. Returns TRUE when it cannot tell - an unnecessary bar is an annoyance, a missing
    /// one is the bug this exists for.
    /// </summary>
    private bool IsFieldCovered(float covered)
    {
        RectTransform rect = FieldRect();
        if (rect == null) return true;

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) return true;
        if (canvas.rootCanvas != null) canvas = canvas.rootCanvas;

        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        float lowest = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            if (point.y < lowest) lowest = point.y;
        }

        return lowest < covered + coveredMargin;
    }

    private RectTransform FieldRect()
    {
        if (boundTmp != null) return boundTmp.transform as RectTransform;
        if (boundLegacy != null) return boundLegacy.transform as RectTransform;
        return null;
    }

    private void Forget()
    {
        decidedFor = null;
        decision = false;
    }

    // ==================================================================
    // Showing
    // ==================================================================

    private void Show()
    {
        if (bar == null || bar.gameObject.activeSelf) return;

        bar.gameObject.SetActive(true);
        if (logShowHide) Debug.Log("[MobileKeyboardEditBar] Shown. " + State());
    }

    private void Hide()
    {
        if (bar == null) return;

        if (bar.gameObject.activeSelf)
        {
            bar.gameObject.SetActive(false);
            if (logShowHide) Debug.Log("[MobileKeyboardEditBar] Hidden. " + State());
        }
    }

    private string State()
    {
        string field = boundTmp != null ? boundTmp.name
                                        : (boundLegacy != null ? boundLegacy.name : "nothing");

        float reported = TouchScreenKeyboard.area.height;

        return $"field: {field}, mobile: {Application.isMobilePlatform}, " +
               $"keyboard visible: {TouchScreenKeyboard.visible}, " +
               $"reported area height: {reported:0} " +
               $"({(Screen.height > 0 ? 100f * reported / Screen.height : 0f):0}% of screen), " +
               $"using: {KeyboardCoverage():0}px, " +
               $"screen: {Screen.width}x{Screen.height}, simulate: {simulateInEditor}";
    }

    private void Mirror()
    {
        string raw = boundTmp != null ? boundTmp.text : (boundLegacy != null ? boundLegacy.text : "");
        if (raw == null) raw = "";

        // Masked here too. Repeating a PIN in clear text on a bar the whole room can see, purely
        // because it is easier to draw, would be a worse bug than the one this fixes.
        if (IsPassword()) raw = new string('*', raw.Length);

        // Unscaled: a paused game still has a keyboard, and Time.time would freeze the caret.
        bool caretOn = Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;

        valueText.text = raw + (caretOn ? "|" : "");
        valueText.color = valueColour;

        labelText.text = LabelForField();
        labelText.color = labelColour;
    }

    private bool IsPassword()
    {
        if (boundTmp != null)
            return boundTmp.contentType == TMP_InputField.ContentType.Password ||
                   boundTmp.contentType == TMP_InputField.ContentType.Pin;

        if (boundLegacy != null)
            return boundLegacy.contentType == InputField.ContentType.Password ||
                   boundLegacy.contentType == InputField.ContentType.Pin;

        return false;
    }

    /// <summary>
    /// The placeholder first - it is the text the designer already wrote to say what goes in this
    /// box - and a tidied object name as the fallback, which still beats a blank line when three
    /// identical IP boxes are stacked behind the bar.
    /// </summary>
    private string LabelForField()
    {
        Graphic placeholder = boundTmp != null ? boundTmp.placeholder
                                               : (boundLegacy != null ? boundLegacy.placeholder : null);

        if (placeholder != null)
        {
            var tmpPlaceholder = placeholder as TMP_Text;
            if (tmpPlaceholder != null && !string.IsNullOrWhiteSpace(tmpPlaceholder.text))
                return tmpPlaceholder.text;

            var legacyPlaceholder = placeholder as Text;
            if (legacyPlaceholder != null && !string.IsNullOrWhiteSpace(legacyPlaceholder.text))
                return legacyPlaceholder.text;
        }

        GameObject owner = boundTmp != null ? boundTmp.gameObject
                                            : (boundLegacy != null ? boundLegacy.gameObject : null);

        return owner != null ? Prettify(owner.name) : "";
    }

    private static string Prettify(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        name = name.Replace("InputField", "").Replace("Input", "").Replace("Field", "")
                   .Replace("_", " ").Trim();

        return string.IsNullOrEmpty(name) ? "Typing" : name;
    }

    private void Done()
    {
        if (boundTmp != null) boundTmp.DeactivateInputField();
        if (boundLegacy != null) boundLegacy.DeactivateInputField();

        // Clearing the selection is what actually closes the keyboard - DeactivateInputField alone
        // leaves the field selected on some Android builds and the keyboard stays up.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        Forget();
        Hide();
    }

    // ==================================================================
    // Placement
    // ==================================================================

    private float BarHeight()
    {
        return Mathf.Clamp(Screen.height * barHeightFraction,
                           minBarHeight,
                           Mathf.Max(minBarHeight, maxBarHeight));
    }

    private void Place(float covered)
    {
        float barHeight = BarHeight();

        // A SINGLE ANCHOR POINT, NOT A STRETCHED PAIR. Stretched x anchors would make sizeDelta.x
        // mean "how much wider than the screen", which gives no way to say which edge a narrower
        // bar belongs to. Anchoring both to one spot makes sizeDelta.x a plain pixel width.
        float anchorX = alignment == BarAlignment.Left ? 0f
                      : alignment == BarAlignment.Right ? 1f
                      : 0.5f;

        // Recomputed every frame rather than cached: Screen.width changes when the device rotates.
        float width = Screen.width * barWidthFraction
                    - sideMargin * (alignment == BarAlignment.Centre ? 2f : 1f);

        // Never narrower than it takes to show something - a negative width draws an invisible bar,
        // which looks exactly like the script not running.
        width = Mathf.Clamp(width, barHeight * 2f, Screen.width);

        float offsetX = alignment == BarAlignment.Left ? sideMargin
                      : alignment == BarAlignment.Right ? -sideMargin
                      : 0f;

        float anchorY = pinToTopOfScreen ? 1f : 0f;

        bar.anchorMin = new Vector2(anchorX, anchorY);
        bar.anchorMax = new Vector2(anchorX, anchorY);
        bar.pivot = new Vector2(anchorX, anchorY);

        bar.anchoredPosition = pinToTopOfScreen
            ? new Vector2(offsetX, -gapAboveKeyboard)          // hangs down from the top edge
            : new Vector2(offsetX, covered + gapAboveKeyboard); // rests on the keyboard

        bar.sizeDelta = new Vector2(width, barHeight);
    }

    // ==================================================================
    // The bar itself, built in code, and NOT UNTIL IT IS NEEDED
    // ==================================================================

    private void EnsureBuilt()
    {
        if (bar != null) return;

        var canvasGo = new GameObject("EditBarCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Above everything: the bar is useless if a dialog draws over it.
        canvas.sortingOrder = 32000;

        var scaler = canvasGo.AddComponent<CanvasScaler>();

        // CONSTANT PIXEL SIZE AT 1 IS THE WHOLE TRICK. TouchScreenKeyboard.area is in screen
        // pixels; any other scale mode would need every position divided by a scale factor that
        // changes with the device, which is how this ends up one phone model wrong.
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        canvasGo.AddComponent<GraphicRaycaster>();

        bar = NewRect("Bar", canvasGo.transform);
        AddImage(bar, barColour, true);

        edge = NewRect("Edge", bar);
        AddImage(edge, boxEdgeColour, false);

        box = NewRect("Box", bar);
        AddImage(box, boxColour, false);

        labelText = AddText(bar, "Label", labelColour);
        valueText = AddText(box, "Value", valueColour);

        doneRect = NewRect("Done", bar);
        Image doneImage = AddImage(doneRect, doneColour, true);

        var doneButton = doneRect.gameObject.AddComponent<Button>();
        doneButton.targetGraphic = doneImage;
        doneButton.onClick.AddListener(Done);

        TMP_Text doneLabel = AddText(doneRect, "Label", doneInkColour);
        doneLabel.text = "Done";
        doneLabel.alignment = TextAlignmentOptions.Center;
        Stretch(doneLabel.rectTransform, 0f);

        bar.gameObject.SetActive(false);
    }

    /// <summary>
    /// Arranges the bar's parts, and only when something that affects them has changed - a phone
    /// rotating, or a field above being dragged in the Inspector while the game runs.
    /// </summary>
    private void Layout()
    {
        float height = BarHeight();

        if (builtHeight > 0f
            && Mathf.Approximately(height, builtHeight)
            && showDoneButton == builtDoneButton) return;

        builtHeight = height;
        builtDoneButton = showDoneButton;

        float pad = height * 0.12f;
        float labelHeight = height * 0.26f;
        float doneWidth = showDoneButton ? height * 1.5f : 0f;
        float boxHeight = height - labelHeight - pad * 2.2f;
        float border = Mathf.Max(2f, height * 0.025f);

        doneRect.gameObject.SetActive(showDoneButton);

        // Label: a thin line along the top, clear of the Done button.
        labelText.rectTransform.anchorMin = new Vector2(0f, 1f);
        labelText.rectTransform.anchorMax = new Vector2(1f, 1f);
        labelText.rectTransform.pivot = new Vector2(0.5f, 1f);
        labelText.rectTransform.offsetMin = new Vector2(pad * 1.5f, -labelHeight);
        labelText.rectTransform.offsetMax = new Vector2(-pad * 1.5f - doneWidth, -pad * 0.5f);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        // Sized from the rect it actually got, never from the band it was carved out of. See
        // FitFontTo below for what happens when a font is bigger than its own rectangle.
        FitFontTo(labelText, labelHeight - pad * 0.5f);

        // Box across the bottom, with Edge drawn as a slightly larger rectangle behind it - a
        // border that needs no sprite and no Outline component.
        box.anchorMin = new Vector2(0f, 0f);
        box.anchorMax = new Vector2(1f, 0f);
        box.pivot = new Vector2(0.5f, 0f);
        box.offsetMin = new Vector2(pad * 1.5f, pad);
        box.offsetMax = new Vector2(-pad * 1.5f - doneWidth, pad + boxHeight);

        edge.anchorMin = box.anchorMin;
        edge.anchorMax = box.anchorMax;
        edge.pivot = box.pivot;
        edge.offsetMin = box.offsetMin - new Vector2(border, border);
        edge.offsetMax = box.offsetMax + new Vector2(border, border);

        // HORIZONTAL INSETS ONLY - THIS IS THE BUG THAT MADE THE BAR LOOK EMPTY.
        // It used to inset by pad on all four sides, which left the text a rectangle 0.236 of the
        // bar's height while the font was set from the BOX height to 0.248 of it. The glyphs were
        // 105% the height of the box they had to fit in, and TMP with overflowMode Ellipsis draws
        // NOTHING at all when a line cannot fit vertically - so the white box appeared, the caret
        // never showed, and every keystroke looked like it had gone missing. It had not: the real
        // field had it the whole time, which is exactly what you saw.
        //
        // Padding a text rect vertically is pointless anyway - the text is already centred in it.
        valueText.rectTransform.anchorMin = Vector2.zero;
        valueText.rectTransform.anchorMax = Vector2.one;
        valueText.rectTransform.offsetMin = new Vector2(pad, 0f);
        valueText.rectTransform.offsetMax = new Vector2(-pad, 0f);
        valueText.alignment = TextAlignmentOptions.MidlineLeft;

        FitFontTo(valueText, boxHeight);

        if (showDoneButton)
        {
            doneRect.anchorMin = new Vector2(1f, 0f);
            doneRect.anchorMax = new Vector2(1f, 0f);
            doneRect.pivot = new Vector2(1f, 0f);
            doneRect.anchoredPosition = new Vector2(-pad * 1.5f, pad);
            doneRect.sizeDelta = new Vector2(doneWidth - pad, boxHeight);

            TMP_Text doneLabel = doneRect.GetComponentInChildren<TMP_Text>(true);
            if (doneLabel != null) FitFontTo(doneLabel, boxHeight, 0.42f);
        }
    }

    // ------------------------------------------------------------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Image AddImage(RectTransform rect, Color colour, bool raycastTarget)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static TMP_Text AddText(RectTransform parent, string name, Color colour)
    {
        RectTransform rect = NewRect(name, parent);

        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.color = colour;
        text.raycastTarget = false;
        text.enableWordWrapping = false;

        // Ellipsis so a long value clips at its own edge instead of drawing over the Done button.
        text.overflowMode = TextOverflowModes.Ellipsis;

        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    /// <summary>
    /// Sets a font size that is guaranteed to fit the height it has been given.
    ///
    /// WHY THIS IS A METHOD AND NOT THREE MULTIPLICATIONS.
    /// A TMP_Text whose font is taller than its own rectangle renders NOTHING in Ellipsis overflow
    /// mode - not clipped text, not a partial glyph, nothing. It looks precisely like the value
    /// never being assigned, which sent this hunt in entirely the wrong direction once already.
    /// Every size here now comes from the rect that will hold it, times a ratio capped well below
    /// 1, so the failure cannot come back by someone adjusting a padding.
    /// </summary>
    private static void FitFontTo(TMP_Text text, float rectHeight, float ratio = 0.52f)
    {
        float safeRatio = Mathf.Clamp(ratio, 0.1f, 0.7f);
        text.fontSize = Mathf.Max(8f, rectHeight * safeRatio);
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    // ==================================================================

    [ContextMenu("Report edit bar state")]
    public void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[MobileKeyboardEditBar] REPORT");
        sb.AppendLine("  " + State());
        sb.AppendLine($"  Only when covered: {onlyWhenCovered}");
        sb.AppendLine($"  Bar built        : {bar != null}");
        sb.AppendLine($"  Bar showing      : {bar != null && bar.gameObject.activeSelf}");
        sb.AppendLine($"  Bar size         : {(bar != null ? bar.rect.width : 0f):0} x " +
                      $"{BarHeight():0} px");

        Debug.Log(sb.ToString(), this);
    }

#if UNITY_EDITOR
    /// <summary>
    /// The safety valve, after the last version left an object behind that outlived its own script.
    /// This one cannot leak - it sets no hideFlags, so Play mode cleans it up - but the menu item
    /// costs nothing and means a stray bar from ANY source can be removed in one click instead of
    /// an evening.
    ///
    /// UnityEditor is only referenced inside this block, so nothing here goes into a build. This is
    /// an Editor-only tool, not a platform branch - the distinction that matters is that no runtime
    /// behaviour depends on a compiler symbol.
    /// </summary>
    [UnityEditor.MenuItem("CircuitCrafter/Remove Leftover Keyboard Bar")]
    private static void RemoveLeftovers()
    {
        int removed = 0;

        foreach (var found in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (found == null || found.name != "EditBarCanvas") continue;

            GameObject holder = found.transform.parent != null
                ? found.transform.parent.gameObject
                : found.gameObject;

            DestroyImmediate(holder);
            removed++;
        }

        Debug.Log(removed == 0
            ? "[MobileKeyboardEditBar] No leftover bar found - nothing to remove."
            : $"[MobileKeyboardEditBar] Removed {removed} leftover bar object(s).");
    }
#endif
}
