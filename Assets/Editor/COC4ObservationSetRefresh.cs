using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CircuitCrafter.Repair;

namespace CircuitCrafter.Repair.EditorTools
{
    /// <summary>
    /// Re-fills the Examine panel's Observation Sets list from everything in the
    /// project, and reports which tickets still have no observations at all.
    ///
    /// MENU: CircuitCrafter > Refresh Observation Sets
    ///
    /// WHY THIS HAS TO EXIST
    /// COC4PanelBuilder fills that list once, when it creates the panel, and then
    /// deliberately never touches an existing panel again - re-running it must not
    /// undo hand-editing. But observation sets keep being created afterwards: a new
    /// ticket, a re-run of the generator, a set authored by hand. The panel's list
    /// is a snapshot of whatever existed on the day it was built, and the only
    /// symptom of it being stale is one warning at runtime, on the one ticket that
    /// happens to be missing:
    ///
    ///   [SiteObservationPanel] No TicketObservationSet found for ticket
    ///   'ticket_coc1_cpu_unseated'. There are 9 set(s) assigned.
    ///
    /// Deleting the panel and rebuilding it works, and costs every bit of styling
    /// and every wired button. This just refills the list.
    ///
    /// IT ALSO ANSWERS THE MORE USEFUL QUESTION. Assigning sets is the easy half;
    /// knowing which tickets have none is the half that actually saves an evening,
    /// because that gap is invisible until a learner walks into that exact ticket
    /// and presses Examine. So it cross-checks every TicketData in the project
    /// against every TicketObservationSet and names the ones with nothing.
    /// </summary>
    public static class COC4ObservationSetRefresh
    {
        [MenuItem("CircuitCrafter/Refresh Observation Sets")]
        public static void Refresh()
        {
            List<TicketObservationSet> sets = LoadAll<TicketObservationSet>();
            List<TicketData> tickets = LoadAll<TicketData>();

            if (sets.Count == 0)
            {
                Debug.LogWarning("[ObservationSetRefresh] There are no TicketObservationSet assets in this project at all. " +
                                 "Run CircuitCrafter > Generate COC4 Tickets first.");
                return;
            }

            // Assign to every Examine panel in the scene, inactive ones included -
            // a panel that starts hidden is the normal case, not the exception.
            var panels = Object.FindObjectsByType<SiteObservationPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (panels.Length == 0)
            {
                Debug.LogWarning("[ObservationSetRefresh] No SiteObservationPanel in the open scene. Open the repair-site scene first.");
            }

            foreach (var panel in panels)
            {
                if (panel == null) continue;

                var so = new SerializedObject(panel);
                var property = so.FindProperty("observationSets");
                if (property == null)
                {
                    Debug.LogWarning($"[ObservationSetRefresh] '{panel.name}' has no Observation Sets field - assign it by hand.", panel);
                    continue;
                }

                property.arraySize = sets.Count;
                for (int i = 0; i < sets.Count; i++)
                    property.GetArrayElementAtIndex(i).objectReferenceValue = sets[i];

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(panel);

                Debug.Log($"[ObservationSetRefresh] '{panel.name}': assigned {sets.Count} observation set(s).", panel);
            }

            if (panels.Length > 0)
                EditorSceneManager.MarkSceneDirty(panels[0].gameObject.scene);

            ReportCoverage(sets, tickets);
        }

        // ==================================================================

        /// <summary>Which tickets have observations and which do not. This is the
        /// part worth reading.</summary>
        private static void ReportCoverage(List<TicketObservationSet> sets, List<TicketData> tickets)
        {
            var covered = new HashSet<string>();
            var duplicates = new List<string>();

            foreach (var set in sets)
            {
                if (set == null || string.IsNullOrEmpty(set.ticketId)) continue;
                if (!covered.Add(set.ticketId)) duplicates.Add(set.ticketId);
            }

            var missing = new List<string>();
            foreach (var ticket in tickets)
            {
                if (ticket == null || string.IsNullOrEmpty(ticket.ticketId)) continue;
                if (!covered.Contains(ticket.ticketId)) missing.Add(ticket.ticketId);
            }

            var orphaned = new List<string>();
            var ticketIds = new HashSet<string>();
            foreach (var ticket in tickets)
                if (ticket != null && !string.IsNullOrEmpty(ticket.ticketId)) ticketIds.Add(ticket.ticketId);
            foreach (string id in covered)
                if (!ticketIds.Contains(id)) orphaned.Add(id);

            Debug.Log($"[ObservationSetRefresh] {tickets.Count} ticket(s), {sets.Count} observation set(s), " +
                      $"{covered.Count} ticket(s) covered.");

            if (missing.Count > 0)
            {
                Debug.LogWarning("[ObservationSetRefresh] These tickets have NO observations. Pressing Examine on any of them " +
                                 "shows an empty panel and the learner cannot gather the evidence the Diagnose gate asks for:\n  " +
                                 string.Join("\n  ", missing) +
                                 "\n\nRe-run CircuitCrafter > Generate COC4 Tickets - it creates missing observation sets without " +
                                 "touching tickets that already exist. If a ticket you wrote by hand is listed, the generator does " +
                                 "not know about it and you will need to author its set yourself.");
            }

            if (duplicates.Count > 0)
            {
                Debug.LogWarning("[ObservationSetRefresh] Two or more observation sets claim the same ticket id. " +
                                 "Only the first found is ever used, so edits to the other one silently do nothing:\n  " +
                                 string.Join("\n  ", duplicates));
            }

            if (orphaned.Count > 0)
            {
                Debug.Log("[ObservationSetRefresh] Observation sets whose ticket id matches no TicketData - harmless, " +
                          "but usually a typo or a ticket that was renamed:\n  " + string.Join("\n  ", orphaned));
            }
        }

        private static List<T> LoadAll<T>() where T : Object
        {
            var found = new List<T>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) found.Add(asset);
            }
            return found;
        }
    }
}
