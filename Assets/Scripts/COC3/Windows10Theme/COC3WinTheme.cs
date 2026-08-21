using TMPro;
using UnityEngine;

/// <summary>
/// The single source of colour, metrics and font for every recreated Windows dialog
/// in COC 3. Change it once and all 35 windows follow.
///
/// CREATE THE ASSET: right-click in the Project window > Create > COC3 > Windows UI Theme.
/// Then drag it into the Theme slot on Tools > COC3 > Dialog Builder.
///
/// FONT: Segoe UI is the real Windows face but is NOT redistributable, so it cannot ship
/// in a build. Import Inter or Open Sans, create a TMP Font Asset from it
/// (Window > TextMeshPro > Font Asset Creator), and drop it in below. Left empty, the
/// builder falls back to your TMP default font - readable, just less Windows-like.
///
/// The palette below is Windows 10 / Server 2019-2022, which is the correct target: the
/// dialogs this simulation recreates are legacy Win32 and are effectively unchanged in
/// Windows 11, and Server 2019/2022 uses the Windows 10 shell.
/// </summary>
[CreateAssetMenu(fileName = "COC3WinTheme", menuName = "COC3/Windows UI Theme")]
public class COC3WinTheme : ScriptableObject
{
    [Header("Font")]
    [Tooltip("Inter or Open Sans, as a TMP Font Asset. Leave empty to use the TMP default.")]
    public TMP_FontAsset uiFont;

    [Tooltip("Base UI font size. Windows dialogs are dense, so this is small on purpose - the window scales up to fill the panel.")]
    public float fontSize = 15f;

    [Tooltip("Title bar font size.")]
    public float titleFontSize = 15f;

    [Header("Window")]
    public Color windowBackground = Hex(0xF0F0F0);
    public Color titleBar = Hex(0xFFFFFF);
    public Color windowBorder = Hex(0xB4B4B4);
    public Color separator = Hex(0xDFDFDF);

    [Header("Text")]
    public Color text = Hex(0x000000);
    public Color disabledText = Hex(0x6D6D6D);
    public Color linkText = Hex(0x0066CC);

    [Header("Fields")]
    public Color fieldBackground = Hex(0xFFFFFF);
    public Color fieldBorder = Hex(0x7A7A7A);
    public Color fieldDisabled = Hex(0xF0F0F0);
    public Color placeholder = Hex(0x9A9A9A);

    [Header("Buttons")]
    public Color buttonFace = Hex(0xE1E1E1);
    public Color buttonBorder = Hex(0xADADAD);
    public Color accent = Hex(0x0078D4);

    [Header("Group boxes / selection")]
    public Color groupBorder = Hex(0xD5DFE5);
    public Color selection = Hex(0xCCE8FF);

    [Header("Console")]
    public Color consoleBackground = Hex(0x0C0C0C);
    public Color consoleText = Hex(0xCCCCCC);

    [Header("Metrics")]
    [Tooltip("Height of the window title bar, in window-space pixels.")]
    public float titleBarHeight = 30f;

    [Tooltip("Padding inside the window body.")]
    public float padding = 14f;

    [Tooltip("Standard control row height.")]
    public float rowHeight = 26f;

    [Tooltip("Vertical gap between rows.")]
    public float rowGap = 8f;

    [Tooltip("Standard button size.")]
    public Vector2 buttonSize = new Vector2(88f, 26f);

    /// <summary>Convenience so the builder never has to null-check a missing asset.</summary>
    public static COC3WinTheme CreateDefault()
    {
        return CreateInstance<COC3WinTheme>();
    }

    private static Color Hex(int rgb)
    {
        return new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
    }
}