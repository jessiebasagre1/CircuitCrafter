using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CircuitCrafter.Assessment.EditorTools
{
    /// <summary>
    /// Creates the four CompetencyDefinition assets, fully populated, from one
    /// menu click:
    ///
    ///     CircuitCrafter &gt; Generate Competency Definitions
    ///
    /// PUT THIS FILE IN: Assets/Scripts/Assessment/Editor/
    /// The folder name matters - Unity only compiles UnityEditor code inside a
    /// folder called "Editor", and a script referencing UnityEditor anywhere else
    /// breaks the player build.
    ///
    /// WHY GENERATE THEM INSTEAD OF TYPING THEM
    /// Every evidence id is a plain string typed into the Inspector, and a typo
    /// there does not throw - the outcome just silently never satisfies, and you
    /// find out weeks later when a learner who did everything right is still
    /// sitting at Practised. Generating them removes that whole class of mistake
    /// for COC1, because the ids below are taken from the actual C# constants
    /// (PCDisassemblySetup.TaskId, SystemConfigTaskIds.InstallDrivers, and so on).
    /// A typo becomes a compile error instead of a silent failure.
    ///
    /// COC2, COC3 and COC4 still use string literals, because their ids do not
    /// exist as constants:
    ///   COC2 - serialized default fields on COC2TaskSetup, editable per scene
    ///   COC3 - AssessmentRecorder manual stage ids, which you type into the
    ///          recorder in step 4d. They must match there character for character.
    ///   COC4 - each ticket's requiredTaskId, authored per TicketData asset.
    ///
    /// SAFE TO RE-RUN
    /// Before creating anything it searches the WHOLE PROJECT for a definition
    /// already claiming that module id, not just the folder it writes to. So a
    /// hand-made asset, or one you moved elsewhere, blocks creation instead of
    /// being quietly duplicated. Nothing existing is ever overwritten - delete
    /// an asset first if you want it regenerated.
    ///
    /// THE OUTCOME CODES AND TITLES BELOW ARE A STARTING POINT, NOT GOSPEL.
    /// Check them against the current Training Regulations before a class uses
    /// this. Codes become keys in learner records, so changing one afterwards
    /// orphans every record filed under the old code.
    /// </summary>
    public static class CompetencyDefinitionGenerator
    {
        private const string FolderParent = "Assets";
        private const string FolderName = "Competency";
        private const string FolderPath = FolderParent + "/" + FolderName;

        // ==================================================================

        [MenuItem("CircuitCrafter/Generate Competency Definitions")]
        public static void Generate()
        {
            EnsureFolder();

            int created = 0;
            created += Build("Competency_COC1", "COC1", "Install and configure computer systems", Coc1Outcomes()) ? 1 : 0;
            created += Build("Competency_COC2", "COC2", "Set up computer networks", Coc2Outcomes()) ? 1 : 0;
            created += Build("Competency_COC3", "COC3", "Set up computer servers", Coc3Outcomes()) ? 1 : 0;
            created += Build("Competency_COC4", "COC4", "Maintain and repair computer systems and networks", Coc4Outcomes()) ? 1 : 0;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (created == 0)
            {
                Debug.Log("[CompetencyDefinitionGenerator] Every module already has a definition somewhere in the project - nothing was created.\n\n" +
                          "Run CircuitCrafter > Validate Competency Definitions to see where they all are.");
                return;
            }

            Debug.Log($"[CompetencyDefinitionGenerator] Created {created} asset(s) in {FolderPath}.\n\n" +
                      "NEXT: select your AssessmentManagers prefab, find the Competency Ledger component, " +
                      "set Definitions size to 4, and drag all four assets in. Nothing is judged until you do.");

            var first = AssetDatabase.LoadAssetAtPath<CompetencyDefinition>(FolderPath + "/Competency_COC1.asset");
            if (first != null)
            {
                Selection.activeObject = first;
                EditorGUIUtility.PingObject(first);
            }
        }

        /// <summary>
        /// Checks every CompetencyDefinition in the project for the authoring
        /// mistakes that fail silently at runtime rather than throwing.
        /// Run it after editing the assets by hand.
        /// </summary>
        [MenuItem("CircuitCrafter/Validate Competency Definitions")]
        public static void Validate()
        {
            string[] guids = AssetDatabase.FindAssets("t:CompetencyDefinition");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[CompetencyDefinitionGenerator] No CompetencyDefinition assets found. Run 'Generate Competency Definitions' first.");
                return;
            }

            // The inventory goes out FIRST, and it lists PATHS rather than asset
            // names. An earlier version printed names, which produced messages
            // like "'Competency_COC1' and 'Competency_COC1' both claim COC1" -
            // technically accurate and completely useless, because duplicates in
            // different folders share a name and the one thing you need in order
            // to fix it is where they are.
            var inventory = new StringBuilder();
            inventory.AppendLine($"[Validate] Found {guids.Length} CompetencyDefinition asset(s):");

            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var d = AssetDatabase.LoadAssetAtPath<CompetencyDefinition>(p);
                if (d == null) continue;

                string module = string.IsNullOrWhiteSpace(d.moduleId) ? "(blank)" : d.moduleId;
                int outcomeCount = d.outcomes != null ? d.outcomes.Count : 0;
                inventory.AppendLine($"    {module,-8} {outcomeCount} outcome(s)   {p}");
            }

            Debug.Log(inventory.ToString());

            int problems = 0;
            var modulesSeen = new Dictionary<string, string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<CompetencyDefinition>(path);
                if (definition == null) continue;

                if (string.IsNullOrWhiteSpace(definition.moduleId))
                {
                    Debug.LogWarning($"[Validate] {path} has a blank Module Id - the ledger can never match it.", definition);
                    problems++;
                }
                else if (modulesSeen.TryGetValue(definition.moduleId, out string other))
                {
                    // The ledger takes the FIRST match in its Definitions list, and
                    // which asset that is depends on the order you dragged them in -
                    // so a duplicate isn't merely redundant, it makes which one wins
                    // arbitrary. Delete the extra rather than leaving it.
                    Debug.LogWarning($"[Validate] Two assets claim module '{definition.moduleId}':\n" +
                                     $"    {other}\n    {path}\n" +
                                     "Only one will ever be used, and which one is arbitrary. Delete the one you don't want.", definition);
                    problems++;
                }
                else
                {
                    modulesSeen[definition.moduleId] = path;
                }

                // An asset with no outcomes at all produced no warnings whatsoever
                // in the first version, because every check below is per-outcome
                // and there was nothing to iterate. That is exactly the shape of a
                // leftover created by hand through Create > CircuitCrafter and then
                // forgotten - it even claims module "COC1", the field's default.
                if (definition.outcomes == null || definition.outcomes.Count == 0)
                {
                    Debug.LogWarning($"[Validate] {path} has no outcomes at all. If this is a leftover created by hand, delete it - " +
                                     "an empty asset that claims a module will shadow the real one.", definition);
                    problems++;
                    continue;
                }

                var codes = new HashSet<string>();
                foreach (var outcome in definition.outcomes)
                {
                    if (outcome == null) continue;

                    if (string.IsNullOrWhiteSpace(outcome.code))
                    {
                        Debug.LogWarning($"[Validate] {path} has an outcome with a blank Code - it can never be recorded.", definition);
                        problems++;
                        continue;
                    }

                    if (!codes.Add(outcome.code))
                    {
                        Debug.LogWarning($"[Validate] {path} has two outcomes coded '{outcome.code}'. Their ledger records collide.", definition);
                        problems++;
                    }

                    int usable = 0;
                    foreach (string id in outcome.evidenceTaskIds)
                    {
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            Debug.LogWarning($"[Validate] {path} {outcome.code} has a blank evidence id in its list.", definition);
                            problems++;
                            continue;
                        }

                        if (id != id.Trim())
                        {
                            // Leading or trailing whitespace is invisible in the
                            // Inspector and compares unequal. Worth its own message.
                            Debug.LogWarning($"[Validate] {path} {outcome.code} evidence id '{id}' has leading or trailing whitespace - it will never match.", definition);
                            problems++;
                        }

                        usable++;
                    }

                    if (usable == 0)
                    {
                        Debug.LogWarning($"[Validate] {path} {outcome.code} has no evidence ids, so it can never be satisfied.", definition);
                        problems++;
                    }

                    if (outcome.requiredEvidenceCount > usable)
                    {
                        Debug.LogWarning($"[Validate] {path} {outcome.code} requires {outcome.requiredEvidenceCount} pieces of evidence but only lists {usable}.", definition);
                        problems++;
                    }
                }
            }

            if (problems == 0)
                Debug.Log($"[Validate] Checked {guids.Length} CompetencyDefinition asset(s) - no problems found.");
            else
                Debug.LogWarning($"[Validate] Finished with {problems} problem(s) across {guids.Length} asset(s). See the warnings above.");
        }

        // ==================================================================
        // Content
        // ==================================================================

        // COC1's ids come straight from the constants, so they cannot be
        // misspelled here - a rename in those files becomes a compile error in
        // this one, which is exactly the safety net string literals don't give.
        private static List<CompetencyDefinition.LearningOutcome> Coc1Outcomes()
        {
            return new List<CompetencyDefinition.LearningOutcome>
            {
                Outcome("LO1", "Assemble and disassemble computer hardware",
                    "Dismantles and reassembles the system unit following the correct sequence, observing correct handling of components.",
                    PCDisassemblySetup.TaskId,
                    PCDisassemblySetup.AssembleTaskId),

                Outcome("LO2", "Prepare installation media",
                    "Obtains the imaging tool and writes a bootable installer to removable media.",
                    RufusSetupManager.TaskId,
                    BootableUSBCreator.TaskId),

                Outcome("LO3", "Install operating system and drivers",
                    "Installs the operating system and all required device drivers.",
                    OSInstallTaskBridge.TaskId,
                    SystemConfigTaskIds.InstallDrivers),

                Outcome("LO4", "Install application software",
                    "Installs the required application software on the configured system.",
                    SystemConfigTaskIds.InstallAppSoftware),

                Outcome("LO5", "Test the system and document the work",
                    "Applies updates and patches, runs diagnostic tests, and completes the service report.",
                    SystemConfigTaskIds.InstallUpdates,
                    SystemConfigTaskIds.TestComputer,
                    SystemConfigTaskIds.DocumentWork),
            };
        }

        // COC2's ids are serialized defaults on COC2TaskSetup rather than
        // constants, so these are literals. If you change them on that component
        // in the scene, change them here too.
        private static List<CompetencyDefinition.LearningOutcome> Coc2Outcomes()
        {
            return new List<CompetencyDefinition.LearningOutcome>
            {
                Outcome("LO1", "Prepare and terminate network cables",
                    "Strips, arranges and crimps UTP cable to the correct standard for both straight-through and crossover.",
                    "crimp_cable"),

                Outcome("LO2", "Test cable terminations",
                    "Verifies continuity and pin order with a cable tester and interprets the result.",
                    "test_cable"),

                Outcome("LO3", "Terminate structured cabling",
                    "Punches down conductors to a patch panel in the correct order and verifies the port.",
                    "punch_patch_panel"),
            };
        }

        // COC3 has no TaskManager tasks at all - these are AssessmentRecorder
        // MANUAL STAGE IDS. They must match the Manual Id fields on the recorder
        // in the COC3 scene character for character, or nothing will ever satisfy.
        private static List<CompetencyDefinition.LearningOutcome> Coc3Outcomes()
        {
            return new List<CompetencyDefinition.LearningOutcome>
            {
                Outcome("LO1", "Set up server addressing and roles",
                    "Assigns a static address, installs the required server roles, and creates the domain.",
                    "coc3_static_ip", "coc3_install_roles", "coc3_create_domain"),

                Outcome("LO2", "Configure users, groups and shared resources",
                    "Creates accounts and groups and applies share permissions appropriately.",
                    "coc3_users_groups", "coc3_shares"),

                Outcome("LO3", "Configure services and document the work",
                    "Configures printing and folder redirection, carries out the test plan, and completes the report.",
                    "coc3_printer", "coc3_redirection", "coc3_test_plan", "coc3_service_report"),
            };
        }

        // One id per TicketData asset's requiredTaskId. Add to this list as you
        // author more tickets, then delete the asset and re-run - or just add the
        // ids in the Inspector.
        private static List<CompetencyDefinition.LearningOutcome> Coc4Outcomes()
        {
            return new List<CompetencyDefinition.LearningOutcome>
            {
                Outcome("LO1", "Diagnose and repair system faults",
                    "Identifies the probable cause from reported symptoms, applies the correct repair, and verifies it.",
                    "ticket_coc1_ram_missing", "ticket_coc1_psu_cable_fix"),
            };
        }

        // ==================================================================
        // Plumbing
        // ==================================================================

        private static CompetencyDefinition.LearningOutcome Outcome(
            string code, string title, string criteria, params string[] evidenceIds)
        {
            return new CompetencyDefinition.LearningOutcome
            {
                code = code,
                title = title,
                criteria = criteria,
                evidenceTaskIds = new List<string>(evidenceIds),
                requiredEvidenceCount = 0   // 0 = every listed id is required
            };
        }

        private static bool Build(string assetName, string moduleId, string unitTitle,
                                  List<CompetencyDefinition.LearningOutcome> outcomes)
        {
            string path = $"{FolderPath}/{assetName}.asset";

            // Check the whole project by MODULE ID, not just this one path.
            //
            // The first version only looked at FolderPath/assetName.asset, which
            // meant an asset for this module living anywhere else - moved to
            // another folder, or created by hand through Create > CircuitCrafter
            // while following the setup guide - was invisible, and Generate
            // happily added a second one. The ledger then picks whichever it
            // meets first, arbitrarily, and a half-filled hand-made asset can
            // shadow the fully populated generated one.
            string existing = FindExistingForModule(moduleId);
            if (existing != null)
            {
                Debug.Log($"[CompetencyDefinitionGenerator] Module '{moduleId}' already has a definition at {existing} - left untouched, nothing created.");
                return false;
            }

            var definition = ScriptableObject.CreateInstance<CompetencyDefinition>();
            definition.moduleId = moduleId;
            definition.unitTitle = unitTitle;
            definition.unitCode = "";      // fill in from the Training Regulations if you want it on reports
            definition.outcomes = outcomes;

            AssetDatabase.CreateAsset(definition, path);
            EditorUtility.SetDirty(definition);
            return true;
        }

        /// <summary>Returns the path of any CompetencyDefinition anywhere in the
        /// project that already claims this module id, or null.</summary>
        private static string FindExistingForModule(string moduleId)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CompetencyDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<CompetencyDefinition>(path);

                if (definition != null && definition.moduleId == moduleId) return path;
            }
            return null;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(FolderPath))
                AssetDatabase.CreateFolder(FolderParent, FolderName);
        }
    }
}