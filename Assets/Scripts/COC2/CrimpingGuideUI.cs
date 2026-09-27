using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Reference-guide panel for ONE connector end: an 8-row list showing the correct
    /// pin-by-pin wire color order for whichever cable type the player picked in
    /// CableTypeSelectionUI  Straight-Through or Crossover. Purely a read-only visual
    /// aid; it never touches the actual wiring/validation logic in
    /// ConnectorEnd/WirePinSlot/CrimpingStationController.
    ///
    /// Put one of these on each of your two guide panels  one set to endNumber = 1
    /// (End A), one set to endNumber = 2 (End B)  so each can be positioned next to its
    /// own connector housing in the crimping view instead of sharing a single combined panel.
    ///
    /// It stays in sync automatically: CrimpingStationController.SetTargetCableType() (called
    /// by CableTypeSelectionUI.Choose()) fires OnTargetCableTypeChanged, which this listens
    /// for and uses to rebuild its list. If targetCableType was already set some other way
    /// (e.g. a default assigned in the Inspector, no CableTypeSelectionUI involved), OnEnable
    /// also refreshes from whatever's currently set, so the guide is never left blank.
    ///
    /// Setup (repeat once per end):
    ///   1. Under your crimping-view Canvas, build a panel (e.g. "CrimpingGuidePanel_EndA")
    ///      with a single list container (e.g. "PinList"), an empty Transform with a
    ///      Vertical Layout Group  this will get 8 rows instantiated into it.
    ///   2. Build a row prefab with CrimpingGuidePinRow on it (pin number text, swatch
    ///      Image, color name text)  the SAME prefab can be reused for both panels  and
    ///      assign it to rowPrefab below.
    ///   3. Assign listParent to the panel's list Transform.
    ///   4. Set endNumber to 1 for the End A panel, 2 for the End B panel.
    ///   5. Optionally assign cableTypeLabel (overall cable name, e.g. "Straight-Through
    ///      (T568B / T568B)") and standardLabel (just this end's standard, e.g. "T568B").
    ///   6. Assign station  the CrimpingStationController for this cable (same station on
    ///      both panels).
    ///   7. Position each panel near its matching connector housing in the crimping view,
    ///      then either parent both under CrimpingViewController.crimpingUI so they show/hide
    ///      with the rest of the crimping UI automatically, or assign the `panel` field and
    ///      wire a "Guide" button to ShowPanel()/HidePanel()/TogglePanel() per panel if you
    ///      want them independently collapsible.
    /// </summary>
    public class CrimpingGuideUI : MonoBehaviour
    {
        [Header("Refs")]
        public CrimpingStationController station;

        [Header("Which End")]
        [Tooltip("Which connector end this panel is a guide for: 1 = End A, 2 = End B. " +
                 "Must match the endNumber on the ConnectorEnd/WirePinSlots you're guiding the player through.")]
        [Range(1, 2)]
        public int endNumber = 1;

        [Header("Panel (optional)")]
        [Tooltip("Leave unassigned if this guide should just always be visible/populated whenever " +
                 "its own GameObject is active (e.g. it's already a child of the crimping view's UI " +
                 "root). Assign a child panel here instead if you want a separate 'Guide' button to " +
                 "show/hide it independently via ShowPanel()/HidePanel()/TogglePanel().")]
        public GameObject panel;

        [Header("Layout")]
        [Tooltip("Row prefab with a CrimpingGuidePinRow component  instantiated 8 times. " +
                 "The same prefab can be shared between the End A and End B panels.")]
        public CrimpingGuidePinRow rowPrefab;
        [Tooltip("Parent Transform (ideally with a Vertical Layout Group) for this end's 8 rows.")]
        public Transform listParent;

        [Header("Labels (optional)")]
        [Tooltip("Overall cable type name, e.g. 'Straight-Through (T568B / T568B)'.")]
        public TMP_Text cableTypeLabel;
        [Tooltip("Just this end's wiring standard, e.g. 'T568B'.")]
        public TMP_Text standardLabel;

        readonly List<CrimpingGuidePinRow> rows = new List<CrimpingGuidePinRow>();

        void Awake()
        {
            if (station != null)
                station.OnTargetCableTypeChanged.AddListener(Refresh);
            else
                Debug.LogWarning("[CrimpingGuideUI] No CrimpingStationController assigned  guide won't stay in sync with the player's cable choice.", this);
        }

        void OnEnable()
        {
            // Covers the case where targetCableType was already set (default in the
            // Inspector, or the player re-opens a station they already picked a type for)
            // before this guide became active/visible.
            if (station != null)
                Refresh(station.targetCableType);
        }

        void OnDestroy()
        {
            if (station != null)
                station.OnTargetCableTypeChanged.RemoveListener(Refresh);
        }

        /// <summary>Rebuilds this end's 8-row list for the given cable type. Safe to call
        /// with null (e.g. nothing picked yet)  the rows just show blank placeholders.</summary>
        public void Refresh(CableTypeSO cableType)
        {
            // GUIDANCE: this chart IS the answer to the crimping task - the colour at every pin. It is
            // a Story Mode reference (GuidanceKind.ReferenceGuides); in an assessment the rows stay
            // blank and the standard is not named, so the candidate works from memory. The cable
            // TYPE is still shown - that is the job's requirement, not the method.
            bool guideAllowed = CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.ReferenceGuides);
            if (!guideAllowed && panel != null && panel != gameObject) panel.SetActive(false);

            BuildList(guideAllowed ? cableType : null);

            if (cableTypeLabel != null)
                cableTypeLabel.text = cableType != null ? cableType.displayName : "No cable type selected";

            if (standardLabel != null)
            {
                WireStandardSO standard = cableType == null || !guideAllowed ? null
                    : (endNumber == 1 ? cableType.endAStandard : cableType.endBStandard);
                standardLabel.text = standard != null ? standard.standard.ToString() : "-";
            }
        }

        void BuildList(CableTypeSO cableType)
        {
            if (listParent == null || rowPrefab == null) return;

            // Reuse rows across refreshes rather than destroy/instantiate every time --
            // keeps things cheap if the player flips between Straight-Through and
            // Crossover a few times before settling on one.
            while (rows.Count < 8)
                rows.Add(Instantiate(rowPrefab, listParent));

            for (int i = rows.Count - 1; i >= 8; i--)
            {
                Destroy(rows[i].gameObject);
                rows.RemoveAt(i);
            }

            for (int pin = 1; pin <= 8; pin++)
            {
                var row = rows[pin - 1];
                if (cableType == null)
                {
                    row.SetEmpty(pin);
                    continue;
                }

                WireColor color = cableType.GetExpectedColorAtPin(endNumber, pin);
                row.SetData(pin, color);
            }
        }

        /// <summary>Wire to a "Guide" button's OnClick if you're using the optional `panel` field.</summary>
        public void ShowPanel()
        {
            if (!CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.ReferenceGuides)) return;
            if (panel != null) panel.SetActive(true);
        }

        /// <summary>Wire to a "Close"/"Back" button's OnClick if you're using the optional `panel` field.</summary>
        public void HidePanel()
        {
            if (panel != null) panel.SetActive(false);
        }

        public void TogglePanel()
        {
            if (panel != null && !panel.activeSelf &&
                !CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.ReferenceGuides)) return;
            if (panel != null) panel.SetActive(!panel.activeSelf);
        }
    }
}