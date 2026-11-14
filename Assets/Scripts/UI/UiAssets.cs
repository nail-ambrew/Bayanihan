using UnityEngine;

/// <summary>
/// The artwork and fonts exported from the Figma file. Lives at Resources/UI/UiAssets so every screen finds it without
/// wiring. Every slot is optional: an empty one is drawn in code in the same colours, so a missing export never leaves a
/// hole on the screen.
/// </summary>
[CreateAssetMenu(menuName = "Bayanihan/UI Assets", fileName = "UiAssets")]
public class UiAssets : ScriptableObject
{
    public const string ResourcePath = "UI/UiAssets";

    [Header("Type")]
    [Tooltip("The chunky display face of the logo, headings and buttons. Empty = Unity's built-in font in bold.")]
    public Font headingFont;
    [Tooltip("Body text: descriptions, fields, the report. Empty = Unity's built-in font.")]
    public Font bodyFont;

    [Header("Brand")]
    [Tooltip("BAYANIHAN! DISASTER RESCUE GAME. Empty = the logo is set in the heading font.")]
    public Sprite logo;
    [Tooltip("The still of the flooded street behind the menus. Empty = a dark gradient.")]
    public Sprite background;
    [Tooltip("The arrow at the top left of the sign-in card.")]
    public Sprite backArrow;

    [Header("Items, in the order of RescueItemId: Lifebuoy, FirstAid, Flashlight, Rope, Radio, Raincoat")]
    public Sprite[] itemIcons = new Sprite[6];

    [Header("Headers")]
    public Sprite equipmentIcon;
    public Sprite loadoutIcon;
    public Sprite dispatchIcon;

    [Header("Mission stats")]
    public Sprite statsIcon;
    public Sprite timeIcon;
    public Sprite survivorsIcon;
    public Sprite areasIcon;
    public Sprite suppliesIcon;

    static UiAssets cached;
    static bool looked;

    /// <summary>The asset in Resources, or an empty one (everything drawn) if there is none.</summary>
    public static UiAssets Current
    {
        get
        {
            if (cached != null) return cached;
            if (!looked)
            {
                looked = true;
                cached = Resources.Load<UiAssets>(ResourcePath);
            }
            if (cached == null) cached = CreateInstance<UiAssets>();
            return cached;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; looked = false; }

    public Sprite ItemIcon(RescueItemId id)
    {
        int i = (int)id;
        return itemIcons != null && i >= 0 && i < itemIcons.Length ? itemIcons[i] : null;
    }
}
