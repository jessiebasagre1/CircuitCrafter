using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Story Mode tutorial for the COC1 PC Disassembly and Assembly station.
///
///   1. ENTERING THE STATION (disassembly stage): a short introduction to the stage, then the
///      station controls - including dragging each removed part onto the hotbar.
///   2. ASSEMBLY STAGE: when disassembly is finished (or on the next visit), a short introduction
///      to assembly and its controls.
///   3. FIRST PART IN THE HOTBAR: tells the learner to press the part in the hotbar to read its
///      description, and (optionally) pulses a ring on that slot until they do.
///
/// ATTACH THIS TO: the Managers object in the COC1 scene. One per scene.
///
/// SETUP: Station -> the assemble/disassemble station's StationCameraSwitch. Everything else is
/// optional: the dialog text is already filled in and can be edited in the Inspector, and the Slot
/// Highlight Prefab can be the same PulsingHighlightRing prefab HotbarTutorialTrigger uses.
///
/// STORY MODE ONLY. Nothing here runs in an assessment or on a COC4 repair ticket.
///
/// ONE DIALOG AT A TIME. DialogManager shows a single dialog and a new one replaces whatever is
/// open, losing that dialog's ending. So every message here waits until no dialog has been open for
/// Quiet Seconds, and nothing is being dragged. HotbarTutorialTrigger, if it is in the scene, waits
/// for this component and shows its hotbar ring dialog right after the controls.
///
/// Text tokens: {Click} / {click} become Click / click on PC and Tap / tap on a phone or tablet;
/// {exit} becomes "press E" / "use the Exit button".
/// </summary>
[DisallowMultipleComponent]
public class AssemblyStationTutorial : MonoBehaviour
{
    public static AssemblyStationTutorial Instance { get; private set; }

    [Header("Station")]
    [Tooltip("The PC assemble/disassemble station's StationCameraSwitch. If empty, it is taken from " +
             "HotbarTutorialTrigger or HotbarVisibilityController in the scene.")]
    [SerializeField] private StationCameraSwitch station;

    [Header("Timing")]
    [Tooltip("Seconds with no dialog open (and nothing being dragged) before a message appears. " +
             "Lets another dialog finish first, e.g. the \"PC disassembled\" message.")]
    [Min(0f)]
    [SerializeField] private float quietSeconds = 0.4f;

    [Tooltip("Show each stage introduction once per session. Untick to see it on every visit while testing.")]
    [SerializeField] private bool showOnlyOnce = true;

    [Header("1. Disassembly stage - shown when the player enters the station")]
    [SerializeField] private bool showDisassemblyIntro = true;

    [Tooltip("A brief introduction to the disassembly stage.")]
    [SerializeField] private List<DialogLine> disassemblyIntroLines = new List<DialogLine>
    {
        Line("Welcome to the PC Disassembly and Assembly station. First you will take the system unit apart, one component at a time. Then you will put it back together."),
        Line("Work like a technician: the PC is shut down and unplugged, and you handle each component by its edges. Follow the correct order - some parts must come off before others."),
    };

    [Tooltip("Station controls on PC (keyboard and mouse). Shown right after the introduction.")]
    [SerializeField] private List<DialogLine> disassemblyControlsPC = new List<DialogLine>
    {
        Line("Controls: click and drag a part to remove it. Parts and cables that glow can be removed now."),
        Line("Drag every part you remove onto an empty slot in the hotbar. The hotbar keeps the parts until you assemble the PC again."),
        Line("To unplug a cable, drag its plug away from the port. Click a RAM clip or the CPU lever to open it."),
        Line("Scroll the mouse wheel to zoom, drag an empty area to move the view, and press R to reset it. Press E to leave the station."),
    };

    [Tooltip("Station controls on a phone or tablet. Shown right after the introduction.")]
    [SerializeField] private List<DialogLine> disassemblyControlsTouch = new List<DialogLine>
    {
        Line("Controls: touch and drag a part to remove it. Parts and cables that glow can be removed now."),
        Line("Drag every part you remove onto an empty slot in the hotbar. The hotbar keeps the parts until you assemble the PC again."),
        Line("To unplug a cable, drag its plug away from the port. Tap a RAM clip or the CPU lever to open it."),
        Line("Pinch to zoom and drag an empty area to move the view. Use the Exit button to leave the station."),
    };

    [Header("2. Assembly stage - shown when assembly begins")]
    [Tooltip("Shown once disassembly is finished - straight after the \"PC disassembled\" message if " +
             "the learner is still at the station, otherwise the next time they enter it.")]
    [SerializeField] private bool showAssemblyIntro = true;

    [SerializeField] private List<DialogLine> assemblyIntroLines = new List<DialogLine>
    {
        Line("Assembly stage: put every part back into the system unit and reconnect every cable you removed."),
        Line("Work in reverse order - the last part you removed goes back first. Close any clip or lever before fitting the part that sits on top of it."),
    };

