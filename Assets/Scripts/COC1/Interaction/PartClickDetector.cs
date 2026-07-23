using UnityEngine;

/// <summary>
/// Forgiving click/drag/hover detection for the assemble view, so parts,
/// RAM locks, and cable connectors stay interactable even when small on
/// screen (camera zoomed out / far away) — no need to zoom in.
///
/// Uses SphereCastAll (not a single-hit raycast) so a small RAM lock isn't
/// shadowed by the larger part collider it sits next to, and explicitly
/// includes trigger colliders in case any interactable uses one.
/// Attach to the Assemble Camera.
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

    private void Reset()
    {
        assembleCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

        UpdateHover();

        if (Input.GetMouseButtonDown(0))
            HandleClick();

        if (Input.GetMouseButtonUp(0) && activeCable != null)
        {
            activeCable.EndDrag();
            activeCable = null;
        }
    }

    /// <summary>Precise thin ray — exactly where the cursor points. Tried first.</summary>
    private RaycastHit[] CastPrecise()
    {
        Ray ray = assembleCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, interactableLayerMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    /// <summary>Forgiving fallback — only used if the precise ray hits nothing.</summary>
    private RaycastHit[] CastForgiving()
    {
        Ray ray = assembleCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.SphereCastAll(ray, clickRadius, maxDistance, interactableLayerMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    /// <summary>Tries precise first (exact cursor position), falls back to forgiving only if nothing's exactly under the cursor.</summary>
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
        // Try an exact hit first — if the cursor is precisely on something, that's
        // always what the player meant, regardless of what else is nearby in the
        // forgiving radius (fixes neighboring RAM locks stealing the click).
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