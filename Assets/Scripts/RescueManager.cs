using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks score, timer and win/lose, draws a temporary HUD (OnGUI) and builds the end-of-mission report
/// that tells the player what their pack choices cost them. Put on an empty GameObject called "RescueManager".
/// Replace the OnGUI HUD with a Canvas/TMP one later - the data below is all public.
/// </summary>
public class RescueManager : MonoBehaviour
{
    public static RescueManager Instance { get; private set; }

    [Header("Rules")]
    public float timeLimit = 180f;      // seconds
    [Tooltip("When the last resident is registered, wait this long before the report covers the screen.")]
    public float reportDelay = 1.5f;
    [Tooltip("When the mission ends the desk finishes registering whoever is already ashore. The report waits for " +
             "that, but never longer than this (seconds).")]
    public float maxRegistrationWait = 30f;

    [Header("Scenes")]
    public string loadoutSceneName = "Loadout";

    [Header("References (optional)")]
    public SimpleBoat boat;
    public PlayerRescuer player;

    public int TotalSurvivors { get; private set; }
    public int PickedUp { get; private set; }
    /// <summary>How many are safe: ashore and written on the list at the Barangay Hall.</summary>
    public int Evacuated { get; private set; }
    public int Score { get; private set; }
    public bool GameOver { get; private set; }
    /// <summary>The clock ran out (as opposed to everybody being registered).</summary>
    public bool TimeIsUp { get; private set; }
    /// <summary>The after-action report is on the screen.</summary>
    public bool ReportVisible => reportVisible;
    public string Report { get; private set; }

    /// <summary>The score, broken down. Rebuilt after every registration, and once more when the mission ends.</summary>
    public ScoreSheet Sheet { get; private set; }
    /// <summary>The registration desk at the hall, if the scene has an evacuation centre.</summary>
    public RegistrationDesk Desk { get; private set; }

    /// <summary>Everybody the mission is about, with how each of them is getting on.</summary>
    public Roster Roster { get; private set; }
    /// <summary>Game time when the mission began. Time.time carries on across a retry, so times are taken from here.</summary>
    public float StartedAt { get; private set; }
    /// <summary>Seconds of game time since the mission began.</summary>
    public float Elapsed => Time.time - StartedAt;

