using UnityEngine;
using UnityEngine.UI;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// The question mark beside a setting. Clicking it shows that setting's
    /// explanation in the help box at the bottom of the panel.
    ///
    /// ATTACH THIS TO: each "?" button. The builder does it for you.
    ///
    /// A COMPONENT RATHER THAN A UnityEvent WITH A STRING ARGUMENT.
    /// Both work. This one is visible: select the button and the Inspector says
    /// which setting it explains, so a mis-wired help button is obvious at a
    /// glance instead of being buried three clicks into an event list. It also
    /// means the id can be corrected by typing, without touching the event.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SettingsHelpButton : MonoBehaviour
    {
        [Tooltip("Which entry in SettingsHelp this explains, e.g. \"mouseLook\". Must match a key in that file exactly.")]
        [SerializeField] private string settingId;

        [Tooltip("The panel that owns the help box. Found automatically in the parents if left empty.")]
        [SerializeField] private SettingsPanel panel;

        public string SettingId => settingId;

        private void Awake()
        {
            var button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(Show);
        }

        private void Start()
        {
            // Searched in the parents rather than the whole scene: a scene can hold
            // more than one settings panel - a full one in the main menu and a
            // short one in the pause menu - and a help button belongs to the panel
            // it sits inside, not to whichever one happens to be found first.
            if (panel == null) panel = GetComponentInParent<SettingsPanel>();

            if (panel == null)
                Debug.LogWarning($"[SettingsHelpButton] '{name}' is not inside a SettingsPanel, so it has nowhere to show its tip.", this);

            if (!SettingsHelp.Has(settingId))
                Debug.LogWarning($"[SettingsHelpButton] '{name}' asks for '{settingId}', which is not in SettingsHelp. Check the spelling.", this);
        }

        /// <summary>Passes its own RectTransform along, so the panel can open the
        /// bubble beside THIS button rather than in a fixed place.</summary>
        public void Show()
        {
            if (panel == null) panel = GetComponentInParent<SettingsPanel>();
            if (panel != null) panel.ShowHelp(settingId, transform as RectTransform);
        }
    }
}