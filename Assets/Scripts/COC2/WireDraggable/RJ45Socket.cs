// RJ45Socket.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// An RJ45 jack a patch cord can be plugged into - a panel front port, a switch port, a router
    /// port, a wall outlet.
    ///
    /// TWO DELIBERATE DIFFERENCES FROM COC1's PortSocket:
    ///
    ///   1. NO COLLIDER, NO LAYER MASK. Every live socket registers itself in a static list, and
    ///      PatchCord finds the nearest one with a plain distance check. COC1's CableConnector uses
    ///      Physics.OverlapSphere on a socketLayer, which needs a collider per socket, a dedicated
    ///      layer, and - the real problem - depends on PhysX knowing where those colliders are. A
    ///      socket that gets repositioned outside FixedUpdate sits at a stale pose and stops being
    ///      found, which is the same class of bug that made the crimping tool unclickable. A list
    ///      of Transforms cannot go stale.
    ///
    ///   2. NO CableType. Every RJ45 plug fits every RJ45 jack - that's the whole point of the
    ///      standard. What actually matters for a training sim is WHICH socket, so this carries a
    ///      groupId and a portNumber instead. That lets a later stage check "panel port 3 should be
    ///      patched to switch port 3" without inventing fake incompatibilities.
    ///
    /// Setup:
    ///   1. Put this on each socket anchor - the model ships them as Socket_Panel_01..08,
    ///      Socket_Switch_01..08, Socket_Router_WAN/LAN1..4 and Socket_Outlet.
    ///   2. Leave Snap Point empty and it uses its own transform, which is what the shipped
    ///      anchors are positioned for.
    ///   3. Set groupId and portNumber to match the anchor's name.
    /// </summary>
    public class RJ45Socket : MonoBehaviour
    {
        /// <summary>Every enabled socket in the scene. PatchCord searches this instead of the
        /// physics scene, so no colliders or layers are involved.</summary>
        public static readonly List<RJ45Socket> All = new List<RJ45Socket>();

        [Header("Identity")]
        [Tooltip("Which piece of equipment this socket belongs to - 'panel', 'switch', 'router', " +
                 "'outlet'. Free text; only used for checking a patching task later.")]
        public string groupId = "panel";
        [Tooltip("Port number within that group, 1-8.")]
        public int portNumber = 1;

        [Header("Snap Target")]
        [Tooltip("Exact position AND rotation a plug sits at when seated. Leave empty to use this " +
                 "object's own transform - correct for the shipped Socket_* anchors.")]
        public Transform snapPoint;

        [Header("Highlight (optional)")]
        [Tooltip("Shown while a dragged plug is close enough that releasing would seat it.")]
        public GameObject highlightVisual;

        [Header("Events")]
        public UnityEvent OnPlugged;
        public UnityEvent OnUnplugged;

        public bool IsOccupied { get; private set; }
        public PatchCord Current { get; private set; }

        /// <summary>Where a plug actually goes. Falls back to this transform.</summary>
        public Transform Snap { get { return snapPoint != null ? snapPoint : transform; } }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void SetOccupied(PatchCord cord, bool occupied)
        {
            IsOccupied = occupied;
            Current = occupied ? cord : null;
            if (occupied) OnPlugged?.Invoke();
            else OnUnplugged?.Invoke();
        }

        public void SetHighlighted(bool on)
        {
            if (highlightVisual != null && highlightVisual.activeSelf != on)
                highlightVisual.SetActive(on);
        }

        /// <summary>
        /// When set, NearestFree ignores every socket outside this group.
        ///
        /// A stage that is asking for one piece of equipment sets this while it runs. Without it a
        /// cord being run to the switch will happily snap into a router jack a couple of centimetres
        /// away - the drop is then refused by the rules, which is correct but reads as the cord
        /// refusing to go where the player clearly put it. Filtering here means the wrong jack is
        /// never a candidate in the first place, so the highlight never offers it either.
        ///
        /// Null or empty = no filter, which is the normal state outside a stage.
        /// </summary>
        public static string OnlyGroup;

        /// <summary>Nearest free socket to a world point, or null. Pure maths - no physics query,
        /// so it cannot be defeated by a stale collider pose or a wrong layer mask.</summary>
        public static RJ45Socket NearestFree(Vector3 position, float maxDistance, RJ45Socket ignore = null)
        {
            RJ45Socket best = null;
            float bestDist = maxDistance;
            for (int i = 0; i < All.Count; i++)
            {
                RJ45Socket s = All[i];
                if (s == null || s == ignore) continue;
                if (s.IsOccupied) continue;
                if (!string.IsNullOrEmpty(OnlyGroup) && s.groupId != OnlyGroup) continue;
                float d = Vector3.Distance(position, s.Snap.position);
                if (d <= bestDist) { bestDist = d; best = s; }
            }
            return best;
        }
    }
}