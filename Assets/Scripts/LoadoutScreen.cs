using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// "Paghahanda - Pack the Boat": the pre-mission screen where the player picks 4 of the 6 items.
/// The whole UI is built in code, so the only setup is: new empty scene "Loadout" -> add this to an empty GameObject.
/// Pressing LAUNCH stores the pack in LoadoutState and loads the Gameplay scene.
///
/// There is more than one map to play (see MapChoice): the strip under the cards picks one, the briefing and the dispatch
/// panel change to that map's mission, and LAUNCH loads its scene. Nothing picked means the Manila map in the Gameplay scene.
/// </summary>
public class LoadoutScreen : MonoBehaviour
{
    [Header("Flow")]
    public string gameplaySceneName = "Gameplay";

    [Header("Look")]
    [Tooltip("Optional. Leave empty for Unity's built-in font.")]
    public Font font;
    [Tooltip("Optional icons in this order: Lifebuoy, FirstAid, Flashlight, Rope, Radio, Raincoat. Empty = coloured letter placeholder.")]
    public Sprite[] itemIcons;

    [Header("Mission text")]
    [Tooltip("The briefing, the roster summary and dispatch's reports come from the mission asset in Resources/Missions " +
             "when there is one. The fields below are only what is shown if there is not.")]
    public bool useMissionAsset = true;
    [Tooltip("How many people the boat holds, for the line under the briefing.")]
    public int boatCapacity = 4;
    public string title = "Paghahanda · Pack the Boat";
    [TextArea(2, 4)] public string subtitle = "This mission gives you 4 slots and six things worth taking. Pack badly if you like - the report afterwards will tell you exactly what it cost.";
    public string missionTitle = "Pagbaha sa Barangay · The Water Rises";
    [TextArea(2, 5)] public string missionBriefing = "The creek jumped its banks before dawn. Families are on their roofs and in the mango trees, waving at anything that floats.";
    public string missionMeta = "Daytime · Flood · 10 residents on the roster · boat holds 3 at a time";
    [TextArea(2, 8)] public string missionIntel = "";

    // ------------------------------------------------------------------ palette
    static readonly Color Bg        = new Color(0.13f, 0.22f, 0.28f);
    static readonly Color Panel     = new Color(0.20f, 0.30f, 0.37f);
    static readonly Color PanelDark = new Color(0.16f, 0.25f, 0.31f);
    static readonly Color Cream     = new Color(1.00f, 0.96f, 0.86f);
    static readonly Color Ink       = new Color(0.22f, 0.16f, 0.10f);
    static readonly Color InkSoft   = new Color(0.45f, 0.40f, 0.34f);
    static readonly Color Gold      = new Color(1.00f, 0.86f, 0.45f);
    static readonly Color Light     = new Color(0.80f, 0.88f, 0.92f);
    static readonly Color SlotEmpty = new Color(0.29f, 0.40f, 0.47f);
    static readonly Color Green     = new Color(0.42f, 0.76f, 0.14f);
    static readonly Color Badge     = new Color(0.98f, 0.90f, 0.72f);

    class Card
    {
        public RescueItemDef def;
        public Button button;
        public CanvasGroup group;
        public Outline outline;
        public Text packed;
    }

    class Slot
    {
        public Image bg;
        public Text label;
        public Button button;
    }

    class MapButton
    {
        public PlayableMap map;
        public Image bg;
        public Outline outline;
        public Text title, place;
    }

    readonly List<RescueItemId> picked = new List<RescueItemId>();
    readonly List<Card> cards = new List<Card>();
    readonly List<MapButton> mapButtons = new List<MapButton>();
    readonly Slot[] slots = new Slot[RescueItems.Slots];
    Text counter, launchLabel;
    Text briefTitleText, briefBodyText, briefMetaText, dispatchText;
    GameObject dispatchPanel;
    Font uiFont;

