using UnityEngine;

/// <summary>
/// The draggable plug end of a cable. Pick it up with the mouse, drag it near a compatible
/// PortSocket, and release to snap it in. The other end of the cable (the PSU side) should be
/// a fixed Transform assigned as VerletRope.startAnchor; this transform is the rope's endAnchor.
///
/// Requires a Collider on this GameObject (for OnMouseDown/Up) and works with the legacy Input
/// Manager. If your project uses the new Input System exclusively, swap the OnMouseDown/OnMouseUp
/// pair for a raycast check driven by your input action (see comment at the bottom).
/// </summary>
[RequireComponent(typeof(Collider))]
public class CableConnector : MonoBehaviour
{
    [Header("Identity")]
    public CableType cableType;

    [Header("Optional: the rope this connector is the free end of")]
    [Tooltip("Only needed if you want to force a rope rebuild after a big reposition, e.g. after unplugging far from where it was plugged in.")]
    public VerletRope rope;

    [Header("Drag Settings")]
    [Tooltip("How close (world units) the connector must be to a socket's snap point to plug in.")]
    public float snapDistance = 0.05f;
    [Tooltip("How closely the connector must be aligned (degrees) with the socket to plug in.")]
    public float snapAngle = 25f;
    [Tooltip("Layer containing PortSocket colliders.")]
    public LayerMask socketLayer;

    [HideInInspector] public bool isPlugged;
    [HideInInspector] public PortSocket pluggedSocket;

    [Header("Camera")]
    [Tooltip("The camera used while assembling/disassembling (your 2D-perspective view). " +
             "Assign it explicitly - relying on Camera.main breaks as soon as a project has more " +
             "than one camera, since Camera.main only finds whichever camera happens to be tagged " +
             "'MainCamera', which may not be the one actually rendering this view.")]
    public Camera dragCamera;

    [Header("Start Already Plugged (optional)")]
    [Tooltip("If set, this connector snaps directly into this socket when the scene starts, " +
             "skipping the drag interaction entirely. It's still fully unplug/replug-able afterward - " +
             "clicking it will unplug it first, same as any other connector.")]
    public PortSocket startSocket;
    public bool startPlugged;
    [Header("Locking")]
    [Tooltip("If true, this connector can never be dragged/unplugged - useful for a fixed end of a cable that should stay in place.")]
    public bool isLocked = false;

    [Header("Assemble/Disassemble")]
    [Tooltip("How far the connector must move away from the socket it just came from before " +
             "it's allowed to auto-plug again. Prevents an unplug immediately snapping back in " +
             "when released without moving far (e.g. a quick click instead of a full drag).")]
    public float minDetachDistance = 0.1f;

    private bool isDragging;
    private Vector3 dragOffset;
    private PortSocket highlightedSocket;

    private Vector3 unplugOrigin;
    private bool justUnplugged;

    void Awake()
    {
        if (dragCamera == null)
        {
            dragCamera = Camera.main;
            if (dragCamera == null)
                Debug.LogWarning($"[CableConnector] '{name}' has no Drag Camera assigned and no " +
                                  "Camera.main was found either - dragging will not work until one is set.", this);
        }

        if (startPlugged && startSocket != null)
        {
            if (!startSocket.Accepts(cableType))
                Debug.LogWarning($"[CableConnector] '{name}' is set to start plugged into " +
                                  $"'{startSocket.name}', but that socket only accepts " +
                                  $"{startSocket.acceptedType} (this connector is {cableType}). " +
                                  "Plugging anyway - double check the assignment.", this);

            Plug(startSocket);
        }
    }

    void OnMouseDown() => BeginDrag();
    void OnMouseUp() => EndDrag();

    public void BeginDrag()
    {
        if (isLocked) return;
        if (isDragging) return;

        if (isPlugged)
        {
            unplugOrigin = transform.position;
            justUnplugged = true;
            Unplug();
        }
        isDragging = true;
        dragOffset = transform.position - RayHitOnDragPlane();
    }

    public void EndDrag()
    {
        if (!isDragging) return;

        isDragging = false;
        ClearHighlight();
        TryAutoPlug();
    }

    void Update()
    {
        if (!isDragging) return;

        transform.position = RayHitOnDragPlane() + dragOffset;
        UpdateHighlight();
    }

