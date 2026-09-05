using UnityEngine;
using UnityEngine.Events;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// One patch of dirt inside the machine. Swiping a cleaning tool across it
    /// wears it away until it is gone.
    ///
    /// ATTACH THIS TO: a flat quad or decal object positioned over the thing that
    /// is dirty - a fan grille, a heatsink, the floor of the case. It needs a
    /// Collider (any kind, Is Trigger is fine) because the scrubbing is a raycast.
    ///
    /// AUTHOR IT INACTIVE. Like every other fault in this project the scene is
    /// authored CLEAN, and DustFault switches the patches on at runtime for its own
    /// ticket only. That is what lets one scene serve a dust ticket and five others
    /// where the machine is spotless.
    ///
    /// WEAR, NOT A SWITCH. Cleanliness runs 0 to 1 and only moves while the pointer
    /// is actually MOVING across the patch - holding the tool still does nothing.
    /// That is the difference between a swipe and a click, and it is the whole
    /// feel of the interaction: dust comes off because you worked at it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DustPatch : MonoBehaviour
    {
        [Header("Which tool")]
        [Tooltip("Only a CleaningTool whose Tool Id matches will clean this. Leave blank to accept any tool.")]
        [SerializeField] private string requiredToolId = "";

        [Header("How much work")]
        [Tooltip("Screen distance the pointer must travel across this patch to clean it completely, in pixels at the reference resolution. 900 is roughly three firm swipes.")]
        [Min(50f)]
        [SerializeField] private float pixelsToClean = 900f;

        [Header("Look")]
        [Tooltip("The renderer that fades out as it is cleaned. Usually on this same object. Leave empty to skip fading.")]
        [SerializeField] private Renderer dustRenderer;

        [Tooltip("Also shrink slightly as it clears, which reads as the dirt breaking up rather than just going transparent.")]
        [SerializeField] private bool shrinkAsItClears = true;

        [Range(0f, 0.9f)]
        [Tooltip("How far it shrinks by the time it is gone.")]
        [SerializeField] private float shrinkTo = 0.55f;

        [Header("Events")]
        public UnityEvent onCleaned;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock block;
        private Vector3 startScale;
        private Color startColor = Color.white;
        private bool hasStartColor;

        /// <summary>0 = filthy, 1 = spotless.</summary>
        public float Cleanliness { get; private set; }

        public bool IsClean => Cleanliness >= 1f;

        public string RequiredToolId => requiredToolId;

        // ==================================================================

        private void Awake()
        {
            startScale = transform.localScale;

            if (dustRenderer == null) dustRenderer = GetComponent<Renderer>();

            if (dustRenderer != null && dustRenderer.sharedMaterial != null)
            {
                // sharedMaterial, deliberately. Reading .material instantiates a
                // copy of the material for every patch, at edit time as well as
                // runtime, and a scene with a dozen dust patches would quietly leak
                // a dozen materials.
                if (dustRenderer.sharedMaterial.HasProperty(BaseColorId))
                {
                    startColor = dustRenderer.sharedMaterial.GetColor(BaseColorId);
                    hasStartColor = true;
                }
                else if (dustRenderer.sharedMaterial.HasProperty(ColorId))
                {
                    startColor = dustRenderer.sharedMaterial.GetColor(ColorId);
                    hasStartColor = true;
                }
            }
        }

        // ==================================================================

        /// <summary>Puts the dirt back and shows it. Called by DustFault at
        /// injection - the scene itself is authored clean.</summary>
        public void MakeDirty()
        {
            Cleanliness = 0f;
            gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>True when this tool is allowed to clean this patch.</summary>
        public bool Accepts(CleaningTool tool)
        {
            if (tool == null) return false;
            if (string.IsNullOrWhiteSpace(requiredToolId)) return true;
            return string.Equals(requiredToolId, tool.ToolId, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Wears the dirt down. <paramref name="pixelsMoved"/> is how far the
        /// pointer travelled across the patch this frame, so a slow careful swipe
        /// and a fast one do the same total work - what counts is distance covered,
        /// not time spent hovering.
        /// </summary>
        public void Scrub(float pixelsMoved, float toolMultiplier)
        {
            if (IsClean || pixelsMoved <= 0f) return;

            Cleanliness = Mathf.Clamp01(Cleanliness + (pixelsMoved * Mathf.Max(0.05f, toolMultiplier)) / pixelsToClean);
            Refresh();

            if (!IsClean) return;

            Debug.Log($"[DustPatch] '{name}' is clean.", this);
            onCleaned?.Invoke();

            // Left active but invisible rather than deactivated: DustFault polls
            // IsClean, and a deactivated patch is one FindObjectsByType away from
            // being forgotten about entirely.
            if (dustRenderer != null) dustRenderer.enabled = false;
        }

        private void Refresh()
        {
            float dirt = 1f - Cleanliness;

            if (dustRenderer != null)
            {
                dustRenderer.enabled = dirt > 0.001f;

                if (hasStartColor)
                {
                    if (block == null) block = new MaterialPropertyBlock();
                    dustRenderer.GetPropertyBlock(block);

                    var faded = startColor;
                    faded.a = startColor.a * dirt;

                    // Both names: URP calls it _BaseColor, the built-in pipeline
                    // calls it _Color, and setting the one that does not exist is
                    // harmless. Cheaper than branching on the render pipeline.
                    block.SetColor(BaseColorId, faded);
                    block.SetColor(ColorId, faded);

                    dustRenderer.SetPropertyBlock(block);
                }
            }

            if (shrinkAsItClears)
                transform.localScale = startScale * Mathf.Lerp(shrinkTo, 1f, dirt);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.5f, 0.3f, 0.5f);
            Gizmos.DrawWireCube(transform.position, transform.lossyScale);
        }
#endif
    }
}
