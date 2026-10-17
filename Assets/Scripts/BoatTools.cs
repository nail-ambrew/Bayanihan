using UnityEngine;

/// <summary>
/// The active items on the boat: Flashlight (toggle, F) and Radyo (radio ping, Q, 20 s cooldown; and C to ask the
/// power company to cut a downed line). Also draws the small "packed items" bar, the radio marker and the
/// live-wire prompt.
/// Added to the boat automatically by SimpleBoat - you don't need to add it yourself.
/// </summary>
[RequireComponent(typeof(SimpleBoat))]
public class BoatTools : MonoBehaviour
{
    [Header("Flashlight")]
    public float flashRange = 45f;
    public float flashAngle = 65f;
    public float flashIntensity = 12f;
    public Color flashColor = new Color(1f, 0.96f, 0.85f);

    [Header("Radio")]
    public float pingDuration = 8f;

    [Header("Power cut")]
    [Tooltip("How far from the edge of a live zone the crew can still radio in a power cut (metres). Each zone can " +
             "also set its own reach; this is only the prompt's range for showing the warning.")]
    public float warnRange = 40f;

    [Header("Discovery")]
    [Tooltip("The crew size somebody up - find them, and read their tag and condition - from this far with nothing " +
             "blocking the view (1.5x with the flashlight on). It works from the swimmer's eyes when the player is in " +
             "the water.")]
    public float discoverRadius = 20f;
    [Tooltip("How high above the boat the crew look out from, for the line-of-sight test.")]
    public float lookoutHeight = 1.6f;
    [Tooltip("Seconds between looks. A sight line is a ray cast against the whole town, so it is not done every frame.")]
    public float lookInterval = 0.2f;

    /// <summary>The live zone C would ask the power company to cut right now, if any.</summary>
    public LiveWireZone ZoneToCut { get; private set; }
    /// <summary>What the live-wire prompt says (null = nothing to say), and whether it is a warning.</summary>
    public string PowerPrompt { get; private set; }
    public bool PowerPromptIsWarning { get; private set; }
    /// <summary>How many power cuts the crew have radioed in.</summary>
    public int PowerCutsRequested { get; private set; }

    SimpleBoat boat;
    PlayerRescuer player;
    Light spot;
    bool flashOn;
    float nextLookAt;
    float radioReadyAt;
    Survivor pingTarget;
    float pingUntil;
    Camera cam;

    GUIStyle boxLabel, markerLabel, powerStyle;

    void Awake() { boat = GetComponent<SimpleBoat>(); }

    void Start()
    {
        player = FindFirstObjectByType<PlayerRescuer>();
        if (LoadoutState.Has(RescueItemId.Flashlight)) BuildSpot();
    }

