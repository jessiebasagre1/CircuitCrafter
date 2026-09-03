using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// Lets a learner say who they are: pick an existing profile, or create one.
    ///
    /// ATTACH THIS TO: a profile panel on your Main Menu canvas. Add it to
    /// MenuNavigator's Screens list and wire a button to
    /// MenuNavigator > Show (GameObject) with this panel in the slot.
    ///
    /// SETUP
    ///   1. A scroll view whose Content has a Vertical Layout Group and a
    ///      Content Size Fitter (vertical = Preferred Size). Assign that Content
    ///      to Row Container.
    ///   2. A ProfileRowView prefab, assigned to Row Prefab.
    ///   3. Two input fields and a Create button for adding a learner.
    ///
    /// ON A SHARED LAB MACHINE this is the screen that matters: without it every
    /// student shares one profile and one set of records. Everything still works
    /// with no picker at all - ProfileManager creates a single "Trainee" on first
    /// run - but the records are then a class average, not a person.
    ///
    /// DELETION IS PERMANENT and takes the learner's ledger and every stored
    /// result with it, so it goes through a confirmation panel. If you leave the
    /// confirm panel unassigned the Delete buttons are hidden entirely rather
    /// than silently becoming one-click destructive.
    /// </summary>
    public class ProfilePickerView : MonoBehaviour
    {
        [Header("List")]
        [Tooltip("The Content object of your scroll view. Rows are spawned in here.")]
        [SerializeField] private RectTransform rowContainer;

        [SerializeField] private ProfileRowView rowPrefab;

        [Tooltip("Shows the currently active learner. Optional.")]
        [SerializeField] private TMP_Text activeProfileText;

        [Header("Create")]
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private TMP_InputField sectionInput;
        [SerializeField] private Button createButton;

        [Tooltip("Shown when Create is pressed with an empty name. Optional.")]
        [SerializeField] private TMP_Text createErrorText;

        [Header("Delete confirmation")]
        [Tooltip("A panel with a Yes and a No button. Leave unassigned to hide deletion entirely.")]
        [SerializeField] private GameObject confirmDeletePanel;

        [SerializeField] private TMP_Text confirmDeleteText;
        [SerializeField] private Button confirmDeleteYes;
        [SerializeField] private Button confirmDeleteNo;

        private readonly List<GameObject> spawnedRows = new List<GameObject>();
        private LearnerProfile pendingDelete;

        // ==================================================================

        private void Awake()
        {
            if (createButton != null) createButton.onClick.AddListener(CreateProfile);
            if (confirmDeleteYes != null) confirmDeleteYes.onClick.AddListener(ConfirmDelete);
            if (confirmDeleteNo != null) confirmDeleteNo.onClick.AddListener(CancelDelete);
        }

        private void OnEnable()
        {
            CancelDelete();
            ReportSetup();
            Refresh();

            if (ProfileManager.Instance != null)
                ProfileManager.Instance.OnActiveProfileChanged += HandleProfileChanged;
        }

        private void OnDisable()
        {
            if (ProfileManager.Instance != null)
                ProfileManager.Instance.OnActiveProfileChanged -= HandleProfileChanged;

            // Close any half-finished confirmation on the way out.
            //
            // Harmless when the panel is a child of this screen - it goes inactive
            // with us anyway. It matters if the panel was placed at Canvas level
            // instead: MenuNavigator only switches the objects in its Screens list,
            // and a popup is deliberately not in that list, so navigating away
            // mid-confirmation would strand a modal on top of the main menu with
            // its Yes button still live and still pointing at a learner.
            CancelDelete();
        }

        private void HandleProfileChanged(LearnerProfile profile) => Refresh();

        /// <summary>
        /// Names any unassigned field the moment the screen opens, rather than
        /// leaving each one to fail quietly at the point it is used. Costs one
        /// console line and turns "nothing happens when I click Create" into a
        /// sentence saying which slot is empty.
        /// </summary>
        private void ReportSetup()
        {
            var missing = new List<string>();

            if (rowContainer == null) missing.Add("Row Container");
            if (rowPrefab == null) missing.Add("Row Prefab");
            if (nameInput == null) missing.Add("Name Input");
            if (createButton == null) missing.Add("Create Button");

            if (missing.Count > 0)
            {
                Debug.LogWarning($"[ProfilePickerView] Unassigned field(s): {string.Join(", ", missing)}. " +
                                 "Without Row Container and Row Prefab no learners are listed even when they exist; " +
                                 "without Name Input and Create Button none can be added.", this);
                return;
            }

            Debug.Log("[ProfilePickerView] Ready.", this);
        }

        // ==================================================================

        public void Refresh()
        {
            ClearRows();

            var manager = ProfileManager.Instance;
            if (manager == null)
            {
                if (activeProfileText != null) activeProfileText.text = "Profiles unavailable";
                Debug.LogWarning("[ProfilePickerView] No ProfileManager is running - add the AssessmentManagers prefab to this scene.", this);
                return;
            }

            if (activeProfileText != null)
            {
                activeProfileText.text = manager.ActiveProfile != null
                    ? manager.ActiveProfile.Label
                    : "No learner selected";
            }

            if (rowPrefab == null || rowContainer == null) return;

            string activeId = manager.ActiveProfileId;
            bool canDelete = confirmDeletePanel != null && manager.AllProfiles.Count > 1;

            foreach (var profile in manager.AllProfiles)
            {
                if (profile == null) continue;

                var row = Instantiate(rowPrefab, rowContainer);
                row.Setup(profile,
                          profile.profileId == activeId,
                          SelectProfile,
                          canDelete ? (System.Action<LearnerProfile>)RequestDelete : null);
                spawnedRows.Add(row.gameObject);
            }
        }

        private void SelectProfile(LearnerProfile profile)
        {
            if (profile == null || ProfileManager.Instance == null) return;

            ProfileManager.Instance.SelectProfile(profile.profileId);
            // No Refresh() here - selecting fires OnActiveProfileChanged, which
            // this view is already subscribed to. Calling both would rebuild the
            // list twice for one click.
        }

        /// <summary>
        /// Wired automatically from the Create Button field in Awake.
        ///
        /// Every refusal below logs a reason. The first version returned silently
        /// on three of these four paths, which made "the Create button does
        /// nothing" impossible to tell apart from "the name box is empty",
        /// "the Name Input field was never assigned" and "there is no
        /// ProfileManager in this scene".
        /// </summary>
        private void CreateProfile()
        {
            if (ProfileManager.Instance == null)
            {
                Fail("Profiles aren't available.",
                     "No ProfileManager is running - the AssessmentManagers prefab isn't in this scene.");
                return;
            }

            if (nameInput == null)
            {
                Fail("This screen isn't finished being set up.",
                     "The Name Input field is unassigned on this component, so there is nothing to read a name from. Assign your TMP_InputField to it.");
                return;
            }

            string name = nameInput.text;

            if (string.IsNullOrWhiteSpace(name))
            {
                // A person leaving the box empty, not a setup mistake - so this one
                // stays at Log rather than LogWarning.
                if (createErrorText != null) createErrorText.text = "Enter a name first.";
                else Debug.Log("[ProfilePickerView] Create was pressed with an empty name. Assign Create Error Text to show that on screen instead of only here.", this);
                return;
            }

            if (createErrorText != null) createErrorText.text = "";

            string section = sectionInput != null ? sectionInput.text : "";
            var created = ProfileManager.Instance.CreateProfile(name, section);

            nameInput.text = "";
            if (sectionInput != null) sectionInput.text = "";

            Debug.Log($"[ProfilePickerView] Created learner '{created.Label}' and made them active.", this);

            // CreateProfile makes the new learner active, which fires
            // OnActiveProfileChanged and refreshes the list.
        }

        /// <summary>Puts the reason on screen where there's a label for it, and in
        /// the console either way.</summary>
        private void Fail(string onScreen, string detail)
        {
            if (createErrorText != null) createErrorText.text = onScreen;
            Debug.LogWarning($"[ProfilePickerView] Couldn't create a learner. {detail}", this);
        }

        // ==================================================================
        // Deletion
        // ==================================================================

        private void RequestDelete(LearnerProfile profile)
        {
            if (profile == null || confirmDeletePanel == null) return;

            pendingDelete = profile;

            if (confirmDeleteText != null)
            {
                confirmDeleteText.text =
                    $"Delete {profile.Label}?\n\n" +
                    "This permanently removes their competency record, every saved result, and their saved game. It cannot be undone.";
            }

            confirmDeletePanel.SetActive(true);
        }

        private void ConfirmDelete()
        {
            if (pendingDelete == null || ProfileManager.Instance == null)
            {
                CancelDelete();
                return;
            }

            ProfileManager.Instance.DeleteProfile(pendingDelete.profileId);
            pendingDelete = null;

            if (confirmDeletePanel != null) confirmDeletePanel.SetActive(false);
            Refresh();
        }

        private void CancelDelete()
        {
            pendingDelete = null;
            if (confirmDeletePanel != null) confirmDeletePanel.SetActive(false);
        }

        private void ClearRows()
        {
            foreach (var row in spawnedRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedRows.Clear();
        }
    }
}