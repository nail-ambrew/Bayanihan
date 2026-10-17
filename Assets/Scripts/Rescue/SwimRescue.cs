using UnityEngine;

/// <summary>
/// The swimmer's side of a rescue. Going in the water is the last resort - reach, throw, row, and only then go - but
/// there are places the boat cannot reach and a line cannot reach: past a debris jam, along an alley too narrow for the
/// hull, round a corner. That is what swimming is for.
///
/// E takes hold of the resident within reach and tows them back at a slower pace. When the swimmer gets them to the
/// boat the crew take them aboard (treating them first if they are hurt). The item they need still has to be packed:
/// the swimmer brings the ring or the rope to them.
///
/// If the Lubid is packed the swimmer is tethered to the boat and cannot go further than the line allows. Without it
/// they can swim as far as they like, and a long way from the boat that is recorded as an untethered swim. So is a
/// swim to somebody a thrown line would have reached.
///
/// Added to the player by PlayerRescuer.
/// </summary>
[DefaultExecutionOrder(-10)]      // E is read before the player decides whether to climb aboard
[RequireComponent(typeof(PlayerRescuer))]
public class SwimRescue : MonoBehaviour
{
    [Header("Grabbing")]
    [Tooltip("How close the swimmer has to be to take hold of somebody (metres).")]
    public float grabRange = 2.6f;

    [Header("Towing")]
    [Tooltip("Swim speed while towing, as a fraction of the normal speed.")]
    [Range(0.2f, 1f)] public float towSpeedFactor = 0.6f;
    [Tooltip("How far behind the swimmer the person in tow trails (metres).")]
    public float towDistance = 1.9f;
    [Tooltip("Within this of the boat's hull the crew take them (metres).")]
    public float handoverRange = 4.5f;

    [Header("Tether")]
    [Tooltip("How far from the boat the swimmer can go on the tether (metres).")]
    public float tetherLength = 35f;
    [Tooltip("Without a tether, swimming further than this from the boat for a few seconds counts as an untethered swim.")]
    public float untetheredDistance = 12f;
    [Tooltip("Seconds beyond that distance before it counts.")]
    public float untetheredSeconds = 3f;

    public Survivor Target { get; private set; }
    public Survivor Towing { get; private set; }
    /// <summary>What the prompt says: how to take hold, or why not. Null when nobody is in reach.</summary>
    public string Prompt { get; private set; }
    public bool PromptIsWarning { get; private set; }
    /// <summary>True on a frame when E went to a resident, so the player does not also climb aboard.</summary>
    public bool InteractConsumedThisFrame { get; private set; }

    public bool IsTethered => LoadoutState.Has(RescueItemId.Rope);
    public int Grabs { get; private set; }
    /// <summary>Swims to somebody a thrown line from the boat could have reached.</summary>
    public int UnneededSwims { get; private set; }
    public int UntetheredSwims { get; private set; }

    PlayerRescuer player;
    Rigidbody rb;
    SimpleBoat boat;
    BoatRescue boatRescue;
    SurvivorVisual towVisual;
    bool handedOver;                    // the crew have them: they stay at the boat's side until they are aboard
    float nextHandoverTryAt;
    float untetheredFor;
    bool untetheredCounted;

    Transform tube;                     // the rescue tube the person in tow hangs on to
    LineRenderer tether;
    Material tetherMaterial;
    GUIStyle promptStyle;

    void Awake()
    {
        player = GetComponent<PlayerRescuer>();
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        boat = FindFirstObjectByType<SimpleBoat>();
        if (boat != null) boatRescue = boat.GetComponent<BoatRescue>();
    }

    void OnDestroy()
    {
        if (tetherMaterial != null) Destroy(tetherMaterial);
    }

    // ------------------------------------------------------------------ each frame
    void Update()
    {
        InteractConsumedThisFrame = false;

        if (player.IsMounted || boat == null)
        {
            Target = null;
            Prompt = null;
            ShowTether(false);
            untetheredFor = 0f;
            untetheredCounted = false;
            return;
        }

        WatchDistance();
        DrawTether();

        if (Towing != null)
        {
            TendToTow();
            if (InputHelper.InteractPressed()) OnInteractWhileTowing();
        }
        else
        {
            FindTarget();
            if (Target != null && InputHelper.InteractPressed())
            {
                InteractConsumedThisFrame = true;
                TryGrab(Target);
            }
        }

        BuildPrompt();
    }