    [SerializeField] private List<DialogLine> assemblyControlsPC = new List<DialogLine>
    {
        Line("Drag a part from the hotbar onto its place in the system unit. Its slot lights up when the part fits there."),
        Line("Glowing cable plugs still need to go back in. Drag each one to its port and let go to plug it in."),
    };

    [SerializeField] private List<DialogLine> assemblyControlsTouch = new List<DialogLine>
    {
        Line("Touch and drag a part from the hotbar onto its place in the system unit. Its slot lights up when the part fits there."),
        Line("Glowing cable plugs still need to go back in. Drag each one to its port and let go to plug it in."),
    };

    [Header("3. First part dragged to the hotbar")]
    [SerializeField] private bool showHotbarDescriptionTip = true;

    [Tooltip("Shown the first time a part removed from the PC lands in the hotbar.")]
    [SerializeField] private List<DialogLine> hotbarDescriptionLines = new List<DialogLine>
    {
        Line("The part is now in your hotbar. {Click} it - press and let go without dragging - to see its description: what the part is and what it does."),
        Line("Get to know each component as you remove it. You will need to recognise every one of them when you put the PC back together."),
    };

    [Tooltip("Optional. A prefab with PulsingHighlightRing (the same one HotbarTutorialTrigger uses). " +
             "It pulses on the slot holding the part until the learner presses it.")]
    [SerializeField] private RectTransform slotHighlightPrefab;

    [Tooltip("Seconds the slot ring stays after the tip closes, if the learner never presses the part.")]
    [Min(0f)]
    [SerializeField] private float slotHighlightMaxSeconds = 20f;

    [Header("Tasks")]
    [SerializeField] private string disassembleTaskId = "disassemble_pc";
    [SerializeField] private string assembleTaskId = "assemble_pc";

    [Header("Diagnostics")]
    [SerializeField] private bool verbose = true;

    // ==================================================================

    private bool disassemblyShown, assemblyShown, tipShown;
    private bool wasInside;
    private bool wantStageIntro;
    private bool wantTip;
    private PCPartItem tipPart;
    private HotbarSlot tipSlot;

    private bool showing;
    private int shownFrame;
    private System.Action afterDialog;
    private float quietSince = -1f;
    private DialogSequence runtimeSequence;
    private bool warnedNoStation, warnedNoDialogManager;

    private RectTransform ring;
    private HotbarSlot ringSlot;
    private PCPartItem ringPart;
    private float ringUntil = float.MaxValue;

    /// <summary>
    /// True while a station introduction is waiting to show or showing. HotbarTutorialTrigger reads
    /// it so its own dialog comes after this one instead of replacing it.
    /// </summary>
    public static bool IsBusy
    {
        get
        {
            AssemblyStationTutorial t = Instance;
            if (t == null || !t.isActiveAndEnabled || !Allowed()) return false;
            if (t.showing) return true;
            if (t.station == null || !t.station.IsUsingStationCamera) return false;

            // wasInside is false on the entry frame until this component's own Update has run -
            // Unity does not order two components' Updates, so the entry is counted from here too.
            bool entering = t.wantStageIntro || !t.wasInside || t.AssemblyJustBegan();
            return entering && t.HasStageIntroToShow();
        }
    }

    // ==================================================================

    private static DialogLine Line(string text) => new DialogLine { speakerName = "Guide", text = text };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[AssemblyStationTutorial] A second one on '{name}' - only the first is used.", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    private void OnEnable() => DragController.PartStoredInHotbar += OnPartStored;

    private void OnDisable()
    {
        DragController.PartStoredInHotbar -= OnPartStored;
        RemoveRing();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (station == null) station = FindStation();

        if (station == null && !warnedNoStation)
        {
            warnedNoStation = true;
            Debug.LogWarning("[AssemblyStationTutorial] No Station assigned - drag the PC assemble/disassemble " +
                             "station's StationCameraSwitch into Station. Only the hotbar tip can show.", this);
        }
    }

    // ==================================================================

    private void Update()
    {
        bool inside = station != null && station.IsUsingStationCamera;

        if (!Allowed())
        {
            wantStageIntro = false;
            wantTip = false;
            RemoveRing();
            wasInside = inside;
            return;
        }

        if (inside && !wasInside) wantStageIntro = true;
        if (!inside) wantStageIntro = false;
        wasInside = inside;

        // The last part just came out while the learner is still at the bench.
        if (inside && AssemblyJustBegan()) wantStageIntro = true;

        // Safety: if another script opened a dialog over this one, its ending never reaches us.
        if (showing && Time.frameCount > shownFrame &&
            (DialogManager.Instance == null || !DialogManager.Instance.IsDialogOpen))
            DialogFinished();

        UpdateRing(inside);

        if (showing || !inside) return;
        if (!Quiet()) return;

        if (wantStageIntro)
        {
            wantStageIntro = false;
            if (TryShowStageIntro()) return;
        }

        if (wantTip)
        {
            wantTip = false;
            ShowHotbarTip();
        }
    }

