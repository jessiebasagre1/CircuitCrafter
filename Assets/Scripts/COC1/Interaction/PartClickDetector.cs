using UnityEngine;

/// <summary>
/// Forgiving click/drag/hover detection for the assemble view, so parts,
/// RAM locks, and cable connectors stay interactable even when small on
/// screen (camera zoomed out / far away) - no need to zoom in.
///
/// Uses SphereCastAll (not a single-hit raycast) so a small RAM lock isn't
/// shadowed by the larger part collider it sits next to, and explicitly
/// includes trigger colliders in case any interactable uses one.
/// Attach to the Assemble Camera.
///
/// Reads pointer state via PointerInput (not Input.mousePosition /
/// Input.GetMouseButtonDown directly) - see PointerInput.cs for why: on
/// mobile, MouseLook.cs turns off Input.simulateMouseWithTouches, which
/// silently kills raw mouse APIs for everyone, not just MouseLook.
/// </summary>
public class PartClickDetector : MonoBehaviour
{
    [SerializeField] private Camera assembleCamera;
    [Tooltip("World-space tolerance radius around the click/hover ray.")]
    [SerializeField] private float clickRadius = 0.15f;
    [SerializeField] private float maxDistance = 15f;
    [SerializeField] private LayerMask interactableLayerMask = ~0;

    private CableConnector activeCable;
    private RAMLock hoveredLock;

    /// <summary>Exposed so TouchLookField can check whether a touch started over an
    /// interactable part, and ignore it (so dragging a part doesn't also spin the
    /// camera via that same finger). Same idea as VirtualJoystick.Background.</summary>
    public static PartClickDetector Instance { get; private set; }

    private void Reset()
    {
        assembleCamera = GetComponent<Camera>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

        UpdateHover();

        if (PointerInput.Down)
            HandleClick();

        if (PointerInput.Up && activeCable != null)
        {
            activeCable.EndDrag();
            activeCable = null;
        }
    }

    /// <summary>True if a tappable part/lock/cable sits under the given screen point.
    /// Used by TouchLookField so it doesn't steal a touch meant for a part.</summary>
    public bool IsOverInteractable(Vector2 screenPos)
    {
        return HasInteractableIn(CastPrecise(screenPos)) || HasInteractableIn(CastForgiving(screenPos));
    }

    private static bool HasInteractableIn(RaycastHit[] hits)
    {
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<RAMLock>() != null) return true;
            if (hit.collider.GetComponentInParent<CPULock>() != null) return true;
            if (hit.collider.GetComponentInParent<CableConnector>() != null) return true;
            if (hit.collider.GetComponentInParent<PCPartItem>() != null) return true;
        }
        return false;
    }

    /// <summary>Precise thin ray - exactly where the cursor points. Tried first.</summary>
    private RaycastHit[] CastPrecise() => CastPrecise(PointerInput.Position);

    private RaycastHit[] CastPrecise(Vector2 screenPos)
    {
        Ray ray = assembleCamera.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, interactableLayerMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    /// <summary>Forgiving fallback - only used if the precise ray hits nothing.</summary>
    private RaycastHit[] CastForgiving() => CastForgiving(PointerInput.Position);

    private RaycastHit[] CastForgiving(Vector2 screenPos)
    {
        Ray ray = assembleCamera.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.SphereCastAll(ray, clickRadius, maxDistance, interactableLayerMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    /// <summary>Tries precise first - if the cursor is precisely on something, that's
    /// always what the player meant, regardless of what else is nearby in the
    /// forgiving radius (fixes neighboring RAM locks stealing the click).</summary>
    private bool TryResolveInteraction(RaycastHit[] hits)
    {
        if (hits.Length == 0) return false;

        RAMLock ramLockHit = null;
        CPULock cpuLockHit = null;
        CableConnector cableHit = null;
        PCPartItem partHit = null;

        foreach (var hit in hits)
        {
            if (ramLockHit == null && hit.collider.TryGetComponent(out RAMLock rl)) ramLockHit = rl;
            if (cpuLockHit == null && hit.collider.TryGetComponent(out CPULock cl)) cpuLockHit = cl;
            if (cableHit == null && hit.collider.TryGetComponent(out CableConnector cc)) cableHit = cc;
            if (partHit == null && hit.collider.TryGetComponent(out PCPartItem pi)) partHit = pi;
        }

        if (ramLockHit != null) { ramLockHit.Toggle(); return true; }
        if (cpuLockHit != null) { cpuLockHit.Toggle(); return true; }
        if (cableHit != null) { cableHit.BeginDrag(); activeCable = cableHit; return true; }
        if (partHit != null) { partHit.TryBeginDrag(); return true; }
        return false;
    }

    private void HandleClick()
    {
        if (TryResolveInteraction(CastPrecise())) return;
        TryResolveInteraction(CastForgiving());
    }

    private void UpdateHover()
    {
        RAMLock lockUnderCursor = FindRAMLockUnderCursor();
        if (lockUnderCursor == hoveredLock) return;

        if (hoveredLock != null) hoveredLock.SetHovering(false);
        hoveredLock = lockUnderCursor;
        if (hoveredLock != null) hoveredLock.SetHovering(true);
    }

    private RAMLock FindRAMLockUnderCursor()
    {
        RAMLock exact = FindRAMLockIn(CastPrecise());
        return exact != null ? exact : FindRAMLockIn(CastForgiving());
    }

    private RAMLock FindRAMLockIn(RaycastHit[] hits)
    {
        foreach (var hit in hits)
        {
            if (hit.collider.TryGetComponent(out RAMLock rl))
                return rl;
        }
        return null;
    }
}