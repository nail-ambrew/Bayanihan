using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks score, timer and win/lose, and builds the end-of-mission report that tells the player what their pack
/// choices cost them. The HUD, the pause menu and the Mission Complete screen are <see cref="GameHud"/>, which this
/// adds to its own object and which reads everything below. Put on an empty GameObject called "RescueManager".
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

        // The HUD (tags over residents, roster, prompts, items, touch controls, pause) and the end screen.
        if (GetComponent<GameHud>() == null) gameObject.AddComponent<GameHud>();
    }

    RescueRules Rules => MissionDefinition.Current.rules;

    void Update()
    {
        if (GameOver)
        {
            // Keyboard shortcuts on the end screen; it has buttons for the same.
            if (!reportVisible || SceneFlow.IsLoading) return;
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

    // ------------------------------------------------------------------ for the HUD
    /// <summary>Seconds left on the mission clock.</summary>
    public float TimeLeft => timeLeft;

    /// <summary>The evacuation centre (the Barangay Hall), or null if the scene has none.</summary>
    public EvacuationCenter Evac => evac;

    /// <summary>
    /// What the boat has to say right now, and in what tone: a status line, why somebody cannot be taken, or how to
    /// take them. Null when there is nothing to say.
    /// </summary>
    public string BoatPrompt(out PromptTone tone)
    {
        tone = PromptTone.Normal;
        if (boat == null || GameOver) return null;

        var target = boat.CurrentTarget;
        string status = boat.ActiveStatus;
        string reason = null;
        bool canRescue = target != null && target.CanBeRescued(out reason);

        if (status != null) { tone = PromptTone.Notice; return status; }
        if (target != null && boat.IsFull) return "Boat full! Go to the evacuation center";
        if (target != null && !canRescue) { tone = PromptTone.Warning; return reason; }
        if (target != null && boat.TooFastToRescue) { tone = PromptTone.Warning; return "Slow down and come alongside"; }
        if (target != null && target.RequiresStabilise) return InputHelper.Hold("E", "RESCUE") + " to treat and rescue";
        if (target != null) return InputHelper.Press("E", "RESCUE") + " to rescue";
        if (boat.IsFull) return "Boat full! Go to the evacuation center";
        return null;
    }

    /// <summary>Boarding or stepping off, when the boat has nothing more urgent to say.</summary>
    public string PlayerPrompt
    {
        get
        {
            if (player == null || GameOver) return null;
            if (boat != null && (boat.ActiveStatus != null || boat.CurrentTarget != null)) return null;
            return player.InteractPrompt;
        }
    }

    /// <summary>Restart the mission with the same pack.</summary>
    public void Restart()
    {
        GamePause.Clear();
        LoadoutState.ResetUses();
        SceneFlow.Reload();
    }

    /// <summary>Leave the mission for the main menu (Quit on the stats, Abort in the pause menu).</summary>
    public void ExitToMenu()
    {
        GamePause.Clear();
        SceneFlow.Load(SceneFlow.Home);
    }

    /// <summary>Back to the loadout to repack for another run (Continue on the stats).</summary>
    public void Repack()
    {
        GamePause.Clear();
        SceneFlow.Load(loadoutSceneName);
    }
}

/// <summary>How a HUD line is coloured.</summary>
public enum PromptTone { Normal, Notice, Warning }
