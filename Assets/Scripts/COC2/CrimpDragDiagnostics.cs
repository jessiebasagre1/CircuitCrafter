using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Tells you WHY the crimping tool won't pick up.
    ///
    /// CrimpToolCrimpDraggable refuses a drag for about eight different reasons, and all of them
    /// look identical from the player's side: you click the tool and nothing happens. This walks
    /// the same gates in the same order and names the first one that blocked, every time you
    /// click.
    ///
    /// It only READS state - it never enables, moves or assigns anything. Safe to leave in while
    /// you debug, then untick.
    ///
    /// IMPORTANT: put this on an ALWAYS-ACTIVE object (your station object is ideal), NOT on the
    /// crimping tool itself. If the tool's GameObject is switched off - which is one of the causes
    /// this is meant to catch - a script living on it would stop running too, and report nothing.
    ///
    /// Setup:
    ///   1. Add to any always-active object in the COC2 scene.
    ///   2. Leave Tool Draggable empty and it'll find the CrimpToolCrimpDraggable by itself,
    ///      including on an inactive object.
    ///   3. Press Play, get to the crimping stage, and click the tool. Read the Console.
    /// </summary>
    public class CrimpDragDiagnostics : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Leave empty to find the CrimpToolCrimpDraggable in the scene automatically.")]
        public CrimpToolCrimpDraggable toolDraggable;

        [Header("When to report")]
        [Tooltip("Log a full state report once at scene start.")]
        public bool reportOnStart = true;
        [Tooltip("Log the first blocking reason every time the pointer goes down.")]
        public bool reportOnClick = true;
        [Tooltip("Press this key any time to dump a full state report.")]
        public KeyCode reportKey = KeyCode.F1;

        [Header("Visual debug")]
        [Tooltip("Draw the pointer ray (cyan) and every collider's world bounds (yellow) in the SCENE " +
                 "view while playing. Turn Gizmos on in the Scene view toolbar to see them. This is the " +
                 "fastest way to spot a ray that passes beside the box you thought it was hitting.")]
        public bool drawRayInSceneView = true;

        bool announcedGrabbable;

        void Start()
        {
            if (toolDraggable == null) toolDraggable = FindDraggable();

            if (toolDraggable == null)
            {
                Debug.LogError("[CrimpDragDiagnostics] No CrimpToolCrimpDraggable anywhere in the scene. " +
                               "Add it to your crimping tool object.", this);
                return;
            }

            if (reportOnStart) Debug.Log(BuildReport(), this);
        }

        void Update()
        {
            if (toolDraggable == null) return;

            if (drawRayInSceneView) DrawDebugGeometry();

            if (Input.GetKeyDown(reportKey))
                Debug.Log(BuildReport(), this);

            if (!reportOnClick) return;

            if (PointerInput.Down)
            {
                string blocker = FirstBlockingReason();
                if (blocker == null)
                {
                    Debug.Log("[CrimpDragDiagnostics] Click accepted - the tool should be dragging now. " +
                              "If it isn't visibly moving, it's a position problem, not an input one: " +
                              "press " + reportKey + " and check the 'On screen' line.", this);
                }
                else
                {
                    Debug.LogWarning("[CrimpDragDiagnostics] Drag blocked: " + blocker, this);
                }
            }

            // One-time note the moment everything lines up, so you know the stage opened correctly
            // even if you haven't clicked yet.
            if (!announcedGrabbable && FirstBlockingReason() == null)
            {
                announcedGrabbable = true;
                Debug.Log("[CrimpDragDiagnostics] The tool is now grabbable.", this);
            }
        }

        // ------------------------------------------------------------------
        // The same gates CrimpToolCrimpDraggable.Update() applies, in the same order.
        // ------------------------------------------------------------------

        string FirstBlockingReason()
        {
            var tool = toolDraggable;

            if (!tool.gameObject.activeInHierarchy)
                return $"the tool's GameObject '{tool.name}' is INACTIVE. Nothing switched it back on. " +
                       "Assign it to CrimpStageViewController > Crimping Tool Visual - that's what shows it " +
                       "for this stage. (CrimpingViewController.ExitCrimpingView() switches it off on the way in.)";

            if (!tool.enabled)
                return $"the CrimpToolCrimpDraggable component on '{tool.name}' is DISABLED. Assign it to " +
                       "CrimpStageViewController > Tool Draggable - that's the only thing that enables it now " +
                       "that CrimpingViewController's own Crimp Tool Draggable field is cleared.";

            if (tool.dragCamera == null)
                return "CrimpToolCrimpDraggable > Drag Camera is EMPTY. The drag ray has no camera to come from.";

            if (!tool.dragCamera.isActiveAndEnabled)
                return $"Drag Camera '{tool.dragCamera.name}' is not rendering right now. It has to be the camera " +
                       "that's actually live in this stage - your third (crimping) camera, not the wiring one.";

            var crimp = CrimpToolCrimpController.Instance;
            if (crimp == null)
                return "no CrimpToolCrimpController in the scene, so there's nothing to drop the tool onto.";

            if (crimp.IsCrimping)
                return "CrimpToolCrimpController.IsCrimping is TRUE - a crimp clip is playing (or a previous one " +
                       "never finished, which happens if its GameObject was switched off mid-clip).";

            if (crimp.AllEndsCrimped())
                return "every end in CrimpToolCrimpController > Ends is already marked crimped, so there's nothing " +
                       "left to do. If this is a fresh Play session, the 'done' flags were saved into the scene - " +
                       "call ResetForReplay() or see the note in the report.";

            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
                return "a dialog is open. Every draggable in COC2 ignores input while DialogManager.IsDialogOpen.";

            if (WireDragController.Instance != null && WireDragController.Instance.IsDragging)
                return "WireDragController thinks a wire is still being dragged, so the tool stands down. " +
                       "A wire drag that never resolved will wedge this permanently.";

            string ui = BlockingUIElement();
            if (ui != null)
                return $"the pointer is over UI - '{ui}' is on top of the tool. Untick 'Raycast Target' on that " +
                       "element (full-screen panel backgrounds are the usual culprit), or move it out of the way.";

            var colliders = tool.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
                return $"'{tool.name}' has no Collider anywhere on it or its children.";

            bool anyEnabled = false;
            foreach (var c in colliders) if (c.enabled && c.gameObject.activeInHierarchy) anyEnabled = true;
            if (!anyEnabled)
                return $"every Collider under '{tool.name}' is disabled.";

            Ray ray = tool.dragCamera.ScreenPointToRay(PointerInput.Position);
            bool rendererHit = RendererBoundsHit(tool, ray);
            bool physicsHit = RayHitsTool(tool, ray, out string whatWasHit);

            // The decisive comparison. Renderer bounds come straight off the Transform, so they are
            // always where the mesh is drawn. Colliders live in PhysX, which only learns about a
            // moved Transform during FixedUpdate. If the ray passes through the visible tool but no
            // collider answers, the collider is simply stale.
            if (rendererHit && !physicsHit)
                return $"the ray passes straight through the tool's VISIBLE bounds, but hits no collider. " +
                       $"The collider is stale - PhysX still has it at an older position. " +
                       $"Time.timeScale = {Time.timeScale}, Physics.autoSyncTransforms = {Physics.autoSyncTransforms}. " +
                       "If timeScale is 0, FixedUpdate never runs and the collider can never catch up; find " +
                       "whatever left it at 0 (DialogManager and PauseMenuController both set it). The updated " +
                       "CrimpToolCrimpDraggable picks off renderer bounds instead and is immune to this.";

            if (!rendererHit && !physicsHit)
            {
                Vector3 sp = tool.dragCamera.WorldToScreenPoint(tool.transform.position);
                float px = Vector2.Distance(new Vector2(sp.x, sp.y), PointerInput.Position);

                if (sp.z <= 0f)
                    return "the tool is BEHIND the drag camera, so it can never be clicked. " +
                           "Give CrimpToolCrimpDraggable a Holster Anchor placed in front of this camera.";

                string hit = whatWasHit == null
                    ? "the ray hit nothing at all"
                    : $"the ray hit '{whatWasHit}' instead";

                return $"the pointer ray misses the tool - {hit}. Your click was {px:0} px from the tool's " +
                       $"centre on screen. If that number is large you're clicking the wrong object (remember " +
                       $"there are two tool visuals in this scene); if it's small, the collider doesn't wrap " +
                       $"the mesh you can see.";
            }

            return null;
        }

        /// <summary>Ray against the tool's VISIBLE bounds. No physics involved, so this is true
        /// whenever the ray passes through the mesh as drawn on screen.</summary>
        static bool RendererBoundsHit(CrimpToolCrimpDraggable tool, Ray ray)
        {
            var renderers = tool.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.bounds.IntersectRay(ray)) return true;
            }
            return false;
        }

        /// <summary>Same test CrimpToolCrimpDraggable now uses: a full physics raycast that accepts a
        /// hit on ANY collider belonging to the tool, and that explicitly collides with triggers so
        /// the project's "Queries Hit Triggers" setting can't silently hide one.</summary>
        static bool RayHitsTool(CrimpToolCrimpDraggable tool, Ray ray, out string whatWasHit)
        {
            whatWasHit = null;
            // Everything, deliberately: the hit still has to belong to this tool to count, so a wide
            // mask costs nothing and keeps this diagnostic independent of the draggable's own settings.
            var hits = Physics.RaycastAll(ray, tool.raycastDistance, ~0, QueryTriggerInteraction.Collide);

            float nearest = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<CrimpToolCrimpDraggable>() == tool) return true;
                if (hit.distance < nearest) { nearest = hit.distance; whatWasHit = PathOf(hit.collider.transform); }
            }
            return false;
        }

        /// <summary>Names the top-most UI element sitting under the pointer, which is what makes
        /// EventSystem.IsPointerOverGameObject() return true and kill the drag.</summary>
        string BlockingUIElement()
        {
            if (EventSystem.current == null) return null;

            bool overUI = Input.touchCount > 0
                ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                : EventSystem.current.IsPointerOverGameObject();

            if (!overUI) return null;

            var pointer = new PointerEventData(EventSystem.current) { position = PointerInput.Position };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);

            return hits.Count > 0 ? PathOf(hits[0].gameObject.transform) : "an unidentified UI element";
        }

        // ------------------------------------------------------------------

        string BuildReport()
        {
            var tool = toolDraggable;
            var b = new StringBuilder();
            b.AppendLine("===== Crimp tool drag report =====");

            if (tool == null) { b.Append("No CrimpToolCrimpDraggable found."); return b.ToString(); }

            b.AppendLine($"Tool object      : {PathOf(tool.transform)}");
            b.AppendLine($"GameObject active: {tool.gameObject.activeInHierarchy}");
            b.AppendLine($"Component enabled: {tool.enabled}");
            b.AppendLine($"Drag Camera      : {(tool.dragCamera == null ? "EMPTY" : tool.dragCamera.name + (tool.dragCamera.isActiveAndEnabled ? " (rendering)" : " (NOT rendering)"))}");
            b.AppendLine($"Drop Distance    : {tool.dropDistance}");
            b.AppendLine($"Holster Anchor   : {(tool.holsterAnchor == null ? "EMPTY - the tool returns to wherever it happened to be when it first woke up, which is usually the STRIPPING pose and often off-screen for this camera" : PathOf(tool.holsterAnchor))}");

            var cols = tool.GetComponentsInChildren<Collider>(true);
            if (cols.Length == 0)
            {
                b.AppendLine("Colliders        : NONE on this object or its children");
            }
            else
            {
                b.AppendLine($"Colliders        : {cols.Length} found");
                foreach (var c in cols)
                    b.AppendLine($"   - {c.GetType().Name} on {c.name}{(c.enabled ? "" : " [disabled]")}{(c.isTrigger ? " [isTrigger]" : "")}, world size {c.bounds.size.x:0.###} x {c.bounds.size.y:0.###} x {c.bounds.size.z:0.###}");
            }
            b.AppendLine($"Time.timeScale   : {Time.timeScale}{(Time.timeScale == 0f ? "  <- FROZEN: FixedUpdate never runs, so moved colliders never sync into PhysX" : "")}");
            b.AppendLine($"autoSyncTransform: {Physics.autoSyncTransforms}{(Physics.autoSyncTransforms ? "" : "  <- OFF: a Transform moved outside FixedUpdate leaves its collider behind")}");
            b.AppendLine($"Queries Hit Trig.: {Physics.queriesHitTriggers}{(Physics.queriesHitTriggers ? "" : "  <- OFF: Collider.Raycast() ignores trigger colliders entirely")}");

            if (tool.dragCamera != null)
            {
                Vector3 vp = tool.dragCamera.WorldToViewportPoint(tool.transform.position);
                bool onScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                b.AppendLine($"On screen        : {(onScreen ? "yes" : "NO - the tool is outside this camera's view, so it can never be clicked")} (viewport {vp.x:0.00}, {vp.y:0.00}, depth {vp.z:0.00})");
            }

            var crimp = CrimpToolCrimpController.Instance;
            if (crimp == null)
            {
                b.AppendLine("Crimp controller : MISSING");
            }
            else
            {
                b.AppendLine($"Crimp controller : IsCrimping={crimp.IsCrimping}, AllEndsCrimped={crimp.AllEndsCrimped()}");
                if (crimp.Ends != null)
                {
                    foreach (var end in crimp.Ends)
                    {
                        if (end == null) { b.AppendLine("  End [empty entry]"); continue; }
                        string anchor = end.toolDropAnchor == null ? "NO ANCHOR" : PathOf(end.toolDropAnchor);
                        float dist = end.toolDropAnchor == null ? -1f : Vector3.Distance(tool.transform.position, end.toolDropAnchor.position);
                        b.AppendLine($"  End {end.endNumber}: anchor={anchor}, distance from tool={(dist < 0f ? "n/a" : dist.ToString("0.###"))}, done={end.done}");
                    }
                }
            }

            var station = CrimpingStationController.Instance;
            if (station == null)
            {
                b.AppendLine("Station          : MISSING");
            }
            else
            {
                string one = station.endOne == null ? "End One NOT ASSIGNED" : $"End 1 wired {station.endOne.FilledCount()}/8, crimped={station.IsEndCrimped(1)}";
                string two = station.endTwo == null ? "End Two NOT ASSIGNED" : $"End 2 wired {station.endTwo.FilledCount()}/8, crimped={station.IsEndCrimped(2)}";
                b.AppendLine($"Station          : {one}; {two}");
                b.AppendLine($"Can crimp now    : End 1 = {station.CanCrimpEnd(1)}, End 2 = {station.CanCrimpEnd(2)}");
            }

            b.AppendLine($"Wiring view open : {(CrimpingViewController.Instance != null && CrimpingViewController.Instance.InCrimpingView)}");
            b.AppendLine($"Crimp stage open : {(CrimpStageViewController.Instance != null ? CrimpStageViewController.Instance.InCrimpView.ToString() : "no CrimpStageViewController in scene")}");
            b.AppendLine($"Dialog open      : {(DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)}");
            b.AppendLine($"Wire being dragged: {(WireDragController.Instance != null && WireDragController.Instance.IsDragging)}");

            string blocker = FirstBlockingReason();
            b.AppendLine();
            b.AppendLine(blocker == null
                ? "VERDICT: nothing is blocking the drag right now."
                : "VERDICT: " + blocker);
            b.Append("==================================");
            return b.ToString();
        }

        /// <summary>Draws the pointer ray in cyan and each of the tool's colliders in yellow, both in
        /// the Scene view while playing. If the cyan line sails past the yellow box, the collider isn't
        /// where the mesh looks like it is - which no amount of squinting at the Inspector will tell you.</summary>
        void DrawDebugGeometry()
        {
            var tool = toolDraggable;
            if (tool.dragCamera == null) return;

            Ray ray = tool.dragCamera.ScreenPointToRay(PointerInput.Position);
            Debug.DrawRay(ray.origin, ray.direction * tool.raycastDistance, Color.cyan);

            var cols = tool.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                if (c == null) continue;
                DrawBounds(c.bounds, c.enabled ? Color.yellow : Color.red);
            }
        }

        static void DrawBounds(Bounds b, Color color)
        {
            Vector3 c = b.center, e = b.extents;

            Vector3 p0 = c + new Vector3(-e.x, -e.y, -e.z);
            Vector3 p1 = c + new Vector3(e.x, -e.y, -e.z);
            Vector3 p2 = c + new Vector3(e.x, -e.y, e.z);
            Vector3 p3 = c + new Vector3(-e.x, -e.y, e.z);
            Vector3 p4 = c + new Vector3(-e.x, e.y, -e.z);
            Vector3 p5 = c + new Vector3(e.x, e.y, -e.z);
            Vector3 p6 = c + new Vector3(e.x, e.y, e.z);
            Vector3 p7 = c + new Vector3(-e.x, e.y, e.z);

            Debug.DrawLine(p0, p1, color); Debug.DrawLine(p1, p2, color);
            Debug.DrawLine(p2, p3, color); Debug.DrawLine(p3, p0, color);
            Debug.DrawLine(p4, p5, color); Debug.DrawLine(p5, p6, color);
            Debug.DrawLine(p6, p7, color); Debug.DrawLine(p7, p4, color);
            Debug.DrawLine(p0, p4, color); Debug.DrawLine(p1, p5, color);
            Debug.DrawLine(p2, p6, color); Debug.DrawLine(p3, p7, color);
        }

        static string PathOf(Transform t)
        {
            if (t == null) return "null";
            var sb = new StringBuilder(t.name);
            var parent = t.parent;
            int guard = 0;
            while (parent != null && guard++ < 12)
            {
                sb.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return sb.ToString();
        }

        static CrimpToolCrimpDraggable FindDraggable()
        {
#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<CrimpToolCrimpDraggable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<CrimpToolCrimpDraggable>(true);
#endif
            return all.Length > 0 ? all[0] : null;
        }
    }
}