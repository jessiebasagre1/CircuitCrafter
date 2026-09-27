using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using CircuitCrafter.Assessment;
using CSSSimulation.Networking;

namespace CircuitCrafter.Guidance.EditorTools
{
    /// <summary>
    /// Checks the open scene for the two things that break the Story / Assessment split:
    /// guidance that is missing in Story Mode, and guidance that would still show in an assessment.
    ///
    /// RUN IT WITH: CircuitCrafter > Guidance > Audit Open Scene. Read the Console.
    ///
    /// The scripts already gate themselves - dialogs, waypoints, hints, highlights, the AI Tutor.
    /// What this looks for is scene CONTENT no script knows about: a label called "Instruction" on a
    /// panel, a "Hint" button, a "Guide" image. Anything it lists either gets a ModeVisibility
    /// component (select it, then CircuitCrafter > Guidance > Add Mode Visibility To Selection) or is
    /// fine as it is - a name is only a clue.
    /// </summary>
    public static class GuidanceAudit
    {
        private static readonly string[] SuspectWords = { "hint", "instruction", "guide", "tutorial", "tip", "highlight", "arrow", "helper" };

        [MenuItem("CircuitCrafter/Guidance/Audit Open Scene", false, 30)]
        public static void Audit()
        {
            var ok = new StringBuilder();
            var warn = new StringBuilder();

            // ---- project ----------------------------------------------------
            GuidanceSettings settings = Resources.Load<GuidanceSettings>(GuidanceSettings.ResourcesName);
            if (settings == null)
                warn.AppendLine("- No Resources/GuidanceSettings asset - built-in defaults are used. Run Create Guidance Settings.");
            else
            {
                ok.AppendLine("- GuidanceSettings found.");
                if (settings.reviewLibrary == null)
                    warn.AppendLine("- GuidanceSettings has no Review Library - mistakes will be listed without a correct procedure.");

                var allowed = new List<string>();
                foreach (GuidanceKind k in System.Enum.GetValues(typeof(GuidanceKind)))
                    if (settings.assessment.Allows(k)) allowed.Add(k.ToString());
                if (allowed.Count > 0)
                    warn.AppendLine("- Assessment currently ALLOWS: " + string.Join(", ", allowed) + ". Each is help given during a graded run.");
                if (settings.editorPreview != GuidanceSettings.EditorPreview.Auto)
                    warn.AppendLine($"- Editor preview is forced to {settings.editorPreview}. Set it back to Auto before testing through the menu.");
            }

            // ---- guidance UI ------------------------------------------------
            Check<ObjectiveHud>(ok, warn, "Current Task HUD (ObjectiveHud)");
            Check<FeedbackToast>(ok, warn, "Feedback toast");
            Check<TutorialCardPanel>(ok, warn, "Tutorial card panel");
            Check<JobOrderPanel>(ok, warn, "Job order panel");
            Check<GuideMarkerOverlay>(ok, warn, "Highlight tooltip (GuideMarkerOverlay)");
            AssessmentReviewPanel review = Check<AssessmentReviewPanel>(ok, warn, "Assessment review panel");

            // ---- story --------------------------------------------------------
            StoryGuide guide = Find<StoryGuide>();
            if (guide == null)
                warn.AppendLine("- No StoryGuide - Story Mode has no Current Task steps, step tutorials or highlights here. " +
                                (Find<TaskWaypointArrow>() != null ? "The HUD will fall back to the waypoint arrow's step labels." : ""));
            else
            {
                ok.AppendLine($"- StoryGuide with {guide.steps.Count} step(s).");
                if (guide.jobOrder == null && (Find<JobOrderPanel>() == null || Find<JobOrderPanel>().jobOrder == null))
                    warn.AppendLine("- No Job Order assigned (StoryGuide or JobOrderPanel).");

                int noInstruction = 0, noHighlight = 0;
                foreach (StoryGuide.GuideStep s in guide.steps)
                {
                    if (s == null) continue;
                    if (string.IsNullOrEmpty(s.instruction)) noInstruction++;
                    if (s.highlight == null || s.highlight.Length == 0) noHighlight++;
                }
                if (noInstruction > 0) warn.AppendLine($"- {noInstruction} StoryGuide step(s) have no Instruction line.");
                if (noHighlight > 0) warn.AppendLine($"- {noHighlight} StoryGuide step(s) highlight nothing (fine for screen-only steps).");
            }

            // ---- assessment ---------------------------------------------------
            AssessmentIntroPanel intro = Find<AssessmentIntroPanel>();
            if (intro != null)
            {
                if (intro.jobOrder == null)
                    warn.AppendLine("- AssessmentIntroPanel shows its Objectives list as the briefing. If those lines describe STEPS, " +
                                    "assign a Job Order instead so the candidate gets the scenario and required outcome only.");
                else ok.AppendLine("- AssessmentIntroPanel shows the job order's assessment brief.");
            }

            if (review != null && review.hideWhileOpen.Length == 0 &&
                (Find<COC1AssessmentManager>() != null || Find<COC2AssessmentManager>() != null || Find<COC3AssessmentManager>() != null))
                warn.AppendLine("- AssessmentReview > Hide While Open is empty. Drag the module's own report panel in, or both show at once.");

            if (Find<COC3Manager>() != null)
            {
                int missing = 0;
                foreach (string g in AssetDatabase.FindAssets("t:ScreenshotStepData"))
                {
                    var d = AssetDatabase.LoadAssetAtPath<ScreenshotStepData>(AssetDatabase.GUIDToAssetPath(g));
                    if (d != null && d.stepType != StepType.Informational && string.IsNullOrEmpty(d.assessmentPrompt)) missing++;
                }
                if (missing > 0)
                    warn.AppendLine($"- {missing} COC3 ScreenshotStepData asset(s) have no Assessment Prompt - they show the generic " +
                                    "\"Continue the server configuration...\" line in an assessment.");
            }

            // ---- suspects -----------------------------------------------------
            var suspects = new List<string>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || !IsSuspect(t)) continue;
                if (t.GetComponentInParent<ModeVisibility>(true) != null) continue;
                if (t.GetComponentInParent<ObjectiveHud>(true) != null || t.GetComponentInParent<TutorialCardPanel>(true) != null ||
                    t.GetComponentInParent<GuideMarkerOverlay>(true) != null) continue;
                suspects.Add(Path(t));
                if (suspects.Count >= 40) break;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[GuidanceAudit] {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            sb.AppendLine("OK:");
            sb.Append(ok.Length > 0 ? ok.ToString() : "- (nothing)\n");
            sb.AppendLine("CHECK:");
            sb.Append(warn.Length > 0 ? warn.ToString() : "- nothing to fix.\n");

            if (suspects.Count > 0)
            {
                sb.AppendLine($"POSSIBLE UNGATED GUIDANCE ({suspects.Count}{(suspects.Count >= 40 ? "+" : "")}) - named like help, with no ModeVisibility. " +
                              "Scripts that already check the mode are fine as they are; for anything else, select it and use " +
                              "Add Mode Visibility To Selection:");
                foreach (string s in suspects) sb.AppendLine("  " + s);
            }

            Debug.Log(sb.ToString());
        }

