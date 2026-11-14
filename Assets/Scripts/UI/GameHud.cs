using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Everything the crew reads off the screen during a mission, in the Figma style:
/// <list type="bullet">
/// <item>top left: the clock, how many are safe, how many are aboard, the score; under them the roster;</item>
/// <item>top right: the pause button and the packed items with their state;</item>
/// <item>over the 3D view: a card over each resident in sight (tag, name, distance, needs, condition), a marker for
/// those already found, live wires, the evacuation center, the radio's find, the throw target, the registration desk;</item>
/// <item>bottom centre: what to do next (the boat, the throw, the swimmer, boarding, the power cut);</item>
/// <item>the touch controls on phones, the pause menu, and the Mission Complete screen at the end.</item>
/// </list>
/// Added to the RescueManager's object by the manager. It only reads the game; it never changes it.
/// </summary>
public class GameHud : MonoBehaviour
{
    [Tooltip("Found residents further than this are not marked on screen (they are still on the roster).")]
    public float knownMarkerRange = 90f;
    [Tooltip("Live wires are labelled on screen when the edge of their zone is closer than this (metres).")]
    public float hazardMarkerRange = 70f;

    RescueManager manager;
    Camera cam;
    BoatTools tools;
    BoatRescue throwing;
    SwimRescue swimming;

    Canvas canvas;
    RectTransform safe, markers, hud;
    Text timeValue, savedValue, boatValue, scoreValue;
    Image timeChip;
    RectTransform rosterCard, rosterRows;
    Text rosterHead;
    bool rosterFolded;
    readonly List<RosterRow> rows = new List<RosterRow>();
    RectTransform promptStack;
    readonly Pill[] prompts = new Pill[4];
    Image holdTrack, holdFill;
    RectTransform itemBar;
    readonly List<ItemChip> items = new List<ItemChip>();
    PauseMenu pause;
    MissionCompleteScreen complete;
    TouchControls touch;

    // marker pools
    readonly Pool<ResidentCard> cards = new Pool<ResidentCard>();
    readonly Pool<Chip> chips = new Pool<Chip>();
    TargetFrame frame;

