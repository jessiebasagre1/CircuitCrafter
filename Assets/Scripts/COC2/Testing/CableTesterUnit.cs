using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// One half of an RJ45 LAN tester - either the MASTER unit (the one with the buttons) or the
    /// REMOTE terminator. Owns 8 LEDs and nothing else: CableTesterController tells it which pin
    /// to light and in what colour, and this just does it.
    ///
    /// The LED objects can be anything - a small emissive quad/sphere in 3D, or a UI Image on a
    /// world-space Canvas glued to the tester model. This script handles both: for each LED it
    /// tints a Renderer's material if it finds one, tints a UI Graphic if it finds one, and
    /// toggles the object on/off either way. So build the LEDs however suits your model.
    ///
    /// Setup (do this twice - once for the master unit, once for the remote):
    ///   1. Add this to the tester unit's GameObject.
    ///   2. Build 8 small LED objects on the unit, in pin order.
    ///   3. Drag them into pinLeds - ELEMENT 0 IS PIN 1, element 7 is pin 8. Getting this order
    ///      wrong is the single easiest mistake here, and it makes a correct cable look miswired.
    ///   4. Set offColor / okColor / faultColor to taste. Leave hideWhenOff ticked if your LEDs
    ///      are glow objects that should vanish rather than dim.
    ///   5. If your LED material is emissive, tick useEmission so the tint is written to the
    ///      _EmissionColor property as well as _Color.
    /// </summary>
    public class CableTesterUnit : MonoBehaviour
    {
        [Header("LEDs")]
        [Tooltip("The 8 LED objects on this unit. ELEMENT 0 = PIN 1, element 7 = pin 8. " +
                 "Double-check this order against the silkscreen on your tester model.")]
        public GameObject[] pinLeds = new GameObject[8];

        [Header("Colors")]
        public Color offColor = new Color(0.15f, 0.15f, 0.15f);
        [Tooltip("Used for a normal continuity hit.")]
        public Color okColor = new Color(0.2f, 1f, 0.3f);
        [Tooltip("Used for an open or shorted conductor - shown on the MASTER unit when the remote stays dark.")]
        public Color faultColor = new Color(1f, 0.2f, 0.15f);

        [Header("Behaviour")]
        [Tooltip("Tick if the LED objects are glow meshes that should be SetActive(false) when unlit, " +
                 "rather than just dimmed to offColor.")]
        public bool hideWhenOff = true;
        [Tooltip("Tick if your LED material uses emission - the tint is then also written to _EmissionColor.")]
        public bool useEmission = true;

        MaterialPropertyBlock propertyBlock;

        void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            AllOff();
        }

        /// <summary>Turns every LED on this unit off.</summary>
        public void AllOff()
        {
            for (int i = 1; i <= 8; i++) SetPin(i, false, okColor);
        }

        /// <summary>Lights (or clears) a single pin's LED. pinNumber is 1-8; anything else is
        /// ignored, which is what makes "remote pin 0 = open circuit" work with no extra checks
        /// at the call site.</summary>
        public void SetPin(int pinNumber, bool lit)
        {
            SetPin(pinNumber, lit, okColor);
        }

        /// <summary>Lights a single pin's LED in a specific colour.</summary>
        public void SetPin(int pinNumber, bool lit, Color color)
        {
            int idx = pinNumber - 1;
            if (pinLeds == null || idx < 0 || idx >= pinLeds.Length) return;

            GameObject led = pinLeds[idx];
            if (led == null) return;

            if (hideWhenOff)
            {
                led.SetActive(lit);
                if (!lit) return;
            }
            else if (!led.activeSelf)
            {
                led.SetActive(true);
            }

            Color tint = lit ? color : offColor;

            var renderer = led.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor("_Color", tint);
                propertyBlock.SetColor("_BaseColor", tint); // URP/HDRP lit shaders
                if (useEmission) propertyBlock.SetColor("_EmissionColor", lit ? tint : Color.black);
                renderer.SetPropertyBlock(propertyBlock);
            }

            var graphic = led.GetComponent<Graphic>();
            if (graphic != null) graphic.color = tint;
        }

        /// <summary>Lights every pin at once - used for the brief "self test" flash before the
        /// sequence starts, the way a real tester lights all its LEDs when you power it on.</summary>
        public void FlashAll(bool lit)
        {
            for (int i = 1; i <= 8; i++) SetPin(i, lit, okColor);
        }
    }
}
