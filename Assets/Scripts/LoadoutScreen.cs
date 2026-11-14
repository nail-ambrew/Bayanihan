using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pre-mission screen, as in the Figma "Loadout" frame: the Equipment grid on the left (six items), the Loadout on
/// the right (four numbered slots), Dispatch and Objectives under the slots, and RECOMMENDED PACK / CLEAR / LAUNCH.
/// Tapping an item packs it into the next free slot, tapping it again (or its slot) takes it out.
///
/// There is more than one map to play (see MapChoice): the selector in the Dispatch box picks one, Dispatch and the
/// Objectives change to that map's mission, and LAUNCH loads its scene behind the loading screen.
/// The whole UI is built in code: the only setup is this component on an empty object in the Loadout scene.
/// </summary>
public class LoadoutScreen : MonoBehaviour
{
    [Header("Flow")]
    public string gameplaySceneName = "Gameplay";

    [Header("Mission text")]
    [Tooltip("Dispatch and the Objectives come from the mission asset of the picked map when there is one.")]
    public bool useMissionAsset = true;
    [Tooltip("How many people the boat holds, for the line under the briefing.")]
    public int boatCapacity = 4;
    public string missionTitle = "Pagbaha sa Barangay · The Water Rises";
    [TextArea(2, 5)] public string missionBriefing = "You need to reach the affected areas and help those in need. Prioritize safety and efficient rescue.";
    public string missionMeta = "Daytime · Flood";

    class Card
    {
        public RescueItemDef def;
        public Image back;
        public Outline rim;
        public Text name;
        public Text sub;
        public Text desc;
    }

    class Slot
    {
        public Image back;
        public RectTransform iconHolder;
    }

    class MapButton
    {
        public PlayableMap map;
        public Image back;
        public Outline outline;
        public Text label;
    }

    readonly List<RescueItemId> picked = new List<RescueItemId>();
    readonly List<Card> cards = new List<Card>();
    readonly List<MapButton> mapButtons = new List<MapButton>();
    readonly Slot[] slots = new Slot[RescueItems.Slots];
    MissionDefinition mission;
    Text titleText, briefText, metaText, objectivesText, launchLabel;
    RectTransform safe;

    // ------------------------------------------------------------------ lifecycle
    void Start()
    {
        MapChoice.Select(MapChoice.Selected.id);        // the mission in force is the picked map's, whatever was played last
        TakeMission();

        // Remember the previous pack if the player comes back from a mission
        foreach (var id in LoadoutState.Selected) picked.Add(id);

        UiKit.EnsureEventSystem();
        BuildUI();
        RefreshMissionText();
        Refresh();
    }

    void Update()
    {
        if (InputHelper.BackPressed() && !SceneFlow.IsLoading) Back();
    }

    void TakeMission()
    {
        mission = useMissionAsset ? (MapChoice.Selected.LoadMission() ?? MissionDefinition.LoadShipped()) : null;
        if (mission == null) return;
        missionTitle = mission.title;
        missionBriefing = mission.briefing;
        missionMeta = mission.Meta(boatCapacity);
    }

