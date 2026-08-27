using UnityEngine;

/// <summary>
/// Simulates a sagging, bendable cable between two transforms using Verlet integration,
/// and renders it as a procedural tube mesh (no LineRenderer needed, looks like real cabling).
///
/// Setup:
///  - startAnchor: fixed point, e.g. the cable's exit point on the PSU mesh.
///  - endAnchor:   the CableConnector's transform (moves while dragging, locks when plugged).
///  - Assign a material in the MeshRenderer that this component requires.
///
/// Segment count is computed once from the *initial* distance between anchors, then held fixed.
/// If you reposition the PSU or drastically change the rest length at runtime, call ForceRebuild().
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VerletRope : MonoBehaviour
{
    [Header("Anchors")]
    public Transform startAnchor;
    public Transform endAnchor;

    [Header("Rope Setup")]
    [Tooltip("Rest distance between simulated points along the cable")]
    public float segmentLength = 0.03f;
    public int minSegments = 8;
    public int maxSegments = 60;

    [Header("Physics")]
    [Range(1, 20)] public int constraintIterations = 12;
    [Range(0f, 1f)] public float damping = 0.98f;
    public Vector3 gravity = new Vector3(0f, -2.5f, 0f);

    [Header("Collision (optional)")]
    [Tooltip("Leave empty to skip collision resolution entirely (cheaper).")]
    public LayerMask collisionMask;
    public float collisionRadius = 0.01f;

    [Header("Time")]
    [Tooltip("Leave OFF for COC1 - the PSU harness should freeze with the rest of the game when " +
             "something pauses it.\n\n" +
             "Turn ON for any cable the player can drag while the game is paused - COC2's patch " +
             "cords, where ESC unlocks the cursor and stops time at the same moment.\n\n" +
             "Unity does not call FixedUpdate AT ALL while Time.timeScale is 0. A rope pinned there " +
             "stops tracking its anchors, while a drag driven from Update keeps running - so the " +
             "plug travels and the cord stays behind, then snaps to it the instant time resumes. " +
             "With this on the simulation runs from Update on unscaled time, so it follows the plug " +
             "no matter who paused the game.")]
    public bool useUnscaledTime = false;

    [Header("Rendering")]
    public float cableRadius = 0.008f;
    [Range(3, 12)] public int radialSegments = 6;

    private Vector3[] points;
    private Vector3[] prevPoints;
    private bool[] locked;
    private int segCount;

    private Mesh mesh;
    private Vector3[] vertBuffer;
    private Vector2[] uvBuffer;
    private int[] triBuffer;

    void Start()
    {
        if (startAnchor == null || endAnchor == null)
        {
            Debug.LogError($"[VerletRope] '{name}' is missing startAnchor or endAnchor in the Inspector. " +
                            "The mesh will stay empty (invisible) until both are assigned.", this);
            enabled = false;
            return;
        }

        mesh = new Mesh { name = "CableMesh_" + name };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().mesh = mesh;
        RebuildPoints();
    }

    /// <summary>Recomputes segment count and rebuilds simulation + mesh buffers from current anchor distance.</summary>
    public void ForceRebuild() => RebuildPoints();

    private void RebuildPoints()
    {
        float dist = Vector3.Distance(startAnchor.position, endAnchor.position);
        segCount = Mathf.Clamp(Mathf.CeilToInt(dist / segmentLength) + 1, minSegments, maxSegments);

        points = new Vector3[segCount];
        prevPoints = new Vector3[segCount];
        locked = new bool[segCount];

        for (int i = 0; i < segCount; i++)
        {
            float t = i / (float)(segCount - 1);
            points[i] = Vector3.Lerp(startAnchor.position, endAnchor.position, t);
            prevPoints[i] = points[i];
        }
        locked[0] = true;
        locked[segCount - 1] = true;

        AllocateMeshBuffers();
    }

    private void AllocateMeshBuffers()
    {
        int vertCount = segCount * radialSegments;
        vertBuffer = new Vector3[vertCount];
        uvBuffer = new Vector2[vertCount];

        int quadCount = (segCount - 1) * radialSegments;
        triBuffer = new int[quadCount * 6];

        // Triangle winding never changes shape (only vertex positions do), so build it once.
        // Single-sided by design: use the material's "Render Face: Both" / Cull Off setting for
        // double-sided rendering instead of duplicating geometry here - two coincident triangles
        // at the same depth causes z-fighting (flickering), which duplicating windings did.
        int t = 0;
        for (int i = 0; i < segCount - 1; i++)
        {
            for (int r = 0; r < radialSegments; r++)
            {
                int curr = i * radialSegments + r;
                int next = i * radialSegments + (r + 1) % radialSegments;
                int currNext = (i + 1) * radialSegments + r;
                int nextNext = (i + 1) * radialSegments + (r + 1) % radialSegments;

                triBuffer[t++] = curr; triBuffer[t++] = currNext; triBuffer[t++] = next;
                triBuffer[t++] = next; triBuffer[t++] = currNext; triBuffer[t++] = nextNext;
            }
        }

        // UVs are also static (based on ring index / radial index), fill once.
        for (int i = 0; i < segCount; i++)
        {
            for (int r = 0; r < radialSegments; r++)
            {
                uvBuffer[i * radialSegments + r] = new Vector2(r / (float)radialSegments, i / (float)(segCount - 1));
            }
        }
    }

    void FixedUpdate()
    {
        if (useUnscaledTime) return;          // Update() is driving it instead
        Simulate(Time.fixedDeltaTime);
    }

    void Update()
    {
        if (!useUnscaledTime) return;         // FixedUpdate() is driving it instead
        // Clamped because an editor hitch or a loading stall can hand back a huge unscaled delta,
        // which would fling the rope across the scene in one step.
        Simulate(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
    }

    private void Simulate(float dt)
    {
        if (points == null) return;

        // Endpoints track the live anchor transforms every step.
        points[0] = startAnchor.position;
        points[segCount - 1] = endAnchor.position;

        Integrate(dt);
        for (int i = 0; i < constraintIterations; i++)
            SatisfyConstraints();

        if (collisionMask.value != 0)
            ResolveCollisions();
    }

    private void Integrate(float dt)
    {
        float dtSq = dt * dt;
        for (int i = 1; i < segCount - 1; i++)
        {
            Vector3 current = points[i];
            Vector3 velocity = (current - prevPoints[i]) * damping;
            points[i] = current + velocity + gravity * dtSq;
            prevPoints[i] = current;
        }
    }

    private void SatisfyConstraints()
    {
        for (int i = 0; i < segCount - 1; i++)
        {
            if (locked[i] && locked[i + 1]) continue;

            Vector3 a = points[i];
            Vector3 b = points[i + 1];
            Vector3 delta = b - a;
            float dist = delta.magnitude;
            if (dist < 0.0001f) continue;

            float error = dist - segmentLength;
            Vector3 correction = delta / dist * error;

            if (locked[i]) points[i + 1] -= correction;
            else if (locked[i + 1]) points[i] += correction;
            else
            {
                points[i] += correction * 0.5f;
                points[i + 1] -= correction * 0.5f;
            }
        }
        // Re-pin exact anchor positions after the relaxation pass.
        points[0] = startAnchor.position;
        points[segCount - 1] = endAnchor.position;
    }

    private void ResolveCollisions()
    {
        for (int i = 1; i < segCount - 1; i++)
        {
            Collider[] hits = Physics.OverlapSphere(points[i], collisionRadius, collisionMask);
            foreach (var hit in hits)
            {
                Vector3 closest = hit.ClosestPoint(points[i]);
                Vector3 dir = points[i] - closest;
                float d = dir.magnitude;
                if (d < collisionRadius && d > 0.0001f)
                    points[i] = closest + dir.normalized * (collisionRadius - d);
            }
        }
    }

    void LateUpdate()
    {
        if (points == null) return;

        // Update-driven mode only: the drag script and this one both run in Update, and script
        // order decides which goes first. If the rope simulated before the plug moved, its end
        // point would be one frame stale - a small but visible lag that reads as the cord
        // "chasing" the plug. Re-pin here, after every Update in the frame has run, so the ends
        // are exactly on the anchors at the moment the mesh is built.
        // FixedUpdate-driven ropes are left alone: COC1's behaviour is unchanged.
        if (useUnscaledTime && startAnchor != null && endAnchor != null)
        {
            points[0] = startAnchor.position;
            points[segCount - 1] = endAnchor.position;
        }

        BuildTubeMesh();
    }

    private void BuildTubeMesh()
    {
        // Tracks the last known-good forward direction so a momentarily
        // collapsed segment (zero-length, e.g. anchors overlapping) reuses
        // the previous ring's orientation instead of feeding a zero vector
        // into Quaternion.LookRotation (which spams "viewing vector is zero").
        Vector3 lastGoodFwd = transform.forward;

        for (int i = 0; i < segCount; i++)
        {
            Vector3 rawFwd = (i < segCount - 1)
                ? (points[i + 1] - points[i])
                : (points[i] - points[i - 1]);

            Vector3 fwd;
            if (rawFwd.sqrMagnitude > 0.0000001f)
            {
                fwd = rawFwd.normalized;
                lastGoodFwd = fwd;
            }
            else
            {
                fwd = lastGoodFwd;
            }

            Quaternion rot = Quaternion.LookRotation(fwd);
            for (int r = 0; r < radialSegments; r++)
            {
                float angle = r / (float)radialSegments * Mathf.PI * 2f;
                Vector3 offset = rot * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * cableRadius;
                Vector3 worldVert = points[i] + offset;
                // Mesh.vertices is interpreted in this GameObject's LOCAL space, but 'points' are
                // world-space (derived from startAnchor/endAnchor.position). Convert here so the
                // rope renders correctly regardless of where this object sits in the hierarchy.
                vertBuffer[i * radialSegments + r] = transform.InverseTransformPoint(worldVert);
            }
        }

        mesh.Clear(); // keepVertexLayout not needed here; buffer sizes are stable per-instance
        mesh.vertices = vertBuffer;
        mesh.uv = uvBuffer;
        mesh.triangles = triBuffer;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}