using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Display-only mapping from WireColor to an actual swatch Color and a human-readable
    /// label, used by CrimpingGuideUI. Nothing in the real wiring/validation logic
    /// (WirePinSlot, ConnectorEnd, CableTypeSO) depends on this  it's purely presentation.
    /// If your wire prefabs/materials already use specific colors, tweak ToUnityColor()
    /// below to match so the guide's swatches read as "the same wire" as what's in-scene.
    /// </summary>
    public static class WireColorPalette
    {
        public static Color ToUnityColor(WireColor color)
        {
            switch (color)
            {
                case WireColor.WhiteOrange: return new Color(1f, 0.87f, 0.72f);
                case WireColor.Orange: return new Color(1f, 0.55f, 0f);
                case WireColor.WhiteGreen: return new Color(0.82f, 1f, 0.82f);
                case WireColor.Green: return new Color(0f, 0.6f, 0.1f);
                case WireColor.WhiteBlue: return new Color(0.78f, 0.87f, 1f);
                case WireColor.Blue: return new Color(0.1f, 0.3f, 0.9f);
                case WireColor.WhiteBrown: return new Color(0.87f, 0.77f, 0.66f);
                case WireColor.Brown: return new Color(0.45f, 0.28f, 0.1f);
                default:
                    Debug.LogWarning($"[WireColorPalette] No swatch color mapped for {color}  add one.");
                    return Color.magenta; // stands out so a forgotten case is obvious in-scene
            }
        }

        public static string ToDisplayName(WireColor color)
        {
            switch (color)
            {
                case WireColor.WhiteOrange: return "White/Orange";
                case WireColor.Orange: return "Orange";
                case WireColor.WhiteGreen: return "White/Green";
                case WireColor.Green: return "Green";
                case WireColor.WhiteBlue: return "White/Blue";
                case WireColor.Blue: return "Blue";
                case WireColor.WhiteBrown: return "White/Brown";
                case WireColor.Brown: return "Brown";
                default: return color.ToString();
            }
        }
    }
}