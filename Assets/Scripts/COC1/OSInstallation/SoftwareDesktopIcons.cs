using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puts a shortcut on the desktop for every application installed from the Software Center, as each
/// one finishes - the way a real installation does.
///
/// ATTACH THIS TO: the desktop panel (the object COC1DesktopScreens built, the one with the "Icons"
/// child under it). Nothing to wire: it finds the Software Center by its group task id, finds the
/// Icons container by name, and uses each item's own name and icon sprite.
///
/// ---------------------------------------------------------------------------------------------
/// WHY THIS IS WORTH HAVING AT ALL
///
/// Installing software has no visible result otherwise. The row in the Software Center turns green
/// and that is the end of it - the learner is TOLD the install worked rather than SEEING that it
/// did. A shortcut appearing on the desktop is the evidence a technician actually looks for, and it
/// is what they check on a real machine before telling a client the job is done.
///
/// It also makes the count obvious. Four applications on the job order, four new icons on the
/// desktop: a learner who has installed three can see the gap without reading a checklist.
/// ---------------------------------------------------------------------------------------------
///
/// IT REBUILDS FROM THE TASK STATE, NOT FROM THE EVENT ALONE. The event is what makes an icon
/// appear the moment an install finishes; the rebuild on enable is what puts them all back when the
/// desktop is closed and reopened, or when a saved game is resumed with the software already
/// installed. An icon list that only listened to the event would be empty after every reload.
///
/// CLICKING A SHORTCUT shows the dialog assigned to Not Part Of Task Dialog - "the software is
/// installed; running it is not part of this job order". Assign a DialogSequence asset to that
/// field, the same way OSInstallManager takes noPowerWarning and BootableUSBCreator takes
/// wrongImageWarning. Nothing is reported as a mistake by default: opening a shortcut out of
/// curiosity is not a procedural error, it is a learner asking what happens next.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class SoftwareDesktopIcons : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The Software Center's ChecklistToolManager. Leave empty to find the one whose Group " +
             "Task Id matches the id below - which is what you want, because Device Manager, " +
             "Windows Update and the Software Center are all this same component.")]
    [SerializeField] private ChecklistToolManager softwareCenter;

    [Tooltip("Used to pick the right manager when the field above is empty.")]
    [SerializeField] private string groupTaskId = "install_app_software";

    [Header("Where the icons go")]
    [Tooltip("Parent for the shortcuts. Leave empty to use the child called \"Icons\" if this object " +
             "has one - which is the container COC1DesktopScreens puts the five built-in icons in - " +
             "and this object itself otherwise.")]
    [SerializeField] private RectTransform iconsRoot;

    [Tooltip("Position of the first shortcut in DESIGN pixels, measured from the TOP-LEFT of the " +
             "desktop, exactly like the numbers in COC1DesktopScreens.\n\nThe five built-in icons " +
             "fill the first column: x = 40, y = 40, 172, 304, 436, 568. The default here starts a " +
             "second column beside them.")]
    [SerializeField] private Vector2 firstIconPosition = new Vector2(190f, 40f);

    [Tooltip("Gap from one shortcut to the next: x across, y DOWN. 132 down matches the built-in " +
             "column's spacing.")]
    [SerializeField] private Vector2 spacing = new Vector2(150f, 132f);

    [Tooltip("How many shortcuts fit in a column before the next column starts. Windows fills a " +
             "column downwards first, and 5 is what the desktop's height allows here.")]
    [SerializeField] private int iconsPerColumn = 5;

    [Tooltip("Size of one shortcut in design pixels. 150 x 118 matches the built-in icons.")]
    [SerializeField] private Vector2 iconSize = new Vector2(150f, 118f);

    [Header("Clicking a shortcut")]
    [Tooltip("ON: a shortcut can be clicked and answers with the dialog below. OFF: the shortcuts " +
             "are scenery and clicks pass straight through them.")]
    [SerializeField] private bool clickable = true;

    [Tooltip("Shown when the learner clicks an installed application. Assign a DialogSequence " +
             "asset, e.g. \"Notepad++ is installed. Running an application is not part of this job " +
             "order - carry on with the remaining tasks in the Software Center.\"\n\nLeave it empty " +
             "and a click writes a Console warning instead, so a missing asset is never silent.")]
    [SerializeField] private DialogSequence notPartOfTaskDialog;

    [Tooltip("ON (recommended): the dialog appears even while an assessment has tutorial dialogs " +
             "suppressed. This is an answer to something the learner just did - suppress it and the " +
             "icon looks broken rather than restrained.")]
    [SerializeField] private bool showDuringAssessment = true;

    [Tooltip("OFF (recommended): clicking a shortcut is curiosity, not a procedural error, so it is " +
             "not recorded against the learner. Tick it only if your assessment is meant to treat " +
             "opening an application as a deviation from the job order.")]
    [SerializeField] private bool reportAsMistake;

    [Header("Look")]
    [Tooltip("Drawn when the item has no icon sprite of its own - a plain tile the same colour as " +
             "the built-in icons, so a missing sprite still looks like a shortcut rather than a bug.")]
    [SerializeField] private Color tileColour = new Color32(0x2E, 0x4C, 0x73, 0xFF);

    [SerializeField] private float tileSize = 64f;
    [SerializeField] private float labelFontSize = 15f;
    [SerializeField] private Color labelColour = Color.white;

    [Header("Diagnostics")]
    [SerializeField] private bool logIcons = true;

    // The design space COC1DesktopScreens lays the desktop out in. Its icons are positioned by
    // NORMALISED anchors against these numbers, so the shortcuts here are too - otherwise they
    // would sit at fixed pixel offsets while the built-in ones moved with the panel, and the two
    // columns would drift apart on a phone.
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;

    // Insertion order, so a shortcut keeps its place and Relayout is predictable. A Dictionary's
    // enumeration order is not something to lay a grid out from.
    private readonly List<string> order = new List<string>();
    private readonly Dictionary<string, GameObject> icons = new Dictionary<string, GameObject>();

    private bool subscribed;
    private float nextClickAllowed;
    private float nextSearch;
    private int searchesLeft = 40;

    // ==================================================================

    private void OnEnable()
    {
        TryBind();
    }

    private void Start()
    {
        // Again in Start: ChecklistToolManager registers its tasks in its own Start, so on the very
        // first frame IsItemComplete can answer "no" for something a loaded save already has.
        TryBind();
    }

    private void OnDisable()
    {
        if (softwareCenter != null && subscribed)
            softwareCenter.OnItemInstallComplete -= HandleInstalled;

        subscribed = false;
    }

    /// <summary>
    /// Keeps looking for the Software Center until it finds one.
    ///
    /// THIS RETRY IS NOT BELT AND BRACES. The desktop panel starts switched off, so this
    /// component's Start may never run at all - the same trap that stopped
    /// OSInstallPercentLabel subscribing. OnEnable covers the moment the desktop is opened; the
    /// retry covers a manager that only wakes up later. It is throttled and it gives up, so a
    /// scene genuinely without a Software Center costs a few searches and one warning rather than
    /// a search every frame forever.
    /// </summary>
    private void LateUpdate()
    {
        if (softwareCenter != null || searchesLeft <= 0) return;
        if (Time.unscaledTime < nextSearch) return;

        nextSearch = Time.unscaledTime + 0.5f;
        searchesLeft--;

        TryBind();

        if (softwareCenter == null && searchesLeft == 0)
            Debug.LogWarning($"[SoftwareDesktopIcons] Gave up looking: no ChecklistToolManager in " +
                             $"this scene has the group task id '{groupTaskId}', so no shortcuts " +
                             "will appear. Assign Software Center by hand, or correct the id.", this);
    }

    private void TryBind()
    {
        EnsureManager();
        Subscribe();
        Refresh();
    }

    private void Subscribe()
    {
        if (softwareCenter == null || subscribed) return;

        softwareCenter.OnItemInstallComplete += HandleInstalled;
        subscribed = true;
    }

    // One icon now, rather than a whole rebuild: the rebuild would also be correct, but this way
    // the shortcut appears as that install finishes rather than all of them appearing together.
    private void HandleInstalled(ChecklistItemData item) => AddIcon(item);

    // ==================================================================

    /// <summary>Brings the desktop in line with what is actually installed.</summary>
    [ContextMenu("Refresh icons")]
    public void Refresh()
    {
        if (softwareCenter == null) return;

        IReadOnlyList<ChecklistItemData> items = softwareCenter.Items;
        if (items == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            ChecklistItemData item = items[i];
            if (item == null) continue;

            if (softwareCenter.IsItemComplete(item)) AddIcon(item);
            else RemoveIcon(item);
        }
    }

    /// <summary>Removes every generated shortcut. Only the generated ones - the built-in icons are
    /// not touched, because this only ever destroys objects it created itself.</summary>
    [ContextMenu("Clear icons")]
    public void Clear()
    {
        foreach (string key in order)
        {
            if (icons.TryGetValue(key, out GameObject go) && go != null) Destroy(go);
        }

        order.Clear();
        icons.Clear();
    }

    private void AddIcon(ChecklistItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemTaskId)) return;
        if (icons.ContainsKey(item.itemTaskId)) return;

        RectTransform parent = ResolveParent();
        if (parent == null)
        {
            Debug.LogWarning("[SoftwareDesktopIcons] Neither this object nor Icons Root is a UI " +
                             "object, so there is nowhere to put the shortcuts. Put this component " +
                             "on the desktop panel, or assign Icons Root.", this);
            return;
        }

        GameObject icon = Build(parent, item, order.Count);

        order.Add(item.itemTaskId);
        icons[item.itemTaskId] = icon;

        if (logIcons)
            Debug.Log($"[SoftwareDesktopIcons] '{Caption(item)}' installed - shortcut added to the " +
                      "desktop.", this);
    }

    private void RemoveIcon(ChecklistItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemTaskId)) return;
        if (!icons.TryGetValue(item.itemTaskId, out GameObject icon)) return;

        if (icon != null) Destroy(icon);

        icons.Remove(item.itemTaskId);
        order.Remove(item.itemTaskId);

        // Everything after it moves up a place, so the desktop keeps no gap where an uninstalled
        // application used to be.
        Relayout();
    }

    private void Relayout()
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (!icons.TryGetValue(order[i], out GameObject icon) || icon == null) continue;

            var rect = icon.transform as RectTransform;
            if (rect != null) PlaceAt(rect, i);
        }
    }

    private RectTransform ResolveParent()
    {
        if (iconsRoot != null) return iconsRoot;

        // The container the five built-in icons live in, so the shortcuts share their sort order
        // and a window opened on top covers them too.
        Transform found = transform.Find("Icons");
        if (found is RectTransform asRect) return asRect;

        return transform as RectTransform;
    }

    // ==================================================================
    // Placement - the same normalised-anchor scheme COC1DesktopScreens.Place uses, so these
    // shortcuts line up with the built-in ones at every screen size.
    // ==================================================================

    private void PlaceAt(RectTransform rect, int index)
    {
        int perColumn = Mathf.Max(1, iconsPerColumn);

        int column = index / perColumn;
        int row = index % perColumn;

        float x = firstIconPosition.x + column * spacing.x;
        float y = firstIconPosition.y + row * spacing.y;   // y grows DOWNWARD, as in the builder

        Anchor(rect, x, y, iconSize.x, iconSize.y);
    }

    /// <summary>
    /// Design-space rectangle, top-left origin, converted to anchors. Identical arithmetic to
    /// COC1DesktopScreens.Place - deliberately, because a shortcut placed any other way would not
    /// stay beside the icons it is meant to sit next to.
    /// </summary>
    private static void Anchor(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = new Vector2(x / DesignWidth, 1f - (y + h) / DesignHeight);
        rect.anchorMax = new Vector2((x + w) / DesignWidth, 1f - y / DesignHeight);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    /// <summary>Child rectangle, positioned inside its parent in the same design pixels.</summary>
    private void AnchorChild(RectTransform rect, float x, float y, float w, float h)
    {
        float cellW = Mathf.Max(1f, iconSize.x);
        float cellH = Mathf.Max(1f, iconSize.y);

        rect.anchorMin = new Vector2(x / cellW, 1f - (y + h) / cellH);
        rect.anchorMax = new Vector2((x + w) / cellW, 1f - y / cellH);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    // ==================================================================

    private GameObject Build(RectTransform parent, ChecklistItemData item, int index)
    {
        var go = new GameObject("Icon_Installed_" + item.itemTaskId, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        PlaceAt(rect, index);

        if (clickable) MakeClickable(rect, item);

        // The tile. Centred in the cell at the same place the built-in icons put theirs.
        var tileGo = new GameObject("IconTile", typeof(RectTransform));
        var tileRect = (RectTransform)tileGo.transform;
        tileRect.SetParent(rect, false);

        float side = Mathf.Max(8f, tileSize);
        AnchorChild(tileRect, (iconSize.x - side) * 0.5f, 4f, side, side);

        var image = tileGo.AddComponent<Image>();

        if (item.icon != null)
        {
            image.sprite = item.icon;
            image.color = Color.white;
            image.preserveAspect = true;
        }
        else
        {
            // No sprite on the asset: a plain tile the colour of the built-in icons. Not an empty
            // space and not a white square - either of those reads as something broken.
            image.color = tileColour;
        }

        // The whole cell is the click target, not the tile - so the caption is clickable too, and a
        // sprite with transparent corners has no dead spots. Same arrangement as the built-in icons.
        image.raycastTarget = false;

        var labelGo = new GameObject("IconLabel", typeof(RectTransform));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.SetParent(rect, false);

        AnchorChild(labelRect, 0f, 4f + side + 6f, iconSize.x, Mathf.Max(20f, iconSize.y - side - 10f));

        var label = labelGo.AddComponent<TextMeshProUGUI>();

        // Set explicitly: a TMP text created from script has no font until something gives it one.
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        label.text = Caption(item);
        label.fontSize = labelFontSize;
        label.color = labelColour;
        label.alignment = TextAlignmentOptions.Top;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        return go;
    }

    private static string Caption(ChecklistItemData item)
    {
        return string.IsNullOrEmpty(item.displayName) ? item.itemTaskId : item.displayName;
    }

    // ==================================================================
    // Clicking a shortcut
    // ==================================================================

    /// <summary>
    /// Turns the whole cell into a button.
    ///
    /// The Image is the trick the built-in icons use and it is not optional: a Button needs a
    /// Graphic to be hit by a raycast, and an alpha of 1/255 is invisible while still being
    /// something to hit. cullTransparentMesh has to be switched off as well, or Unity drops the
    /// mesh for being invisible and the click target disappears with it.
    /// </summary>
    private void MakeClickable(RectTransform rect, ChecklistItemData item)
    {
        var hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 1f / 255f);
        hit.raycastTarget = true;

        var cr = rect.GetComponent<CanvasRenderer>();
        if (cr != null) cr.cullTransparentMesh = false;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;

        // Captured, so each shortcut reports its own application by name.
        ChecklistItemData clicked = item;
        button.onClick.AddListener(() => HandleShortcutClicked(clicked));
    }

    private void HandleShortcutClicked(ChecklistItemData item)
    {
        if (item == null) return;

        // A tap on a phone lands twice often enough to matter, and two identical dialogs queued on
        // top of each other means the learner dismisses the same message twice.
        if (Time.unscaledTime < nextClickAllowed) return;
        nextClickAllowed = Time.unscaledTime + 0.5f;

        if (reportAsMistake)
        {
            COC1AssessmentManager.ReportMistake(
                $"{Caption(item)}: opened an installed application - running software is not part " +
                "of this job order.", item);
        }

        if (notPartOfTaskDialog != null && DialogManager.Instance != null)
        {
            // ignoreSuppression when showDuringAssessment: this answers something the learner just
            // did. See the tooltip - a silent icon reads as a broken one.
            DialogManager.Instance.ShowDialog(notPartOfTaskDialog, null, showDuringAssessment);
            return;
        }

        // Named plainly, the way OSInstallManager reports a missing warning asset, because the
        // symptom otherwise is "clicking the icon does nothing" with nothing to go on.
        Debug.LogWarning($"[SoftwareDesktopIcons] '{Caption(item)}' was clicked but no dialog is " +
                         "assigned to Not Part Of Task Dialog" +
                         (DialogManager.Instance == null ? " (and there is no DialogManager in the " +
                          "scene)" : "") + " - the player saw nothing on screen.", this);
    }

    // ==================================================================

    /// <summary>
    /// Finds the Software Center among the checklist tools in the scene.
    ///
    /// Matched on groupTaskId rather than "the first one found", because Device Manager, Windows
    /// Update and the Software Center are all the same component - and drivers and patches do not
    /// put shortcuts on the desktop. Picking the wrong one would litter the desktop with icons for
    /// things that are not applications.
    /// </summary>
    private void EnsureManager()
    {
        if (softwareCenter != null) return;

        FieldInfo field = typeof(ChecklistToolManager).GetField(
            "groupTaskId", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null)
        {
            Debug.LogWarning("[SoftwareDesktopIcons] ChecklistToolManager has no field called " +
                             "'groupTaskId' any more, so the Software Center cannot be identified " +
                             "automatically. Assign Software Center on this component by hand.", this);
            searchesLeft = 0;
            return;
        }

        ChecklistToolManager[] candidates = FindObjectsByType<ChecklistToolManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < candidates.Length; i++)
        {
            ChecklistToolManager candidate = candidates[i];
            if (candidate == null) continue;

            string id = field.GetValue(candidate) as string;
            if (id != groupTaskId) continue;

            softwareCenter = candidate;

            if (logIcons)
                Debug.Log($"[SoftwareDesktopIcons] Using '{candidate.name}' as the Software Center " +
                          $"(group task '{groupTaskId}').", this);
            return;
        }
    }

    // ==================================================================

    [ContextMenu("Report")]
    public void Report()
    {
        string manager = softwareCenter != null ? softwareCenter.name : "not found yet";
        RectTransform parent = ResolveParent();

        Debug.Log("[SoftwareDesktopIcons] REPORT\n" +
                  $"  Software Center : {manager} (group '{groupTaskId}')\n" +
                  $"  Icons parent    : {(parent != null ? parent.name : "none - no RectTransform")}\n" +
                  $"  Shortcuts now   : {order.Count}\n" +
                  $"  Grid            : first ({firstIconPosition.x}, {firstIconPosition.y}), " +
                  $"spacing ({spacing.x}, {spacing.y}), {Mathf.Max(1, iconsPerColumn)} per column",
                  this);
    }
}