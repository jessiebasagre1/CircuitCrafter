using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// One COC badge: the module's artwork, locked until every outcome in that module is Competent.
    ///
    /// ATTACH THIS TO: each badge object in the badge strip. CompetencyScreenBuilder makes four of
    /// them for you - COC1 to COC4 - and you drop your own sprite into Badge Image afterwards.
    ///
    /// WHAT DECIDES LOCKED OR UNLOCKED
    /// CompetencyLedger.IsModuleCompetent(moduleId). Not "did they finish the module", not "did
    /// they score well" - every learning outcome in that module's definition has to stand at
    /// Competent. That is the same bar the ledger applies everywhere else, so a badge can never
    /// disagree with the outcome list sitting underneath it on the same screen.
    ///
    /// WHY THE LOCKED STATE SHOWS THE SAME ARTWORK
    /// A locked badge is drawn in a muted blue rather than hidden or replaced, because a learner who can see
    /// what they are working toward has a reason to go back to the outcome they skipped. A blank
    /// square teaches nothing. The padlock says it is not earned yet; the shape says what it is.
    /// </summary>
    public class CompetencyBadgeView : MonoBehaviour
    {
        [Header("Which module")]
        [Tooltip("COC1, COC2, COC3 or COC4. Must match the Module Id on that module's " +
                 "CompetencyDefinition asset - it is what the ledger is asked about. Matched " +
                 "trimmed and case-insensitively, so a stray space cannot silently lock a badge " +
                 "forever.")]
        public string moduleId = "COC1";

        [Header("Artwork - drop your sprites here")]
        [Tooltip("The badge artwork. Assign YOUR sprite here; the builder leaves it empty with a " +
                 "placeholder circle so the layout is visible before the art exists.")]
        [SerializeField] private Image badgeImage;

        [Tooltip("Optional ring or plate drawn behind the badge. Assign a sprite to it or leave it " +
                 "as a flat colour.")]
        [SerializeField] private Image frameImage;

        [Tooltip("Optional padlock shown only while the badge is locked. The builder makes one from " +
                 "a text glyph so it works with no art at all - swap in a sprite whenever you like.")]
        [SerializeField] private GameObject lockOverlay;

        [Header("Text")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("The unit title, read from the CompetencyDefinition. Optional.")]
        [SerializeField] private TMP_Text subtitleText;

        [Tooltip("\"Competent\" when earned, otherwise \"3 of 5 outcomes\". Optional but worth " +
                 "having - a locked badge with no number gives the learner nothing to aim at.")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("The BLUE bar under the badge: outcomes earned so far. Optional.")]
        [SerializeField] private Slider progressBar;

        [Tooltip("OPTIONAL. The YELLOW bar behind it: outcomes at Practised OR BETTER - what the " +
                 "learner has worked through in this module.\n\n" +
                 "Same RectTransform as the blue bar, EARLIER in the hierarchy so it draws behind, " +
                 "Background image off. Leave it empty and the badge behaves exactly as before.")]
        [SerializeField] private Slider storyBar;

        [Header("Locked look")]
        [Tooltip("Tint multiplied over the artwork while locked. This DARKENS rather than " +
                 "desaturates - a red badge goes dark red. For true greyscale assign Locked " +
                 "Material below.")]
        [SerializeField] private Color lockedTint = new Color32(0x5A, 0x6A, 0x9E, 0xFF);

        [Tooltip("Tint while unlocked. White means 'draw the sprite exactly as authored', which is " +
                 "almost always what you want.")]
        [SerializeField] private Color unlockedTint = Color.white;

        [Tooltip("OPTIONAL. A UI material that desaturates, for a properly grey locked badge rather " +
                 "than a dark one. CircuitCrafter/UI Grayscale does this. Leave empty and the tint " +
                 "above is used instead - which needs no material and cannot render magenta.")]
        [SerializeField] private Material lockedMaterial;

        [Header("Text colours")]
        [SerializeField] private Color lockedTextColor = new Color32(0x8F, 0xA3, 0xC9, 0xFF);
        [SerializeField] private Color unlockedTextColor = new Color32(0xFF, 0xFF, 0xFF, 0xFF);

        [Tooltip("The status line's colour once the badge is earned. Kept separate so 'Competent' " +
                 "can read as an award without recolouring the title with it.\n\nGold by default, to match " +
                 "the Achievements card and trophy on the main menu - earned things in this game " +
                 "are gold.")]
        [SerializeField] private Color competentAccent = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        // ==================================================================

        /// <summary>True when every outcome in this module stands at Competent.</summary>
        public bool IsUnlocked { get; private set; }

        private void OnEnable()
        {
            Refresh();

            if (CompetencyLedger.Instance != null)
                CompetencyLedger.Instance.OnLedgerChanged += Refresh;

            if (ProfileManager.Instance != null)
                ProfileManager.Instance.OnActiveProfileChanged += HandleProfileChanged;
        }

        private void OnDisable()
        {
            if (CompetencyLedger.Instance != null)
                CompetencyLedger.Instance.OnLedgerChanged -= Refresh;

            if (ProfileManager.Instance != null)
                ProfileManager.Instance.OnActiveProfileChanged -= HandleProfileChanged;
        }

        private void HandleProfileChanged(LearnerProfile profile) => Refresh();

        // ==================================================================

        /// <summary>Re-reads the ledger and redraws. Public so the strip can drive it, and so a
        /// button can be wired to it while you are testing.</summary>
        public void Refresh()
        {
            string id = string.IsNullOrWhiteSpace(moduleId) ? "" : moduleId.Trim();

            CompetencyLedger ledger = CompetencyLedger.Instance;

            int competent = 0;
            int practised = 0;
            int total = 0;
            bool unlocked = false;

            if (ledger != null && id.Length > 0)
            {
                ledger.GetModuleProgress(id, out competent, out practised, out total);
                unlocked = ledger.IsModuleCompetent(id);
            }

            IsUnlocked = unlocked;

            ApplyArtwork(unlocked);
            ApplyText(id, ledger, unlocked, competent, total);

            if (progressBar != null)
            {
                progressBar.maxValue = Mathf.Max(1, total);
                progressBar.value = competent;
            }

            if (storyBar != null)
            {
                storyBar.maxValue = Mathf.Max(1, total);
                storyBar.value = practised;
            }

            if (lockOverlay != null) lockOverlay.SetActive(!unlocked);
        }

        private void ApplyArtwork(bool unlocked)
        {
            if (badgeImage != null)
            {
                badgeImage.color = unlocked ? unlockedTint : lockedTint;

                // Only ever ASSIGNED when locked, and cleared when unlocked. Leaving a greyscale
                // material on an earned badge is the one way this could show the wrong state while
                // every colour beneath it is right.
                badgeImage.material = (!unlocked && lockedMaterial != null) ? lockedMaterial : null;
            }

            if (frameImage != null)
            {
                // The frame is dimmed with the badge but never desaturated - it is furniture, and a
                // grey plate behind a grey badge loses the shape of both.
                Color c = frameImage.color;
                float a = c.a;
                c = unlocked ? new Color(1f, 1f, 1f, a) : new Color(0.58f, 0.64f, 0.86f, a);
                frameImage.color = c;
            }
        }

        private void ApplyText(string id, CompetencyLedger ledger, bool unlocked, int competent, int total)
        {
            if (titleText != null)
            {
                titleText.text = FormatModuleName(id);
                titleText.color = unlocked ? unlockedTextColor : lockedTextColor;
            }

            if (subtitleText != null)
            {
                CompetencyDefinition definition = ledger != null ? ledger.GetDefinition(id) : null;

                subtitleText.text = definition != null && !string.IsNullOrWhiteSpace(definition.unitTitle)
                    ? definition.unitTitle
                    : "";

                subtitleText.color = unlocked ? unlockedTextColor : lockedTextColor;
            }

            if (statusText == null) return;

            if (total <= 0)
            {
                // No definition, or one with no outcomes. Saying "0 of 0" would read as a module the
                // learner has failed rather than one nobody has set up.
                statusText.text = "Not set up";
                statusText.color = lockedTextColor;
                return;
            }

            statusText.text = unlocked ? "COMPETENT" : $"{competent} of {total} outcomes";
            statusText.color = unlocked ? competentAccent : lockedTextColor;
        }

        /// <summary>"COC2" reads as a filename; "COC 2" reads as a name. Anything that is not in
        /// that shape is shown exactly as typed.</summary>
        private static string FormatModuleName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";

            if (id.Length == 4 && id.StartsWith("COC", System.StringComparison.OrdinalIgnoreCase)
                && char.IsDigit(id[3]))
                return "COC " + id[3];

            return id;
        }
    }
}