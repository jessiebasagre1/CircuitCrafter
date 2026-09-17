using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CircuitCrafter.COC1.EditorTools
{
    /// <summary>
    /// Switches Emission on for every material used by a PC part or a cable, so
    /// DisassemblyHintGlow can actually make them shine.
    ///
    /// RUN IT WITH: CircuitCrafter > Prepare Materials For Glow, with COC 1 open.
    ///
    /// WHY THIS CANNOT BE DONE AT RUNTIME, which is the whole reason it is a menu item.
    /// DisassemblyHintGlow writes emission through a MaterialPropertyBlock, and a property block
    /// can set a shader PROPERTY but cannot enable a shader KEYWORD. Emission in URP is behind
    /// the _EMISSION keyword: with it off, the emission colour is written, accepted, and
    /// multiplied by nothing. No error, no warning, no glow - the part just tints and stops.
    ///
    /// Enabling the keyword means editing the material asset, which is an Editor-only,
    /// version-controlled change. Hence a tool you run once rather than something a component
    /// does behind your back.
    ///
    /// IT SETS EMISSION TO NEARLY BLACK, NOT BLACK. The keyword needs to be on, but the part
    /// must not glow on its own - only when the hint says so. Pure black would do that, except
    /// that Unity strips emission from a material it believes emits nothing, and the keyword
    /// goes with it at build time. A value just above zero keeps the variant alive and is
    /// invisible in play.
    ///
    /// SAFE TO RUN TWICE. A material already set up is counted and skipped.
    /// </summary>
    public static class GlowMaterialPrep
    {
        /// <summary>Dark enough to be invisible, bright enough that Unity keeps the emission
        /// variant rather than optimising it away.</summary>
        private static readonly Color NearBlack = new Color(0.015f, 0.015f, 0.015f);

        [MenuItem("CircuitCrafter/Prepare Materials For Glow", false, 47)]
        public static void Prepare()
        {
            var materials = new HashSet<Material>();

            Collect<PCPartItem>(materials, out int partCount);
            Collect<CableConnector>(materials, out int cableCount);

            if (materials.Count == 0)
            {
                Debug.LogWarning("[GlowMaterialPrep] No PCPartItem or CableConnector with a Renderer " +
                                 "was found in the open scene(s). Open COC 1 and run this again.");
                return;
            }

            var changed = new List<Material>();
            int already = 0;
            int noEmissionProperty = 0;

            foreach (Material m in materials)
            {
                if (m == null) continue;

                if (!m.HasProperty("_EmissionColor"))
                {
                    // An Unlit or custom shader with nowhere to put emission. Not an error - the
                    // colour tint still works on it - but worth naming, because it is the one
                    // material that will never shine no matter what is set.
                    noEmissionProperty++;
                    continue;
                }

                if (m.IsKeywordEnabled("_EMISSION")) { already++; continue; }

                Undo.RecordObject(m, "Prepare Material For Glow");

                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", NearBlack);

                // Without this the lightmapper treats the material as a light source and bakes
                // the hint colour into the scene - a part that stays lit after the glow stops.
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

                EditorUtility.SetDirty(m);
                changed.Add(m);
            }

            AssetDatabase.SaveAssets();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[GlowMaterialPrep] Looked at {materials.Count} material(s) across " +
                          $"{partCount} part(s) and {cableCount} cable(s).");
            sb.AppendLine($"  Emission switched ON : {changed.Count}");
            sb.AppendLine($"  Already ready        : {already}");

            if (noEmissionProperty > 0)
                sb.AppendLine($"  No emission property : {noEmissionProperty}  (Unlit or a custom " +
                              "shader - these will tint but never shine; swap them to URP/Lit if " +
                              "they need to)");

            if (changed.Count > 0)
            {
                sb.AppendLine("  Changed:");
                for (int i = 0; i < changed.Count && i < 25; i++) sb.AppendLine($"    {changed[i].name}");
                if (changed.Count > 25) sb.AppendLine($"    ...and {changed.Count - 25} more");
            }

            sb.AppendLine();
            sb.AppendLine("STILL TO DO, and emission does nothing without it:");
            sb.AppendLine("  1. GameObject > Volume > Global Volume. New profile. Add Override >");
            sb.AppendLine("     Post-processing > Bloom. Threshold ~0.9, Intensity ~1.");
            sb.AppendLine("  2. On the camera: tick Post Processing.");
            sb.AppendLine("  3. On the URP asset (Project Settings > Graphics): HDR ON. Bloom cannot");
            sb.AppendLine("     see anything brighter than 1 without it, which is exactly the range");
            sb.AppendLine("     the glow works in.");
            sb.AppendLine();
            sb.AppendLine("Then right-click DisassemblyHintGlow > Check materials can glow to confirm.");

            Debug.Log(sb.ToString());
        }

        private static void Collect<T>(HashSet<Material> into, out int count) where T : Component
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            count = found.Length;

            foreach (T item in found)
            {
                if (item == null) continue;

                foreach (Renderer r in item.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;

                    // sharedMaterials, never materials. Reading .materials in the Editor clones
                    // every one of them into the scene and leaves the originals untouched - so the
                    // tool would report success having edited copies that are thrown away on the
                    // next domain reload.
                    foreach (Material m in r.sharedMaterials)
                        if (m != null) into.Add(m);
                }
            }
        }
    }
}