    void BuildSpot()
    {
        var go = new GameObject("Flashlight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        spot = go.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = flashRange;
        spot.spotAngle = flashAngle;
        spot.intensity = flashIntensity;
        spot.color = flashColor;
        spot.shadows = LightShadows.None;
        spot.enabled = false;
    }

    void Update()
    {
        // Discovery (used by the radio: "nearest resident you have not found yet")
        if (Time.time >= nextLookAt)
        {
            nextLookAt = Time.time + lookInterval;
            LookForResidents();
        }

        // Both tools live on the boat, so they only answer while the player is at the helm.
        if (spot != null && InputHelper.FlashlightPressed())
        {
            if (!boat.IsPlayerAboard) boat.SetStatus("The flashlight is fixed to the boat - climb aboard to use it.");
            else
            {
                flashOn = !flashOn;
                spot.enabled = flashOn;
                if (flashOn) LoadoutState.RegisterUse(RescueItemId.Flashlight);
            }
        }

        if (LoadoutState.Has(RescueItemId.Radio) && InputHelper.RadioPressed())
        {
            if (!boat.IsPlayerAboard) boat.SetStatus("The Radyo is on the boat - climb aboard to use it.");
            else TryPing();
        }

        UpdatePowerPrompt();
        if (InputHelper.PowerCutPressed()) TryRequestCut();

        if (pingTarget != null && (pingTarget.IsRescued || Time.time > pingUntil)) pingTarget = null;
    }

    // ------------------------------------------------------------------ live wires
    /// <summary>
    /// A downed line the crew are near. With the Radyo packed, C asks for the power to be cut (it takes a while,
    /// and the water stays live until it is). Without it there is nothing to be done except keep clear.
    /// </summary>
    void UpdatePowerPrompt()
    {
        ZoneToCut = null;
        PowerPrompt = null;
        PowerPromptIsWarning = false;
        if (!boat.IsPlayerAboard) return;

        LiveWireZone nearest = null;
        float nearestEdge = float.MaxValue;
        foreach (var zone in LiveWireZone.All)
        {
            if (zone == null || !zone.IsLive) continue;
            float edge = zone.DistanceToEdge(transform.position);
            if (edge < nearestEdge) { nearestEdge = edge; nearest = zone; }
        }
        if (nearest == null || nearestEdge > warnRange) return;

        bool radio = LoadoutState.Has(RescueItemId.Radio);
        if (nearest.CutRequested)
        {
            PowerPrompt = "Power company: the line will be cut in " + Mathf.CeilToInt(nearest.CutTimeLeft) + " s - keep clear";
        }
        else if (!radio)
        {
            PowerPrompt = "Live wire ahead - no Radyo packed to have the power cut. Keep clear of it";
            PowerPromptIsWarning = true;
        }
        else if (nearestEdge > nearest.requestRange)
        {
            PowerPrompt = "Live wire " + Mathf.Max(0, Mathf.RoundToInt(nearestEdge)) + " m off - close in to radio it";
        }
        else
        {
            PowerPrompt = "Live wire - press C to have the power cut (Radyo)";
            ZoneToCut = nearest;
        }
    }

    /// <summary>Asks the power company to cut the nearest live line in range. True if the call was made.</summary>
    public bool TryRequestCut()
    {
        if (!boat.IsPlayerAboard)
        {
            boat.SetStatus("The Radyo is on the boat - climb aboard to use it.");
            return false;
        }
        if (!LoadoutState.Has(RescueItemId.Radio))
        {
            boat.SetStatus("No Radyo packed - there is no way to call the power company.");
            return false;
        }

        LiveWireZone zone = null;
        float best = float.MaxValue;
        foreach (var z in LiveWireZone.All)
        {
            if (z == null || !z.IsLive || z.CutRequested) continue;
            float edge = z.DistanceToEdge(transform.position);
            if (edge <= z.requestRange && edge < best) { best = edge; zone = z; }
        }

        if (zone == null)
        {
            boat.SetStatus("No live wire in range of the Radyo.");
            return false;
        }

        if (!zone.RequestCut()) return false;
        PowerCutsRequested++;
        LoadoutState.RegisterUse(RescueItemId.Radio);
        boat.SetStatus("Radyo: power company notified - the line will be cut in " + Mathf.CeilToInt(zone.cutDelay) + " s.", 3f);
        return true;
    }

    /// <summary>
    /// Sizes up the residents the crew can actually see. Distance alone is not enough in a town: somebody two streets
    /// away behind a row of houses is close, and still completely hidden. Whoever can be seen is found, and while
    /// they stay in view their tag and condition are read live (see MissionHud).
    /// </summary>
    void LookForResidents()
    {
        float r = discoverRadius * (flashOn ? 1.5f : 1f);
        Vector3 eye = LookoutPoint();

        foreach (var s in Survivor.All)
        {
            if (s == null) continue;
            if (s.IsRescued) { s.InView = false; continue; }

            Vector3 head = s.SightPoint;
            bool seen = (head - eye).sqrMagnitude <= r * r && LineOfSight.Clear(eye, head);
            s.InView = seen;
            if (seen) s.Discovered = true;
        }
    }

    /// <summary>Where the looking is done from: the boat's lookout, or the swimmer's head if the player is in the water.</summary>
    Vector3 LookoutPoint()
    {
        if (player != null && !player.IsMounted) return player.transform.position + Vector3.up * 0.5f;
        return transform.position + Vector3.up * lookoutHeight;
    }

    void LateUpdate()
    {
        // keep the beam level with the sea, pointing where the bow points (ignores wave tilt)
        if (spot != null)
            spot.transform.rotation = Quaternion.LookRotation(boat.HeadingForward + Vector3.down * 0.18f);
    }

    void TryPing()
    {
        if (Time.time < radioReadyAt)
        {
            boat.SetStatus($"Radyo recharging - {Mathf.CeilToInt(radioReadyAt - Time.time)}s");
            return;
        }

        Survivor best = null;
        float bestDist = float.MaxValue;
        foreach (var s in Survivor.All)
        {
            if (s == null || s.IsRescued || s.Discovered) continue;
            float d = (s.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = s; }
        }

        if (best == null)
        {
            boat.SetStatus("Radyo: every resident is already accounted for.");
            return;
        }

        pingTarget = best;
        pingUntil = Time.time + pingDuration;
        radioReadyAt = Time.time + RescueItems.Get(RescueItemId.Radio).cooldown;
        LoadoutState.RegisterUse(RescueItemId.Radio);
        boat.SetStatus("Radyo: resident located - follow the marker.");
    }

    // ------------------------------------------------------------------ HUD
    void OnGUI()
    {
        if (boxLabel == null)
        {
            boxLabel = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            boxLabel.normal.textColor = Color.white;
            markerLabel = new GUIStyle(boxLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            markerLabel.normal.textColor = new Color(1f, 0.9f, 0.3f);
        }

        DrawPackBar();
        DrawPingMarker();
        DrawPowerPrompt();
    }

    void DrawPowerPrompt()
    {
        if (Event.current.type != EventType.Repaint || PowerPrompt == null) return;
        if (RescueManager.Instance != null && RescueManager.Instance.GameOver) return;

        if (powerStyle == null)
            powerStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        powerStyle.normal.textColor = PowerPromptIsWarning ? new Color(1f, 0.6f, 0.35f) : new Color(1f, 0.9f, 0.4f);
        GUI.Label(new Rect(0f, Screen.height - 230f, Screen.width, 36f), PowerPrompt, powerStyle);
    }

    void DrawPackBar()
    {
        var items = LoadoutState.Selected;
        if (items.Count == 0) return;

        const float w = 190f, h = 44f, gap = 6f;
        float x = Screen.width - w - 20f;
        float y = Screen.height - 20f - items.Count * (h + gap);

        foreach (var id in items)
        {
            var def = RescueItems.Get(id);
            string state = "Passive";
            if (id == RescueItemId.Flashlight) state = flashOn ? "F · ON" : "F · off";
            else if (id == RescueItemId.Radio)
                state = Time.time < radioReadyAt ? $"Q · {Mathf.CeilToInt(radioReadyAt - Time.time)}s" : "Q · ready";

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = def.color;
            GUI.DrawTexture(new Rect(x, y, 6f, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(x + 14f, y, w - 20f, h), $"{def.name}   {state}", boxLabel);
            y += h + gap;
        }
    }

    void DrawPingMarker()
    {
        if (pingTarget == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector2 at = HudMarker.ScreenPosition(cam, pingTarget.transform.position + Vector3.up * 1.5f, 60f);

        float dist = Vector3.Distance(transform.position, pingTarget.transform.position);
        var rect = new Rect(at.x - 55f, at.y - 18f, 110f, 36f);

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, $"! {dist:0} m", markerLabel);
    }
}