    /// <summary>
    /// What the crew has to do, from the mission itself so it cannot disagree with the level: who is out there and
    /// where, what to pack for them, the evacuation centre, the hazards and the clock.
    /// </summary>
    public static string Objectives(MissionDefinition m)
    {
        if (m == null)
            return "• Rescue stranded residents on rooftops and upper floors.\n" +
                   "• Move the residents to the nearest evacuation center.\n" +
                   "• Avoid hazards.\n" +
                   "• Complete the mission within the time limit.";

        var lines = new List<string>();
        var where = new List<string>();
        Add(where, m.Count(Situation.OpenWater), "in the water");
        Add(where, m.Count(Situation.Clinging), "clinging to posts");
        Add(where, m.Count(Situation.Rooftop), "on rooftops");
        Add(where, m.Count(Situation.Trapped), "behind debris");
        lines.Add("Rescue all " + m.residents.Count + " residents" + (where.Count > 0 ? ": " + string.Join(", ", where) : "") + ".");

        var needs = new List<string>();
        Add(needs, m.CountNeeding(Need.Lifebuoy), "need a Floatie");
        Add(needs, m.CountNeeding(Need.Rope), "need a Rope");
        Add(needs, m.CountNeeding(Need.FirstAid), "are hurt (First-Aid Kit)");
        if (needs.Count > 0) lines.Add(string.Join(", ", needs) + ".");

        lines.Add("Bring them to the evacuation center and have them registered.");
        lines.Add("Avoid hazards: live wires and floating debris.");
        int mins = Mathf.FloorToInt(m.timeLimit / 60f), secs = Mathf.FloorToInt(m.timeLimit % 60f);
        lines.Add("Complete the mission within " + mins + ":" + secs.ToString("00") + ".");
        return "• " + string.Join("\n• ", lines);
    }

    static void Add(List<string> into, int n, string what)
    {
        if (n > 0) into.Add(n + " " + what);
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

    void Recommended()
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
        if (SceneFlow.IsLoading) return;
        LoadoutState.Set(picked);
        MapChoice.Select(MapChoice.Selected.id);
        // The default map is whatever this screen's own field says, as it always was; any other is the scene its map names.
        string scene = MapChoice.IsDefault ? gameplaySceneName : MapChoice.Selected.sceneName;
        SceneFlow.Load(scene);
    }

    void Back()
    {
        LoadoutState.Set(picked);       // keep the pack for next time
        SceneFlow.Load(SceneFlow.Home);
    }

    /// <summary>Picks a map: the mission in force changes with it, and so does everything on this screen that reads the mission.</summary>
    void PickMap(PlayableMap map)
    {
        MapChoice.Select(map.id);
        TakeMission();
        RefreshMissionText();
    }