    // Drags along a plane facing the camera, passing through the connector's current position.
    // Good enough for a desktop "pick up and place" feel; swap for a depth-locked plane if you
    // want the player constrained to moving along the case's surface instead.
    private Vector3 RayHitOnDragPlane()
    {
        Plane dragPlane = new Plane(dragCamera.transform.forward, transform.position);
        Ray ray = dragCamera.ScreenPointToRay(Input.mousePosition);
        return dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
    }

    private PortSocket FindCompatibleSocketNearby()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, snapDistance * 2f, socketLayer);
        PortSocket best = null;
        float bestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            var socket = hit.GetComponent<PortSocket>();
            if (socket == null || !socket.Accepts(cableType) || socket.isOccupied) continue;

            float d = Vector3.Distance(transform.position, socket.snapPoint.position);
            if (d < bestDist) { bestDist = d; best = socket; }
        }
        return best;
    }

    private void UpdateHighlight()
    {
        var socket = FindCompatibleSocketNearby();
        if (socket == highlightedSocket) return;

        ClearHighlight();
        highlightedSocket = socket;
        if (highlightedSocket != null)
            highlightedSocket.SetHighlighted(true); // e.g. outline shader / glow, implement in PortSocket
    }

    private void ClearHighlight()
    {
        if (highlightedSocket != null) highlightedSocket.SetHighlighted(false);
        highlightedSocket = null;
    }

    private void TryAutoPlug()
    {
        if (justUnplugged)
        {
            justUnplugged = false;
            if (Vector3.Distance(transform.position, unplugOrigin) < minDetachDistance)
                return; // didn't move far enough to count as an intentional replug attempt
        }

        var socket = FindCompatibleSocketNearby();
        if (socket == null) return;

        float dist = Vector3.Distance(transform.position, socket.snapPoint.position);
        float angle = Quaternion.Angle(transform.rotation, socket.snapPoint.rotation);
        if (dist <= snapDistance && angle <= snapAngle)
        {
            Plug(socket);
        }
        else
        {
            // DEBUG: a socket was found nearby (close enough to highlight, which uses a more
            // generous snapDistance * 2 radius and no rotation check) but the stricter final
            // plug thresholds weren't met. If this keeps firing right when you release the
            // cable directly at the socket, angle is almost always the culprit: dragging only
            // moves position (see Update() above), never rotation, so the connector keeps
            // whatever rotation it had at rest the whole time you drag it. Fix by rotating the
            // connector's resting transform in the editor to sit closer to snapPoint's
            // rotation, or by raising Snap Angle if the visual mismatch is intentional.
            Debug.Log($"[CableConnector] '{name}' near '{socket.name}' but didn't snap - " +
                      $"distance {dist:F3} (needs <= {snapDistance:F3}), " +
                      $"angle {angle:F1} deg (needs <= {snapAngle:F1} deg).", this);
        }
        // else: connector is simply released mid-air/mid-case; the rope's gravity + damping
        // will let it hang naturally until picked up again.
    }

    /// <summary>
    /// Plugs this connector into the given socket. Cables do NOT report task progress -
    /// only PCPartItem parts (CPU, RAM, PSU, SSD, etc.) count toward disassemble_pc/assemble_pc.
    /// </summary>
    public void Plug(PortSocket socket)
    {
        transform.SetPositionAndRotation(socket.snapPoint.position, socket.snapPoint.rotation);
        transform.SetParent(socket.snapPoint, true);
        isPlugged = true;
        pluggedSocket = socket;
        socket.SetOccupied(this, true);
    }

    /// <summary>
    /// Unplugs this connector. Cables do NOT report task progress - only PCPartItem parts
    /// count toward disassemble_pc/assemble_pc.
    /// </summary>
    public void Unplug()
    {
        if (pluggedSocket != null) pluggedSocket.SetOccupied(this, false);
        transform.SetParent(null);
        isPlugged = false;
        pluggedSocket = null;
    }

    /*
     * NEW INPUT SYSTEM ALTERNATIVE:
     * Replace OnMouseDown/OnMouseUp with, e.g., an InputAction "Click" and a manual raycast:
     *
     *   if (Mouse.current.leftButton.wasPressedThisFrame) {
     *       Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
     *       if (Physics.Raycast(ray, out var hit) && hit.transform == transform) OnMouseDown();
     *   }
     *   if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging) OnMouseUp();
     */
}