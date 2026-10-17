using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A downed power line in the floodwater. The water around it is live: the boat that drives in has its engine cut out,
/// a swimmer is thrown clear and stunned, and nobody in it can be rescued - a line will not be thrown to them, and
/// they wear down twice as fast. Nobody is killed; it is a delay and a mark in the report, and an instruction to
/// get the power cut first.
///
/// With the Radyo packed, C asks the power company to cut the line. It takes ten seconds or so, during which the
/// water is still live. Without the Radyo the zone stays live all mission, and whoever is inside it has to be left.
///
/// Put this on an empty object where the cable reaches the water. The danger disc shows how far the live water
/// reaches; its cable runs up to the object in "Cable From" (a pole top), or hangs from nowhere in particular.
/// </summary>
[DisallowMultipleComponent]
public class LiveWireZone : MonoBehaviour
{
    public static readonly List<LiveWireZone> All = new List<LiveWireZone>();

    [Header("Zone")]
    [Tooltip("How far from this object the water is live (metres).")]
    public float radius = 6f;
    [Tooltip("A downed wire is live until the power is cut.")]
    public bool liveAtStart = true;
    [Tooltip("Where the cable comes from: a pole top, say. Empty = it hangs from 9 m above this object.")]
    public Transform cableFrom;

    [Header("Shock")]
    [Tooltip("How long the boat's engine stays cut out after a shock (seconds).")]
    public float boatStallSeconds = 3f;
    [Tooltip("How long a swimmer is stunned (seconds).")]
    public float swimmerStunSeconds = 3f;
    [Tooltip("Seconds between shocks while somebody stays in the zone.")]
    public float shockInterval = 4f;

    [Header("Power cut")]
    [Tooltip("How long the power company takes to cut the line once the Radyo has called it in (seconds).")]
    public float cutDelay = 10f;
    [Tooltip("The Radyo can ask for a cut from this far from the edge of the zone (metres).")]
    public float requestRange = 35f;

    public bool IsLive { get; private set; }
    public bool CutRequested { get; private set; }
    public float CutTimeLeft { get; private set; }

    SimpleBoat boat;
    PlayerRescuer player;
    float nextBoatShockAt, nextPlayerShockAt, nextSparkAt;
    readonly HashSet<Survivor> flagged = new HashSet<Survivor>();