        [MenuItem("CircuitCrafter/Guidance/Add Mode Visibility To Selection", false, 31)]
        private static void AddModeVisibility()
        {
            int added = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go.GetComponent<ModeVisibility>() != null) continue;
                ModeVisibility mv = Undo.AddComponent<ModeVisibility>(go);
                string n = go.name.ToLowerInvariant();
                mv.kind = n.Contains("instruction") ? GuidanceKind.StepInstructions
                        : n.Contains("guide") ? GuidanceKind.ReferenceGuides
                        : n.Contains("highlight") || n.Contains("glow") ? GuidanceKind.ObjectHighlights
                        : n.Contains("arrow") || n.Contains("waypoint") ? GuidanceKind.Waypoints
                        : n.Contains("tutorial") ? GuidanceKind.ProcedureTutorial
                        : GuidanceKind.Hints;
                added++;
            }
            Debug.Log($"[GuidanceAudit] Added ModeVisibility to {added} object(s). Check each one's Kind.");
        }

        [MenuItem("CircuitCrafter/Guidance/Add Mode Visibility To Selection", true)]
        private static bool AddModeVisibilityValidate() => Selection.gameObjects.Length > 0;

        // ------------------------------------------------------------------

        private static T Find<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        private static T Check<T>(StringBuilder ok, StringBuilder warn, string label) where T : Object
        {
            T found = Find<T>();
            if (found != null) ok.AppendLine("- " + label + " present.");
            else warn.AppendLine("- No " + label + ". Run Build Guidance UI In Open Scene.");
            return found;
        }

        private static bool IsSuspect(Transform t)
        {
            string n = t.name.ToLowerInvariant();
            bool named = false;
            foreach (string w in SuspectWords) if (n.Contains(w)) { named = true; break; }
            if (!named) return false;

            // Only visible things: a text, an image, a renderer or a button.
            return t.GetComponent<TMP_Text>() != null || t.GetComponent<UnityEngine.UI.Graphic>() != null ||
                   t.GetComponent<Renderer>() != null || t.GetComponent<UnityEngine.UI.Button>() != null;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
