using UnityEngine;

/// <summary>
/// A ring or a rope thrown from the boat to somebody who cannot be reached by hand: in open water beyond arm's
/// length, clinging to a pole, up on a roof, or behind debris. It flies out, they take hold, and then they are brought
/// in to the side of the boat - pulled across the water, or, from a roof, walked to the edge and lowered down the line.
/// Once they are alongside the ordinary rescue (E) takes them aboard.
///
/// A resident in the water can lose their grip: if the boat pulls away too fast or too far, the line goes taut and
/// they let go. Somebody who has tied off on a roof, or behind debris, cannot be lost that way.
///
/// Built and owned by BoatRescue. Everything it shows is made in code.
/// </summary>
public class RescueLine : MonoBehaviour
{
    public enum Phase { Flying, Pulling, Alongside, Done }

    public Phase Stage { get; private set; } = Phase.Flying;
    public Survivor Target { get; private set; }
    public bool CarriesLifebuoy { get; private set; }
    public bool CarriesRope { get; private set; }

    BoatRescue owner;
    SimpleBoat boat;
    SurvivorVisual visual;

    // flight
    Vector3 flightStart;
    float flightTime, flown, arcHeight;

    // bringing them in
    bool canLetGo;                  // false once they have tied off on a roof or behind debris
    bool onRoof;
    bool atEdge;                    // a roof resident has walked to the edge and now comes down
    Vector3 edgePoint;
    Vector3 ringPoint;              // where the ring (or the end of the rope) is: in front of them

    // what is on show
    Transform projectile;
    GameObject ownRing;             // the thrown copy of the deck lifebuoy, if there is one
    LineRenderer rope;
    Material ropeMaterial;

    const int RopePoints = 14;

    // ------------------------------------------------------------------ launching
    public static RescueLine Launch(BoatRescue owner, Survivor target, bool lifebuoy, bool rope)
    {
        var go = new GameObject("Line to " + target.DisplayName);
        var line = go.AddComponent<RescueLine>();
        line.Begin(owner, target, lifebuoy, rope);
        return line;
    }

    void Begin(BoatRescue from, Survivor target, bool lifebuoy, bool ropeBag)
    {
        owner = from;
        boat = from.Boat;
        Target = target;
        visual = target.GetComponent<SurvivorVisual>();
        CarriesLifebuoy = lifebuoy;
        CarriesRope = ropeBag;
        onRoof = target.situation == Situation.Rooftop;
        canLetGo = !target.RequiresLine;

        flightStart = owner.ThrowOrigin;
        Vector3 end = LandingPoint();
        float distance = Vector3.Distance(flightStart, end);
        flightTime = Mathf.Clamp(distance / Mathf.Max(owner.throwSpeed, 1f), 0.35f, 1.1f);
        arcHeight = Mathf.Clamp(0.12f * distance + 0.5f * Mathf.Max(0f, end.y - flightStart.y), 0.7f, 3.5f);

        BuildVisuals();
    }

    void BuildVisuals()
    {
        ownRing = CopyOfLifebuoy();
        if (ownRing != null)
        {
            projectile = ownRing.transform;
        }
        else
        {
            // A rope thrown on its own goes as a bag with the line paid out behind it.
            var bag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bag.GetComponent<Collider>());
            bag.name = "Throw bag";
            bag.transform.SetParent(transform, false);
            bag.transform.localScale = new Vector3(0.2f, 0.16f, 0.2f);
            bag.GetComponent<Renderer>().sharedMaterial = FlatMaterial(new Color(0.88f, 0.52f, 0.12f));
            projectile = bag.transform;
        }
        projectile.position = flightStart;

        var shader = Shader.Find("Sprites/Default");
        ropeMaterial = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"))
        {
            color = new Color(0.86f, 0.72f, 0.42f)
        };