    public static GameHud Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        manager = GetComponent<RescueManager>();
    }

    void OnEnable() => Instance = this;

    void Start()
    {
        UiKit.EnsureEventSystem();
        safe = UiKit.NewCanvas("GameHud", 10, out canvas, centred: false);

        markers = UiKit.Rect("Markers", safe);
        UiKit.Stretch(markers);
        touch = TouchControls.Create(safe);
        hud = UiKit.Rect("Hud", safe);
        UiKit.Stretch(hud);

        BuildTopLeft();
        BuildRoster();
        BuildItemsAndPause();
        BuildPrompts();
        frame = TargetFrame.Create(markers);

        pause = PauseMenu.Create(canvas.transform, manager);
        complete = MissionCompleteScreen.Create(canvas.transform, manager);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void Update()
    {
        if (manager == null || manager.Roster == null || hud == null) return;
        if (cam == null) cam = Camera.main;
        if (tools == null) tools = FindFirstObjectByType<BoatTools>();
        if (throwing == null) throwing = FindFirstObjectByType<BoatRescue>();
        if (swimming == null) swimming = FindFirstObjectByType<SwimRescue>();

        // Esc / Android back: pause, or close what is open.
        if (!manager.GameOver && !SceneFlow.IsLoading && InputHelper.BackPressed()) pause.Toggle();

        bool showHud = !manager.ReportVisible && !GamePause.IsPaused;
        hud.gameObject.SetActive(showHud);
        markers.gameObject.SetActive(showHud);
        if (!showHud) return;

        RefreshTopLeft();
        RefreshRoster();
        RefreshItems();
        RefreshPrompts();
        RefreshMarkers();
    }

    // ------------------------------------------------------------------ top left: clock, safe, aboard, score
    void BuildTopLeft()
    {
        var row = UiKit.Rect("Stats", hud);
        UiKit.Place(row, UiKit.TopLeft, new Vector2(12, -10), new Vector2(330, 26));
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 5;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        timeChip = StatChip(row, "TIME", 72, out timeValue);
        StatChip(row, "SAFE", 66, out savedValue);
        StatChip(row, "BOAT", 62, out boatValue);
        StatChip(row, "SCORE", 84, out scoreValue);
    }

    static Image StatChip(Transform row, string caption, float width, out Text value)
    {
        var chip = UiKit.Panel("Chip_" + caption, row, UiTheme.Orange, UiTheme.RadiusCard);
        chip.raycastTarget = false;     // a look-drag that starts on a chip still turns the camera
        var le = chip.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        chip.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.3f);
        var cap = UiKit.Label("Caption", chip.transform, caption, UiTheme.SmallSize, UiTheme.Fade(UiTheme.White, 0.85f), TextAnchor.UpperLeft, heading: true);
        UiKit.Inset(cap.rectTransform, 7, 2, 4, 14);
        value = UiKit.Label("Value", chip.transform, "", UiTheme.BodySize + 4, UiTheme.White, TextAnchor.LowerLeft, heading: true);
        UiKit.Inset(value.rectTransform, 7, 9, 4, 2);
        return chip;
    }

    void RefreshTopLeft()
    {
        float left = manager.TimeLeft;
        int mins = Mathf.FloorToInt(left / 60f), secs = Mathf.FloorToInt(left % 60f);
        timeValue.text = mins.ToString("00") + ":" + secs.ToString("00");
        bool hurry = left < 60f && !manager.GameOver;
        timeChip.color = hurry ? Color.Lerp(UiTheme.Error, UiTheme.Orange, (Mathf.Sin(Time.unscaledTime * 6f) + 1f) * 0.25f) : UiTheme.Orange;

        savedValue.text = manager.Evacuated + "/" + manager.TotalSurvivors;
        int aboard = manager.boat != null ? manager.boat.Passengers : 0;
        int cap = manager.boat != null ? manager.boat.capacity : 0;
        boatValue.text = aboard + "/" + cap;
        scoreValue.text = manager.Score.ToString("N0");
    }

    // ------------------------------------------------------------------ roster
    class RosterRow
    {
        public Image pip;
        public Text name, status;
        public ResidentRecord record;
    }

    void BuildRoster()
    {
        var rosterBack = UiKit.Panel("Roster", hud, UiTheme.Fade(UiTheme.Paper, 0.9f), UiTheme.RadiusCard);
        rosterBack.raycastTarget = false;     // only its header is a button
        rosterCard = rosterBack.rectTransform;
        UiKit.Place(rosterCard, UiKit.TopLeft, new Vector2(12, -42), new Vector2(170, 20));
        // square underneath, so it sits flush on the card instead of showing the card's corners below its own
        var head = UiKit.Image("Head", rosterCard, UiTheme.OrangeDeep, UiKit.Rounded(UiTheme.RadiusCard, UiTheme.RadiusCard, 0f, 0f));
        head.rectTransform.anchorMin = new Vector2(0, 1);
        head.rectTransform.anchorMax = new Vector2(1, 1);
        head.rectTransform.pivot = new Vector2(0.5f, 1);
        head.rectTransform.offsetMin = new Vector2(0, -18);
        head.rectTransform.offsetMax = Vector2.zero;
        var fold = head.gameObject.AddComponent<Button>();
        fold.onClick.AddListener(() => rosterFolded = !rosterFolded);
        rosterHead = UiKit.Label("Label", head.transform, "ROSTER", UiTheme.BodySize, UiTheme.White, TextAnchor.MiddleLeft, heading: true);
        UiKit.Inset(rosterHead.rectTransform, 7, 0, 7, 0);

        rosterRows = UiKit.Rect("Rows", rosterCard);
        UiKit.Inset(rosterRows, 6, 20, 6, 3);
        var v = rosterRows.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.spacing = 0;

        foreach (var r in manager.Roster.Records)
        {
            var row = UiKit.Rect("Row_" + r.name, rosterRows);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 11;
            var pip = UiKit.Image("Pip", row, UiTheme.Placeholder, UiKit.Circle());
            UiKit.Place(pip.rectTransform, UiKit.Left, new Vector2(1, 0), new Vector2(6, 6));
            var name = UiKit.Label("Name", row, r.name, UiTheme.SmallSize + 1, UiTheme.Ink, TextAnchor.MiddleLeft);
            UiKit.Inset(name.rectTransform, 11, 0, 50, 0);
            var status = UiKit.Label("Status", row, "", UiTheme.SmallSize, UiTheme.InkSoft, TextAnchor.MiddleRight, heading: true);
            UiKit.Inset(status.rectTransform, 100, 0, 0, 0);
            rows.Add(new RosterRow { pip = pip, name = name, status = status, record = r });
        }
        // On a phone the roster starts folded so it does not cover the view; a tap on its header opens it.
        rosterFolded = InputHelper.TouchActive;
    }

    void RefreshRoster()
    {
        var records = manager.Roster.Records;
        int safeCount = manager.Roster.CountWith(ResidentStatus.Registered);
        rosterHead.text = "ROSTER   " + safeCount + " / " + records.Count + " SAFE   " + (rosterFolded ? "+" : "-");

        // Never taller than the room above the joystick and prompts.
        float room = ((RectTransform)hud).rect.height - 42f - 120f;
        float rowH = Mathf.Clamp(room / Mathf.Max(1, records.Count), 0f, 11f);
        bool folded = rosterFolded || rowH < 7f;
        rosterRows.gameObject.SetActive(!folded);
        rosterCard.sizeDelta = new Vector2(170, folded ? 18 : 22 + rowH * records.Count);

        foreach (var row in rows)
        {
            row.name.transform.parent.GetComponent<LayoutElement>().preferredHeight = rowH;
            var r = row.record;
            var s = r.survivor;
            bool done = r.status == ResidentStatus.Registered;
            bool known = s != null && s.Discovered;
            row.pip.color = done ? UiTheme.Ok : known ? TriageStyle.Colour(s.Triage) : UiTheme.Placeholder;
            row.name.color = done ? UiTheme.Fade(UiTheme.Ink, 0.5f) : UiTheme.Ink;
            row.status.text = StatusText(r, known, manager.Desk);
            row.status.color = known && s != null && s.IsCritical && !done ? UiTheme.Error : UiTheme.InkSoft;
        }
    }

    static string StatusText(ResidentRecord r, bool known, RegistrationDesk desk)
    {
        switch (r.status)
        {
            case ResidentStatus.Registered: return "SAFE";
            case ResidentStatus.Ashore:     return desk != null && desk.Current == r ? "REGISTERING" : "AT THE HALL";
            case ResidentStatus.Aboard:     return "ABOARD";
            case ResidentStatus.Held:       return "COMING IN";
            default:
                if (!known) return "?";
                var s = r.survivor;
                return s != null && s.IsCritical ? "CRITICAL" : r.situation == Situation.Rooftop ? "ROOF" :
                       r.situation == Situation.Clinging ? "POST" : r.situation == Situation.Trapped ? "DEBRIS" : "WATER";
        }
    }

    // ------------------------------------------------------------------ top right: pause and items
    class ItemChip
    {
        public RescueItemId id;
        public Text state;
        public Image back;
    }

    void BuildItemsAndPause()
    {
        var pauseBack = UiKit.Image("Pause", hud, UiTheme.Orange, UiKit.Circle());
        UiKit.Place(pauseBack.rectTransform, UiKit.TopRight, new Vector2(-12, -8), new Vector2(30, 30));
        pauseBack.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.3f);
        var bar1 = UiKit.Image("Bar", pauseBack.transform, UiTheme.White, UiKit.Rounded(1f));
        UiKit.Place(bar1.rectTransform, UiKit.Center, new Vector2(-4, 0), new Vector2(4, 12));
        var bar2 = UiKit.Image("Bar", pauseBack.transform, UiTheme.White, UiKit.Rounded(1f));
        UiKit.Place(bar2.rectTransform, UiKit.Center, new Vector2(4, 0), new Vector2(4, 12));
        pauseBack.gameObject.AddComponent<Button>().onClick.AddListener(() => pause.Open());

        itemBar = UiKit.Rect("Items", hud);
        UiKit.Place(itemBar, UiKit.TopRight, new Vector2(-48, -8), new Vector2(200, 30));
        var h = itemBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4;
        h.childAlignment = TextAnchor.UpperRight;
        h.childControlWidth = false;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        h.reverseArrangement = false;

        foreach (var id in LoadoutState.Selected)
        {
            var def = RescueItems.Get(id);
            var back = UiKit.Panel("Item_" + id, itemBar, UiTheme.Fade(UiTheme.CreamLight, 0.92f), UiTheme.RadiusCard);
            back.raycastTarget = false;
            back.rectTransform.sizeDelta = new Vector2(44, 30);
            var iconHolder = UiKit.Rect("IconArea", back.transform);
            UiKit.Place(iconHolder, UiKit.Top, new Vector2(0, -2), new Vector2(18, 16));
            var icon = UiKit.ItemIcon(iconHolder, def);
            UiKit.Stretch(icon.rectTransform);
            var state = UiKit.Label("State", back.transform, "", UiTheme.SmallSize - 1, UiTheme.Ink, TextAnchor.LowerCenter, heading: true);
            UiKit.Inset(state.rectTransform, 1, 18, 1, 1);
            items.Add(new ItemChip { id = id, state = state, back = back });
        }
    }

    void RefreshItems()
    {
        foreach (var c in items)
        {
            var def = RescueItems.Get(c.id);
            string state = def.name.ToUpperInvariant();
            bool active = false;
            if (c.id == RescueItemId.Flashlight && tools != null)
            {
                active = tools.FlashlightOn;
                state = (active ? "ON" : "OFF") + (InputHelper.TouchActive ? "" : " · F");
            }
            else if (c.id == RescueItemId.Radio && tools != null)
            {
                float left = tools.RadioCooldownLeft;
                state = left > 0f ? Mathf.CeilToInt(left) + "s" : "READY" + (InputHelper.TouchActive ? "" : " · Q");
            }
            c.state.text = state;
            c.back.color = active ? UiTheme.Notice : UiTheme.Fade(UiTheme.CreamLight, 0.92f);
        }
    }

    // ------------------------------------------------------------------ prompts
    class Pill
    {
        public Image back;
        public Text text;
    }

    void BuildPrompts()
    {
        promptStack = UiKit.Rect("Prompts", hud);
        UiKit.Place(promptStack, UiKit.Bottom, new Vector2(0, 14), new Vector2(400, 110));

        for (int i = 0; i < prompts.Length; i++)
        {
            var back = UiKit.Panel("Prompt" + i, promptStack, UiTheme.HudPanel, UiTheme.RadiusPanel);
            back.raycastTarget = false;
            back.rectTransform.anchorMin = back.rectTransform.anchorMax = back.rectTransform.pivot = UiKit.Bottom;
            var text = UiKit.Label("Text", back.transform, "", UiTheme.BodySize + 2, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
            UiKit.Inset(text.rectTransform, 10, 3, 10, 3);
            prompts[i] = new Pill { back = back, text = text };
            back.gameObject.SetActive(false);
        }

        holdTrack = UiKit.Panel("Hold", promptStack, UiTheme.HudPanel, UiTheme.RadiusSmall);
        holdTrack.raycastTarget = false;
        UiKit.Place(holdTrack.rectTransform, UiKit.Bottom, Vector2.zero, new Vector2(140, 8));
        holdFill = UiKit.Panel("Fill", holdTrack.transform, UiTheme.Green, 2f);
        holdFill.rectTransform.anchorMin = Vector2.zero;
        holdFill.rectTransform.anchorMax = new Vector2(0, 1);
        holdFill.rectTransform.offsetMin = new Vector2(1, 1);
        holdFill.rectTransform.offsetMax = new Vector2(-1, -1);
        holdTrack.gameObject.SetActive(false);
    }

    void RefreshPrompts()
    {
        int used = 0;
        if (!manager.GameOver)
        {
            // Most urgent nearest the middle of the screen: the power cut, the throw, the swimmer, then the boat.
            string boatSays = manager.BoatPrompt(out var tone);
            if (boatSays != null) Set(used++, boatSays, tone);
            else if (manager.PlayerPrompt != null) Set(used++, manager.PlayerPrompt, PromptTone.Normal);
            if (swimming != null && swimming.Prompt != null && manager.player != null && !manager.player.IsMounted)
                Set(used++, swimming.Prompt, swimming.PromptIsWarning ? PromptTone.Warning : PromptTone.Normal);
            if (throwing != null && throwing.Prompt != null && throwing.Target != null && manager.boat != null && manager.boat.IsPlayerAboard)
                Set(used++, throwing.Prompt, throwing.PromptIsWarning ? PromptTone.Warning : PromptTone.Normal);
            if (tools != null && tools.PowerPrompt != null)
                Set(used++, tools.PowerPrompt, tools.PowerPromptIsWarning ? PromptTone.Warning : PromptTone.Notice);
        }
        for (int i = used; i < prompts.Length; i++) prompts[i].back.gameObject.SetActive(false);

        // Stack them bottom-up, each sized to its text, the hold bar under the boat's line.
        float hold = manager.boat != null && !manager.GameOver ? manager.boat.HoldProgress01 : 0f;
        holdTrack.gameObject.SetActive(hold > 0f);
        holdFill.rectTransform.anchorMax = new Vector2(hold, 1);
        float y = hold > 0f ? 12f : 0f;
        for (int i = 0; i < used; i++)
        {
            var p = prompts[i];
            float w = Mathf.Clamp(p.text.preferredWidth + 22f, 90f, 400f);
            float h = w >= 400f ? 32f : 20f;            // a long line wraps onto two
            p.back.rectTransform.sizeDelta = new Vector2(w, h);
            p.back.rectTransform.anchoredPosition = new Vector2(0, y);
            y += h + 4f;
        }
    }

    void Set(int i, string text, PromptTone tone)
    {
        if (i >= prompts.Length) return;
        var p = prompts[i];
        p.back.gameObject.SetActive(true);
        p.text.text = text;
        p.text.color = tone == PromptTone.Warning ? UiTheme.Warning : tone == PromptTone.Notice ? UiTheme.Notice : UiTheme.White;
    }

    // ------------------------------------------------------------------ over the 3D view
    void RefreshMarkers()
    {
        cards.Begin();
        chips.Begin();
        frame.Hide();
        if (cam == null) { cards.End(); chips.End(); return; }

        if (!manager.GameOver)
        {
            // Those only remembered go down first, so the cards for people in view are never hidden behind them.
            for (int pass = 0; pass < 2; pass++)
                foreach (var record in manager.Roster.Records)
                {
                    var s = record.survivor;
                    if (s == null || s.IsRescued || !s.Discovered) continue;
                    if (s.InView == (pass == 0)) continue;
                    if (s.InView) ResidentMarker(s); else KnownMarker(s);
                }

            Hazards();
            Evacuation();
            RadioFind();
            ThrowTarget();
        }
        Registration();

        cards.End();
        chips.End();
    }

    bool ToLocal(Vector3 world, out Vector2 local, float marginPx = 30f)
    {
        local = default;
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return false;
        if (sp.x < marginPx || sp.x > Screen.width - marginPx || sp.y < marginPx || sp.y > Screen.height - marginPx) return false;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(markers, sp, null, out local);
    }

    /// <summary>A point pinned to the edge of the screen when it is out of view (HudMarker), in marker-space.</summary>
    Vector2 EdgeLocal(Vector3 world, float marginPx)
    {
        Vector2 gui = HudMarker.ScreenPosition(cam, world, marginPx, marginPx);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(markers, new Vector2(gui.x, Screen.height - gui.y), null, out var local);
        return local;
    }

    void ResidentMarker(Survivor s)
    {
        if (!ToLocal(s.SightPoint + Vector3.up * 0.7f, out var at)) return;
        float distance = Vector3.Distance(cam.transform.position, s.transform.position);
        cards.Next(markers, ResidentCard.Create).Show(s, at, distance);
    }

    void KnownMarker(Survivor s)
    {
        float distance = Vector3.Distance(cam.transform.position, s.transform.position);
        if (distance > knownMarkerRange) return;
        if (!ToLocal(s.SightPoint + Vector3.up * 0.7f, out var at)) return;
        chips.Next(markers, Chip.Create).Show(FirstName(s) + " · " + distance.ToString("0") + " m", at,
            UiTheme.Fade(UiTheme.HudPanel, 0.6f), UiTheme.White, TriageStyle.Colour(s.Triage));
    }

    void Hazards()
    {
        foreach (var zone in LiveWireZone.All)
        {
            if (zone == null || !zone.IsLive) continue;
            float away = Mathf.Max(0f, zone.DistanceToEdge(cam.transform.position));
            if (away > hazardMarkerRange) continue;
            Vector3 sp = cam.WorldToScreenPoint(zone.transform.position + Vector3.up * 1.6f);
            if (sp.z <= 0f) continue;
            // Kept on screen: a wire straight ahead at the edge of the view still wants its label.
            sp.x = Mathf.Clamp(sp.x, Screen.width * 0.12f, Screen.width * 0.88f);
            sp.y = Mathf.Clamp(sp.y, Screen.height * 0.15f, Screen.height * 0.85f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(markers, sp, null, out var at);

            string text = zone.CutRequested
                ? "POWER OFF IN " + Mathf.CeilToInt(zone.CutTimeLeft) + " s"
                : "LIVE WIRE · " + (away < 1f ? "HERE" : away.ToString("0") + " m");
            var colour = zone.CutRequested ? UiTheme.Notice : UiTheme.Error;
            chips.Next(markers, Chip.Create).Show(text, at, UiTheme.HudPanel, colour, colour);
        }
    }

    /// <summary>Points the way to the evacuation center once there is somebody aboard to take there.</summary>
    void Evacuation()
    {
        var evac = manager.Evac;
        if (evac == null || manager.boat == null || manager.boat.Passengers <= 0) return;
        Vector3 offset = evac.transform.position - manager.boat.transform.position;
        offset.y = 0f;
        float dist = offset.magnitude;
        if (dist < 10f) return;
        var at = EdgeLocal(evac.transform.position + Vector3.up * 2f, Screen.height * 0.12f);
        chips.Next(markers, Chip.Create).Show("EVAC  " + dist.ToString("0") + " m", at, UiTheme.Green, UiTheme.Ink, UiTheme.GreenDeep);
    }

    void RadioFind()
    {
        var target = tools != null ? tools.PingTarget : null;
        if (target == null) return;
        float dist = Vector3.Distance(tools.transform.position, target.transform.position);
        var at = EdgeLocal(target.transform.position + Vector3.up * 1.5f, Screen.height * 0.1f);
        chips.Next(markers, Chip.Create).Show("RADIO  !  " + dist.ToString("0") + " m", at, UiTheme.Notice, UiTheme.Ink, UiTheme.OrangeDeep);
    }

    void ThrowTarget()
    {
        if (throwing == null || throwing.Target == null || throwing.Prompt == null) return;
        if (manager.boat == null || !manager.boat.IsPlayerAboard) return;
        if (!ToLocal(throwing.Target.SightPoint, out var at, 0f)) return;
        frame.Show(at, throwing.PromptIsWarning ? UiTheme.Warning : UiTheme.Notice);
    }

    /// <summary>Over the resident being written on the list at the hall: who, and how far through.</summary>
    void Registration()
    {
        var desk = manager.Desk;
        if (desk == null || desk.Current == null || desk.Current.landedAs == null) return;
        if (!ToLocal(desk.Current.landedAs.position + Vector3.up * 2.2f, out var at)) return;
        chips.Next(markers, Chip.Create).Show("REGISTERING  " + desk.Current.name, at, UiTheme.HudPanel, UiTheme.White, UiTheme.Notice, desk.Progress01);
    }

    static string FirstName(Survivor s)
    {
        string full = s.DisplayName;
        int space = full.IndexOf(' ');
        return space > 0 ? full.Substring(0, space) : full;
    }

    // ------------------------------------------------------------------ marker widgets
    /// <summary>Reuses marker objects frame to frame instead of making new ones.</summary>
    class Pool<T> where T : Component
    {
        readonly List<T> items = new List<T>();
        int used;
        public void Begin() => used = 0;
        public T Next(RectTransform parent, System.Func<RectTransform, T> make)
        {
            if (used == items.Count) items.Add(make(parent));
            var item = items[used++];
            item.gameObject.SetActive(true);
            return item;
        }
        public void End()
        {
            for (int i = used; i < items.Count; i++) items[i].gameObject.SetActive(false);
        }
    }

    /// <summary>The card over a resident in sight: tag strip, name and distance, needs, condition, where they are.</summary>
    class ResidentCard : MonoBehaviour
    {
        Image strip, bar, barBack;
        Text title, where;
        RectTransform needs;
        readonly List<Image> needIcons = new List<Image>();

        public static ResidentCard Create(RectTransform parent)
        {
            var back = UiKit.Panel("ResidentCard", parent, UiTheme.Fade(UiTheme.Paper, 0.94f), UiTheme.RadiusCard);
            back.raycastTarget = false;
            back.rectTransform.pivot = new Vector2(0.5f, 0f);
            back.rectTransform.sizeDelta = new Vector2(118, 40);
            back.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.3f);
            var c = back.gameObject.AddComponent<ResidentCard>();
            // the triage colour down the left: as wide as the card's corner, so its curve is the card's curve
            c.strip = UiKit.Image("Tag", back.transform, Color.white, UiKit.Rounded(UiTheme.RadiusCard, 0f, 0f, UiTheme.RadiusCard));
            c.strip.rectTransform.anchorMin = Vector2.zero;
            c.strip.rectTransform.anchorMax = new Vector2(0, 1);
            c.strip.rectTransform.pivot = new Vector2(0, 0.5f);
            c.strip.rectTransform.sizeDelta = new Vector2(UiTheme.RadiusCard, 0);
            c.strip.rectTransform.anchoredPosition = Vector2.zero;
            c.title = UiKit.Label("Name", back.transform, "", UiTheme.BodySize, UiTheme.Ink, TextAnchor.MiddleLeft, heading: true);
            UiKit.Inset(c.title.rectTransform, 10, 2, 3, 26);
            c.needs = UiKit.Rect("Needs", back.transform);
            UiKit.Inset(c.needs, 10, 15, 50, 13);
            c.barBack = UiKit.Panel("Condition", back.transform, UiTheme.Fade(UiTheme.Ink, 0.25f), 2f);
            UiKit.Place(c.barBack.rectTransform, UiKit.TopRight, new Vector2(-5, -18), new Vector2(44, 5));
            c.bar = UiKit.Panel("Fill", c.barBack.transform, Color.white, 2f);
            c.bar.rectTransform.anchorMin = Vector2.zero;
            c.bar.rectTransform.anchorMax = new Vector2(1, 1);
            c.bar.rectTransform.offsetMin = c.bar.rectTransform.offsetMax = Vector2.zero;
            c.where = UiKit.Label("Where", back.transform, "", UiTheme.SmallSize, UiTheme.InkSoft, TextAnchor.MiddleLeft);
            UiKit.Inset(c.where.rectTransform, 9, 28, 3, 2);
            return c;
        }

        public void Show(Survivor s, Vector2 at, float distance)
        {
            ((RectTransform)transform).anchoredPosition = at + new Vector2(0, 4);
            var tag = TriageStyle.Colour(s.Triage);
            strip.color = tag;
            title.text = FirstName(s) + " · " + distance.ToString("0") + " m";

            int n = 0;
            if ((s.needs & Need.Lifebuoy) != 0) ShowNeed(n++, RescueItemId.Lifebuoy);
            if ((s.needs & Need.Rope) != 0) ShowNeed(n++, RescueItemId.Rope);
            if (s.NeedsFirstAidKit) ShowNeed(n++, RescueItemId.FirstAid);
            for (int i = n; i < needIcons.Count; i++) needIcons[i].gameObject.SetActive(false);

            float cond = s.Condition != null ? s.Condition.Condition01 : 1f;
            bar.color = tag;
            bar.rectTransform.anchorMax = new Vector2(cond, 1);

            string place = s.situation == Situation.Rooftop ? "on a roof" :
                           s.situation == Situation.Trapped ? "behind debris" :
                           s.situation == Situation.Clinging ? "clinging to a post" : "in the water";
            if (s.IsCritical) place = "CRITICAL · " + place;
            else if (s.RequiresLine && !s.LineDelivered) place += " · line (" + InputHelper.Key("T", "THROW") + ")";
            where.text = place;
            where.color = s.IsCritical ? UiTheme.Error : UiTheme.InkSoft;
        }

        /// <summary>A small icon for something they need; packed, or not packed (crossed out in red).</summary>
        void ShowNeed(int index, RescueItemId item)
        {
            while (needIcons.Count <= index)
            {
                var holder = UiKit.Image("Need", needs, UiTheme.White, UiKit.Rounded(UiTheme.RadiusSmall));
                UiKit.Place(holder.rectTransform, UiKit.Left, new Vector2(needIcons.Count * 14f, 0), new Vector2(12, 12));
                var cross = UiKit.Image("Missing", holder.transform, UiTheme.Error);
                UiKit.Place(cross.rectTransform, UiKit.Center, Vector2.zero, new Vector2(15, 1.6f));
                cross.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
                needIcons.Add(holder);
                needItems.Add((RescueItemId)(-1));
                needCrosses.Add(cross);
            }
            var box = needIcons[index];
            box.gameObject.SetActive(true);
            if (needItems[index] != item)
            {
                var old = box.transform.Find("Icon");
                if (old != null) Destroy(old.gameObject);
                var icon = UiKit.ItemIcon(box.transform, RescueItems.Get(item));
                UiKit.Inset(icon.rectTransform, 1f);
                icon.transform.SetSiblingIndex(0);
                needItems[index] = item;
            }
            bool packed = LoadoutState.Has(item);
            box.color = packed ? UiTheme.White : UiTheme.Fade(UiTheme.Error, 0.35f);
            needCrosses[index].gameObject.SetActive(!packed);
        }

        readonly List<RescueItemId> needItems = new List<RescueItemId>();
        readonly List<Image> needCrosses = new List<Image>();
    }

    /// <summary>A one-line label over the 3D view, with a coloured edge and an optional progress bar.</summary>
    class Chip : MonoBehaviour
    {
        Image back, edge, bar;
        Text text;

        public static Chip Create(RectTransform parent)
        {
            var b = UiKit.Panel("Chip", parent, UiTheme.HudPanel, UiTheme.RadiusCard);
            b.raycastTarget = false;
            b.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var c = b.gameObject.AddComponent<Chip>();
            c.back = b;
            c.edge = UiKit.Image("Edge", b.transform, Color.white, UiKit.Rounded(UiTheme.RadiusCard, 0f, 0f, UiTheme.RadiusCard));
            c.edge.rectTransform.anchorMin = Vector2.zero;
            c.edge.rectTransform.anchorMax = new Vector2(0, 1);
            c.edge.rectTransform.pivot = new Vector2(0, 0.5f);
            c.edge.rectTransform.sizeDelta = new Vector2(UiTheme.RadiusCard, 0);
            c.text = UiKit.Label("Text", b.transform, "", UiTheme.BodySize, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
            UiKit.Inset(c.text.rectTransform, 9, 0, 5, 0);
            c.bar = UiKit.Image("Bar", b.transform, UiTheme.Notice);
            c.bar.rectTransform.anchorMin = new Vector2(0, 0);
            c.bar.rectTransform.anchorMax = new Vector2(0, 0);
            c.bar.rectTransform.pivot = Vector2.zero;
            return c;
        }

        public void Show(string label, Vector2 at, Color backColour, Color textColour, Color edgeColour, float progress = -1f)
        {
            var rt = (RectTransform)transform;
            rt.anchoredPosition = at;
            text.text = label;
            text.color = textColour;
            back.color = backColour;
            edge.color = edgeColour;
            float w = Mathf.Clamp(text.preferredWidth + 16f, 40f, 220f);
            rt.sizeDelta = new Vector2(w, progress >= 0f ? 18f : 15f);
            bar.gameObject.SetActive(progress >= 0f);
            if (progress >= 0f)
            {
                bar.rectTransform.anchoredPosition = new Vector2(4, 1.5f);
                bar.rectTransform.sizeDelta = new Vector2((w - 8f) * Mathf.Clamp01(progress), 2f);
            }
        }
    }

    /// <summary>Corner brackets round the person a throw would go to.</summary>
    class TargetFrame : MonoBehaviour
    {
        readonly Image[] corners = new Image[8];

        public static TargetFrame Create(RectTransform parent)
        {
            var rt = UiKit.Rect("ThrowTarget", parent);
            rt.sizeDelta = new Vector2(26, 26);
            var f = rt.gameObject.AddComponent<TargetFrame>();
            const float t = 1.6f, c = 7f;
            var specs = new (Vector2 anchor, Vector2 size)[]
            {
                (UiKit.TopLeft, new Vector2(c, t)), (UiKit.TopLeft, new Vector2(t, c)),
                (UiKit.TopRight, new Vector2(c, t)), (UiKit.TopRight, new Vector2(t, c)),
                (UiKit.BottomLeft, new Vector2(c, t)), (UiKit.BottomLeft, new Vector2(t, c)),
                (UiKit.BottomRight, new Vector2(c, t)), (UiKit.BottomRight, new Vector2(t, c)),
            };
            for (int i = 0; i < specs.Length; i++)
            {
                f.corners[i] = UiKit.Image("Corner", rt, Color.white);
                UiKit.Place(f.corners[i].rectTransform, specs[i].anchor, Vector2.zero, specs[i].size);
            }
            rt.gameObject.SetActive(false);
            return f;
        }

        public void Show(Vector2 at, Color colour)
        {
            gameObject.SetActive(true);
            ((RectTransform)transform).anchoredPosition = at;
            foreach (var c in corners) c.color = colour;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
