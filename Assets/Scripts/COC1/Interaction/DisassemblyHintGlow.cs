using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes whatever the learner can act on RIGHT NOW pulse, so a beginner staring at a full
/// system unit can see where to start.
///
/// ATTACH THIS TO: the Managers object in COC 1. One per scene. Nothing to assign - it finds
/// the parts and cables itself.
///
/// STORY MODE ONLY, AND THAT IS THE POINT. In an assessment, knowing which part comes off next
/// IS the thing being marked. A glow that told them would not be a hint, it would be the
/// answer - and the mark would measure whether they can follow a light. It stands itself down
/// the moment AssessmentMode says this is a graded run.
///
/// IT DOES NOT NEED A LIST OF WHAT GLOWS, and that is what stops it going stale. Every part
/// already knows whether it can be picked up - cables unplugged, locks open, removal order
/// satisfied - because that is the same question TryBeginDrag asks before allowing a drag. This
/// reads that answer. Add a part to the PC, change a removal order, gate something new behind a
/// cable, and the glow follows without anyone editing a list.
///
/// So the glow means exactly one thing: THIS CAN BE DRAGGED NOW. Which teaches the order by
/// itself - the heatsink cable pulses, then the heatsink, then the locks - without a script
/// anywhere spelling that order out a second time.
///
/// HOW IT GLOWS. It pushes colour through a MaterialPropertyBlock rather than touching
/// materials. Assigning to renderer.material clones the material for every part, which leaks
/// one instance per object and, worse, means any part sharing a material silently stops sharing
/// it. A property block writes per-renderer with no cloning and is cleared away completely when
/// the glow stops.
/// </summary>
public class DisassemblyHintGlow : MonoBehaviour
{
    [Header("When it runs")]
    [Tooltip("The module this scene is, so it can stand down during that module's assessment.")]
    [SerializeField] private string moduleId = "COC1";

    [Tooltip("Turn the glow off entirely during an assessment. LEAVE THIS ON - in a graded run, " +
             "showing which part comes off next is showing the answer.")]
    [SerializeField] private bool storyModeOnly = true;

    [Header("Look")]
    [Tooltip("The colour mixed into the part while it is glowing.")]
    [SerializeField] private Color glowTint = new Color(0.45f, 0.85f, 1f);

    [Tooltip("How far the pulse goes at its brightest. 0 is nothing, 1 is the part fully taking " +
             "the tint. 0.35 reads clearly without hiding what the part actually looks like - " +
             "which matters, because recognising the part by sight is half of what is being learnt.")]
    [Range(0f, 1f)]
    [SerializeField] private float strength = 0.35f;

    [Tooltip("Pulses per second. Around 1 reads as breathing; above 2 it reads as an error state " +
             "and gets tiring to work beside.")]
    [Range(0.1f, 4f)]
    [SerializeField] private float pulsesPerSecond = 0.9f;

    [Tooltip("Also drive the material's emission, so the part genuinely glows rather than just " +
             "changing colour.\n\n" +
             "NEEDS TWO THINGS SET UP FIRST, neither of which a script can do at runtime:\n" +
             "  1. Emission ticked on each part's material (menu: CircuitCrafter > Prepare " +
             "Materials For Glow does every one of them at once).\n" +
             "  2. A Global Volume with Bloom in the scene - bloom is what turns a bright " +
             "surface into light spilling past its edges.\n\n" +
             "Without those the tint still works; the part changes colour but does not shine. " +
             "Right-click this component > Check materials can glow to see which is missing.")]
    [SerializeField] private bool useEmission = true;

    [Tooltip("How far above full brightness the emission is pushed at the top of the pulse.\n\n" +
             "Bloom only picks up what is brighter than its threshold, so a value of 1 or less " +
             "usually produces NO visible glow at all - the surface goes pale and stops there. " +
             "3 is a clear halo. Raise it until the part reads at a glance from across the " +
             "bench, then stop: past about 6 the shape of the part is lost inside its own light, " +
             "and recognising the part by sight is half of what is being learnt.")]
    [Range(0.5f, 8f)]
    [SerializeField] private float emissionIntensity = 3f;

