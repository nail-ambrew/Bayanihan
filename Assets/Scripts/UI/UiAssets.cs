using TMPro;
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

    [Header("Type (TextMesh Pro SDF font assets)")]
    [Tooltip("The chunky display face of the logo, headings and buttons (Lilita One). Empty = TMP's default font in bold.")]
    public TMP_FontAsset headingFont;
    [Tooltip("Body text and the small type: descriptions, fields, the roster, the report (Merge One). Empty = TMP's default font.")]
    public TMP_FontAsset bodyFont;

    [Header("Text effects (material presets, so their shader variants ship in builds)")]
    [Tooltip("The heading font's material with its underlay on: the drop shadow under titles and buttons.")]
    public Material headingShadow;
    [Tooltip("The heading font's material with its outline on: the logo's burnt-orange edge.")]
    public Material headingOutline;
    [Tooltip("The body font's material with its underlay on.")]
    public Material bodyShadow;

    [Header("Music (MusicPlayer)")]
    [Tooltip("The sign-in, main menu and loadout screens.")]
    public AudioClip menuMusic;
    [Tooltip("A mission: any scene with a RescueManager.")]
    public AudioClip gameplayMusic;

    [Header("Game sounds")]
    [Tooltip("The outboard, looped (BoatEngineSound). From \"River boat with outboard engine\" by nomadas, Freesound, CC0.")]
    public AudioClip boatEngine;

    [Header("Interface sounds (UiSound; Kenney Interface Sounds, CC0). Empty = silent.")]
    public AudioClip clickSound;
    public AudioClip hoverSound;
    public AudioClip openSound;
    public AudioClip backSound;
    public AudioClip toggleSound;
    public AudioClip tickSound;
    public AudioClip questionSound;
    public AudioClip errorSound;
    public AudioClip confirmSound;
    public AudioClip packInSound;
    public AudioClip packOutSound;
    public AudioClip pauseOpenSound;
    public AudioClip pauseCloseSound;

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
