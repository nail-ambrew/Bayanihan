using UnityEngine;

/// <summary>
/// The signs that the boat is a machine moving through water: the outboard swings with the tiller, a wake
/// trails from the stern, and spray comes off the bow at speed.
/// Added to the boat automatically by SimpleBoat. Everything is built in code - no assets needed.
/// </summary>
[RequireComponent(typeof(SimpleBoat))]
public class BoatEffects : MonoBehaviour
{
    [Header("Outboard")]
    [Tooltip("Name of the motor object under the boat model. It is turned about its own up axis with the steering.")]
    public string motorName = "outboard_motor";
    [Tooltip("How far the motor swings at full steer (degrees).")]
    public float motorSwing = 28f;

    [Header("Wake and spray")]
    [Tooltip("Wake particles per second at top speed.")]
    public float wakeRate = 70f;
    [Tooltip("Bow spray particles per second at top speed. Spray only starts above half speed.")]
    public float sprayRate = 60f;
    public Color foamColor = new Color(0.9f, 0.94f, 0.95f, 0.7f);
    [Tooltip("How far above the water surface the wake foam lies (metres). The waves are a few centimetres high.")]
    public float wakeHeight = 0.08f;
    [Tooltip("How far above the water surface the bow spray starts (metres).")]
    public float sprayHeight = 0.15f;
    [Tooltip("How fast the bow spray is thrown upwards (m/s, random between the two).")]
    public Vector2 sprayLift = new Vector2(2.2f, 3.4f);

    SimpleBoat boat;
    Transform motor;
    Quaternion motorRest;
    ParticleSystem wake, spray;
    Material foamMaterial;
    Texture2D foamTexture;

    void Awake() { boat = GetComponent<SimpleBoat>(); }

    void Start()
    {
        motor = CharacterRig.FindDeep(transform, motorName);
        if (motor != null) motorRest = motor.localRotation;

        var shader = Shader.Find("Sprites/Default");     // simple unlit alpha shader, works in URP
        if (shader != null)
        {
            foamTexture = SoftDot();
            foamMaterial = new Material(shader) { mainTexture = foamTexture };
        }

        var box = GetComponent<BoxCollider>();
        float halfLength = box != null ? Mathf.Abs(Vector3.Dot(box.size * 0.5f, boat.LocalBow)) : 2f;
        Vector3 centre = box != null ? box.center : Vector3.zero;

        // The wake is foam lying flat on the water behind the stern; the spray is thrown up and out at the bow.
        wake = BuildFoam("Wake", centre - boat.LocalBow * halfLength, 1.8f, new Vector2(0.45f, 0.8f),
                         new Vector2(0.15f, 0.45f), 0f, 0.3f, 0f, ParticleSystemRenderMode.HorizontalBillboard);
        spray = BuildFoam("BowSpray", centre + boat.LocalBow * halfLength, 0.62f, new Vector2(0.18f, 0.34f),
                          sprayLift, 1f, 0.5f, 42f, ParticleSystemRenderMode.Billboard);
    }

    void OnDestroy()
    {
        if (foamMaterial != null) Destroy(foamMaterial);
        if (foamTexture != null) Destroy(foamTexture);
    }

    /// <summary>A round, soft-edged dot. The default square sprite reads as blocks of cotton wool.</summary>
    static Texture2D SoftDot()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            name = "FoamDot",
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float edge = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge * edge * (3f - 2f * edge)));
            }
        }
        tex.Apply();
        return tex;
    }

    ParticleSystem BuildFoam(string name, Vector3 localPosition, float lifetime, Vector2 size, Vector2 speed,
                             float gravity, float radius, float coneAngle, ParticleSystemRenderMode mode)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);    // emit upwards

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = foamColor;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;    // left behind on the water, not dragged along
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.radius = radius;
        if (mode == ParticleSystemRenderMode.HorizontalBillboard)
        {
            // Foam on the water spreads outwards across it: a flat disc, not a cone.
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radiusThickness = 1f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
        }

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        if (mode == ParticleSystemRenderMode.HorizontalBillboard)
        {
            // Foam spreads as it ages instead of staying the size it was born.
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
        }
        else
        {
            // Spray keeps some of the boat's way on it, so it streams back along the hull instead of
            // hanging in the air where it was thrown.
            var inherit = ps.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = 0.6f;
        }

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (foamMaterial != null) renderer.sharedMaterial = foamMaterial;

        ps.Play();
        return ps;
    }

    void Update()
    {
        if (motor != null)
            motor.localRotation = motorRest * Quaternion.Euler(0f, -boat.Steer * motorSwing, 0f);

        float speed01 = Mathf.Clamp01(Mathf.Abs(boat.ForwardSpeed) / Mathf.Max(boat.maxSpeed, 0.01f));

        if (wake != null)
        {
            var emission = wake.emission;
            emission.rateOverTime = speed01 > 0.08f ? wakeRate * speed01 : 0f;
        }
        if (spray != null)
        {
            var emission = spray.emission;
            emission.rateOverTime = boat.ForwardSpeed > boat.maxSpeed * 0.5f ? sprayRate * speed01 : 0f;
        }
    }

    void LateUpdate()
    {
        RideTheWater(wake, wakeHeight);
        RideTheWater(spray, sprayHeight);
    }

    /// <summary>
    /// The hull's origin sits a little below the surface and pitches with the waves, so an emitter fixed to it ends up
    /// under the water - which is where the bow spray used to start, and why it could not be seen.
    /// </summary>
    static void RideTheWater(ParticleSystem ps, float lift)
    {
        var water = WaterSurface.Instance;
        if (ps == null || water == null) return;

        Transform t = ps.transform;
        Vector3 p = t.position;
        p.y = water.GetHeight(p) + lift;
        t.position = p;
    }
}
