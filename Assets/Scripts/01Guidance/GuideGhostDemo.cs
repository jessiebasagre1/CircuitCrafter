using System.Collections;
using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// A DEMONSTRATION: a see-through copy of a part floats from where it is to where it goes - the
    /// RAM stick into its DIMM slot, the CPU onto its socket, the RJ45 plug into the port - then
    /// fades. The real part never moves. "Demonstrate" in Explain -> Demonstrate -> Perform.
    ///
    /// ATTACH THIS TO: any object in the scene. Assign Source (the part to copy), From and To.
    /// Call Play() from a tutorial card's Show Me (TutorialTrigger > On Show Me), from a StoryGuide
    /// step's Demo, or from any UnityEvent.
    ///
    /// Story only: Play() does nothing in an assessment.
    ///
    /// HOW THE COPY STAYS HARMLESS. It is instantiated under an INACTIVE parent, so none of its
    /// scripts ever wake up - no PCPartItem registering itself, no singleton fighting the real one.
    /// Everything except its meshes is stripped before it is switched on.
    ///
    /// GHOST MATERIAL: assign a transparent material (the builder creates GuideGhost.mat). Without
    /// one, the copy keeps the part's own materials - still a clear demonstration, just not
    /// see-through.
    /// </summary>
    public class GuideGhostDemo : MonoBehaviour
    {
        [Tooltip("The part to copy. Its meshes are copied; the part itself is never touched.")]
        public GameObject source;

        [Tooltip("Where the ghost starts. Empty = the source's current position.")]
        public Transform from;

        [Tooltip("Where the ghost ends - an empty GameObject placed exactly where the part seats.")]
        public Transform to;

        [Min(0.2f)] public float duration = 1.6f;

        [Tooltip("How many times to play the move before the ghost disappears.")]
        [Range(1, 5)] public int repeats = 2;

        [Tooltip("A transparent material. Empty = keep the part's own materials.")]
        public Material ghostMaterial;

        public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Also glow the destination while the ghost moves (adds a GuideHighlight to To).")]
        public bool highlightDestination = true;

        private GameObject ghost;
        private Coroutine routine;

        public bool IsPlaying => routine != null;

        public void Play()
        {
            if (!GuidancePolicy.Allows(GuidanceKind.ProcedureTutorial)) return;
            if (source == null || to == null)
            {
                Debug.LogWarning("[GuideGhostDemo] Source and To must both be assigned.", this);
                return;
            }

            Stop();
            routine = StartCoroutine(Run());
        }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (ghost != null) Destroy(ghost);
            ghost = null;
        }

        private void OnDisable() => Stop();

        private IEnumerator Run()
        {
            ghost = BuildGhost();
            if (ghost == null) { routine = null; yield break; }

            Vector3 startPos = from != null ? from.position : source.transform.position;
            Quaternion startRot = from != null ? from.rotation : source.transform.rotation;

            if (highlightDestination) GuideHighlight.Show(to.gameObject).Pulse(duration * repeats + 0.5f);

            for (int pass = 0; pass < repeats; pass++)
            {
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime / duration;
                    float k = ease.Evaluate(Mathf.Clamp01(t));
                    ghost.transform.SetPositionAndRotation(Vector3.Lerp(startPos, to.position, k),
                                                           Quaternion.Slerp(startRot, to.rotation, k));
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.35f);
            }

            if (highlightDestination) GuideHighlight.Hide(to.gameObject);
            Destroy(ghost);
            ghost = null;
            routine = null;
        }

        private GameObject BuildGhost()
        {
            var holder = new GameObject("GhostDemoHolder");
            holder.SetActive(false);   // nothing instantiated under here runs Awake

            GameObject copy = Instantiate(source, source.transform.position, source.transform.rotation, holder.transform);
            copy.name = source.name + " (demo)";

            // Scripts first, then everything else that is not a mesh. Scripts first because a script
            // can [RequireComponent] a Collider, and Unity refuses to remove a component something
            // still depends on. Each object's components are walked LAST-TO-FIRST for the same
            // reason: RequireComponent adds the required one first, so the dependant comes later.
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) Strip(t.gameObject, scriptsOnly: true);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) Strip(t.gameObject, scriptsOnly: false);

            if (ghostMaterial != null)
            {
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            copy.transform.SetParent(null, true);
            copy.transform.localScale = source.transform.lossyScale;
            copy.SetActive(true);
            Destroy(holder);
            return copy;
        }

        private static void Strip(GameObject go, bool scriptsOnly)
        {
            Component[] all = go.GetComponents<Component>();
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Component c = all[i];
                if (c == null) continue;
                if (c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                if (scriptsOnly && !(c is MonoBehaviour)) continue;
                DestroyImmediate(c);
            }
        }
    }
}
