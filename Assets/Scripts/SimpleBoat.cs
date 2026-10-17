using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-controlled rescue boat with arcade-style handling.
/// W/S = throttle / brake / reverse, A/D = steer, E = pick up the nearest survivor in range.
/// Works together with the Buoyancy component (recommended). Without it, the boat is locked flat on the water.
///
/// The boat moves relative to the water (see FloodCurrent), carries the weight of everyone aboard, and keeps a
/// record of how it was handled - hull strikes, close passes at speed, rushed pick-ups - for the mission report.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SimpleBoat : MonoBehaviour
{
    public enum BowDirection { PositiveZ, NegativeZ, PositiveX, NegativeX }

    [Header("Orientation")]
    [Tooltip("Which local axis of this object points out of the boat's bow (front). " +
             "If W drives the boat backwards, switch this (usually to NegativeZ).")]
    public BowDirection bowDirection = BowDirection.PositiveZ;

    [Header("Speed")]
    public float maxSpeed = 8f;
    public float reverseSpeed = 4f;
    [Tooltip("How fast the boat speeds up (m/s per second)")]
    public float acceleration = 6f;
    [Tooltip("How hard S brakes while moving forward (and W while reversing)")]
    public float brakeDeceleration = 14f;
    [Tooltip("How fast the boat slows down when you let go of the keys")]
    public float coastDeceleration = 3f;

    [Header("Steering")]
    [Tooltip("Turn rate in degrees per second at full steer")]
    public float turnSpeed = 90f;
    [Tooltip("0.6 = can still turn at 60% rate while standing still. 1 = same turn rate at any speed.")]
    [Range(0.2f, 1f)] public float turnAtRest = 0.65f;
    [Tooltip("Higher = snappier turning")]
    public float turnResponsiveness = 8f;
    [Tooltip("Higher = boat grips the water and doesn't slide sideways")]
    public float sidewaysGrip = 8f;
    [Tooltip("Off = A always turns left, D always right (easier). On = steering flips when reversing, like a car.")]
    public bool invertSteeringInReverse = false;
    [Tooltip("Tick this if A/D turn the wrong way on screen (e.g. your camera looks at the boat from the front).")]
    public bool invertSteering = false;

    [Header("Feel")]
    [Tooltip("How quickly key presses ramp up to full input (higher = more instant)")]
    public float inputRamp = 5f;

    [Header("Rescue")]
    public int capacity = 4;

    [Header("Load")]
    [Tooltip("Weight of the empty boat with its motor, fuel and gear (kg).")]
    public float hullMass = 320f;
    [Tooltip("Weight added for each person aboard (kg).")]
    public float personMass = 70f;
    [Tooltip("With every seat taken: top speed as a fraction of the empty boat's.")]
    [Range(0.4f, 1f)] public float loadedSpeedFactor = 0.8f;
    [Tooltip("With every seat taken: acceleration as a fraction of the empty boat's.")]
    [Range(0.4f, 1f)] public float loadedAccelerationFactor = 0.65f;
    [Tooltip("With every seat taken: turn rate as a fraction of the empty boat's.")]
    [Range(0.4f, 1f)] public float loadedTurnFactor = 0.85f;

    [Header("Safe handling")]
    [Tooltip("The boat has to be slower than this, relative to the person in the water, before they can be " +
             "brought aboard (m/s). A rescue boat comes alongside at a crawl.")]
    public float maxPickupSpeed = 3f;
    [Tooltip("Passing this close to somebody in the water (metres from the hull)...")]
    public float closePassDistance = 2.5f;
    [Tooltip("...faster than this (m/s) is logged as an unsafe pass.")]
    public float closePassSpeed = 5f;
    [Tooltip("Hitting something solid harder than this (m/s) is logged as a hull strike.")]
    public float hullStrikeSpeed = 3f;

    /// <summary>The boat's own hull collider. Trigger zones must test against THIS, not just
    /// "something with a SimpleBoat above it" - evacuees and props are parented to the boat too.</summary>
    public Collider Hull { get; private set; }

    public int Passengers { get; private set; }
    public bool IsFull => Passengers >= capacity;
    /// <summary>0 with no evacuees aboard, 1 with every seat taken.</summary>
    public float Load01 => capacity > 0 ? Mathf.Clamp01((float)Passengers / capacity) : 0f;
    /// <summary>Bumped on every unload. A resident still mid-hop when the boat empties carries a stale
    /// epoch and is dropped, so Passengers and the visible riders can't drift apart.</summary>
    public int BoardingEpoch { get; private set; }

    /// <summary>Set by PlayerRescuer. With nobody at the helm the boat ignores driving and rescue input.</summary>
    public bool IsPlayerAboard { get; set; }
    /// <summary>True if the boat acted on this frame's Interact press, so the player doesn't also step off.</summary>
    public bool InteractConsumedThisFrame { get; private set; }
    public bool HasSurvivorInRange => nearby.Count > 0;
    /// <summary>Is this resident close enough to be taken aboard by hand (inside their pick-up radius)?</summary>
    public bool IsInReach(Survivor s) => nearby.Contains(s);
    /// <summary>Forward speed through the water in m/s (handy for a speedometer or wake effects).</summary>
    public float ForwardSpeed { get; private set; }
    /// <summary>Smoothed steering input, -1 (hard to port) to 1 (hard to starboard).</summary>
    public float Steer => input.x;
    /// <summary>Smoothed throttle input, -1 (full astern) to 1 (full ahead).</summary>
    public float Throttle => input.y;
    /// <summary>Speed over the ground in m/s, whichever way the boat is moving.</summary>
    public float GroundSpeed
    {
        get
        {
            Vector3 v = rb.linearVelocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    // ---- How the boat was handled, for the mission report ----
    /// <summary>Times the hull hit something solid at speed.</summary>
    public int HullStrikes { get; private set; }
    /// <summary>Times the boat went close past somebody in the water at speed.</summary>
    public int UnsafePasses { get; private set; }
    /// <summary>Times a pick-up was tried while the boat was still moving too fast.</summary>
    public int RushedPickups { get; private set; }
    /// <summary>Times the boat ran into a live wire in the water.</summary>
    public int ShocksTaken { get; private set; }

    /// <summary>The engine has cut out, after a shock: no steering and no throttle until it is restarted.</summary>
    public bool IsStalled => Time.time < stalledUntil;
    float stalledUntil;

    Rigidbody rb;
    Buoyancy buoyancy;
    BoatPassengers riders;
    Vector2 rawInput;
    Vector2 input; // smoothed
    readonly List<Survivor> nearby = new List<Survivor>();
    readonly HashSet<Survivor> passedTooClose = new HashSet<Survivor>();
    float nextStrikeAt;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        buoyancy = GetComponent<Buoyancy>();
        // Physics steps 50 times a second and the screen refreshes faster: without this the boat, everyone
        // sitting in it and the camera chasing it all move in visible steps.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // The hull is the boat's own solid collider. Trigger zones and pick-up radii match against THIS
        // exact collider, so anything parented to the deck later can't fire events on the boat's behalf.
        foreach (var c in GetComponents<Collider>())
            if (!c.isTrigger) { Hull = c; break; }
        if (Hull == null) Hull = GetComponent<Collider>();

        // The boat as it leaves the dock: hull, motor and one person at the helm. Buoyancy floats exactly this
        // much at the designed waterline, so everybody who comes aboard after that pushes it lower.
        rb.mass = hullMass + personMass;

        if (buoyancy != null)
        {
            // Let the waves pitch/roll the boat - Buoyancy handles gravity, damping and stability.
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.None;
            buoyancy.SetDesignMass(rb.mass);
        }
        else
        {
            // Fallback: boat glued flat to the water.
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX
                           | RigidbodyConstraints.FreezeRotationZ
                           | RigidbodyConstraints.FreezePositionY;
        }

        // Flashlight / radio logic + packed-items HUD live in BoatTools
        if (GetComponent<BoatTools>() == null) gameObject.AddComponent<BoatTools>();

        // Motor movement, wake and spray
        if (GetComponent<BoatEffects>() == null) gameObject.AddComponent<BoatEffects>();

        // Throwing a line to somebody out of reach
        if (GetComponent<BoatRescue>() == null) gameObject.AddComponent<BoatRescue>();

        // Rescuer crew + evacuees visible on the deck
        riders = GetComponent<BoatPassengers>();
        if (riders == null) riders = gameObject.AddComponent<BoatPassengers>();
    }

    void Update()
    {
        InteractConsumedThisFrame = false;

        // No driver, no control - the boat coasts to a stop instead of steering itself. A stalled engine gives none either.
        rawInput = IsPlayerAboard && !IsStalled ? InputHelper.Move() : Vector2.zero;

        // Smooth the keys so W/A/S/D feel analogue instead of on/off
        float step = inputRamp * Time.deltaTime;
        input.x = Mathf.MoveTowards(input.x, rawInput.x, step);
        input.y = Mathf.MoveTowards(input.y, rawInput.y, step);

        if (IsPlayerAboard) HandleRescueInput();
        else { CurrentTarget = null; TooFastToRescue = false; ResetHold(); }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Everyone aboard is weight the hull has to carry. Eased in, so somebody climbing aboard doesn't kick it.
        rb.mass = Mathf.MoveTowards(rb.mass, hullMass + personMass * PeopleAboard, 200f * dt);

        float sub = buoyancy != null ? buoyancy.Submerged : 1f;
        if (sub < 0.05f) return; // propeller out of the water - no control

        float grip = Mathf.Clamp01(sub * 2f); // weaker control when only partly in the water

        // A full boat is slower off the mark, slower flat out and slower to come round.
        float load = Load01;
        float topSpeed = maxSpeed * Mathf.Lerp(1f, loadedSpeedFactor, load);
        float topReverse = reverseSpeed * Mathf.Lerp(1f, loadedSpeedFactor, load);
        float accel = acceleration * Mathf.Lerp(1f, loadedAccelerationFactor, load);
        float turn = turnSpeed * Mathf.Lerp(1f, loadedTurnFactor, load);

        // Horizontal heading (ignores pitch/roll from waves)
        Vector3 fwd = Vector3.ProjectOnPlane(transform.TransformDirection(ForwardVector()), Vector3.up);
        if (fwd.sqrMagnitude < 0.0001f) return;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        // The motor pushes against the water, and the water is moving. Everything below is worked out relative
        // to the flow and the flow is added back at the end - so with the throttle shut the boat drifts.
        // With the player overboard and a crew member at the helm, the crew hold her where she was left.
        bool crewHoldingStation = !IsPlayerAboard && riders != null && riders.HasHelmCrew;
        Vector3 flow = crewHoldingStation ? Vector3.zero : FloodCurrent.Sample(rb.position);

        Vector3 v = rb.linearVelocity;
        var throughWater = new Vector3(v.x - flow.x, 0f, v.z - flow.z);
        float fwdSpeed = Vector3.Dot(throughWater, fwd);
        float sideSpeed = Vector3.Dot(throughWater, right);

        // ---- Forward / back: move toward a target speed ----
        float targetSpeed = input.y >= 0f ? input.y * topSpeed : input.y * topReverse;

        float rate;
        if (Mathf.Abs(input.y) < 0.05f)
            rate = coastDeceleration;                                   // no keys: glide to a stop
        else if (Mathf.Abs(fwdSpeed) > 0.5f && Mathf.Sign(fwdSpeed) != Mathf.Sign(input.y))
            rate = brakeDeceleration;                                   // pressing the opposite way: brake hard
        else
            rate = accel;                                               // speeding up
        fwdSpeed = Mathf.MoveTowards(fwdSpeed, targetSpeed, rate * grip * dt);

        // ---- Sideways: grip the water so the boat doesn't slide ----
        sideSpeed *= Mathf.Exp(-sidewaysGrip * grip * dt);

        // Rebuild horizontal velocity, keep vertical motion for the waves
        Vector3 horizontal = fwd * fwdSpeed + right * sideSpeed + flow;
        rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
        ForwardSpeed = fwdSpeed;

        // ---- Steering: set yaw rate directly, works even when standing still ----
        float speed01 = Mathf.Clamp01(Mathf.Abs(fwdSpeed) / (maxSpeed * 0.5f));
        float turnFactor = Mathf.Lerp(turnAtRest, 1f, speed01);
        float dir = (invertSteeringInReverse && fwdSpeed < -0.3f) ? -1f : 1f;
        if (invertSteering) dir = -dir;
        float targetYaw = input.x * dir * turn * Mathf.Deg2Rad * turnFactor * grip;

        Vector3 av = rb.angularVelocity;
        av.y = Mathf.Lerp(av.y, targetYaw, 1f - Mathf.Exp(-turnResponsiveness * dt));
        rb.angularVelocity = av; // pitch/roll from waves are left alone

        WatchForUnsafePasses();
    }

    /// <summary>The player if they are at the helm, the crew who are visibly aboard, and every evacuee.</summary>
    int PeopleAboard
    {
        get
        {
            int crew = riders != null ? riders.CrewAboard : 0;
            if (IsPlayerAboard) crew++;
            return Passengers + Mathf.Max(crew, 1);   // somebody is always minding the boat
        }
    }

    /// <summary>Horizontal direction the bow is pointing (respects Bow Direction). Used by the chase camera.</summary>
    public Vector3 HeadingForward
    {
        get
        {
            Vector3 f = Vector3.ProjectOnPlane(transform.TransformDirection(ForwardVector()), Vector3.up);
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }
    }

    /// <summary>The bow direction in this object's local space (respects Bow Direction).</summary>
    public Vector3 LocalBow => ForwardVector();

    Vector3 ForwardVector()
    {
        switch (bowDirection)
        {
            case BowDirection.NegativeZ: return Vector3.back;
            case BowDirection.PositiveX: return Vector3.right;
            case BowDirection.NegativeX: return Vector3.left;
            default: return Vector3.forward;
        }
    }

    // ---- Called by Survivor triggers ----
    public void RegisterNearby(Survivor s) { if (!nearby.Contains(s)) nearby.Add(s); }

    public void UnregisterNearby(Survivor s)
    {
        nearby.Remove(s);
        passedTooClose.Remove(s);   // coming back for another pass counts again
    }

    // ---------------------------------------------------------------- safe handling
    /// <summary>
    /// How fast the boat is closing on (or sweeping past) this person, in m/s. Somebody adrift is moving with
    /// the water, so it is the difference that matters, not the boat's speed over the ground.
    /// </summary>
    float RelativeSpeedTo(Survivor s)
    {
        Vector3 v = rb.linearVelocity - s.DriftVelocity;
        v.y = 0f;
        return v.magnitude;
    }

    /// <summary>How fast the boat is moving relative to this person, in m/s: what matters for a throw or a pick-up.</summary>
    public float SpeedRelativeTo(Survivor s) => RelativeSpeedTo(s);

    /// <summary>
    /// A propeller and a hull at speed are the two things most likely to hurt somebody already in the water.
    /// Going close past a person fast is recorded, once per pass.
    /// </summary>
    void WatchForUnsafePasses()
    {
        if (!IsPlayerAboard || Hull == null) return;

        for (int i = 0; i < nearby.Count; i++)
        {
            var s = nearby[i];
            if (s == null || s.IsRescued || passedTooClose.Contains(s)) continue;
            if (RelativeSpeedTo(s) < closePassSpeed) continue;

            Vector3 head = s.SightPoint;
            Vector3 gap = Hull.ClosestPoint(head) - head;
            gap.y = 0f;
            if (gap.sqrMagnitude > closePassDistance * closePassDistance) continue;

            passedTooClose.Add(s);
            UnsafePasses++;
            SetStatus("Too close, too fast - you nearly ran them down.", 2.5f);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // Only hard, fixed things: nudging floating debris aside is what a boat does. (A piece of debris reports a
        // hard hit itself, through Strike.)
        if (collision.rigidbody != null && !collision.rigidbody.isKinematic) return;
        if (collision.relativeVelocity.magnitude < hullStrikeSpeed) return;

        Strike("Hull strike! Ease off in tight water.");
    }

    /// <summary>
    /// Logs a hull strike for the report and says so. One scrape along a wall is one strike, not twenty, so a second
    /// within a moment of the first is not counted. Returns whether this one was.
    /// </summary>
    public bool Strike(string message)
    {
        if (Time.time < nextStrikeAt) return false;

        nextStrikeAt = Time.time + 1.5f;
        HullStrikes++;
        SetStatus(message, 2f);
        return true;
    }

    // ---------------------------------------------------------------- rescuing
    const float StabiliseSeconds = 2f;   // hold E this long to treat an injured resident (needs the Botika)
    const float MaxTreatSpeed = 2.5f;    // the boat must be nearly still while treating

    /// <summary>Closest resident in range (null if none). Read by the HUD.</summary>
    public Survivor CurrentTarget { get; private set; }
    /// <summary>True while there is somebody in reach but the boat is still moving too fast to take them. Read by the HUD.</summary>
    public bool TooFastToRescue { get; private set; }
    /// <summary>0..1 progress of the first-aid hold. Read by the HUD.</summary>
    public float HoldProgress01 { get; private set; }
    /// <summary>Short message for the HUD ("Boat is full", "Radyo recharging"...). Null when nothing to show.</summary>
    public string ActiveStatus => (statusMessage != null && Time.unscaledTime < statusUntil) ? statusMessage : null;

    string statusMessage;
    float statusUntil;
    float holdTimer;
    Survivor holdTarget;
    float interactReadyAt;

    /// <summary>A live wire has got the boat: the engine cuts out for a few seconds.</summary>
    public void Shock(float seconds, string message)
    {
        stalledUntil = Mathf.Max(stalledUntil, Time.time + seconds);
        ShocksTaken++;
        SetStatus(message, 3f);
    }

    public void SetStatus(string message, float seconds = 3f)
    {
        statusMessage = message;
        statusUntil = Time.unscaledTime + seconds;
    }

    /// <summary>
    /// Ignore Interact presses for a moment. PlayerRescuer calls this as it climbs aboard: the press that boarded
    /// is still "pressed this frame", and without this it could also pull the nearest resident in.
    /// </summary>
    public void SuppressInteract(float seconds)
    {
        interactReadyAt = Mathf.Max(interactReadyAt, Time.time + seconds);
    }

    Survivor NearestNearby()
    {
        Survivor best = null;
        float bestDist = float.MaxValue;
        foreach (var s in nearby)
        {
            float d = (s.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = s; }
        }
        return best;
    }

    void ResetHold()
    {
        holdTimer = 0f;
        HoldProgress01 = 0f;
        holdTarget = null;
    }

    void HandleRescueInput()
    {
        nearby.RemoveAll(s => s == null || s.IsRescued);
        CurrentTarget = NearestNearby();
        TooFastToRescue = false;
        bool pressed = Time.time >= interactReadyAt && InputHelper.InteractPressed();

        if (CurrentTarget == null) { ResetHold(); return; }

        if (IsFull)
        {
            ResetHold();
            if (pressed) { SetStatus("Boat is full - drop residents at the evacuation centre."); InteractConsumedThisFrame = true; }
            return;
        }

        // Do we have the right gear packed for this person?
        if (!CurrentTarget.CanBeRescued(out string reason))
        {
            ResetHold();
            if (pressed) { SetStatus(reason); InteractConsumedThisFrame = true; }
            return;
        }

        // Nobody is hauled over the side of a boat that is still under way.
        float closing = RelativeSpeedTo(CurrentTarget);
        TooFastToRescue = closing > maxPickupSpeed;

        // Normal resident: one press
        if (!CurrentTarget.RequiresStabilise)
        {
            ResetHold();
            if (pressed)
            {
                if (TooFastToRescue)
                {
                    RushedPickups++;
                    SetStatus("Too fast - ease off and come alongside to bring them aboard.", 2f);
                }
                else PickUp(CurrentTarget);
                InteractConsumedThisFrame = true;
            }
            return;
        }

        // An injured resident in reach: the hold belongs to them, not to stepping off the boat.
        InteractConsumedThisFrame = true;

        // Injured resident + Botika packed: hold E for 2 seconds, boat nearly still
        if (holdTarget != CurrentTarget) { holdTarget = CurrentTarget; holdTimer = 0f; }

        if (!InputHelper.InteractHeld()) { holdTimer = 0f; HoldProgress01 = 0f; return; }

        if (closing > Mathf.Min(MaxTreatSpeed, maxPickupSpeed))
        {
            holdTimer = 0f; HoldProgress01 = 0f;
            SetStatus("Too fast - slow down to treat them.", 0.5f);
            return;
        }

        holdTimer += Time.deltaTime;
        HoldProgress01 = Mathf.Clamp01(holdTimer / StabiliseSeconds);
        if (holdTimer >= StabiliseSeconds)
        {
            PickUp(CurrentTarget);
            ResetHold();
        }
    }

    void PickUp(Survivor s)
    {
        if (IsFull) { SetStatus("Boat is full - drop residents at the evacuation centre."); return; }
        nearby.Remove(s);
        passedTooClose.Remove(s);
        Passengers++;          // counted now, not when the hop lands, so capacity holds while they're in the air
        s.Rescue(this);
    }

    /// <summary>
    /// The crew take somebody aboard who has been brought to the boat by a swimmer, while the player is still in the
    /// water. Whoever is hurt (or has been left too long) is treated first, which takes the crew two seconds with the
    /// boat nearly still. Returns false, and says why, if they cannot be taken now.
    /// </summary>
    public bool TakeAboardFromWater(Survivor s, out string reason)
    {
        reason = null;
        if (s == null || s.IsRescued) return false;
        if (treating == s) return true;                      // already being seen to
        if (IsFull) { reason = "Boat is full - drop residents at the evacuation centre."; return false; }
        if (!s.CanBeRescued(out reason)) return false;
        if (RelativeSpeedTo(s) > maxPickupSpeed) { reason = "The boat is moving too fast to take them aboard."; return false; }

        if (s.RequiresStabilise)
        {
            StartCoroutine(TreatThenTake(s));
            return true;
        }
        PickUp(s);
        return true;
    }

    Survivor treating;

    IEnumerator TreatThenTake(Survivor s)
    {
        treating = s;
        float done = 0f;
        while (done < StabiliseSeconds && s != null && !s.IsRescued)
        {
            // The crew need the boat nearly still to work on somebody.
            if (RelativeSpeedTo(s) > Mathf.Min(MaxTreatSpeed, maxPickupSpeed)) done = 0f;
            else done += Time.deltaTime;

            HoldProgress01 = Mathf.Clamp01(done / StabiliseSeconds);

            // Whoever is being seen to stays at the side of the boat, wherever it drifts.
            Vector3 side = Survivor.HullSide(this, s.transform.position);
            Vector3 p = s.transform.position;
            s.transform.position = Vector3.Lerp(p, new Vector3(side.x, p.y, side.z), 1f - Mathf.Exp(-8f * Time.deltaTime));
            yield return null;
        }

        HoldProgress01 = 0f;
        treating = null;
        if (s != null && !s.IsRescued) PickUp(s);
    }

    /// <summary>
    /// Called by the EvacuationCenter. Returns how many people were dropped off; <paramref name="residents"/> is
    /// filled with who they were, and <paramref name="onLanded"/> is called for each as they stand ashore.
    /// </summary>
    public int DropOffAll(Collider evacuationZone = null, List<Survivor> residents = null,
                          System.Action<Survivor, Transform> onLanded = null)
    {
        int n = Passengers;
        if (n <= 0) return 0;
        Passengers = 0;
        BoardingEpoch++;
        GetComponent<BoatPassengers>()?.UnloadAll(evacuationZone, residents, onLanded);
        return n;
    }
}
