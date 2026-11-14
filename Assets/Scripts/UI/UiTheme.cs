using UnityEngine;

/// <summary>
/// The Figma "Bayanihan UI" look, in one place: colours, sizes and the design frame every screen is laid out on.
/// Screens are laid out in Figma units (the frames are 852 x 393, a landscape phone) and the canvas scales them to the
/// real screen, so a number here is the number in the Figma file.
/// </summary>
public static class UiTheme
{
    /// <summary>The size of a Figma frame. Every screen is laid out on this and scaled to the device.</summary>
    public static readonly Vector2 Reference = new Vector2(852f, 393f);

    // ------------------------------------------------------------------ palette (read off the Figma frames)
    /// <summary>Buttons, headers, the loading bar: the Bayanihan orange.</summary>
    public static readonly Color Orange      = Hex(0xF0, 0x9A, 0x47);
    public static readonly Color OrangeDeep  = Hex(0xD9, 0x7C, 0x2A);
    /// <summary>The logo's dark outline and pressed buttons.</summary>
    public static readonly Color OrangeBurnt = Hex(0xA8, 0x55, 0x14);
    /// <summary>The Equipment panel.</summary>
    public static readonly Color Brown       = Hex(0x9C, 0x62, 0x2F);
    public static readonly Color BrownDark   = Hex(0x6B, 0x41, 0x1E);
    /// <summary>Item cards, the slots, the Dispatch box.</summary>
    public static readonly Color Cream       = Hex(0xF8, 0xD5, 0xA6);
    public static readonly Color CreamLight  = Hex(0xFD, 0xEA, 0xCF);
    /// <summary>The paper of the Loadout panel and the Mission Stats card.</summary>
    public static readonly Color Paper       = Hex(0xF6, 0xE3, 0xC4);
    public static readonly Color Ink         = Hex(0x2E, 0x1F, 0x14);
    public static readonly Color InkSoft     = Hex(0x6B, 0x55, 0x42);
    /// <summary>A picked item and the Launch button.</summary>
    public static readonly Color Green       = Hex(0x8E, 0xE3, 0x6A);
    public static readonly Color GreenDeep   = Hex(0x4E, 0x9C, 0x2C);
    public static readonly Color White       = Color.white;
    public static readonly Color Placeholder = Hex(0x9A, 0x9A, 0x9A);
    public static readonly Color Error       = Hex(0xD2, 0x2E, 0x26);
    public static readonly Color Ok          = Hex(0x3F, 0x8C, 0x25);
    /// <summary>The blurred dim behind the pause menu and its dialogs.</summary>
    public static readonly Color Scrim       = new Color(0.08f, 0.06f, 0.05f, 0.72f);
    /// <summary>The see-through orange of the Mission Complete board.</summary>
    public static readonly Color Board       = new Color(0.94f, 0.60f, 0.28f, 0.88f);
    /// <summary>HUD chips and cards over the 3D view.</summary>
    public static readonly Color HudPanel    = new Color(0.17f, 0.11f, 0.07f, 0.78f);
    public static readonly Color Warning     = Hex(0xFF, 0x9A, 0x5C);
    public static readonly Color Notice      = Hex(0xFF, 0xE0, 0x6E);

    // ------------------------------------------------------------------ type sizes (Figma px)
    public const int LogoSize = 26;
    public const int TitleSize = 22;      // MISSION COMPLETE!, EQUIPMENT, LOADOUT
    public const int MenuSize = 13;       // HOW TO PLAY, PLAY ...
    public const int ButtonSize = 11;
    public const int BodySize = 8;
    public const int SmallSize = 6;

    public const float Radius = 8f;
    public const float SmallRadius = 4f;

    static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

    /// <summary>A colour with a different alpha.</summary>
    public static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);
}
