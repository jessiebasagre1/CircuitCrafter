using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Attach this to each individual wire object (e.g. RJ45_Wire_End1_01_WhiteOrange).
    /// It just tags the object with its color so slots/validators can query it without
    /// parsing the object's name at runtime.
    /// </summary>
    public class WireIdentity : MonoBehaviour
    {
        [Tooltip("The wire color this object represents. Set this per-object in the Inspector.")]
        public WireColor color;

        [Tooltip("Which connector end this wire belongs to (1 or 2). Used so a wire from " +
                 "End 1 can't accidentally be dropped into an End 2 slot.")]
        public int endNumber = 1;
    }
}