    // ------------------------------------------------------------------ lifecycle
    void Start()
    {
        uiFont = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        MapChoice.Select(MapChoice.Selected.id);        // the mission in force is the picked map's, whatever was played last
        TakeMissionText();

        // Remember the previous pack if the player comes back from a mission
        foreach (var id in LoadoutState.Selected) picked.Add(id);

        EnsureEventSystem();
        BuildUI();
        Refresh();
    }

    /// <summary>The briefing is the mission's own, so it can never disagree with who is actually out there.</summary>
    void TakeMissionText()
    {
        if (!useMissionAsset) return;
        var mission = MapChoice.Selected.LoadMission() ?? MissionDefinition.LoadShipped();
        if (mission == null) return;

        missionTitle = mission.title;
        missionBriefing = mission.briefing;
        missionMeta = mission.Meta(boatCapacity);
        missionIntel = mission.Intel();
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    // ------------------------------------------------------------------ actions
    void Toggle(RescueItemId id)
    {
        if (picked.Contains(id)) picked.Remove(id);
        else if (picked.Count < RescueItems.Slots) picked.Add(id);
        Refresh();
    }

    void RemoveAt(int slotIndex)
    {
        if (slotIndex < picked.Count) picked.RemoveAt(slotIndex);
        Refresh();
    }

    void Suggested()
    {
        picked.Clear();
        picked.AddRange(RescueItems.Suggested);
        Refresh();
    }

    void Clear()
    {
        picked.Clear();
        Refresh();
    }

    void Launch()
    {
        LoadoutState.Set(picked);
        MapChoice.Select(MapChoice.Selected.id);
        // The default map is whatever this screen's own field says, as it always was; any other is the scene its map names.
        string scene = MapChoice.IsDefault ? gameplaySceneName : MapChoice.Selected.sceneName;
        SceneManager.LoadScene(scene);
    }

    /// <summary>Picks a map: the mission in force changes with it, and so does everything on this screen that reads the mission.</summary>
    void PickMap(PlayableMap map)
    {
        MapChoice.Select(map.id);
        TakeMissionText();
        RefreshMissionText();
        RefreshMaps();
    }

    // ------------------------------------------------------------------ refresh
    void Refresh()
    {
        bool full = picked.Count >= RescueItems.Slots;

        foreach (var c in cards)
        {
            int idx = picked.IndexOf(c.def.id);
            bool sel = idx >= 0;
            c.outline.enabled = sel;
            c.packed.gameObject.SetActive(sel);
            if (sel) c.packed.text = $"PACKED · SLOT {idx + 1}";
            c.button.interactable = sel || !full;
            c.group.alpha = sel ? 0.7f : (full ? 0.55f : 1f);
        }

        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (i < picked.Count)
            {
                var def = RescueItems.Get(picked[i]);
                s.bg.color = def.color;
                s.label.fontSize = 22;
                s.label.text = def.name;
                s.label.color = Color.white;
            }
            else
            {
                s.bg.color = SlotEmpty;
                s.label.fontSize = 48;
                s.label.text = "+";
                s.label.color = Light;
            }
        }

        RefreshMaps();
        counter.text = $"{picked.Count} / {RescueItems.Slots} slots packed";
        launchLabel.text = picked.Count == 0 ? "ILUNSAD · LAUNCH EMPTY" : "ILUNSAD · LAUNCH  >";
    }

