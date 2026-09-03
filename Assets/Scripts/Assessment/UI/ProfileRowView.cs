using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// One learner in the profile picker.
    ///
    /// PUT THIS ON: a prefab, assigned to ProfilePickerView.rowPrefab.
    ///
    /// Suggested layout: [name] [section] [last played] [Select] [Delete]
    /// Only nameText is required.
    /// </summary>
    public class ProfileRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text sectionText;
        [SerializeField] private TMP_Text lastPlayedText;
        [SerializeField] private Button selectButton;
        [SerializeField] private Button deleteButton;

        [Tooltip("Optional - switched on for the profile that is currently active.")]
        [SerializeField] private GameObject activeMarker;

        public LearnerProfile Profile { get; private set; }

        public void Setup(LearnerProfile profile, bool isActive,
                          Action<LearnerProfile> onSelect, Action<LearnerProfile> onDelete)
        {
            Profile = profile;
            if (profile == null) return;

            if (nameText != null) nameText.text = profile.displayName;
            if (sectionText != null) sectionText.text = profile.section;
            if (lastPlayedText != null) lastPlayedText.text = FormatDate(profile.lastPlayedUtc);
            if (activeMarker != null) activeMarker.SetActive(isActive);

            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => onSelect?.Invoke(profile));

                // Nothing to switch to when it is already the active one.
                selectButton.interactable = !isActive;
            }

            if (deleteButton != null)
            {
                deleteButton.onClick.RemoveAllListeners();
                deleteButton.onClick.AddListener(() => onDelete?.Invoke(profile));
            }
        }

        /// <summary>
        /// Turns the stored round-trip UTC stamp into something a person reads.
        /// Falls back to a dash rather than showing a raw ISO string if parsing
        /// fails - a profile written by an older build, or edited by hand, should
        /// not make the row look broken.
        /// </summary>
        private static string FormatDate(string utc)
        {
            if (string.IsNullOrEmpty(utc)) return "-";

            if (DateTime.TryParse(utc, null,
                                  System.Globalization.DateTimeStyles.RoundtripKind,
                                  out DateTime parsed))
            {
                return parsed.ToLocalTime().ToString("d MMM yyyy, HH:mm");
            }

            return "-";
        }
    }
}