    float timeLeft;
    bool reportVisible;
    EvacuationCenter evac;
    Camera cam;
    GUIStyle label, center, big, body, marker;

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
        LoadoutState.ResetUses();   // a retry reloads this scene without going back through the loadout screen
    }

    // Statics do not survive a script reload in Play mode; OnEnable runs again afterwards and Awake does not.
    void OnEnable()
    {
        Instance = this;
    }

    void Start()
    {
        timeLeft = timeLimit;
        StartedAt = Time.time;
        var survivors = FindObjectsByType<Survivor>(FindObjectsSortMode.None);
        TotalSurvivors = survivors.Length;
        Roster = new Roster(survivors);
        if (boat == null) boat = FindFirstObjectByType<SimpleBoat>();
        if (player == null) player = FindFirstObjectByType<PlayerRescuer>();
        evac = FindFirstObjectByType<EvacuationCenter>();
        Desk = evac != null ? evac.Desk : null;
        RefreshScore(false);

        // Tags above the residents the crew can see, and the roster panel.
        if (GetComponent<MissionHud>() == null) gameObject.AddComponent<MissionHud>();
    }

    RescueRules Rules => MissionDefinition.Current.rules;

    void Update()
    {
        if (GameOver)
        {
            if (!reportVisible) return;
            if (InputHelper.RestartPressed())
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            else if (InputHelper.BackPressed() && Application.CanStreamedLevelBeLoaded(loadoutSceneName))
                SceneManager.LoadScene(loadoutSceneName);
            return;
        }

        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            TimeIsUp = true;
            EndGame(0f);
        }
    }

    /// <summary>
    /// Somebody is being brought aboard. Their tag and condition are noted now, as they were on the way in, and so is
    /// whether the most urgent person the crew knew about was the one taken.
    /// </summary>
    public void OnSurvivorPickedUp(Survivor survivor)
    {
        PickedUp++;

        var record = Roster != null ? Roster.Find(survivor) : null;
        if (record == null) return;

        record.status = ResidentStatus.Aboard;
        record.pickedUpAt = Elapsed;
        record.tagWhenPickedUp = survivor.Triage;
        record.criticalWhenPickedUp = survivor.IsCritical;
        record.conditionWhenPickedUp = survivor.Condition != null ? survivor.Condition.Condition01 : 1f;
        record.followedPriority = Roster.WasMostUrgent(record, record.tagWhenPickedUp);
        record.broughtByLine = survivor.ArrivedBy == Survivor.Arrival.Line;
        record.broughtBySwimmer = survivor.ArrivedBy == Survivor.Arrival.Swimmer;
    }

    /// <summary>The boat has unloaded at the hall. They are ashore, not yet safe: each still has to be registered.</summary>
    public void OnDroppedOff(int count, IList<Survivor> residents)
    {
        if (residents == null) return;
        foreach (var s in residents)
        {
            var record = Roster != null ? Roster.Find(s) : null;
            if (record == null) continue;
            record.status = ResidentStatus.Ashore;
            record.droppedAt = Elapsed;
        }
    }

    /// <summary>The desk has written this resident on the list: they are safe, and they score.</summary>
    public void OnRegistered(ResidentRecord record)
    {
        if (record == null || record.status == ResidentStatus.Registered) return;

        record.status = ResidentStatus.Registered;
        record.registeredAt = Elapsed;
        record.points = Rules.Points(record.tagWhenPickedUp, record.criticalWhenPickedUp);
        Evacuated++;
        RefreshScore(false);

        // Nothing is left to change the result, so there is time to see the last of them registered.
        if (Evacuated >= TotalSurvivors) EndGame(reportDelay);
    }

    /// <summary>Works the score out from the roster. At the end it includes the bonuses for how the mission was run.</summary>
    void RefreshScore(bool final)
    {
        if (Roster == null) return;
        Sheet = ScoreSheet.Build(Roster.Records, Rules, Safety(), final);
        Score = Sheet.Total;
    }

    /// <summary>How the crew handled things, from the counters on the boat and the swimmer.</summary>
    public SafetyRecord Safety()
    {
        var safety = new SafetyRecord();
        if (boat != null)
        {
            safety.HullStrikes = boat.HullStrikes;
            safety.UnsafePasses = boat.UnsafePasses;
            safety.RushedPickups = boat.RushedPickups;
            safety.Shocks += boat.ShocksTaken;
            var throwing = boat.GetComponent<BoatRescue>();
            if (throwing != null) safety.LinesLost = throwing.LinesLost;
        }
        if (player != null)
        {
            safety.Shocks += player.ShocksTaken;
            if (player.Rescue != null)
            {
                safety.UntetheredSwims = player.Rescue.UntetheredSwims;
                safety.UnneededSwims = player.Rescue.UnneededSwims;
            }
        }
        return safety;
    }

    void EndGame(float delay)
    {
        if (GameOver) return;
        GameOver = true;
        StartCoroutine(ShowReport(delay));
    }

    IEnumerator ShowReport(float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        // The report stops time, so anybody still stepping ashore would be left frozen mid-stride under it, and
        // anybody on the porch not yet written down would never be. They come off one after another and the desk
        // sees them one at a time, so wait until both are done - but never for ever.
        var riders = boat != null ? boat.GetComponent<BoatPassengers>() : null;
        float giveUpAt = Time.realtimeSinceStartup + maxRegistrationWait;
        while (Time.realtimeSinceStartup < giveUpAt &&
               ((riders != null && riders.IsUnloading) || (Desk != null && Desk.Pending > 0)))
            yield return null;

        RefreshScore(true);
        Report = BuildReport();
        reportVisible = true;
        Time.timeScale = 0f; // pause physics; Update still runs for restart input
    }

    // ------------------------------------------------------------------ end-of-mission report

    /// <summary>The headline over the report.</summary>
    public string ReportTitle { get; private set; }
    /// <summary>The report, section by section, as drawn on the screen.</summary>
    public List<ReportSection> ReportSections { get; private set; }

    string BuildReport()
    {
        // What the people still out there were like when it ended (for the reasons the report gives).
        foreach (var record in Roster.Records)
        {
            var s = record.survivor;
            if (s != null && s.DiscoveredAt >= 0f && record.foundAt < 0f) record.foundAt = Mathf.Max(0f, s.DiscoveredAt - StartedAt);
            if (record.status == ResidentStatus.Registered || s == null) continue;

            record.endTag = s.Triage;
            record.endCritical = s.IsCritical;
            record.leftInLiveWire = s.InLiveWire;
        }

        var pack = new List<PackUse>();
        foreach (var id in LoadoutState.Selected) pack.Add(new PackUse { id = id, uses = LoadoutState.Uses(id) });

        var input = new ReportInput
        {
            records = Roster.Records,
            sheet = Sheet,
            safety = Safety(),
            timeUp = TimeIsUp,
            elapsed = Elapsed,
            timeLimit = timeLimit,
            hasLoadout = LoadoutState.HasLoadout,
            pack = pack,
            isPacked = LoadoutState.Has
        };

        ReportTitle = AfterActionReport.Title(input);
        ReportSections = AfterActionReport.Build(input);
        return AfterActionReport.ToText(ReportTitle, ReportSections);
    }

    // ------------------------------------------------------------------ HUD
    void EnsureStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        label.normal.textColor = Color.white;
        center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        big = new GUIStyle(label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
        body = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Normal, wordWrap = true };
        marker = new GUIStyle(label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        marker.normal.textColor = new Color(0.55f, 1f, 0.6f);
    }

    void DrawCentered(string text, float y, Color color)
    {
        center.normal.textColor = color;
        GUI.Label(new Rect(0, y, Screen.width, 40), text, center);
        center.normal.textColor = Color.white;
    }

    void OnGUI()
    {
        EnsureStyles();

        if (GameOver && reportVisible)
        {
            DrawReport();                   // it covers the screen: nothing of the HUD shows through
            return;
        }

        int mins = Mathf.FloorToInt(timeLeft / 60f);
        int secs = Mathf.FloorToInt(timeLeft % 60f);
        int onBoat = boat != null ? boat.Passengers : 0;
        int cap = boat != null ? boat.capacity : 0;

        GUI.Label(new Rect(20, 15, 500, 32), $"Time {mins:00}:{secs:00}", label);
        GUI.Label(new Rect(20, 45, 500, 32), $"Evacuated {Evacuated}/{TotalSurvivors}", label);
        GUI.Label(new Rect(20, 75, 500, 32), $"On boat {onBoat}/{cap}", label);
        GUI.Label(new Rect(20, 105, 500, 32), $"Score {Score}", label);

        if (GameOver) return;
        if (boat != null) DrawPrompt();
        DrawPlayerPrompt();
        DrawEvacMarker();
    }

    /// <summary>
    /// Points the way to the evacuation centre once there is somebody aboard to take there. It can be a long way
    /// down the street and out of sight, and "go to the evacuation centre" is no help without a direction.
    /// </summary>
    void DrawEvacMarker()
    {
        if (evac == null || boat == null || boat.Passengers <= 0) return;

        Vector3 target = evac.transform.position;
        Vector3 offset = target - boat.transform.position;
        offset.y = 0f;
        float dist = offset.magnitude;
        if (dist < 10f) return;                 // already there

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // A small top/bottom margin tucks it under the prompt lines when the centre is behind the camera.
        Vector2 at = HudMarker.ScreenPosition(cam, target + Vector3.up * 2f, 75f, 32f);
        var rect = new Rect(at.x - 65f, at.y - 18f, 130f, 36f);

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, $"EVAC  {dist:0} m", marker);
    }

    /// <summary>Boarding / stepping off. Only shown when the boat has nothing more urgent to say.</summary>
    void DrawPlayerPrompt()
    {
        if (player == null) return;
        if (boat != null && (boat.ActiveStatus != null || boat.CurrentTarget != null)) return;

        string prompt = player.InteractPrompt;
        if (prompt != null) DrawCentered(prompt, Screen.height - 150f, new Color(0.6f, 0.9f, 1f));
    }

    void DrawPrompt()
    {
        float y = Screen.height - 110f;
        var target = boat.CurrentTarget;
        string status = boat.ActiveStatus;
        string reason = null;
        bool canRescue = target != null && target.CanBeRescued(out reason);

        if (status != null)
            DrawCentered(status, y, new Color(1f, 0.9f, 0.4f));
        else if (target != null && boat.IsFull)
            DrawCentered("Boat full! Go to the evacuation centre", y, Color.white);
        else if (target != null && !canRescue)
            DrawCentered(reason, y, new Color(1f, 0.55f, 0.35f));
        else if (target != null && boat.TooFastToRescue)
            DrawCentered("Slow down and come alongside", y, new Color(1f, 0.55f, 0.35f));
        else if (target != null && target.RequiresStabilise)
            DrawCentered("Hold E to treat and rescue", y, Color.white);
        else if (target != null)
            DrawCentered("Press E to rescue", y, Color.white);
        else if (boat.IsFull)
            DrawCentered("Boat full! Go to the evacuation centre", y, Color.white);

        if (boat.HoldProgress01 > 0f)
        {
            var bar = new Rect(Screen.width / 2f - 130f, y + 44f, 260f, 16f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.9f, 0.3f);
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * boat.HoldProgress01, bar.height - 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    /// <summary>The report is laid out on a 1280 x 720 page and the page is scaled to the window, so it reads the same at any size.</summary>
    const float PageWidth = 1280f, PageHeight = 720f;

    void DrawReport()
    {
        GUI.color = new Color(0f, 0f, 0f, 0.88f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float scale = Mathf.Min(Screen.width / PageWidth, Screen.height / PageHeight);
        var prior = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(
            new Vector3((Screen.width - PageWidth * scale) * 0.5f, (Screen.height - PageHeight * scale) * 0.5f, 0f),
            Quaternion.identity, new Vector3(scale, scale, 1f));

        int bigSize = big.fontSize;
        big.fontSize = 34;
        GUI.Label(new Rect(0, 14, PageWidth, 50), ReportTitle ?? "Mission over", big);
        big.fontSize = bigSize;
        GUI.Label(new Rect(0, 62, PageWidth, 32), $"Final score: {Score}", center);

        if (ReportSections != null) DrawReportSections();

        string hint = Application.CanStreamedLevelBeLoaded(loadoutSceneName)
            ? "R - try again with this pack     Esc - repack the boat"
            : "R - try again";
        GUI.Label(new Rect(0, PageHeight - 44, PageWidth, 36), hint, center);
        GUI.matrix = prior;
    }

    const float ReportTop = 106f, ReportBottom = 52f, ReportGap = 40f, ReportMargin = 48f;

    /// <summary>Two columns of headed sections: the outcome on the left, the debrief on the right.</summary>
    void DrawReportSections()
    {
        EnsureReportStyles();

        float colWidth = (PageWidth - ReportMargin * 2f - ReportGap) / 2f;
        float available = PageHeight - ReportTop - ReportBottom;

        // A mission with a great many residents makes a long list: shrink the print until both columns fit the page.
        if (fitSections != ReportSections)
        {
            fitSections = ReportSections;
            reportFit = 1f;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                ApplyReportFit();
                float tallest = Mathf.Max(ColumnHeight(0, colWidth), ColumnHeight(1, colWidth));
                if (tallest <= available || reportFit <= 0.62f) break;
                reportFit = Mathf.Max(0.62f, reportFit * available / tallest * 0.98f);
            }
            ApplyReportFit();
        }

        var y = new float[2] { ReportTop, ReportTop };
        foreach (var section in ReportSections)
        {
            int c = Mathf.Clamp(section.column, 0, 1);
            float x = ReportMargin + c * (colWidth + ReportGap);

            GUI.Label(new Rect(x, y[c], colWidth, HeadingHeight), section.title, reportHeading);
            y[c] += HeadingHeight;

            foreach (var line in section.lines)
            {
                float indent = line.hasTag ? 18f : 0f;
                float height = LineHeight(line, colWidth);
                if (line.hasTag)
                {
                    var prior = GUI.color;
                    GUI.color = TriageStyle.Colour(line.tag);
                    float size = 9f * reportFit;
                    GUI.DrawTexture(new Rect(x + 2f, y[c] + (height - size) * 0.5f, size, size), Texture2D.whiteTexture);
                    GUI.color = prior;
                }

                reportLine.normal.textColor = ToneColour(line.tone);
                GUI.Label(new Rect(x + indent, y[c], colWidth - indent, height), line.text, reportLine);
                y[c] += height;
            }
            y[c] += SectionGap;
        }
    }

    float HeadingHeight => 24f * reportFit;
    float SectionGap => 10f * reportFit;

    float LineHeight(ReportLine line, float colWidth)
    {
        float indent = line.hasTag ? 18f : 0f;
        return Mathf.Max(20f * reportFit, reportLine.CalcHeight(new GUIContent(line.text), colWidth - indent));
    }

    float ColumnHeight(int column, float colWidth)
    {
        float h = 0f;
        foreach (var section in ReportSections)
        {
            if (Mathf.Clamp(section.column, 0, 1) != column) continue;
            h += HeadingHeight + SectionGap;
            foreach (var line in section.lines) h += LineHeight(line, colWidth);
        }
        return h;
    }

    void ApplyReportFit()
    {
        reportHeading.fontSize = Mathf.RoundToInt(17f * reportFit);
        reportLine.fontSize = Mathf.RoundToInt(15f * reportFit);
    }

    static Color ToneColour(ReportTone tone)
    {
        switch (tone)
        {
            case ReportTone.Good:    return new Color(0.6f, 0.95f, 0.6f);
            case ReportTone.Warning: return new Color(1f, 0.85f, 0.45f);
            case ReportTone.Bad:     return new Color(1f, 0.55f, 0.45f);
            case ReportTone.Dim:     return new Color(0.7f, 0.74f, 0.8f);
            default:                 return Color.white;
        }
    }

    GUIStyle reportHeading, reportLine;
    float reportFit = 1f;
    List<ReportSection> fitSections;

    void EnsureReportStyles()
    {
        if (reportHeading != null) return;
        reportHeading = new GUIStyle(label) { fontSize = 17 };
        reportHeading.normal.textColor = new Color(1f, 0.86f, 0.45f);
        reportLine = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Normal, wordWrap = true };
    }
}