    // ------------------------------------------------------------------ UI construction
    void BuildUI()
    {
        var canvasGO = new GameObject("LoadoutCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasGO.transform;

        // background
        var bg = NewImage("Background", root, Bg);
        Stretch(bg.rectTransform);

        // header
        var t = NewLabel("Title", root, title, 52, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        Place(t.rectTransform, new Vector2(0, 1), new Vector2(60, -30), new Vector2(1180, 70));
        var sub = NewLabel("Subtitle", root, subtitle, 22, Light, TextAnchor.UpperLeft);
        Place(sub.rectTransform, new Vector2(0, 1), new Vector2(60, -105), new Vector2(1180, 70));

        BuildCards(root);
        BuildDispatchPanel(root);
        BuildMapStrip(root);
        BuildBoatPanel(root);
    }

    /// <summary>
    /// What dispatch has told the crew: how many are where, and which items they will want. It sits in the empty
    /// corner of the card grid, because packing is a choice and this is what it is a choice about.
    /// </summary>
    void BuildDispatchPanel(Transform root)
    {
        var panel = NewImage("Dispatch", root, PanelDark);
        Place(panel.rectTransform, new Vector2(0, 1), new Vector2(660, -520), new Vector2(580, 300));
        var head = NewLabel("Head", panel.transform, "Dispatch", 26, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        Place(head.rectTransform, new Vector2(0, 1), new Vector2(18, -12), new Vector2(544, 34));
        dispatchText = NewLabel("Text", panel.transform, missionIntel, 18, Color.white, TextAnchor.UpperLeft);
        Place(dispatchText.rectTransform, new Vector2(0, 1), new Vector2(18, -52), new Vector2(544, 240));
        dispatchPanel = panel.gameObject;
        dispatchPanel.SetActive(!string.IsNullOrWhiteSpace(missionIntel));      // nothing to say, nothing shown
    }

    /// <summary>The strip under the cards where the map is picked: one button for each place the mission can be played.</summary>
    void BuildMapStrip(Transform root)
    {
        if (MapChoice.All.Count < 2) return;            // only one map: nothing to choose

        var head = NewLabel("MapHead", root, "Mapa · Where is the flood?", 26, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        Place(head.rectTransform, new Vector2(0, 1), new Vector2(60, -846), new Vector2(1180, 34));

        float width = (1180f - 24f * (MapChoice.All.Count - 1)) / MapChoice.All.Count;
        for (int i = 0; i < MapChoice.All.Count; i++)
        {
            var map = MapChoice.All[i];
            var rt = NewImage("Map_" + map.id, root, PanelDark);
            Place(rt.rectTransform, new Vector2(0, 1), new Vector2(60 + i * (width + 24f), -888), new Vector2(width, 108));
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.85f, 0.25f);
            outline.effectDistance = new Vector2(5, -5);
            outline.enabled = false;
            AddButton(rt.gameObject, rt, () => PickMap(map));

            var title = NewLabel("Title", rt.transform, map.title, 30, Cream, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(20, -16), new Vector2(width - 40, 40));
            var place = NewLabel("Place", rt.transform, map.place, 20, Light, TextAnchor.UpperLeft);
            Place(place.rectTransform, new Vector2(0, 1), new Vector2(20, -62), new Vector2(width - 40, 34));
            mapButtons.Add(new MapButton { map = map, bg = rt, outline = outline, title = title, place = place });
        }
    }

    void RefreshMaps()
    {
        foreach (var b in mapButtons)
        {
            bool on = b.map == MapChoice.Selected;
            b.outline.enabled = on;
            b.bg.color = on ? Cream : PanelDark;
            b.title.color = on ? Ink : Cream;
            b.place.color = on ? InkSoft : Light;
        }
    }

    /// <summary>The briefing and the dispatch panel say what the picked map's mission says.</summary>
    void RefreshMissionText()
    {
        if (briefTitleText != null) briefTitleText.text = missionTitle;
        if (briefBodyText != null) briefBodyText.text = missionBriefing;
        if (briefMetaText != null) briefMetaText.text = missionMeta;
        if (dispatchText != null) dispatchText.text = missionIntel;
        if (dispatchPanel != null) dispatchPanel.SetActive(!string.IsNullOrWhiteSpace(missionIntel));
    }

    void BuildCards(Transform root)
    {
        var gridRT = NewRect("Cards", root);
        Place(gridRT, new Vector2(0, 1), new Vector2(60, -200), new Vector2(1180, 640));
        var grid = gridRT.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280, 300);
        grid.spacing = new Vector2(20, 20);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        for (int i = 0; i < RescueItems.All.Length; i++)
            cards.Add(BuildCard(gridRT, RescueItems.All[i], i));
    }

    Card BuildCard(Transform parent, RescueItemDef def, int index)
    {
        var rt = NewRect("Card_" + def.id, parent);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = Cream;

        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.85f, 0.25f);
        outline.effectDistance = new Vector2(5, -5);
        outline.enabled = false;

        var group = rt.gameObject.AddComponent<CanvasGroup>();
        var id = def.id;
        var button = AddButton(rt.gameObject, bg, () => Toggle(id));

        // icon (sprite if provided, coloured letter otherwise)
        var icon = NewImage("Icon", rt, def.color);
        Place(icon.rectTransform, new Vector2(0, 1), new Vector2(18, -18), new Vector2(64, 64));
        if (itemIcons != null && index < itemIcons.Length && itemIcons[index] != null)
        {
            icon.sprite = itemIcons[index];
            icon.color = Color.white;
            icon.preserveAspect = true;
        }
        else
        {
            var g = NewLabel("Glyph", icon.transform, def.glyph, 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(g.rectTransform);
        }

        var nameL = NewLabel("Name", rt, def.name, 26, Ink, TextAnchor.UpperLeft, FontStyle.Bold);
        Place(nameL.rectTransform, new Vector2(0, 1), new Vector2(96, -18), new Vector2(170, 34));
        var engL = NewLabel("English", rt, def.english, 18, InkSoft, TextAnchor.UpperLeft);
        Place(engL.rectTransform, new Vector2(0, 1), new Vector2(96, -54), new Vector2(170, 28));

        var desc = NewLabel("Description", rt, def.description, 19, Ink, TextAnchor.UpperLeft);
        Place(desc.rectTransform, new Vector2(0, 1), new Vector2(18, -100), new Vector2(244, 140));

        // use badge (Passive / Toggle - F / 20s cooldown - Q)
        var badge = NewImage("Badge", rt, Badge);
        Place(badge.rectTransform, new Vector2(0, 0), new Vector2(18, 16), new Vector2(190, 32));
        var badgeText = NewLabel("Text", badge.transform, def.BadgeText, 16, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(badgeText.rectTransform);

        // REC tag
        if (def.recommended)
        {
            var rec = NewImage("Rec", rt, new Color(0.30f, 0.65f, 0.10f));
            Place(rec.rectTransform, new Vector2(1, 1), new Vector2(-10, -10), new Vector2(50, 24));
            var recText = NewLabel("Text", rec.transform, "REC", 15, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(recText.rectTransform);
        }

        // "packed" marker
        var packed = NewLabel("Packed", rt, "PACKED", 18, new Color(0.15f, 0.55f, 0.10f), TextAnchor.LowerRight, FontStyle.Bold);
        Place(packed.rectTransform, new Vector2(1, 0), new Vector2(-14, 14), new Vector2(150, 32));
        packed.gameObject.SetActive(false);

        return new Card { def = def, button = button, group = group, outline = outline, packed = packed };
    }

    void BuildBoatPanel(Transform root)
    {
        var panel = NewImage("BoatPanel", root, Panel);
        Place(panel.rectTransform, new Vector2(1, 1), new Vector2(-40, -40), new Vector2(540, 820));
        var p = panel.transform;

        var head = NewLabel("Head", p, "Ang Bangka · The Boat", 32, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        Place(head.rectTransform, new Vector2(0, 1), new Vector2(24, -20), new Vector2(492, 44));
        var note = NewLabel("Note", p, "Supply slots are physical space on the deck.", 18, Light, TextAnchor.UpperLeft);
        Place(note.rectTransform, new Vector2(0, 1), new Vector2(24, -66), new Vector2(492, 30));

        // 4 slots
        for (int i = 0; i < slots.Length; i++)
        {
            int idx = i;
            var img = NewImage("Slot" + (i + 1), p, SlotEmpty);
            Place(img.rectTransform, new Vector2(0, 1), new Vector2(24 + i * 128, -120), new Vector2(110, 110));
            var btn = AddButton(img.gameObject, img, () => RemoveAt(idx));

            var lbl = NewLabel("Label", img.transform, "+", 48, Light, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(lbl.rectTransform);
            var num = NewLabel("Number", img.transform, (i + 1).ToString(), 18, new Color(0.7f, 0.9f, 1f), TextAnchor.UpperLeft, FontStyle.Bold);
            Place(num.rectTransform, new Vector2(0, 1), new Vector2(8, -6), new Vector2(30, 26));

            slots[i] = new Slot { bg = img, label = lbl, button = btn };
        }

        counter = NewLabel("Counter", p, "", 22, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        Place(counter.rectTransform, new Vector2(0, 1), new Vector2(24, -244), new Vector2(492, 32));

        // mission briefing
        var brief = NewImage("Briefing", p, PanelDark);
        Place(brief.rectTransform, new Vector2(0, 1), new Vector2(24, -300), new Vector2(492, 262));
        var bt = NewLabel("Title", brief.transform, missionTitle, 20, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        Place(bt.rectTransform, new Vector2(0, 1), new Vector2(16, -12), new Vector2(460, 30));
        var bb = NewLabel("Text", brief.transform, missionBriefing, 20, Color.white, TextAnchor.UpperLeft);
        Place(bb.rectTransform, new Vector2(0, 1), new Vector2(16, -46), new Vector2(460, 144));
        var bm = NewLabel("Meta", brief.transform, missionMeta, 18, Light, TextAnchor.UpperLeft);
        Place(bm.rectTransform, new Vector2(0, 1), new Vector2(16, -194), new Vector2(460, 60));
        briefTitleText = bt;
        briefBodyText = bb;
        briefMetaText = bm;

        // buttons
        NewTextButton(p, "SUGGESTED PACK", new Vector2(24, -570), new Vector2(492, 56), SlotEmpty, Suggested, out _);
        NewTextButton(p, "CLEAR", new Vector2(24, -636), new Vector2(492, 56), SlotEmpty, Clear, out _);
        NewTextButton(p, "", new Vector2(24, -706), new Vector2(492, 84), Green, Launch, out launchLabel);
    }

    // ------------------------------------------------------------------ tiny UI helpers
    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Image NewImage(string name, Transform parent, Color color)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    Text NewLabel(string name, Transform parent, string text, int size, Color color,
                  TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
    {
        var t = NewRect(name, parent).gameObject.AddComponent<Text>();
        t.font = uiFont;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        return t;
    }

    Button AddButton(GameObject go, Graphic target, UnityEngine.Events.UnityAction onClick)
    {
        var b = go.AddComponent<Button>();
        b.targetGraphic = target;
        var c = b.colors;
        c.normalColor = new Color(0.94f, 0.94f, 0.94f);
        c.highlightedColor = Color.white;
        c.pressedColor = new Color(0.72f, 0.72f, 0.72f);
        c.disabledColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        b.colors = c;
        b.onClick.AddListener(onClick);
        return b;
    }

    void NewTextButton(Transform parent, string text, Vector2 pos, Vector2 size, Color color,
                       UnityEngine.Events.UnityAction onClick, out Text label)
    {
        var img = NewImage("Button_" + text, parent, color);
        Place(img.rectTransform, new Vector2(0, 1), pos, size);
        AddButton(img.gameObject, img, onClick);
        label = NewLabel("Label", img.transform, text, 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(label.rectTransform);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Anchor + pivot at the same corner, then position/size from that corner.</summary>
    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