    [Header("What glows")]
    [Tooltip("Parts still in the PC that can be removed now.")]
    [SerializeField] private bool glowParts = true;

    [Tooltip("Cables still plugged in that can be unplugged now.")]
    [SerializeField] private bool glowCables = true;

    [Tooltip("Retention locks that are still closed over a part waiting to come out - the RAM " +
             "clips and the CPU levers.\n\n" +
             "LEAVE THIS ON, or the hint dies exactly where a beginner needs it most. A RAM stick " +
             "under closed clips is not removable, so it correctly does not glow - and without " +
             "this, nothing does. The learner is left looking at a bench where the hint has simply " +
             "stopped, with no way to know the clips are the thing to press.")]
    [SerializeField] private bool glowLocks = true;

    [Header("When it stops")]
    [Tooltip("Stop glowing once the disassembly task is finished.\n\n" +
             "Without this the hint carries straight on into assembly and starts pointing at " +
             "parts the learner has just fitted - because a part correctly installed is, by " +
             "definition, a part that can be removed again. Asking them to put the PC together " +
             "while every finished part pulses \"take me out\" is worse than no hint at all.")]
    [SerializeField] private bool stopWhenDisassembled = true;

    [Tooltip("The task that counts as \"disassembly finished\". Matches PCDisassemblySetup.TaskId.")]
    [SerializeField] private string disassembleTaskId = "disassemble_pc";

    [Header("Performance")]
    [Tooltip("Seconds between re-scans for new parts and cables. The scene does not gain parts " +
             "mid-run, so this is only for safety - 2 is plenty and costs nothing on a phone.")]
    [Min(0.25f)]
    [SerializeField] private float rescanSeconds = 2f;

    [Header("Diagnostics")]
    [SerializeField] private bool verbose;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private PCPartItem[] parts = new PCPartItem[0];
    private CableConnector[] cables = new CableConnector[0];
    private RAMLock[] ramLocks = new RAMLock[0];
    private CPULock[] cpuLocks = new CPULock[0];

    /// <summary>Which socket each RAM clip belongs to. RAMLock does not know its own socket -
    /// RAMSocket holds the list - so the relationship has to be read the other way round and
    /// remembered, or a clip cannot tell whether there is still anything underneath it.</summary>
    private readonly Dictionary<RAMLock, RAMSocket> clipOwner = new Dictionary<RAMLock, RAMSocket>();

    private bool finishedForThisRun;

    /// <summary>Renderers currently being written to, and the base colour each one started with.
    /// Kept so the glow can be taken off cleanly rather than leaving the last pulse frozen on a
    /// part the learner has just finished with.</summary>
    private readonly Dictionary<Renderer, Color> litNow = new Dictionary<Renderer, Color>();
    private readonly List<Renderer> toClear = new List<Renderer>();

    private MaterialPropertyBlock block;
    private float nextScan;
    private bool standingDown;

    // ==================================================================

    private void Start()
    {
        block = new MaterialPropertyBlock();

        if (storyModeOnly && AssessmentMode.IsAssessing(moduleId))
        {
            standingDown = true;
            enabled = false;
            Debug.Log($"[DisassemblyHintGlow] {moduleId} is being assessed, so the hint glow is off. " +
                      "Which part comes off next is part of what is being marked.", this);
            return;
        }

        Rescan();
    }

    private void OnDisable()
    {
        // Everything gets its own colour back. A component switched off mid-pulse would otherwise
        // leave a part stuck at whatever brightness the sine happened to be at.
        ClearAll();
    }

