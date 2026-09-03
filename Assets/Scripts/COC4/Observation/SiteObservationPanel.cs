using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The "examine the machine" screen: a list of things the learner can check,
    /// and a log of what they found.
    ///
    /// ATTACH THIS TO: an observation panel in each repair-site scene.
    ///
    /// This is the screen that replaces being handed symptomText. Nothing here
    /// tells the learner what is wrong - each action reports only what it would
    /// actually reveal, and it is their job to put them together.
    ///
    /// DOES NOT AUTO-OPEN, deliberately. The existing diagnostic panel pops up on
    /// scene load, which is what makes the symptom feel handed over. This one
    /// waits to be opened, so walking up to the machine and choosing to look at
    /// it is the learner's first decision.
    /// </summary>
    public class SiteObservationPanel : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("Every observation set that could apply at this site - one per ticket that sends the player here. The matching one is picked automatically by ticket id.")]
        [SerializeField] private List<TicketObservationSet> observationSets = new List<TicketObservationSet>();

        [Header("References")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("One button per possible observation. Spare buttons are hidden automatically, so add a few more than your longest set needs.")]
        [SerializeField] private List<Button> actionButtons = new List<Button>();

        [SerializeField] private List<TMP_Text> actionLabels = new List<TMP_Text>();

        [Tooltip("Where findings are listed as they are made. This is the learner's notes - it should stay on screen.")]
        [SerializeField] private TMP_Text findingsText;

        [Tooltip("Optional - shows e.g. \"3 checks, 00:55 spent\".")]
        [SerializeField] private TMP_Text statusText;

        [SerializeField] private Button closeButton;

        [Header("Assessment (optional)")]
        [Tooltip("Leave blank to record nothing.")]
        [SerializeField] private string stageId = "";

        /// <summary>Fires after each new observation, with how many have now been
        /// made. The diagnosis panel listens so it can unlock itself.</summary>
        public static event Action<int> OnObservationMade;

        private TicketObservationSet activeSet;
        private bool resolved;
        private readonly List<string> findings = new List<string>();

        // ==================================================================

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            // NOTHING IS HIDDEN HERE, and that is the whole point.
            //
            // Panel Root is normally this same GameObject. Deactivating it during
            // Awake means Unity never calls Start on this component at all - so
            // the lookup that used to live there never ran, activeSet stayed null,
            // and RefreshButtons hid every action button. The panel opened
            // completely empty and nothing said why.
            //
            // That is a sibling of the trap TicketDiagnosticPanelView documents:
            // there, a panel that BEGINS disabled never gets Awake. Here, a panel
            // that DISABLES ITSELF in Awake never gets Start. Hiding moved to
            // Start (below), which still runs because the object is active until
            // the end of it.
        }

        private void Start()
        {
            Resolve();

            // Now it is safe to hide - Start has run, so nothing is waiting on it.
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>
        /// Works out which observation set applies. Called from Start AND from
        /// Open, because Open must not depend on Start having run - if this
        /// component ever ends up on an object that starts inactive, Start never
        /// fires and Open is the only thing that will.
        /// </summary>
        private void Resolve()
        {
            if (resolved) return;
            if (!RepairTicketMode.IsActive) return;   // stay unresolved; try again on Open

            resolved = true;

            ServiceCallState.EnsureTicket(RepairTicketMode.ActiveTicketId);
            activeSet = FindSetForActiveTicket();

            if (activeSet == null)
            {
                Debug.LogWarning($"[SiteObservationPanel] No TicketObservationSet found for ticket '{RepairTicketMode.ActiveTicketId}'. " +
                                 $"There are {observationSets.Count} set(s) assigned. Add this ticket's *_Observations asset to the Observation Sets list.", this);
            }
            else
            {
                Debug.Log($"[SiteObservationPanel] Ready with {activeSet.observations.Length} check(s) for '{RepairTicketMode.ActiveTicketId}'.", this);
            }
        }

        private TicketObservationSet FindSetForActiveTicket()
        {
            foreach (var set in observationSets)
            {
                if (set != null && set.ticketId == RepairTicketMode.ActiveTicketId) return set;
            }
            return null;
        }

        // ==================================================================

        /// <summary>Wire this to the machine's "Examine" interaction, or to a
        /// button. Public so an InteractableObject can call it.</summary>
        public void Open()
        {
            if (!RepairTicketMode.IsActive)
            {
                Debug.LogWarning("[SiteObservationPanel] No active ticket - nothing to examine.", this);
                return;
            }

            // Resolve here too, not only in Start - see Resolve()'s comment.
            Resolve();

            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshButtons();
            RefreshFindings();
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ==================================================================

        private void RefreshButtons()
        {
            var list = activeSet != null ? activeSet.observations : new TicketObservationSet.Observation[0];

            for (int i = 0; i < actionButtons.Count; i++)
            {
                if (actionButtons[i] == null) continue;

                bool has = i < list.Length && list[i] != null;
                actionButtons[i].gameObject.SetActive(has);
                if (!has) continue;

                var observation = list[i];
                bool alreadyDone = ServiceCallState.HasObserved(observation.observationId);

                if (i < actionLabels.Count && actionLabels[i] != null)
                {
                    // The time cost is shown up front. It has to be, or the learner
                    // cannot make the trade-off the mechanic exists to teach.
                    actionLabels[i].text = alreadyDone
                        ? $"{observation.action}  (done)"
                        : $"{observation.action}  ({observation.timeCostSeconds}s)";
                }

                // Left clickable when already done - re-reading a finding is free
                // and should not be blocked, it just does not cost or re-record.
                int captured = i;
                actionButtons[i].onClick.RemoveAllListeners();
                actionButtons[i].onClick.AddListener(() => Observe(captured));
            }
        }

        private void Observe(int index)
        {
            if (activeSet == null || index >= activeSet.observations.Length) return;

            var observation = activeSet.observations[index];
            if (observation == null) return;

            bool isNew = ServiceCallState.RecordObservation(observation.observationId, observation.timeCostSeconds);

            if (isNew)
            {
                findings.Add($"{observation.action}\n    {observation.result}");

                if (!string.IsNullOrEmpty(stageId))
                    CircuitCrafter.Assessment.AssessmentRecorder.Attempt(stageId, true);

                OnObservationMade?.Invoke(ServiceCallState.ObservationCount);
                Debug.Log($"[SiteObservationPanel] Observed '{observation.observationId}' (+{observation.timeCostSeconds}s).", this);
            }

            RefreshButtons();
            RefreshFindings();
        }

        private void RefreshFindings()
        {
            if (findingsText != null)
            {
                if (activeSet == null)
                {
                    // Says so on screen, not only in the Console. Without this the
                    // panel opens with every button hidden and "Nothing checked
                    // yet" underneath - which reads as a broken panel rather than
                    // a missing asset, and there is nothing to act on.
                    findingsText.text =
                        "No checks are set up for this ticket.\n\n" +
                        $"This panel has no TicketObservationSet whose Ticket Id is \"{RepairTicketMode.ActiveTicketId}\". " +
                        "Generate it with CircuitCrafter > Generate COC4 Tickets, then drag the *_Observations asset " +
                        "into this component's Observation Sets list.";
                }
                else
                {
                    findingsText.text = findings.Count == 0
                        ? "Nothing checked yet."
                        : string.Join("\n\n", findings.ToArray());
                }
            }

            if (statusText != null)
            {
                statusText.text = $"{ServiceCallState.ObservationCount} check(s), {ServiceCallState.FormatTime()} spent";
            }
        }

        /// <summary>Everything found so far, for a service report.</summary>
        public string BuildFindingsReport()
        {
            if (findings.Count == 0) return "No observations were recorded.";

            var sb = new StringBuilder();
            foreach (string finding in findings) sb.AppendLine(finding);
            return sb.ToString();
        }
    }
}