using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a one-time "this is your hotbar" tutorial with a pulsing highlight
/// ring around the hotbar, the first time the player enters a given
/// assemble/disassemble station -- Story Mode only.
///
/// Supports MULTIPLE hotbar panels at once (e.g. two separate 5-slot
/// panels) -- every entry in Hotbar Roots gets its own highlight ring
/// instance, all shown/hidden together.
///
/// IMPORTANT if a hotbar panel has a Vertical/Horizontal/Grid Layout Group:
/// that component actively resizes and repositions EVERY child to fit its
/// own arrangement -- a plain instantiated ring would get squashed down to
/// a sliver like just another slot, instead of stretching to cover the
/// whole panel. This script adds a LayoutElement to each ring instance and
/// sets ignoreLayout = true, which tells the layout group to skip it
/// entirely, leaving the ring's own anchor-stretch settings in full effect.
/// This is harmless to add even if a given panel has no layout group at all.
///
/// WHY THIS CHECKS AssessmentMode.IsActive ITSELF: AssessmentMode's own doc
/// comment says "tutorials are suppressed" during a graded run, but nothing
/// in the project currently enforces that automatically -- no existing
/// script reads AssessmentMode.IsActive anywhere yet. So this checks it
/// directly rather than assuming some other system already filtered it out.
///
/// Deliberately a standalone companion script rather than a change to
/// StationCameraSwitch or InteractableObject, so existing station/tutorial
/// behavior elsewhere is completely untouched -- this only watches
/// StationCameraSwitch.IsUsingStationCamera from the outside.
///
/// ATTACH THIS TO: any object already in the scene (e.g. the Managers
/// object) -- it doesn't need to be on the station or either hotbar.
///
/// SETUP:
///   1. Station -> the assemble/disassemble station's StationCameraSwitch
///      (the same one already toggling the hotbar(s) via its
///      "Enable On Station View" array).
///   2. Hotbar Tutorial Dialog -> a DialogSequence, e.g. "This is your
///      hotbar -- this is where you drag PC parts after disassembling them."
///   3. Hotbar Roots -> BOTH hotbar panels' RectTransforms (size 2 for a
///      two-panel setup). Each is the whole panel, not one individual slot.
///   4. Highlight Ring Prefab -> a prefab with PulsingHighlightRing on it
///      (the same one used for the USB creator tutorial works as-is).
/// </summary>
public class HotbarTutorialTrigger : MonoBehaviour
{
    [Tooltip("The assemble/disassemble station to watch for the player entering.")]
    [SerializeField] private StationCameraSwitch station;

    [Tooltip("E.g. \"This is your hotbar -- this is where you drag PC parts after disassembling them.\"")]
    [SerializeField] private DialogSequence hotbarTutorialDialog;

    [Tooltip("Every hotbar panel to highlight at once (the whole panel, not one slot). Size 2 for a two-panel setup.")]
    [SerializeField] private RectTransform[] hotbarRoots;

    [Tooltip("A prefab with a PulsingHighlightRing component. One instance is created per Hotbar Root entry, stretched to fill it, while the tutorial dialog is open, then all are destroyed when the dialog closes.")]
    [SerializeField] private RectTransform highlightRingPrefab;

    [Tooltip("Show only once per session. Untick to re-test repeatedly without restarting Play mode.")]
    [SerializeField] private bool showOnlyOnce = true;

    [Tooltip("Seconds with no dialog open before this one appears. It waits its turn: the station's " +
             "own introduction and controls (AssemblyStationTutorial) come first.")]
    [Min(0f)]
    [SerializeField] private float quietSecondsBeforeShowing = 0.3f;

    private bool hasShown;
    private bool wasUsingStationCamera;
    private bool pending;
    private float quietSince = -1f;
    private readonly List<RectTransform> activeRingInstances = new List<RectTransform>();

    private void Update()
    {
        if (station == null) return;

        bool isUsingStationCamera = station.IsUsingStationCamera;

        // Rising edge only -- the moment the player enters, not every frame they're inside.
        if (isUsingStationCamera && !wasUsingStationCamera) { pending = true; quietSince = -1f; }
        if (!isUsingStationCamera) pending = false;
        wasUsingStationCamera = isUsingStationCamera;

        // WAITS ITS TURN instead of firing on the entry frame. DialogManager shows one dialog at a
        // time: opening this over another one (the station introduction, a removal tip) cut that
        // one off and lost its callback - and if this one was the one cut off, its rings stayed
        // pulsing for good, because HideHighlights is that callback.
        if (!pending) return;

        if (OtherDialogBusy())
        {
            quietSince = -1f;
            return;
        }

        if (quietSince < 0f) quietSince = Time.unscaledTime;
        if (Time.unscaledTime - quietSince < quietSecondsBeforeShowing) return;

        pending = false;
        TryShowTutorial();
    }

    private static bool OtherDialogBusy()
    {
        if (AssemblyStationTutorial.IsBusy) return true;
        return DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;
    }

    private void TryShowTutorial()
    {
        if (AssessmentMode.IsActive) return; // Story Mode only
        if (showOnlyOnce && hasShown) return;

        if (hotbarTutorialDialog == null)
        {
            Debug.LogWarning("[HotbarTutorialTrigger] No Hotbar Tutorial Dialog assigned.", this);
            return;
        }

        if (DialogManager.Instance == null)
        {
            Debug.LogWarning("[HotbarTutorialTrigger] No DialogManager in this scene.", this);
            return;
        }

        hasShown = true;

        ShowHighlights();
        DialogManager.Instance.ShowDialog(hotbarTutorialDialog, HideHighlights);
    }

    private void ShowHighlights()
    {
        if (highlightRingPrefab == null || hotbarRoots == null) return;
        if (activeRingInstances.Count > 0) return; // already showing, don't stack duplicates

        foreach (var root in hotbarRoots)
        {
            if (root == null) continue;

            var ring = Instantiate(highlightRingPrefab, root);

            // Must happen BEFORE the anchor changes below take visible effect -- otherwise the
            // parent's layout group (if any) fights back and resizes the ring on its next
            // rebuild, which can happen as soon as the same frame.
            var layoutElement = ring.GetComponent<LayoutElement>();
            if (layoutElement == null) layoutElement = ring.gameObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            ring.anchorMin = Vector2.zero;
            ring.anchorMax = Vector2.one;
            ring.offsetMin = Vector2.zero;
            ring.offsetMax = Vector2.zero;

            activeRingInstances.Add(ring);
        }

        if (activeRingInstances.Count == 0)
            Debug.LogWarning("[HotbarTutorialTrigger] No valid Hotbar Roots assigned -- dialog will still show, but nothing will highlight.", this);
    }

    private void HideHighlights()
    {
        foreach (var ring in activeRingInstances)
        {
            if (ring != null) Destroy(ring.gameObject);
        }
        activeRingInstances.Clear();
    }
}