    private void Rescan()
    {
        nextScan = Time.time + rescanSeconds;
        parts = FindObjectsByType<PCPartItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        cables = FindObjectsByType<CableConnector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        ramLocks = FindObjectsByType<RAMLock>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        cpuLocks = FindObjectsByType<CPULock>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        clipOwner.Clear();
        foreach (RAMSocket socket in FindObjectsByType<RAMSocket>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None))
        {
            if (socket == null || socket.locks == null) continue;
            foreach (RAMLock clip in socket.locks)
                if (clip != null) clipOwner[clip] = socket;
        }

        if (verbose)
            Debug.Log($"[DisassemblyHintGlow] Watching {parts.Length} part(s), {cables.Length} cable(s), " +
                      $"{ramLocks.Length} RAM clip(s) and {cpuLocks.Length} CPU lock(s).", this);
    }

    // ==================================================================

    private void LateUpdate()
    {
        if (standingDown || finishedForThisRun) return;

        // The job is done - hands off. Checked before anything else so the last pulse is cleared
        // on the same frame the final part comes out, rather than one frame into assembly.
        if (stopWhenDisassembled && DisassemblyFinished())
        {
            finishedForThisRun = true;
            ClearAll();
            Debug.Log("[DisassemblyHintGlow] Disassembly finished - hints off for the rest of this " +
                      "run. Putting the PC back together is the learner's own work.", this);
            return;
        }

        if (Time.time >= nextScan) Rescan();

        // Unscaled, so the pulse keeps breathing while a dialog has paused the game. A hint that
        // freezes the moment a popup opens looks like a bug in the popup.
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulsesPerSecond * Mathf.PI * 2f);
        float amount = wave * Mathf.Clamp01(strength);

        toClear.Clear();
        foreach (var pair in litNow) toClear.Add(pair.Key);

        if (glowParts)
        {
            foreach (PCPartItem part in parts)
            {
                if (part == null || !IsReady(part)) continue;
                LightUp(part.gameObject, amount, toClear);
            }
        }

        if (glowCables)
        {
            foreach (CableConnector cable in cables)
            {
                if (cable == null || !IsReady(cable)) continue;
                LightUp(cable.gameObject, amount, toClear);
            }
        }

        if (glowLocks)
        {
            foreach (RAMLock clip in ramLocks)
            {
                if (clip == null || !IsReady(clip)) continue;
                LightUp(clip.gameObject, amount, toClear);
            }

            foreach (CPULock lever in cpuLocks)
            {
                if (lever == null || !IsReady(lever)) continue;
                LightUp(lever.gameObject, amount, toClear);
            }
        }

        // Anything lit last frame and not re-lit this one has been dealt with - unplugged,
        // removed, or newly blocked. Put it back.
        for (int i = 0; i < toClear.Count; i++) Clear(toClear[i]);
    }

    // ==================================================================

    /// <summary>
    /// A part glows when it is in the PC and could be dragged out this instant.
    ///
    /// IsPickupReady is the property PCPartItem publishes from the same check its own
    /// pickup-ready visual uses - see the note in that file. Asking the part rather than working
    /// it out here is what keeps this component from having its own, second, slightly different
    /// idea of when a part is removable.
    /// </summary>
    private static bool IsReady(PCPartItem part)
    {
        return part.isActiveAndEnabled && !part.IsRemoved && part.IsPickupReady;
    }

    /// <summary>A cable glows while it is still plugged in and not locked down.</summary>
    private static bool IsReady(CableConnector cable)
    {
        return cable.isActiveAndEnabled && cable.isPlugged && !cable.isLocked;
    }

    /// <summary>
    /// A RAM clip glows while it is closed over a stick that still has to come out.
    ///
    /// The "still something underneath" half matters. A clip left closed on an EMPTY slot is not
    /// in anyone's way, and glowing it would send the learner to press a catch that achieves
    /// nothing - on a board with four slots and two sticks, that is half the hints pointing at
    /// busywork.
    /// </summary>
    private bool IsReady(RAMLock clip)
    {
        if (!clip.isActiveAndEnabled || clip.IsOpen) return false;

        if (!clipOwner.TryGetValue(clip, out RAMSocket socket) || socket == null) return false;

        return socket.Occupant != null && !socket.Occupant.IsRemoved;
    }

    /// <summary>
    /// A CPU lever glows while it is closed, reachable, and its turn has come.
    ///
    /// IsCovered is the heatsink still sitting on top - the lever physically cannot be worked
    /// then, and CPULock.Toggle refuses it. The Case lever additionally waits on the Main one,
    /// which is the same order Toggle enforces. Reading both from the lock rather than deciding
    /// here means the hint cannot drift away from what the lever will actually allow.
    /// </summary>
    private static bool IsReady(CPULock lever)
    {
        if (!lever.isActiveAndEnabled || lever.IsOpen || lever.IsCovered) return false;

        if (lever.role == CPULockRole.Case)
            return lever.mainLock != null && lever.mainLock.IsOpen;

        return true;
    }

    /// <summary>True once TaskManager says the disassembly task is complete.</summary>
    private bool DisassemblyFinished()
    {
        if (string.IsNullOrWhiteSpace(disassembleTaskId)) return false;
        if (TaskManager.Instance == null) return false;

        return TaskManager.Instance.IsTaskComplete(disassembleTaskId.Trim());
    }

    // ==================================================================

    private void LightUp(GameObject go, float amount, List<Renderer> stillToClear)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(false);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            stillToClear.Remove(r);

            if (!litNow.TryGetValue(r, out Color baseColor))
            {
                baseColor = ReadBaseColor(r);
                litNow[r] = baseColor;
            }

            Color lit = Color.Lerp(baseColor, glowTint, amount);

            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, lit);
            block.SetColor(ColorId, lit);          // built-in / Unlit materials use _Color

            // Scaled by emissionIntensity, NOT by strength. Strength governs how far the
            // part's own colour is allowed to shift, which must stay modest; emission is a
            // separate quantity that has to exceed bloom's threshold before anything glows at
            // all. Tying them together meant the tint was either too strong or the glow too
            // weak, with no setting that gave both.
            if (useEmission) block.SetColor(EmissionId, glowTint * (amount * emissionIntensity));

            r.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// The renderer's colour BEFORE any glow - read from the shared material, never from the
    /// property block.
    ///
    /// Reading the block back would sample whatever the last pulse wrote, so the base would
    /// drift brighter every frame until the whole part was the tint. This is the bug that makes
    /// a pulse look like a fade-to-white.
    /// </summary>
    private static Color ReadBaseColor(Renderer r)
    {
        Material m = r.sharedMaterial;
        if (m == null) return Color.white;

        if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
        if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
        return Color.white;
    }

    private void Clear(Renderer r)
    {
        if (r == null) { litNow.Remove(r); return; }

        // An empty block, not a block set back to the base colour. Clearing hands the renderer
        // back to its material completely, so a material changed at runtime - a dust overlay, a
        // fault tint - is not frozen at whatever it was when the glow started.
        r.SetPropertyBlock(null);
        litNow.Remove(r);
    }

    private void ClearAll()
    {
        toClear.Clear();
        foreach (var pair in litNow) toClear.Add(pair.Key);
        for (int i = 0; i < toClear.Count; i++) Clear(toClear[i]);

        litNow.Clear();
    }

    // ==================================================================

    /// <summary>
    /// Says whether a real glow is even possible in this scene, and which of the two setup steps
    /// is missing.
    ///
    /// Both failures look identical from the Game view - the part changes colour and does not
    /// shine - and neither raises an error, because writing an emission colour to a material
    /// with emission switched off is a perfectly legal thing to do that simply has no effect.
    /// </summary>
    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    private static void InspectMaterials(GameObject go, HashSet<Material> seen,
                                        List<string> without, ref int withEmission)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;

            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m)) continue;

                if (m.IsKeywordEnabled("_EMISSION")) withEmission++;
                else without.Add(m.name);
            }
        }
    }

    [ContextMenu("Check materials can glow")]
    private void CheckMaterialsCanGlow()
    {
        if (parts.Length == 0 && cables.Length == 0) Rescan();

        var seen = new HashSet<Material>();
        var without = new List<string>();
        int withEmission = 0;

        foreach (PCPartItem p in parts)
            if (p != null) InspectMaterials(p.gameObject, seen, without, ref withEmission);

        foreach (CableConnector c in cables)
            if (c != null) InspectMaterials(c.gameObject, seen, without, ref withEmission);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[DisassemblyHintGlow] Glow readiness for {seen.Count} material(s):");
        sb.AppendLine($"  Emission ON : {withEmission}");
        sb.AppendLine($"  Emission OFF: {without.Count}");

        if (without.Count > 0)
        {
            sb.AppendLine("  These will tint but never shine:");
            for (int i = 0; i < without.Count && i < 20; i++) sb.AppendLine($"    {without[i]}");
            if (without.Count > 20) sb.AppendLine($"    ...and {without.Count - 20} more");
            sb.AppendLine("  Fix all of them at once: menu CircuitCrafter > Prepare Materials For Glow.");
        }

        // The second half, and the one people miss. Emission makes a surface bright; only bloom
        // makes brightness spill past the silhouette, which is what reads as glowing.
        var volumes = FindObjectsByType<UnityEngine.Rendering.Volume>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        sb.AppendLine(volumes.Length == 0
            ? "  NO Volume in this scene, so there is no Bloom - emission will brighten the part " +
              "and stop at its edges. Add: GameObject > Volume > Global Volume, New profile, " +
              "Add Override > Post-processing > Bloom."
            : $"  {volumes.Length} Volume(s) in the scene. Check one has a Bloom override, that " +
              "Post Processing is ticked on the camera, and that the URP asset has HDR on - " +
              "bloom cannot pick up values above 1 without it.");

        Debug.Log(sb.ToString(), this);
    }

    [ContextMenu("Report glow")]
    private void ReportGlow()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[DisassemblyHintGlow] on '{name}':");
        sb.AppendLine($"  Assessment for '{moduleId}': {AssessmentMode.IsAssessing(moduleId)}" +
                      (standingDown ? "   <-- standing down, no glow this run" : ""));
        sb.AppendLine($"  Watching {parts.Length} part(s), {cables.Length} cable(s).");
        sb.AppendLine($"  Glowing right now ({litNow.Count} renderer(s)):");

        foreach (PCPartItem part in parts)
            if (part != null && IsReady(part)) sb.AppendLine($"    PART  {part.name}");

        foreach (CableConnector cable in cables)
            if (cable != null && IsReady(cable)) sb.AppendLine($"    CABLE {cable.name}");

        foreach (RAMLock clip in ramLocks)
            if (clip != null && IsReady(clip)) sb.AppendLine($"    CLIP  {clip.name}");

        foreach (CPULock lever in cpuLocks)
            if (lever != null && IsReady(lever)) sb.AppendLine($"    LEVER {lever.name}");

        // The renderers by name, because "the wrong thing is lighting up" is the one complaint
        // the list above cannot answer - a part whose mesh lives on a child, or shares an object
        // with its socket, glows something the learner did not expect and the part name gives no
        // clue which object that was.
        if (litNow.Count > 0)
        {
            sb.AppendLine($"  Renderers being written to ({litNow.Count}):");
            int shown = 0;
            foreach (var pair in litNow)
            {
                if (pair.Key == null) continue;
                if (shown++ >= 20) { sb.AppendLine("    ..."); break; }
                sb.AppendLine($"    {PathOf(pair.Key.transform)}");
            }
        }

        if (litNow.Count == 0)
            sb.AppendLine("    nothing - everything removable has been dealt with, or everything " +
                          "left is still blocked by a cable, a lock or the removal order.");

        Debug.Log(sb.ToString(), this);
    }
}