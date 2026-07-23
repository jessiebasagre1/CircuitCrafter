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
        if (points == null) return;

        // Endpoints track the live anchor transforms every step.
        points[0] = startAnchor.position;
        points[segCount - 1] = endAnchor.position;

        Integrate();
        for (int i = 0; i < constraintIterations; i++)
            SatisfyConstraints();

        if (collisionMask.value != 0)
            ResolveCollisions();
    }

    private void Integrate()
    {
        float dtSq = Time.fixedDeltaTime * Time.fixedDeltaTime;
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
        BuildTubeMesh();
    }

    private void BuildTubeMesh()
    {
        for (int i = 0; i < segCount; i++)
        {
            Vector3 fwd = (i < segCount - 1)
                ? (points[i + 1] - points[i]).normalized
                : (points[i] - points[i - 1]).normalized;

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