    private static bool Allowed()
    {
        // Story Mode only: an assessment shows no tutorials, and a COC4 repair ticket is not COC1.
        return !AssessmentMode.IsActive && !RepairTicketMode.IsActive;
    }

    private bool AssemblyJustBegan()
    {
        return showAssemblyIntro && !assemblyShown && Done(disassembleTaskId) && !Done(assembleTaskId);
    }

    /// <summary>True when the current stage still has an introduction to show.</summary>
    private bool HasStageIntroToShow()
    {
        if (Done(assembleTaskId)) return false;

        if (!Done(disassembleTaskId))
            return showDisassemblyIntro && !(showOnlyOnce && disassemblyShown) &&
                   (Any(disassemblyIntroLines) || Any(Controls(disassemblyControlsPC, disassemblyControlsTouch)));

        return showAssemblyIntro && !(showOnlyOnce && assemblyShown) &&
               (Any(assemblyIntroLines) || Any(Controls(assemblyControlsPC, assemblyControlsTouch)));
    }

    private bool TryShowStageIntro()
    {
        if (!HasStageIntroToShow()) return false;

        var lines = new List<DialogLine>();

        if (!Done(disassembleTaskId))
        {
            disassemblyShown = true;
            AddLines(lines, disassemblyIntroLines);
            AddLines(lines, Controls(disassemblyControlsPC, disassemblyControlsTouch));
            Log("Station entered - disassembly introduction and controls.");
        }
        else
        {
            assemblyShown = true;
            AddLines(lines, assemblyIntroLines);
            AddLines(lines, Controls(assemblyControlsPC, assemblyControlsTouch));
            Log("Assembly stage - introduction and controls.");
        }

        return Show(lines, null);
    }

    // ==================================================================
    // Hotbar description tip
    // ==================================================================

    private void OnPartStored(PCPartItem part, HotbarSlot slot, bool fromWorld)
    {
        if (!fromWorld || !showHotbarDescriptionTip || tipShown || !Allowed()) return;

        tipPart = part;
        tipSlot = slot;
        wantTip = true;
    }

    private void ShowHotbarTip()
    {
        HotbarSlot slot = SlotHolding(tipPart);
        if (slot == null && tipSlot != null && !tipSlot.IsEmpty) slot = tipSlot;
        if (slot == null) slot = AnyFullSlot();
        if (slot == null) return;   // the part has already gone back out - nothing to press

        tipShown = true;
        Log($"First part in the hotbar ('{(slot.StoredItem != null ? slot.StoredItem.name : "?")}') - description tip.");

        AddRing(slot);
        ringUntil = float.MaxValue;   // counted from when the dialog closes

        Show(new List<DialogLine>(Fill(hotbarDescriptionLines)), () =>
        {
            ringUntil = Time.unscaledTime + slotHighlightMaxSeconds;
        });
    }

    private static HotbarSlot SlotHolding(PCPartItem part)
    {
        if (part == null || HotbarManager.Instance == null || HotbarManager.Instance.slots == null) return null;
        foreach (HotbarSlot s in HotbarManager.Instance.slots)
            if (s != null && s.StoredItem == part) return s;
        return null;
    }

    private static HotbarSlot AnyFullSlot()
    {
        if (HotbarManager.Instance == null || HotbarManager.Instance.slots == null) return null;
        foreach (HotbarSlot s in HotbarManager.Instance.slots)
            if (s != null && !s.IsEmpty) return s;
        return null;
    }