    // ------------------------------------------------------------------ taking hold
    void FindTarget()
    {
        Target = null;
        float best = grabRange;
        Vector3 here = transform.position;

        foreach (var s in Survivor.All)
        {
            if (s == null || s.IsRescued || s.IsHeld || s.situation == Situation.Rooftop) continue;     // a roof is out of the water
            Vector3 d = s.transform.position - here;
            if (Mathf.Abs(d.y) > 2f) continue;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < best) { best = dist; Target = s; }
        }
    }

    /// <summary>Null if they can be taken hold of; otherwise why not.</summary>
    string GrabBlock(Survivor s)
    {
        if (s.InLiveWire) return "live wire in the water - keep clear!";
        if (Missing(s, Need.Lifebuoy, RescueItemId.Lifebuoy)) return "needs a Salbabida - not packed";
        if (Missing(s, Need.Rope, RescueItemId.Rope)) return "needs a Lubid - not packed";
        if (s.NeedsFirstAidKit && !LoadoutState.Has(RescueItemId.FirstAid)) return "needs a Botika - not packed";
        return null;
    }

    static bool Missing(Survivor s, Need need, RescueItemId item) => (s.needs & need) != 0 && !LoadoutState.Has(item);

    /// <summary>Takes hold of them and starts the tow back. Returns whether it worked.</summary>
    public bool TryGrab(Survivor s)
    {
        string block = GrabBlock(s);
        if (block != null)
        {
            boat.SetStatus(s.DisplayName + ": " + block, 2.5f);
            return false;
        }

        // Going in for somebody a line could have reached is not the last resort it is meant to be.
        if (boatRescue != null && boatRescue.CouldThrowTo(s)) UnneededSwims++;

        Grabs++;
        Towing = s;
        handedOver = false;
        towVisual = s.GetComponent<SurvivorVisual>();
        s.IsHeld = true;
        s.ArrivedBy = Survivor.Arrival.Swimmer;
        s.Dislodge();
        player.SpeedScale = towSpeedFactor;
        BuildTube();
        boat.SetStatus("You have " + s.DisplayName + " - tow them to the boat.", 3f);
        return true;
    }

    // ------------------------------------------------------------------ the tow
    void TendToTow()
    {
        var s = Towing;
        if (s == null || s.IsRescued)
        {
            // Aboard now (or gone): the swim is done.
            EndTow(releaseSurvivor: false);
            return;
        }

        Vector3 here = transform.position;
        Vector3 hullPoint = boat.Hull != null ? boat.Hull.ClosestPoint(here) : boat.transform.position;
        float toHull = Vector3.Distance(here, hullPoint);

        Vector3 goal;
        if (handedOver)
        {
            // The crew have them. They wait at the side of the boat while they are seen to.
            goal = Survivor.HullSide(boat, s.transform.position);
        }
        else
        {
            Vector3 back = -transform.forward;
            back.y = 0f;
            goal = here + back.normalized * towDistance;

            if (toHull <= handoverRange && Time.time >= nextHandoverTryAt) TryHandOver(s);
        }

        Vector3 p = s.transform.position;
        goal.y = p.y;                                           // the water keeps their height
        s.transform.position = Vector3.Lerp(p, goal, 1f - Mathf.Exp(-6f * Time.deltaTime));

        if (towVisual != null)
        {
            towVisual.FaceTowards(handedOver ? boat.transform.position : here);
            Vector3 hold = TubePoint();
            Vector3 across = Vector3.Cross(Vector3.up, transform.forward).normalized;
            towVisual.HoldOn(hold + across * 0.28f, hold - across * 0.28f);
        }
        PlaceTube();
    }

    void TryHandOver(Survivor s)
    {
        nextHandoverTryAt = Time.time + 0.5f;

        // Somebody the boat could not reach is within reach once they have been brought to it.
        bool before = s.LineDelivered;
        s.LineDelivered = true;

        if (boat.TakeAboardFromWater(s, out string reason))
        {
            handedOver = true;
            player.SpeedScale = 1f;
            return;
        }

        s.LineDelivered = before;
        if (!string.IsNullOrEmpty(reason)) boat.SetStatus(s.DisplayName + ": " + reason, 2.5f);
    }

    void OnInteractWhileTowing()
    {
        InteractConsumedThisFrame = true;
        if (handedOver) return;
        LetGoOfTow();
        boat.SetStatus("You let go.", 1.5f);
    }

    /// <summary>Lets go of whoever is in tow. They are in the water where they were, and carried off by the current.</summary>
    public void LetGoOfTow()
    {
        // Once the crew have them they are the crew's: climbing aboard does not drop them.
        if (Towing != null) EndTow(releaseSurvivor: !handedOver);
    }

    void EndTow(bool releaseSurvivor)
    {
        if (Towing != null && releaseSurvivor)
        {
            Towing.IsHeld = false;
            Towing.ArrivedBy = Survivor.Arrival.Boat;
            if (towVisual != null) towVisual.LetGo();
        }
        else if (Towing != null && towVisual != null)
        {
            towVisual.LetGo();
        }

        Towing = null;
        towVisual = null;
        handedOver = false;
        player.SpeedScale = 1f;
        if (tube != null) { Destroy(tube.gameObject); tube = null; }
    }

    // ------------------------------------------------------------------ the rescue tube
    void BuildTube()
    {
        var t = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(t.GetComponent<Collider>());
        t.name = "Rescue tube";
        t.transform.localScale = new Vector3(0.16f, 0.32f, 0.16f);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        t.GetComponent<Renderer>().sharedMaterial = new Material(shader != null ? shader : Shader.Find("Standard")) { color = new Color(0.95f, 0.45f, 0.1f) };
        tube = t.transform;
        PlaceTube();
    }

    Vector3 TubePoint()
    {
        Vector3 p = transform.position - transform.forward * (towDistance * 0.45f);
        p.y = (WaterSurface.Instance != null ? WaterSurface.Instance.GetHeight(p) : p.y) + 0.06f;
        return p;
    }

    void PlaceTube()
    {
        if (tube == null) return;
        tube.position = TubePoint();
        tube.rotation = Quaternion.LookRotation(transform.right, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);   // lying across the way you swim
    }

    // ------------------------------------------------------------------ the tether
    /// <summary>
    /// Called by the player's swimming: keeps them within the tether's length of the boat. Without a tether nothing
    /// holds them, and the player's own speed and the current are all there is.
    /// </summary>
    public Vector3 ApplyTether(Vector3 position, Vector3 velocity)
    {
        if (!IsTethered || boat == null || player.IsMounted) return velocity;

        Vector3 outward = position - boat.transform.position;
        outward.y = 0f;
        float distance = outward.magnitude;
        if (distance < tetherLength - 0.01f) return velocity;

        outward /= Mathf.Max(distance, 0.01f);
        float away = Vector3.Dot(velocity, outward);
        if (away > 0f) velocity -= outward * away;                      // the line is taut: no further out
        if (distance > tetherLength + 0.3f) velocity -= outward * 3f;   // and it draws them back in if they were thrown past it
        return velocity;
    }

    void DrawTether()
    {
        if (!IsTethered)
        {
            ShowTether(false);
            return;
        }

        if (tether == null)
        {
            var go = new GameObject("Tether");
            go.transform.SetParent(transform, false);
            tether = go.AddComponent<LineRenderer>();
            tether.useWorldSpace = true;
            tether.positionCount = 12;
            tether.widthMultiplier = 0.04f;
            tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tether.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default");
            tetherMaterial = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { color = new Color(0.86f, 0.72f, 0.42f) };
            tether.sharedMaterial = tetherMaterial;
        }
        ShowTether(true);

        Vector3 a = boat.transform.position - boat.HeadingForward * 2f + Vector3.up * 0.7f;       // the stern cleat
        Vector3 b = transform.position + Vector3.up * 0.15f;
        float sag = Mathf.Clamp(0.04f * Vector3.Distance(a, b), 0.05f, 0.6f);
        var water = WaterSurface.Instance;
        for (int i = 0; i < tether.positionCount; i++)
        {
            float t = i / (tether.positionCount - 1f);
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
            if (water != null) p.y = Mathf.Max(p.y, water.GetHeight(p) + 0.03f);
            tether.SetPosition(i, p);
        }
    }

    void ShowTether(bool on)
    {
        if (tether != null) tether.enabled = on;
    }

    /// <summary>Counts a swim as untethered once the swimmer has been well away from the boat for a few seconds without a line.</summary>
    void WatchDistance()
    {
        if (IsTethered) { untetheredFor = 0f; untetheredCounted = false; return; }

        float distance = Vector3.Distance(transform.position, boat.transform.position);
        if (distance > untetheredDistance)
        {
            untetheredFor += Time.deltaTime;
            if (!untetheredCounted && untetheredFor >= untetheredSeconds)
            {
                UntetheredSwims++;
                untetheredCounted = true;
            }
        }
        else if (distance < untetheredDistance * 0.7f)
        {
            untetheredFor = 0f;
            untetheredCounted = false;      // back at the boat: the next excursion counts again
        }
    }

    // ------------------------------------------------------------------ screen
    void BuildPrompt()
    {
        Prompt = null;
        PromptIsWarning = false;

        if (Towing != null)
        {
            Prompt = handedOver ? "The crew are taking " + Towing.DisplayName + " aboard..." : "Tow " + Towing.DisplayName + " to the boat · E to let go";
        }
        else if (Target != null)
        {
            string block = GrabBlock(Target);
            Prompt = block != null ? Target.DisplayName + ": " + block : "E · take hold of " + Target.DisplayName;
            PromptIsWarning = block != null;
        }
        else if (!IsTethered && untetheredFor > 0.5f)
        {
            Prompt = "No Lubid packed - you are swimming untethered";
            PromptIsWarning = true;
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (Prompt == null || player.IsMounted) return;
        if (RescueManager.Instance != null && RescueManager.Instance.GameOver) return;

        if (promptStyle == null)
            promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        promptStyle.normal.textColor = PromptIsWarning ? new Color(1f, 0.6f, 0.35f) : new Color(0.7f, 0.95f, 1f);
        GUI.Label(new Rect(0f, Screen.height - 190f, Screen.width, 40f), Prompt, promptStyle);
    }
}
