using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// One row in the crimping guide: pin number, a color swatch, and the wire color's
    /// display name (e.g. "1   White/Orange"). Put this on a small prefab  ideally under
    /// a parent with a Vertical Layout Group so 8 of these stack cleanly  and assign the
    /// prefab to CrimpingGuideUI.rowPrefab.
    ///
    /// Setup: build a row with a pin-number label, a swatch Image, and a color-name label,
    /// add this script, and drag each into the fields below.
    /// </summary>
    public class CrimpingGuidePinRow : MonoBehaviour
    {
        [SerializeField] TMP_Text pinNumberText;
        [SerializeField] Image swatch;
        [SerializeField] TMP_Text colorNameText;

        /// <summary>Fills the row in for a real pin/color pair.</summary>
        public void SetData(int pinNumber, WireColor color)
        {
            gameObject.SetActive(true);
            if (pinNumberText != null) pinNumberText.text = pinNumber.ToString();
            if (swatch != null) swatch.color = WireColorPalette.ToUnityColor(color);
            if (colorNameText != null) colorNameText.text = WireColorPalette.ToDisplayName(color);
        }

        /// <summary>Used when no cable type is selected yet  keeps the pin number visible
        /// but blanks the swatch/color so the guide doesn't show stale or misleading info.</summary>
        public void SetEmpty(int pinNumber)
        {
            gameObject.SetActive(true);
            if (pinNumberText != null) pinNumberText.text = pinNumber.ToString();
            if (swatch != null) swatch.color = Color.gray;
            if (colorNameText != null) colorNameText.text = "-";
        }
    }
}