    private void AddRing(HotbarSlot slot)
    {
        RemoveRing();
        if (slotHighlightPrefab == null || slot == null) return;

        RectTransform parent = slot.transform as RectTransform;
        if (parent == null) return;

        ring = Instantiate(slotHighlightPrefab, parent);

        // A layout group on the slot would squash the ring like another child.
        LayoutElement layout = ring.GetComponent<LayoutElement>();
        if (layout == null) layout = ring.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        ring.anchorMin = Vector2.zero;
        ring.anchorMax = Vector2.one;
        ring.offsetMin = Vector2.zero;
        ring.offsetMax = Vector2.zero;

        // It stays up after the dialog closes, so it must never catch the press it is asking for.
        foreach (Graphic g in ring.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

        ringSlot = slot;
        ringPart = slot.StoredItem;
    }

    private void UpdateRing(bool inside)
    {
        if (ring == null) return;

        bool pressed = DescriptionOpen() || ringSlot == null || ringSlot.StoredItem != ringPart;
        if (pressed || !inside || Time.unscaledTime >= ringUntil) RemoveRing();
    }

    private void RemoveRing()
    {
        if (ring != null) Destroy(ring.gameObject);
        ring = null;
        ringSlot = null;
        ringPart = null;
    }

    private static bool DescriptionOpen()
    {
        PartDescriptionPanel panel = PartDescriptionPanel.Instance;
        return panel != null && panel.panelRoot != null && panel.panelRoot.activeInHierarchy;
    }

    // ==================================================================
    // Showing
    // ==================================================================

    /// <summary>No dialog, quiz, pause menu, description or drag for Quiet Seconds.</summary>
    private bool Quiet()
    {
        DialogManager dm = DialogManager.Instance;
        if (dm == null)
        {
            if (!warnedNoDialogManager)
            {
                warnedNoDialogManager = true;
                Debug.LogWarning("[AssemblyStationTutorial] No DialogManager in this scene - nothing can show.", this);
            }
            return false;
        }

        bool busy = dm.IsDialogOpen
                    || PauseMenuController.AnyPaused
                    || (QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen)
                    || (DragController.Instance != null && DragController.Instance.IsDragging)
                    || CableConnector.IsAnyDragging
                    || DescriptionOpen();

        if (busy)
        {
            quietSince = -1f;
            return false;
        }

        if (quietSince < 0f) quietSince = Time.unscaledTime;
        return Time.unscaledTime - quietSince >= quietSeconds;
    }

    private bool Show(List<DialogLine> lines, System.Action whenClosed)
    {
        if (lines == null || lines.Count == 0 || DialogManager.Instance == null) return false;

        if (runtimeSequence == null)
        {
            runtimeSequence = ScriptableObject.CreateInstance<DialogSequence>();
            runtimeSequence.name = "AssemblyStationTutorial (runtime)";
        }
        runtimeSequence.lines = lines;

        showing = true;
        shownFrame = Time.frameCount;
        afterDialog = whenClosed;
        quietSince = -1f;

        DialogManager.Instance.ShowDialog(runtimeSequence, DialogFinished);
        return true;
    }

    private void DialogFinished()
    {
        if (!showing) return;
        showing = false;
        quietSince = -1f;

        System.Action next = afterDialog;
        afterDialog = null;
        next?.Invoke();
    }

    // ==================================================================
    // Text
    // ==================================================================

    private static bool TouchDevice => Application.isMobilePlatform || CursorControl.TouchControlsVisible;

    private static List<DialogLine> Controls(List<DialogLine> pc, List<DialogLine> touch) =>
        TouchDevice && Any(touch) ? touch : pc;

    private static bool Any(List<DialogLine> lines)
    {
        if (lines == null) return false;
        foreach (DialogLine l in lines)
            if (l != null && !string.IsNullOrWhiteSpace(l.text)) return true;
        return false;
    }

    private static void AddLines(List<DialogLine> into, List<DialogLine> from)
    {
        into.AddRange(Fill(from));
    }

    private static IEnumerable<DialogLine> Fill(List<DialogLine> lines)
    {
        if (lines == null) yield break;

        bool touch = TouchDevice;
        foreach (DialogLine l in lines)
        {
            if (l == null || string.IsNullOrWhiteSpace(l.text)) continue;

            string text = l.text
                .Replace("{Click}", touch ? "Tap" : "Click")
                .Replace("{click}", touch ? "tap" : "click")
                .Replace("{exit}", touch ? "use the Exit button" : "press E");

            yield return new DialogLine
            {
                speakerName = string.IsNullOrWhiteSpace(l.speakerName) ? "Guide" : l.speakerName,
                text = text
            };
        }
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private static bool Done(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId) || TaskManager.Instance == null) return false;
        return TaskManager.Instance.IsTaskComplete(taskId.Trim());
    }

    /// <summary>The station the hotbar scripts already point at, so an empty Station field still works.</summary>
    private StationCameraSwitch FindStation()
    {
        StationCameraSwitch found =
            ReadField<StationCameraSwitch>(FindAnyObjectByType<HotbarTutorialTrigger>(), "station") ??
            ReadField<StationCameraSwitch>(FindAnyObjectByType<HotbarVisibilityController>(), "assemblyStation");

        if (found != null) Log($"Station not assigned - using '{found.name}' from the hotbar scripts.");
        return found;
    }

    private static T ReadField<T>(Object owner, string field) where T : Object
    {
        if (owner == null) return null;
        FieldInfo f = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return f != null ? f.GetValue(owner) as T : null;
    }

    private void Log(string message)
    {
        if (verbose) Debug.Log("[AssemblyStationTutorial] " + message, this);
    }

    [ContextMenu("Test: forget what was shown")]
    private void ForgetShown()
    {
        disassemblyShown = assemblyShown = tipShown = false;
        Log("Will show every message again.");
    }
}
