using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// A tool lying somewhere in the world that the learner walks up to and grabs.
    ///
    /// ATTACH THIS TO: the brush / blower / cloth object itself, alongside its
    /// PCPartItem and its CleaningTool. Then put that object ANYWHERE in the scene
    /// - on a shelf, under a desk, in the store room. It does NOT need a
    /// PartSocket, a tool tray, or to be in any camera's frame.
    ///
    /// WHY THIS REPLACED CLICKING A TOOL TRAY
    /// The tray version worked, but it made the tool part of the workbench: it had
    /// to sit inside the station camera's view, which meant it was already in front
    /// of the learner at the moment they needed it. Nothing was ever looked for.
    /// Fetching your own tools before you open a machine is most of what preparing
    /// for a service call actually is, and it is the one part of the job a
    /// simulation can teach for free just by putting the brush across the room.
    ///
    /// HOW IT BEHAVES
    /// Walk within range and a prompt appears. Press G (ToolBelt owns the key) and
    /// the tool goes with you - it disappears from the world and is carried. It
    /// does NOT go into the hotbar yet, because the hotbar is not on screen while
    /// you are walking around. It lands in the hotbar the moment you enter the
    /// system unit station, which is the moment you can use it.
    ///
    /// AUTHOR IT ACTIVE. It registers itself in Awake, and Unity does not run Awake
    /// on an object that starts switched off - a tool authored inactive would never
    /// join the list and G would never see it. If you want a tool that only exists
    /// for one ticket, leave it active here and let the fault switch it off.
    /// </summary>
    [RequireComponent(typeof(PCPartItem))]
    public class ToolPickup : MonoBehaviour
    {
        /// <summary>Every pickup in the scene. ToolBelt walks this to find the
        /// nearest one in range - the same registry pattern the USB drives use, and
        /// for the same reason: a per-frame FindObjectsByType would search the whole
        /// scene sixty times a second to answer a question about three objects.</summary>
        public static readonly List<ToolPickup> All = new List<ToolPickup>();

        [Header("Identity")]
        [Tooltip("Used in the walk-up prompt: \"Press G to pick up the brush\". Write it lower-case, as the end of that sentence.")]
        [SerializeField] private string displayName = "the brush";

        [Header("Reach")]
        [Tooltip("How close the learner has to be. Keep it small - the point is that the tool has to be found and walked to, not spotted from the doorway.")]
        [Min(0.25f)]
        [SerializeField] private float grabRange = 1.8f;

        [Tooltip("What the distance is measured to. Leave empty to use this object's own position.")]
        [SerializeField] private Transform grabPoint;

        [Header("Events")]
        [Tooltip("Fires when the learner picks this up. Optional - a sound, a particle, a hint.")]
        public UnityEvent onGrabbed;

        private PCPartItem item;

        private Transform homeParent;
        private Vector3 homePosition;
        private Quaternion homeRotation;

        /// <summary>True once the learner has it. Carried tools are skipped when
        /// looking for something to grab, so a tool cannot be picked up twice.</summary>
        public bool IsCarried { get; private set; }

        /// <summary>True once it has been handed to the hotbar. Kept separate from
        /// IsCarried so entering the station a second time does not try to deposit
        /// the same brush into a second slot.</summary>
        public bool IsInHotbar { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        public PCPartItem Item => item;

        public Vector3 Position => grabPoint != null ? grabPoint.position : transform.position;

        // ==================================================================

        private void Awake()
        {
            item = GetComponent<PCPartItem>();

            homeParent = transform.parent;
            homePosition = transform.position;
            homeRotation = transform.rotation;

            if (!All.Contains(this)) All.Add(this);

            if (GetComponent<CleaningTool>() == null)
            {
                // Not fatal - a ToolPickup on a non-cleaning part is a perfectly
                // reasonable way to make any item fetchable - but for a dust ticket
                // it is almost always a mistake, and it fails silently: the learner
                // picks the thing up, drags it onto the dirt, and nothing happens.
                Debug.LogWarning($"[ToolPickup] '{name}' has no CleaningTool component, so it will not shift any dust. Add one if this is meant to be a cleaning tool.", this);
            }
        }

        private void OnDestroy()
        {
            All.Remove(this);
        }

        // ==================================================================

        /// <summary>True when the learner is close enough to take it.</summary>
        public bool IsInRange(Vector3 playerPosition)
        {
            if (IsCarried) return false;
            if (!gameObject.activeInHierarchy) return false;
            return Vector3.Distance(playerPosition, Position) <= grabRange;
        }

        public float DistanceTo(Vector3 playerPosition)
        {
            return Vector3.Distance(playerPosition, Position);
        }

        /// <summary>
        /// Takes the tool. It leaves the world and is carried - it is NOT put in the
        /// hotbar here, because the hotbar is hidden while walking and an icon that
        /// appears in a panel nobody can see teaches nothing.
        ///
        /// Hiding the object outright rather than parenting it to the player is
        /// deliberate: a brush stuck to the camera would clip through every doorway
        /// in the site, and the learner already knows they have it because the
        /// prompt is gone and a line said so.
        /// </summary>
        public bool Grab()
        {
            if (IsCarried) return false;

            IsCarried = true;
            gameObject.SetActive(false);

            Debug.Log($"[ToolPickup] Picked up '{name}'.", this);
            onGrabbed?.Invoke();
            return true;
        }

        /// <summary>Puts it back exactly where it was authored. Nothing calls this
        /// automatically - it is here for a "drop it" button, or for a site that
        /// wants tools returned to the store room at the end of a call.</summary>
        public void ReturnToWorld()
        {
            if (!IsCarried) return;

            IsCarried = false;
            IsInHotbar = false;

            transform.SetParent(homeParent, true);
            transform.SetPositionAndRotation(homePosition, homeRotation);
            gameObject.SetActive(true);

            Debug.Log($"[ToolPickup] '{name}' put back.", this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.8f);
            Gizmos.DrawWireSphere(grabPoint != null ? grabPoint.position : transform.position, grabRange);
        }
#endif
    }
}