    // what is on show
    GameObject disc;
    Material discMaterial;
    Texture2D discTexture;
    LineRenderer cable;
    Material cableMaterial;
    ParticleSystem sparks;
    Material sparkMaterial;
    Texture2D sparkTexture;
    Light glow;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
        ClearFlags();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); }

    void Awake()
    {
        IsLive = liveAtStart;
    }

    void Start()
    {
        boat = FindFirstObjectByType<SimpleBoat>();
        player = FindFirstObjectByType<PlayerRescuer>();
        BuildVisuals();
    }

    void OnDestroy()
    {
        if (discMaterial != null) Destroy(discMaterial);
        if (discTexture != null) Destroy(discTexture);
        if (cableMaterial != null) Destroy(cableMaterial);
        if (sparkMaterial != null) Destroy(sparkMaterial);
        if (sparkTexture != null) Destroy(sparkTexture);
    }

    // ------------------------------------------------------------------ the zone
    /// <summary>Is this point over the live water? Only the horizontal position matters.</summary>
    public bool Contains(Vector3 p)
    {
        p.y = transform.position.y;
        return Vector3.Distance(p, transform.position) <= radius;
    }

    /// <summary>How far this point is from the edge of the zone (zero or less inside it).</summary>
    public float DistanceToEdge(Vector3 p)
    {
        p.y = transform.position.y;
        return Vector3.Distance(p, transform.position) - radius;
    }

    /// <summary>Is this point over live water, in any zone?</summary>
    public static bool IsLiveAt(Vector3 p)
    {
        foreach (var zone in All)
            if (zone != null && zone.IsLive && zone.Contains(p)) return true;
        return false;
    }

    /// <summary>Asks for the power to be cut. False if the line is dead already or the call has been made.</summary>
    public bool RequestCut()
    {
        if (!IsLive || CutRequested) return false;
        CutRequested = true;
        CutTimeLeft = cutDelay;
        return true;
    }

    void PowerOff()
    {
        IsLive = false;
        CutRequested = false;
        CutTimeLeft = 0f;
        ClearFlags();
        if (boat != null) boat.SetStatus("The power is off - the wire is dead.", 3f);
    }

    // ------------------------------------------------------------------ each frame
    void Update()
    {
        if (IsLive && CutRequested)
        {
            CutTimeLeft -= Time.deltaTime;
            if (CutTimeLeft <= 0f) PowerOff();
        }

        FlagResidents();
        ShockWhoeverIsInIt();
        UpdateVisuals();
    }

    /// <summary>Anybody waiting in the live water is marked as such: not rescuable, and wearing down faster.</summary>
    void FlagResidents()
    {
        foreach (var s in Survivor.All)
        {
            if (s == null) continue;
            bool inside = IsLive && !s.IsRescued && Contains(s.transform.position);
            if (inside) { s.InLiveWire = true; flagged.Add(s); }
            else if (flagged.Remove(s) && !IsLiveAt(s.transform.position)) s.InLiveWire = false;     // another zone may still hold them
        }
    }

    void ClearFlags()
    {
        foreach (var s in flagged)
            if (s != null && !IsLiveAt(s.transform.position)) s.InLiveWire = false;
        flagged.Clear();
    }

    void ShockWhoeverIsInIt()
    {
        if (!IsLive) return;

        if (boat != null && boat.Hull != null && Time.time >= nextBoatShockAt)
        {
            Vector3 nearest = boat.Hull.ClosestPoint(transform.position);
            if (Contains(nearest))
            {
                nextBoatShockAt = Time.time + shockInterval;
                boat.Shock(boatStallSeconds, "Live wire! The engine cut out - drift clear and wait.");
                Spark(nearest, 40);
            }
        }

        if (player != null && !player.IsMounted && Time.time >= nextPlayerShockAt && Contains(player.transform.position))
        {
            nextPlayerShockAt = Time.time + shockInterval;
            Vector3 away = player.transform.position - transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
            player.Stun(swimmerStunSeconds, away * 5f);
            Spark(player.transform.position, 40);
            if (boat != null) boat.SetStatus("Live wire! You are thrown clear - get out of the water until the power is off.", 3f);
        }
    }

    // ------------------------------------------------------------------ what it looks like
    void BuildVisuals()
    {
        // The danger disc: red while live, green once dead.
        disc = new GameObject("Danger disc");
        disc.transform.SetParent(transform, false);
        disc.AddComponent<MeshFilter>().sharedMesh = ProceduralArt.Disc(radius);
        var discRenderer = disc.AddComponent<MeshRenderer>();
        discTexture = ProceduralArt.ZoneRim();
        discMaterial = ProceduralArt.Unlit(new Color(1f, 0.2f, 0.15f, 0.5f), discTexture);
        discRenderer.sharedMaterial = discMaterial;
        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        discRenderer.receiveShadows = false;

        // The cable.
        var cableObject = new GameObject("Cable");
        cableObject.transform.SetParent(transform, false);
        cable = cableObject.AddComponent<LineRenderer>();
        cable.useWorldSpace = true;
        cable.positionCount = 16;
        cable.widthMultiplier = 0.07f;
        cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cableMaterial = ProceduralArt.Unlit(new Color(0.06f, 0.06f, 0.07f));
        cable.sharedMaterial = cableMaterial;

        // The sparks.
        var sparkObject = new GameObject("Sparks");
        sparkObject.transform.SetParent(transform, false);
        sparks = sparkObject.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
        main.startColor = new Color(1f, 0.95f, 0.6f);
        main.gravityModifier = 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = sparks.emission;
        emission.rateOverTime = 0f;
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.1f;
        sparkObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var fade = sparks.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 1f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        sparkTexture = ProceduralArt.SoftDot();
        sparkMaterial = ProceduralArt.Unlit(Color.white, sparkTexture);
        var sparkRenderer = sparkObject.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.sharedMaterial = sparkMaterial;
        sparkRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // A flicker of blue-white light where the wire meets the water.
        var glowObject = new GameObject("Glow");
        glowObject.transform.SetParent(transform, false);
        glowObject.transform.localPosition = Vector3.up * 0.8f;
        glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 9f;
        glow.color = new Color(0.65f, 0.8f, 1f);
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;
    }

    void UpdateVisuals()
    {
        if (disc == null) return;

        var water = WaterSurface.Instance;
        float surface = water != null ? water.GetHeight(transform.position) : transform.position.y;

        // The disc rides just above the highest the waves get, so a crest never cuts a bite out of it.
        disc.transform.position = new Vector3(transform.position.x, surface + 0.22f, transform.position.z);

        // Red and pulsing while live, amber once the power company has been called, a calm green once it is dead.
        float pulse = 0.82f + 0.18f * Mathf.Sin(Time.time * (CutRequested ? 6f : 3.5f));
        Color target;
        if (IsLive) target = CutRequested ? new Color(1f, 0.72f, 0.12f, 0.5f * pulse) : new Color(1f, 0.18f, 0.12f, 0.55f * pulse);
        else target = new Color(0.25f, 0.85f, 0.4f, 0.3f);
        discMaterial.color = target;

        DrawCable(surface);

        if (IsLive && Time.time >= nextSparkAt)
        {
            nextSparkAt = Time.time + Random.Range(0.25f, 1.1f);
            Spark(new Vector3(transform.position.x, surface + 0.1f, transform.position.z), Random.Range(8, 18));
        }

        if (glow != null)
        {
            glow.intensity = IsLive ? Random.Range(0f, 3.2f) * (Random.value < 0.3f ? 1.8f : 1f) : 0f;
            glow.transform.position = new Vector3(transform.position.x, surface + 0.8f, transform.position.z);
        }
    }

    void DrawCable(float surface)
    {
        Vector3 top = cableFrom != null ? cableFrom.position : transform.position + Vector3.up * 9f;
        Vector3 end = new Vector3(transform.position.x, surface + 0.05f, transform.position.z);
        float sag = Mathf.Clamp(Vector3.Distance(top, end) * 0.12f, 0.3f, 2.5f);

        for (int i = 0; i < cable.positionCount; i++)
        {
            float t = i / (cable.positionCount - 1f);
            Vector3 p = Vector3.Lerp(top, end, t) + Vector3.down * (sag * 4f * t * (1f - t));
            if (i == cable.positionCount - 1) p = end;
            cable.SetPosition(i, p);
        }
    }

    void Spark(Vector3 at, int count)
    {
        if (sparks == null) return;
        sparks.transform.position = at;
        sparks.Emit(count);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.15f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
        if (cableFrom != null) Gizmos.DrawLine(cableFrom.position, transform.position);
    }
}