        var ropeGo = new GameObject("Rope");
        ropeGo.transform.SetParent(transform, false);
        rope = ropeGo.AddComponent<LineRenderer>();
        rope.useWorldSpace = true;
        rope.positionCount = RopePoints;
        rope.widthMultiplier = 0.045f;
        rope.numCapVertices = 2;
        rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rope.receiveShadows = false;
        rope.sharedMaterial = ropeMaterial;
    }

    /// <summary>A copy of the lifebuoy that lies in the boat, to throw. The one on the deck is hidden while it is out.</summary>
    GameObject CopyOfLifebuoy()
    {
        if (!CarriesLifebuoy) return null;
        var deck = owner.DeckLifebuoy;
        if (deck == null) return FlatRing();

        var copy = Instantiate(deck.gameObject, transform);
        copy.name = "Thrown lifebuoy";
        copy.SetActive(true);
        copy.transform.position = deck.position;
        copy.transform.rotation = Quaternion.identity;
        return copy;
    }

    GameObject FlatRing()
    {
        // A boat with no lifebuoy model still gets a red ring to throw.
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(ring.GetComponent<Collider>());
        ring.name = "Thrown lifebuoy";
        ring.transform.SetParent(transform, false);
        ring.transform.localScale = new Vector3(0.75f, 0.06f, 0.75f);
        ring.GetComponent<Renderer>().sharedMaterial = FlatMaterial(new Color(0.9f, 0.3f, 0.22f));
        return ring;
    }

    static Material FlatMaterial(Color colour)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        return new Material(shader != null ? shader : Shader.Find("Standard")) { color = colour };
    }

    // ------------------------------------------------------------------ each frame
    void Update()
    {
        if (Target == null || boat == null || Target.IsRescued) { Finish(); return; }

        switch (Stage)
        {
            case Phase.Flying:    Fly(); break;
            case Phase.Pulling:   Pull(); break;
            case Phase.Alongside: Hold(); break;
        }
        DrawRope();
    }

    // ------------------------------------------------------------------ the throw
    /// <summary>Where the ring comes down: just in front of them, on the side the boat is.</summary>
    Vector3 LandingPoint()
    {
        Vector3 p = Target.transform.position;
        Vector3 toBoat = flightStart - p;
        toBoat.y = 0f;
        toBoat = toBoat.sqrMagnitude > 0.01f ? toBoat.normalized : Vector3.forward;
        p += toBoat * 0.45f;

        if (onRoof) p.y = Target.transform.position.y + 0.1f;                                   // at their feet
        else if (WaterSurface.Instance != null) p.y = WaterSurface.Instance.GetHeight(p) + 0.05f;
        return p;
    }

    void Fly()
    {
        flown += Time.deltaTime / flightTime;
        float t = Mathf.Clamp01(flown);

        // Aim for where they are now, so somebody carried along by the current is still hit.
        Vector3 end = LandingPoint();
        Vector3 p = Vector3.Lerp(flightStart, end, t);
        p.y += arcHeight * 4f * t * (1f - t);
        projectile.position = p;
        projectile.Rotate(Vector3.up, 360f * Time.deltaTime, Space.World);

        if (t >= 1f) Land();
    }

    /// <summary>They have it. From here the line is what brings them in.</summary>
    void Land()
    {
        Stage = Phase.Pulling;
        Target.IsHeld = true;
        Target.Line = this;
        Target.ArrivedBy = Survivor.Arrival.Line;
        Target.Dislodge();                      // they let go of the pole to take hold of the ring

        if (onRoof)
        {
            edgePoint = FindRoofEdge();
            atEdge = false;
        }
        owner.OnLineHeld(this);
    }

    // ------------------------------------------------------------------ bringing them in
    void Pull()
    {
        Vector3 pos = Target.transform.position;
        Vector3 hull = Survivor.HullSide(boat, pos);

        if (canLetGo)
        {
            // Hold on to a ring while the boat shoots away? Nobody can.
            float gap = HorizontalDistance(boat.transform.position, pos);
            if (gap > owner.maxLineLength) { LetGo("The line went taut - they could not hold on."); return; }
            if (boat.GroundSpeed > owner.maxLineSpeed) { LetGo("Too fast - they lost their grip on the line."); return; }
        }

        Vector3 goal = hull;
        float speed = owner.pullSpeed;

        if (onRoof)
        {
            // Walk to the edge of the roof, then come down the line to the water beside the boat.
            if (!atEdge)
            {
                goal = edgePoint;
                speed = owner.roofWalkSpeed;
                if (HorizontalDistance(pos, edgePoint) < 0.12f)
                {
                    atEdge = true;              // the next frame starts the way down
                    TendToTheirHands(boat.transform.position);
                    return;
                }
            }
            else
            {
                goal.y = SurfaceAt(hull) + 0.25f;
                speed = owner.descendSpeed;
            }
        }
        else
        {
            goal.y = pos.y;                 // the water keeps their height
            // Dragged over debris rather than through it.
            if (Target.situation == Situation.Trapped && Physics.Linecast(pos + Vector3.up * 0.5f, goal + Vector3.up * 0.5f))
                speed = owner.pullSpeed * 0.8f;
        }

        Vector3 next = Vector3.MoveTowards(pos, goal, speed * Time.deltaTime);
        if (Target.situation == Situation.Trapped && !onRoof) next.y = pos.y;
        Target.transform.position = next;

        TendToTheirHands(boat.transform.position);

        bool there = onRoof ? (atEdge && Vector3.Distance(next, goal) < 0.3f) : HorizontalDistance(next, hull) < 0.45f;
        if (there) Arrive();
    }

    /// <summary>Alongside: the line keeps them at the side of the boat, wherever it goes, until they are taken aboard.</summary>
    void Hold()
    {
        Vector3 pos = Target.transform.position;
        Vector3 hull = Survivor.HullSide(boat, pos);
        Vector3 next = Vector3.Lerp(pos, new Vector3(hull.x, pos.y, hull.z), 1f - Mathf.Exp(-8f * Time.deltaTime));
        Target.transform.position = next;
        TendToTheirHands(boat.transform.position);
    }

    void Arrive()
    {
        Stage = Phase.Alongside;
        Target.LineDelivered = true;
        if (onRoof && visual != null) visual.EnterWater();      // lowered into the water at the side of the boat
        owner.OnLineAlongside(this);
    }

    /// <summary>Faces them towards the boat, and puts their hands on the ring - or on the rope, if there is no ring.</summary>
    void TendToTheirHands(Vector3 boatPosition)
    {
        if (visual == null) return;
        visual.FaceTowards(boatPosition);

        Vector3 p = Target.transform.position;
        Vector3 toBoat = boatPosition - p;
        toBoat.y = 0f;
        toBoat = toBoat.sqrMagnitude > 0.01f ? toBoat.normalized : Vector3.forward;
        Vector3 across = Vector3.Cross(Vector3.up, toBoat);

        bool inWater = !onRoof || Stage == Phase.Alongside;
        float height = inWater && WaterSurface.Instance != null ? WaterSurface.Instance.GetHeight(p) + 0.05f : p.y + 1.1f;
        ringPoint = new Vector3(p.x, height, p.z) + toBoat * 0.5f;

        projectile.position = ringPoint;
        if (CarriesLifebuoy && inWater) projectile.rotation = Quaternion.identity;

        visual.HoldOn(ringPoint + across * 0.3f, ringPoint - across * 0.3f);
    }

    // ------------------------------------------------------------------ letting go, and being done
    void LetGo(string why)
    {
        owner.OnLineLost(this, why);
        Release();
        Finish();
    }

    void Release()
    {
        if (Target == null) return;
        Target.IsHeld = false;
        Target.Line = null;
        Target.ArrivedBy = Survivor.Arrival.Boat;
        if (visual != null) visual.LetGo();
    }

    /// <summary>The resident has been taken aboard: the ring and the rope come back with them.</summary>
    public void Retrieve()
    {
        if (Target != null) { Target.Line = null; if (visual != null) visual.LetGo(); }
        Finish();
    }

    void Finish()
    {
        if (Stage == Phase.Done) return;
        Stage = Phase.Done;
        if (owner != null) owner.OnLineDone(this);
        if (ropeMaterial != null) Destroy(ropeMaterial);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (Stage != Phase.Done)
        {
            Stage = Phase.Done;
            if (owner != null) owner.OnLineDone(this);
        }
    }

    // ------------------------------------------------------------------ the rope
    void DrawRope()
    {
        if (rope == null) return;
        Vector3 a = owner.ThrowOrigin;
        Vector3 b = projectile.position;
        float length = Vector3.Distance(a, b);
        // Slack when the ring is close, taut when it is a long way off.
        float sag = Mathf.Clamp(0.06f * length, 0.1f, 0.7f) * (Stage == Phase.Flying ? 0.3f : 1f);

        var water = WaterSurface.Instance;
        for (int i = 0; i < RopePoints; i++)
        {
            float t = i / (RopePoints - 1f);
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
            if (water != null && Stage != Phase.Flying) p.y = Mathf.Max(p.y, water.GetHeight(p) + 0.03f);   // floats on the water
            rope.SetPosition(i, p);
        }
    }

    // ------------------------------------------------------------------ small helpers
    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }

    static float SurfaceAt(Vector3 p) => WaterSurface.Instance != null ? WaterSurface.Instance.GetHeight(p) : p.y;

    /// <summary>
    /// The lip of the roof on the side the boat is: walk outwards from where they stand until the ground drops away.
    /// </summary>
    Vector3 FindRoofEdge()
    {
        Vector3 start = Target.transform.position;
        Vector3 toBoat = boat.transform.position - start;
        toBoat.y = 0f;
        toBoat = toBoat.sqrMagnitude > 0.01f ? toBoat.normalized : Vector3.forward;

        Vector3 last = start;
        for (float d = 0.1f; d <= 4f; d += 0.1f)
        {
            Vector3 p = start + toBoat * d;
            bool ground = Physics.Raycast(p + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 1.5f, ~0, QueryTriggerInteraction.Ignore)
                          && hit.collider.attachedRigidbody == null;
            if (!ground) return last - toBoat * 0.05f;      // one step short of the lip
            last = new Vector3(p.x, hit.point.y, p.z);
        }
        return last;
    }
}