    // ------------------------------------------------------------------ refresh
    void Refresh()
    {
        bool full = picked.Count >= RescueItems.Slots;
        foreach (var c in cards)
        {
            bool sel = picked.Contains(c.def.id);
            c.back.color = sel ? UiTheme.Green : UiTheme.CreamLight;
            c.rim.effectColor = sel ? UiTheme.White : UiTheme.CardEdge;
            c.back.canvasRenderer.SetAlpha(!sel && full ? 0.6f : 1f);
        }

        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            foreach (Transform child in s.iconHolder) Destroy(child.gameObject);
            if (i < picked.Count)
            {
                s.back.color = UiTheme.Green;
                var icon = UiKit.ItemIcon(s.iconHolder, RescueItems.Get(picked[i]));
                UiKit.Inset(icon.rectTransform, 3f);
            }
            else s.back.color = UiTheme.Cream;
        }

    }

    void RefreshMaps()
    {
        foreach (var b in mapButtons)
        {
            bool on = b.map == MapChoice.Selected;
            b.outline.enabled = on;
            b.back.color = on ? UiTheme.Orange : UiTheme.CreamLight;
            b.label.color = on ? UiTheme.White : UiTheme.OrangeDeep;
        }
    }

    /// <summary>Dispatch and the Objectives say what the picked map's mission says.</summary>
    void RefreshMissionText()
    {
        if (titleText != null) titleText.text = missionTitle;
        if (briefText != null) briefText.text = missionBriefing;
        if (metaText != null) metaText.text = missionMeta;
        if (objectivesText != null) objectivesText.text = Objectives(mission);
        RefreshMaps();
    }

    // ------------------------------------------------------------------ building
    // Figma positions (852 x 393 frame).
    const float LeftX = 61f, LeftW = 403f, RightX = 493f, RightW = 298f, HeaderY = 46f, HeaderH = 34f, PanelY = 92f;

    void BuildUI()
    {
        safe = UiKit.NewCanvas("LoadoutCanvas", 0, out var canvas);
        UiKit.Background(canvas.transform);
        var dim = UiKit.Image("Dim", canvas.transform, new Color(0, 0, 0, 0.18f));
        UiKit.Stretch(dim.rectTransform);
        dim.transform.SetSiblingIndex(1);

        // Back to the main menu (Esc or Android back does the same).
        UiKit.BackButton(safe, UiKit.TopLeft, new Vector2(14, -14), Back, 22f);

        BuildEquipment(safe);
        BuildLoadout(safe);
    }

    RectTransform Header(Transform parent, string text, Sprite icon, float x, float width)
    {
        var bar = UiKit.Panel("Header_" + text, parent, UiTheme.Orange, UiTheme.RadiusCard);
        UiKit.Place(bar.rectTransform, UiKit.TopLeft, new Vector2(x, -HeaderY), new Vector2(width, HeaderH));
        var label = UiKit.Label("Label", bar.transform, text, UiTheme.TitleSize - 4, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
        UiKit.Stretch(label.rectTransform);
        label.gameObject.AddComponent<Shadow>().effectColor = UiTheme.TitleShadow;
        if (icon != null)
        {
            // icon to the left of the word, as in Figma
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.offsetMin = new Vector2(26, 0);
            UiKit.OptionalIcon(bar.transform, icon, UiKit.Center, new Vector2(-label.preferredWidth * 0.5f - 6f, 0), new Vector2(22, 22));
        }
        return bar.rectTransform;
    }

    void BuildEquipment(Transform root)
    {
        Header(root, "EQUIPMENT", UiAssets.Current.equipmentIcon, LeftX, LeftW);

        var panel = UiKit.Panel("Equipment", root, UiTheme.Brown, UiTheme.RadiusPanel);
        UiKit.Place(panel.rectTransform, UiKit.TopLeft, new Vector2(LeftX, -PanelY), new Vector2(LeftW, 252));
        UiKit.Edge(panel);

        var grid = UiKit.Rect("Grid", panel.transform);
        UiKit.Inset(grid, 12, 12, 12, 12);
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(117, 108);
        g.spacing = new Vector2(10, 12);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 3;
        g.childAlignment = TextAnchor.MiddleCenter;

        foreach (var id in RescueItems.DisplayOrder) cards.Add(BuildCard(grid, RescueItems.Get(id)));
    }

    Card BuildCard(Transform parent, RescueItemDef def)
    {
        var back = UiKit.Panel("Card_" + def.id, parent, UiTheme.CreamLight, UiTheme.RadiusCard);
        var rim = UiKit.Edge(back);
        var id = def.id;
        var button = back.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        UiKit.PressFeedback(button);
        button.onClick.AddListener(() => Toggle(id));

        var iconRoot = UiKit.Rect("IconArea", back.transform);
        UiKit.Place(iconRoot, UiKit.Top, new Vector2(0, -6), new Vector2(64, 50));
        var icon = UiKit.ItemIcon(iconRoot, def);
        UiKit.Stretch(icon.rectTransform);

        var name = UiKit.Label("Name", back.transform, def.name, UiTheme.BodySize + 1, UiTheme.Ink, TextAnchor.LowerLeft, heading: true);
        UiKit.Place(name.rectTransform, UiKit.TopLeft, new Vector2(7, -58), new Vector2(103, 12));
        var sub = UiKit.Label("Filipino", back.transform, def.filipino != def.name ? def.filipino : "", UiTheme.SmallSize, UiTheme.InkSoft, TextAnchor.LowerRight);
        UiKit.Place(sub.rectTransform, UiKit.TopRight, new Vector2(-7, -58), new Vector2(60, 12));
        var desc = UiKit.Label("Description", back.transform, def.description, UiTheme.SmallSize, UiTheme.Ink, TextAnchor.UpperLeft);
        UiKit.Place(desc.rectTransform, UiKit.TopLeft, new Vector2(7, -71), new Vector2(103, 26));
        desc.Shrink(4);

        // How it is used, and the recommendation, in the corners.
        if (def.use != ItemUse.Passive)
        {
            var badge = UiKit.Label("Badge", back.transform, def.BadgeText, UiTheme.SmallSize - 1, UiTheme.OrangeDeep, TextAnchor.LowerLeft, heading: true);
            UiKit.Place(badge.rectTransform, UiKit.BottomLeft, new Vector2(7, 3), new Vector2(103, 9));
        }
        if (def.recommended)
        {
            var rec = UiKit.Panel("Rec", back.transform, UiTheme.GreenDeep, UiTheme.RadiusSmall);
            UiKit.Place(rec.rectTransform, UiKit.TopRight, new Vector2(-5, -5), new Vector2(24, 10));
            var recText = UiKit.Label("Text", rec.transform, "REC", UiTheme.SmallSize - 1, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
            UiKit.Stretch(recText.rectTransform);
        }

        return new Card { def = def, back = back, rim = rim, name = name, sub = sub, desc = desc };
    }

    void BuildLoadout(Transform root)
    {
        Header(root, "LOADOUT", UiAssets.Current.loadoutIcon, RightX, RightW);

        var paper = UiKit.Panel("Loadout", root, UiTheme.Paper, UiTheme.RadiusPanel);
        UiKit.Place(paper.rectTransform, UiKit.TopLeft, new Vector2(RightX, -PanelY), new Vector2(RightW, 214));
        UiKit.Edge(paper);
        var p = paper.transform;

        // Four numbered slots.
        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            var num = UiKit.Label("Number" + (i + 1), p, (i + 1).ToString(), UiTheme.SmallSize + 1, UiTheme.Ink, TextAnchor.UpperLeft, heading: true);
            UiKit.Place(num.rectTransform, UiKit.TopLeft, new Vector2(12 + i * 70, -8), new Vector2(12, 10));
            var box = UiKit.Panel("Slot" + (i + 1), p, UiTheme.Cream, UiTheme.RadiusSmall);
            UiKit.Place(box.rectTransform, UiKit.TopLeft, new Vector2(18 + i * 70, -14), new Vector2(56, 56));
            UiKit.Edge(box, UiTheme.White);
            var slotButton = box.gameObject.AddComponent<Button>();
            UiKit.PressFeedback(slotButton);
            slotButton.onClick.AddListener(() => RemoveAt(index));
            var holder = UiKit.Rect("Icon", box.transform);
            UiKit.Stretch(holder);
            slots[i] = new Slot { back = box, iconHolder = holder };
        }

        // Dispatch: where, what, and the map.
        var dispatch = UiKit.Panel("Dispatch", p, UiTheme.Cream, UiTheme.RadiusPanel);
        UiKit.Place(dispatch.rectTransform, UiKit.TopLeft, new Vector2(14, -80), new Vector2(RightW - 28, 124));
        var d = dispatch.transform;

        var pinArt = UiAssets.Current.dispatchIcon;
        if (pinArt != null) UiKit.OptionalIcon(d, pinArt, UiKit.TopLeft, new Vector2(6, -5), new Vector2(16, 18));
        else
        {
            var pin = UiKit.Image("Pin", d, UiTheme.BrownDark, UiKit.Circle());
            UiKit.Place(pin.rectTransform, UiKit.TopLeft, new Vector2(8, -7), new Vector2(12, 12));
            var dot = UiKit.Image("Dot", pin.transform, UiTheme.Cream, UiKit.Circle());
            UiKit.Place(dot.rectTransform, UiKit.Center, Vector2.zero, new Vector2(5, 5));
        }
        var head = UiKit.Label("Head", d, "DISPATCH", UiTheme.MenuSize - 1, UiTheme.Ink, TextAnchor.MiddleLeft, heading: true);
        UiKit.Place(head.rectTransform, UiKit.TopLeft, new Vector2(25, -4), new Vector2(90, 18));

        BuildMapSelector(d);

        titleText = UiKit.Label("Title", d, "", UiTheme.SmallSize + 1, UiTheme.OrangeBurnt, TextAnchor.UpperLeft, heading: true);
        UiKit.Place(titleText.rectTransform, UiKit.TopLeft, new Vector2(25, -21), new Vector2(240, 10));
        titleText.Shrink(5);
        briefText = UiKit.Label("Text", d, "", UiTheme.SmallSize, UiTheme.Ink, TextAnchor.UpperLeft);
        UiKit.Place(briefText.rectTransform, UiKit.TopLeft, new Vector2(25, -31), new Vector2(240, 22));
        briefText.Shrink(4);
        metaText = UiKit.Label("Meta", d, "", UiTheme.SmallSize - 1, UiTheme.InkSoft, TextAnchor.UpperLeft);
        UiKit.Place(metaText.rectTransform, UiKit.TopLeft, new Vector2(25, -53), new Vector2(240, 8));
        metaText.Shrink(4);

        var line = UiKit.Image("Line", d, UiTheme.Rule);
        UiKit.Place(line.rectTransform, UiKit.TopLeft, new Vector2(8, -63), new Vector2(RightW - 44, UiTheme.RuleThickness));
        var objHead = UiKit.Label("ObjectivesHead", d, "OBJECTIVES", UiTheme.BodySize, UiTheme.Ink, TextAnchor.UpperLeft, heading: true);
        UiKit.Place(objHead.rectTransform, UiKit.TopLeft, new Vector2(8, -66), new Vector2(120, 11));
        objectivesText = UiKit.Label("Objectives", d, "", UiTheme.SmallSize, UiTheme.Ink, TextAnchor.UpperLeft);
        UiKit.Place(objectivesText.rectTransform, UiKit.TopLeft, new Vector2(8, -78), new Vector2(RightW - 44, 44));
        objectivesText.Shrink(4);

        // RECOMMENDED PACK · CLEAR · LAUNCH
        float bw = 94f, by = -(PanelY + 214 + 14);
        var rec = UiKit.Button(root, "RECOMMENDED\nPACK", ButtonStyle.Secondary, Recommended, UiTheme.BodySize);
        rec.name = "Button_RECOMMENDED_PACK";
        UiKit.Place((RectTransform)rec.transform, UiKit.TopLeft, new Vector2(RightX, by), new Vector2(bw, 36));
        var clear = UiKit.Button(root, "CLEAR", ButtonStyle.Secondary, Clear, UiTheme.BodySize + 1);
        UiKit.Place((RectTransform)clear.transform, UiKit.TopLeft, new Vector2(RightX + bw + 8, by), new Vector2(bw, 36));
        var launch = UiKit.Button(root, "LAUNCH", ButtonStyle.Go, Launch, out launchLabel, UiTheme.BodySize + 1);
        UiKit.Place((RectTransform)launch.transform, UiKit.TopLeft, new Vector2(RightX + RightW - bw, by), new Vector2(bw, 36));
    }

    /// <summary>One small button per map, at the top right of Dispatch: where the flood is.</summary>
    void BuildMapSelector(Transform dispatch)
    {
        if (MapChoice.All.Count < 2) return;
        float w = 54f;
        for (int i = 0; i < MapChoice.All.Count; i++)
        {
            var map = MapChoice.All[i];
            string shortName = map.title.Split('·')[0].Trim();
            var b = UiKit.Button(dispatch, shortName, ButtonStyle.Secondary, () => PickMap(map), out var label, UiTheme.SmallSize + 1);
            var rt = (RectTransform)b.transform;
            rt.gameObject.name = "Map_" + map.id;
            UiKit.Place(rt, UiKit.TopRight, new Vector2(-6 - (MapChoice.All.Count - 1 - i) * (w + 4), -5), new Vector2(w, 14));
            var outline = b.gameObject.AddComponent<Outline>();
            outline.effectColor = UiTheme.OrangeBurnt;
            outline.enabled = false;
            mapButtons.Add(new MapButton { map = map, back = (Image)b.targetGraphic, outline = outline, label = label });
        }
    }
}
