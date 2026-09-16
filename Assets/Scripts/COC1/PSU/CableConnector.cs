using UnityEngine;
using CircuitCrafter.Repair;

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

    [Tooltip("ON: unplugging this cable adds a step to assemble_pc, and plugging it back completes " +
             "that step - so the module is not finished, and the quiz does not fire, while this " +
             "lead is hanging loose.\n\n" +
             "Turn it OFF for a cable that is decoration, or one the procedure never asks the " +
             "learner to reconnect. PCDisassemblySetup has its own master switch that turns the " +
             "whole behaviour off at once.")]
    public bool countsTowardAssembly = true;
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

    [Header("Drag Leash")]
    [Tooltip("Longest distance, in world units, this plug may get from the cable's FIXED end " +
             "(the rope's Start Anchor). A real 30cm fan lead cannot reach the bottom of the " +
             "case, and neither should this one.\n\n" +
             "This is also a safety net: if the Drag Camera is wrong, the drag plane maps mouse " +
             "movement to enormous world movement and the plug shoots off somewhere it can no " +
             "longer be clicked - unrecoverable without reloading the scene. The leash makes " +
             "that failure impossible rather than merely unlikely.\n\n" +
             "0 = off, which is the old behaviour. Set it to roughly the real cable's length.")]
    [Min(0f)]
    public float maxReachFromAnchor = 0f;

    [Header("Diagnostics")]
    [Tooltip("Logs the camera, pointer position and resolved world point on every drag. Turn on " +
             "when a connector is jumping somewhere unexpected, then off again.")]
    public bool logDragDiagnostics = false;

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

        // COC4: a cable is hardware too. Same rule as parts - see
        // DragController.BeginDragFromWorld. Inert outside RepairTicketMode.
        if (RepairWorkGate.Refuse("the cable")) return;
        if (isDragging) return;

        if (isPlugged)
        {
            unplugOrigin = transform.position;
            justUnplugged = true;
            Unplug();
        }
        isDragging = true;
        dragOffset = transform.position - RayHitOnDragPlane();

        if (logDragDiagnostics) ReportDragSetup();
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

        transform.position = ClampToLeash(RayHitOnDragPlane() + dragOffset);
        UpdateHighlight();
    }

    // Drags along a plane facing the camera, passing through the connector's current position.
    // Good enough for a desktop "pick up and place" feel; swap for a depth-locked plane if you
    // want the player constrained to moving along the case's surface instead.
    private Vector3 RayHitOnDragPlane()
    {
        if (dragCamera == null) return transform.position;

        // PointerInput.Position, NOT Input.mousePosition. MouseLook turns off
        // Input.simulateMouseWithTouches on mobile, which silently kills the raw mouse
        // API for EVERYONE - not just MouseLook. Reading it here returned a stale or
        // zeroed position, and a zeroed one puts the ray through the bottom-left corner
        // of the screen, which is precisely how a plug ends up somewhere it was never
        // dragged. DragController already reads pointer state this way; this did not.
        Plane dragPlane = new Plane(dragCamera.transform.forward, transform.position);
        Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
        return dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
    }

    /// <summary>The cable's fixed end - what the leash measures from.</summary>
    private Transform LeashAnchor => rope != null ? rope.startAnchor : null;

    /// <summary>
    /// Keeps the plug within reach of the cable's fixed end.
    ///
    /// Without this, one wrong Inspector reference is enough to lose a connector for good:
    /// a drag plane built from the wrong camera turns a few pixels of mouse movement into
    /// metres of world movement, the plug leaves the station camera's view, and since
    /// OnMouseDown needs the collider under the pointer, there is then no way to pick it
    /// up again. A leash turns that from "reload the scene" into "the cable stops where a
    /// cable would stop".
    /// </summary>
    private Vector3 ClampToLeash(Vector3 target)
    {
        if (maxReachFromAnchor <= 0f) return target;

        Transform anchor = LeashAnchor;
        if (anchor == null) return target;

        Vector3 offset = target - anchor.position;
        float distance = offset.magnitude;
        if (distance <= maxReachFromAnchor || distance < 0.0001f) return target;

        return anchor.position + offset / distance * maxReachFromAnchor;
    }

    /// <summary>
    /// Brings a lost connector back beside the cable's fixed end. Right-click the component
    /// header in Play mode to rescue one that has already wandered off.
    /// </summary>
    [ContextMenu("Recover to cable anchor")]
    public void RecoverToAnchor()
    {
        Transform anchor = LeashAnchor;
        if (anchor == null)
        {
            Debug.LogWarning($"[CableConnector] '{name}' cannot recover - no Rope assigned, so " +
                             "there is no anchor to return to. Assign the VerletRope in the Rope " +
                             "field.", this);
            return;
        }

        isDragging = false;
        float back = maxReachFromAnchor > 0f ? maxReachFromAnchor * 0.5f : 0.1f;
        transform.position = anchor.position + anchor.forward * back;
        if (rope != null) rope.ForceRebuild();

        Debug.Log($"[CableConnector] '{name}' recovered to '{anchor.name}'.", this);
    }

    /// <summary>
    /// Prints everything that decides where a drag puts this connector.
    ///
    /// The usual culprit is Drag Camera: COC1 has a player camera, a station camera and a
    /// quiz camera, and OnMouseDown fires from whichever camera is actually rendering while
    /// the drag maths uses whatever is in this field. Get those two out of step and the plug
    /// moves along the wrong plane, by the wrong amount, in the wrong direction.
    /// </summary>
    [ContextMenu("Report cable drag setup")]
    public void ReportDragSetup()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CableConnector] === {name} ===");
        sb.AppendLine($"  Drag Camera     : {(dragCamera != null ? dragCamera.name : "<NONE>")}" +
                      (dragCamera != null && !dragCamera.isActiveAndEnabled ? "  <-- NOT ENABLED" : ""));
        sb.AppendLine($"  Camera.main     : {(Camera.main != null ? Camera.main.name : "<none>")}");

        Camera rendering = null;
        foreach (var c in Camera.allCameras)
            if (rendering == null || c.depth > rendering.depth) rendering = c;
        sb.AppendLine($"  Rendering now   : {(rendering != null ? rendering.name : "<none>")}");

        if (dragCamera != null && rendering != null && dragCamera != rendering)
            sb.AppendLine("  *** MISMATCH: the camera you are LOOKING THROUGH is not the Drag Camera. ***");

        sb.AppendLine($"  Pointer         : {PointerInput.Position}");
        sb.AppendLine($"  Plugged         : {isPlugged}" +
                      (pluggedSocket != null ? $" into '{pluggedSocket.name}'" : ""));
        sb.AppendLine($"  Position        : {transform.position}");
        sb.AppendLine($"  Plane hit       : {RayHitOnDragPlane()}");

        Transform anchor = LeashAnchor;
        if (anchor != null)
            sb.AppendLine($"  Anchor          : '{anchor.name}' at {anchor.position}, " +
                          $"distance {Vector3.Distance(transform.position, anchor.position):F3}");
        else
            sb.AppendLine("  Anchor          : <no Rope assigned, so the leash cannot work>");

        sb.AppendLine($"  Leash           : {(maxReachFromAnchor > 0f ? maxReachFromAnchor.ToString("F3") : "off")}");
        Debug.Log(sb.ToString(), this);
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
    /// Plugs this connector into the given socket.
    ///
    /// Reports a step of assemble_pc, but ONLY for a cable the learner had previously unplugged -
    /// PCDisassemblySetup decides that, not this method. The start-plugged connectors run through
    /// here during setup too, and crediting those would finish the assembly before the learner had
    /// touched anything.
    /// </summary>
    public void Plug(PortSocket socket)
    {
        transform.SetPositionAndRotation(socket.snapPoint.position, socket.snapPoint.rotation);
        transform.SetParent(socket.snapPoint, true);
        isPlugged = true;
        pluggedSocket = socket;
        socket.SetOccupied(this, true);

        if (countsTowardAssembly) PCDisassemblySetup.NotifyCablePlugged(this);
    }

    /// <summary>
    /// Unplugs this connector.
    ///
    /// Tells PCDisassemblySetup, which raises what assemble_pc asks for by one: a cable the learner
    /// has taken out is a cable they have to put back before the PC is assembled.
    /// </summary>
    public void Unplug()
    {
        if (pluggedSocket != null) pluggedSocket.SetOccupied(this, false);
        transform.SetParent(null);
        isPlugged = false;
        pluggedSocket = null;

        if (countsTowardAssembly) PCDisassemblySetup.NotifyCableUnplugged(this);
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