using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Turns dragging a cleaning tool over a dust patch into cleaning.
    ///
    /// ATTACH THIS TO: the Managers object in each repair-site scene that has dust
    /// in it. One per scene. It needs nothing assigned.
    ///
    /// HOW THE SWIPE WORKS
    /// The learner drags the tool out of the hotbar; DragController is already
    /// carrying it and moving a UI icon under the pointer. Every frame, this casts
    /// a ray from the pointer into the world and, if it lands on a DustPatch, hands
    /// that patch the distance the pointer moved since last frame. Releasing the
    /// drag finds no PartSocket, so DragController returns the tool to the hotbar
    /// on its own - which is exactly the behaviour we want and none of it had to be
    /// written.
    ///
    /// WHY DISTANCE AND NOT TIME
    /// Scrubbing by Time.deltaTime would let a learner park the tool on the dirt
    /// and wait. Scrubbing by pointer travel means the dust only comes off if you
    /// actually move, which is the difference between a swipe and a click.
    ///
    /// WHY IT FINDS THE CAMERA EACH FRAME
    /// A repair site switches cameras - walking, monitor station, system unit
    /// station - and the raycast has to come from whichever one is rendering. One
    /// camera is enabled at a time in this project, so taking the first enabled one
    /// is both correct and free of any wiring to get wrong.
    /// </summary>
    public class DustScrubber : MonoBehaviour
    {
        [Header("Reach")]
        [Tooltip("Layers the dust patches live on. Narrow this if the ray keeps hitting the case before the dirt.")]
        [SerializeField] private LayerMask dustLayers = ~0;

        [Min(1f)]
        [SerializeField] private float rayDistance = 100f;

        [Header("Camera")]
        [Tooltip("Leave empty to use whichever camera is currently rendering - correct at every station without wiring.")]
        [SerializeField] private Camera scrubCamera;

        [Header("Feedback")]
        [Tooltip("Seconds before the wrong-tool message can appear again.")]
        [Min(0f)]
        [SerializeField] private float messageCooldownSeconds = 6f;

        private Vector2 lastPointer;
        private bool hadPointer;
        private float nextMessageTime;
        private DialogSequence generatedWrongTool;

        // ==================================================================

        private void Update()
        {
            if (DragController.Instance == null || !DragController.Instance.IsDragging)
            {
                hadPointer = false;
                return;
            }

            Vector2 pointer = PointerInput.Position;

            // First frame of a drag has no previous position to measure against,
            // and using a stale one from the last drag would score a full-screen
            // swipe the learner never made.
            if (!hadPointer)
            {
                lastPointer = pointer;
                hadPointer = true;
                return;
            }

            float moved = Vector2.Distance(pointer, lastPointer);
            lastPointer = pointer;

            if (moved <= 0.01f) return;

            CleaningTool tool = CleaningTool.On(DragController.Instance.DraggedItem);

            DustPatch patch = PatchUnder(pointer);
            if (patch == null || patch.IsClean) return;

            if (tool == null)
            {
                Say("[DustScrubber] That is not a cleaning tool - it will not shift the dirt.",
                    null,
                    "This is not going to clean anything. I need the proper tool for this.");
                return;
            }

            if (!patch.Accepts(tool))
            {
                Say($"[DustScrubber] '{tool.ToolId}' is the wrong tool for '{patch.name}' - it wants '{patch.RequiredToolId}'.",
                    tool.WrongToolDialog,
                    "Wrong tool for this. It is not shifting anything.");
                return;
            }

            // Cleaning IS a repair, so the diagnose-first rule applies to it - even
            // though picking the brush up was allowed. Refuse() shows its own dialog
            // and records the mistake.
            if (RepairWorkGate.Refuse("the dust")) return;

            patch.Scrub(moved, tool.ScrubMultiplier);
        }

        // ==================================================================

        private DustPatch PatchUnder(Vector2 screenPosition)
        {
            Camera cam = ActiveCamera();
            if (cam == null) return null;

            Ray ray = cam.ScreenPointToRay(screenPosition);

            // RaycastAll rather than Raycast: the dust sits ON something, so the
            // surface underneath is often nearer to nothing and a plain raycast can
            // return the case panel instead of the dirt lying on it.
            RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance, dustLayers);
            for (int i = 0; i < hits.Length; i++)
            {
                var patch = hits[i].collider.GetComponentInParent<DustPatch>();
                if (patch != null) return patch;
            }
            return null;
        }

        private Camera ActiveCamera()
        {
            if (scrubCamera != null && scrubCamera.isActiveAndEnabled) return scrubCamera;

            var all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].isActiveAndEnabled) return all[i];
            }
            return Camera.main;
        }

        private void Say(string consoleLine, DialogSequence authored, string fallbackLine)
        {
            if (Time.time < nextMessageTime) return;
            nextMessageTime = Time.time + messageCooldownSeconds;

            Debug.Log(consoleLine, this);

            if (DialogManager.Instance == null) return;

            if (authored != null)
            {
                DialogManager.Instance.ShowDialog(authored);
                return;
            }

            if (generatedWrongTool == null)
                generatedWrongTool = ScriptableObject.CreateInstance<DialogSequence>();

            generatedWrongTool.lines.Clear();
            generatedWrongTool.lines.Add(new DialogLine { speakerName = "You", text = fallbackLine });

            DialogManager.Instance.ShowDialog(generatedWrongTool);
        }
    }
}
