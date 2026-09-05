using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Marks a PCPartItem as a cleaning tool rather than a computer part.
    ///
    /// ATTACH THIS TO: the brush / blower / cloth object, alongside its
    /// PCPartItem. Nothing else needs to change - it is a normal part as far as
    /// the hotbar and the drag system are concerned.
    ///
    /// WHY IT IS A PCPartItem AT ALL
    /// Because that is what the hotbar stores. HotbarSlot.StoredItem is typed to
    /// PCPartItem and DragController drags PCPartItems, so a tool that wanted its
    /// own type would need a parallel copy of both. Sitting the brush in a
    /// PartSocket on a tool tray gets picking up, carrying, the hotbar icon and
    /// dragging back out for free, and none of it had to be written.
    ///
    /// IT DOES NOT NEED A PARTSOCKET ANY MORE. The tool used to have to start
    /// seated in a socket on a tray, because clicking a part in the world goes
    /// through PCPartItem.TryBeginDrag, which refuses anything with no HomeSocket.
    /// Add a ToolPickup instead and the tool is fetched with the grab key rather
    /// than clicked, so that rule never applies: it can lie anywhere in the scene,
    /// and ToolBelt hands it to the hotbar when the learner reaches the system
    /// unit. Dragging it back OUT of the hotbar has never needed a socket.
    ///
    /// A socket still works if you want it - the two pickup styles can coexist -
    /// but the world-placed one is the default, because walking over to find your
    /// own brush is part of the job the tray version quietly skipped.
    /// </summary>
    [RequireComponent(typeof(PCPartItem))]
    public class CleaningTool : MonoBehaviour
    {
        [Tooltip("Which tool this is, e.g. \"brush\" or \"blower\". A DustPatch that names a Required Tool Id will only be cleaned by a matching tool. Leave both blank to let any tool clean anything.")]
        [SerializeField] private string toolId = "brush";

        [Tooltip("How fast this tool cleans. 1 is the baseline; a blower might be 1.5, a cloth 0.7.")]
        [Min(0.05f)]
        [SerializeField] private float scrubMultiplier = 1f;

        [Tooltip("Shown once if the learner tries to scrub with the wrong tool. Optional - there is a Console line either way.")]
        [SerializeField] private DialogSequence wrongToolDialog;

        public string ToolId => toolId;
        public float ScrubMultiplier => scrubMultiplier;
        public DialogSequence WrongToolDialog => wrongToolDialog;

        /// <summary>True when the given part is a cleaning tool. Used by the work
        /// gate: picking up a brush is not a repair, so it is not blocked by the
        /// diagnose-first rule - using it on the dirt is, and that is checked
        /// separately in DustScrubber.</summary>
        public static bool IsTool(PCPartItem item)
        {
            return item != null && item.GetComponent<CleaningTool>() != null;
        }

        public static CleaningTool On(PCPartItem item)
        {
            return item != null ? item.GetComponent<CleaningTool>() : null;
        }
    }
}