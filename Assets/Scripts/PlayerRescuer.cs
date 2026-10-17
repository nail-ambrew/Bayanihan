using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// The player: a rescue volunteer who swims the floodwater and drives the boat.
///
/// Put this on an empty GameObject where the player should start. Everything else is built at runtime - the
/// character, the collider, the rigidbody and the swim camera - so there is nothing to wire up in the scene.
///
/// The camera rig lives at the scene root and only follows the swimmer's position, never their rotation, so
/// looking around and swimming stay independent. Movement is relative to where the camera is pointing and the
/// swimmer turns to face it, which is what makes the water feel navigable instead of like steering a tank.
///
/// Interact (E) boards the boat when close enough, and steps off again. While aboard, E still rescues first -
/// dismount only happens when there is nobody in reach, so one button covers both without a mode switch.
///
/// The mouse looks around in both modes: it turns the swim camera in the water, and swings the boat's own chase
/// camera while at the helm.
/// </summary>
[DisallowMultipleComponent]
public class PlayerRescuer : MonoBehaviour
{
    public enum PovMode { ThirdPerson, FirstPerson }

    [Header("Character")]
    [Tooltip("Which rescuer the player is. C21-C24 are the Rescuer characters in the model.")]
    public string playerCharacterId = "C24";

    [Header("Swimming")]
    [Tooltip("Metres per second through the water. A strong swimmer in rescue gear manages 1 to 1.5; this is " +
             "already generous so that swimming stays playable.")]
    public float swimSpeed = 2.8f;
    public float reverseSpeed = 2f;
    [Tooltip("How fast the swimmer gets up to speed (m/s per second).")]
    public float acceleration = 7f;
    [Tooltip("How fast they slow down when you let go.")]
    public float coastDeceleration = 5f;
    [Tooltip("How fast the swimmer turns to face the way you are swimming, in degrees per second.")]
    public float turnSpeed = 360f;
    [Tooltip("How quickly key presses ramp up to full input.")]
    public float inputRamp = 6f;
    [Tooltip("Higher = the swimmer holds their line instead of drifting sideways.")]
    public float sidewaysGrip = 6f;
    [Tooltip("On = W swims where the camera looks (third-person style). Off = W is forward, A/D turn (like the boat).")]
    public bool cameraRelativeMovement = true;

    [Header("Floating")]
    [Tooltip("Fine-tune only. This object rides at the water surface and the character is sunk to the right depth " +
             "for its pose automatically (shoulders when treading water, the body when swimming).")]
    public float waterOffset = 0f;
    [Tooltip("Speed above which the swim animation plays instead of the idle float.")]
    public float swimAnimationThreshold = 0.6f;

    [Header("Body")]
    public float bodyRadius = 0.35f;
    public float bodyHeight = 1.6f;

    [Header("Boarding")]
    [Tooltip("How close to the boat's hull the player has to be to climb aboard.")]
    public float mountRange = 2.5f;
    [Tooltip("Seconds after mounting/dismounting before Interact is listened to again.")]
    public float boardingCooldown = 0.35f;

    [Header("Swim camera")]
    public PovMode povMode = PovMode.ThirdPerson;
    [Tooltip("Height the camera pivots around, above the swimmer's origin.")]
    public float eyeHeight = 1.1f;
    [Tooltip("Third person only: how far behind the swimmer the camera sits.")]
    public float cameraDistance = 4.5f;
    public float minCameraDistance = 2f;
    public float maxCameraDistance = 10f;
    [Tooltip("Degrees per pixel of mouse movement.")]
    public float lookSensitivity = 0.18f;
    [Tooltip("How far the camera can be swung down/up, in degrees.")]
    public Vector2 pitchLimits = new Vector2(-25f, 60f);
    public bool invertY = false;
    public float fieldOfView = 65f;
    [Tooltip("Hide and lock the cursor while swimming. In the editor, Escape frees it again.")]
    public bool lockCursor = true;
    [Tooltip("Priority while swimming. The boat camera uses the default of 0, so anything above it wins.")]
    public int povPriorityActive = 20;
    [Tooltip("Priority while aboard, so the boat's own camera takes back over.")]
    public int povPriorityInactive = -10;

    [Header("Boat camera")]
    [Tooltip("Seconds after the mouse stops moving before the boat camera swings back behind the helm.")]
    public float boatLookRecenterDelay = 1.5f;
    [Tooltip("How long that swing back takes.")]
    public float boatLookRecenterTime = 1f;

    public bool IsMounted { get; private set; }

    /// <summary>Stunned by a shock: no swimming and no climbing aboard until it passes.</summary>
    public bool IsStunned => Time.time < stunnedUntil;
    /// <summary>Times the swimmer was shocked by a live wire.</summary>
    public int ShocksTaken { get; private set; }
    float stunnedUntil, pushUntil;
    Vector3 pushVelocity;

    /// <summary>A live wire has got the swimmer: they are thrown clear of it and cannot move for a few seconds.</summary>
    public void Stun(float seconds, Vector3 push)
    {
        stunnedUntil = Time.time + seconds;
        pushUntil = Time.time + 0.6f;
        pushVelocity = push;
        ShocksTaken++;
    }

    /// <summary>Multiplies the swim speed: somebody towing a resident cannot swim as fast as somebody on their own.</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>The swimmer's rescue work: grabbing somebody, towing them, the tether.</summary>
    public SwimRescue Rescue { get; private set; }

    SimpleBoat boat;
    BoatPassengers passengers;
    CharacterAnimator anim;
    Rigidbody rb;
    CapsuleCollider body;
    CinemachineCamera pov;
    Transform camRig;
    bool bodyShaped;
    bool lettingGo;                 // false until the player first swims or boards
    CinemachineOrbitalFollow boatOrbit;                       // the boat's chase camera, if it has one
    readonly Collider[] overlaps = new Collider[8];

    Vector2 rawInput, input;
    float yaw;                       // the swimmer's facing
    float camYaw, camPitch;          // where the player is looking
    float boardingReadyAt;

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        boat = FindFirstObjectByType<SimpleBoat>();
        if (boat != null) passengers = boat.GetComponent<BoatPassengers>();

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.mass = 1f;

        body = GetComponent<CapsuleCollider>();
        if (body == null) body = gameObject.AddComponent<CapsuleCollider>();
        body.radius = bodyRadius;
        body.height = bodyHeight;
        ShapeBody(false);

        yaw = transform.eulerAngles.y;
        camYaw = yaw;
        camPitch = 12f;

        Rescue = GetComponent<SwimRescue>();
        if (Rescue == null) Rescue = gameObject.AddComponent<SwimRescue>();
    }

    void Start()
    {
        var library = CharacterLibrary.Instance;
        if (library == null) { enabled = false; return; }

        var entry = library.Find(playerCharacterId) ?? library.RandomRescuer();
        if (entry == null)
        {
            Debug.LogWarning("PlayerRescuer: no rescuer '" + playerCharacterId + "' in the library.", this);
            enabled = false;
            return;
        }

        anim = CharacterRig.Spawn(entry, transform, CharacterPose.Float);
        if (anim == null)
        {
            Debug.LogWarning("PlayerRescuer: the character failed to spawn - you will be swimming invisibly. " +
                             "Run Bayanihan > Rebuild Character Library.", this);
        }

        // Swimming through the hull beats shoving the boat around every time the player brushes against it.
        if (boat != null && boat.Hull != null) Physics.IgnoreCollision(body, boat.Hull, true);

        BuildCamera();
        SetUpBoatCamera();
        SnapToWater();
        ApplyCursorLock(true);
    }

    /// <summary>
    /// The boat's chase camera is a Cinemachine orbit that nothing was steering, so the view sat dead astern - and
    /// searching a flooded street means looking down the side alleys as you pass them. The mouse swings it while
    /// aboard, and it drifts back behind the helm once the mouse is left alone.
    /// </summary>
    void SetUpBoatCamera()
    {
        if (boat == null) return;

        foreach (var orbit in FindObjectsByType<CinemachineOrbitalFollow>(FindObjectsSortMode.None))
        {
            if (orbit.FollowTarget != boat.transform) continue;
            boatOrbit = orbit;
            break;
        }
        if (boatOrbit == null) return;

        var recenter = new InputAxis.RecenteringSettings
        {
            Enabled = true,
            Wait = boatLookRecenterDelay,
            Time = boatLookRecenterTime
        };

        // Recentre on wherever the scene parked the camera, not on the target's +Z. Which end of this boat is the
        // bow is SimpleBoat's business (Bow Direction), and Cinemachine would otherwise settle looking at the stern.
        boatOrbit.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.AxisCenter;
        boatOrbit.HorizontalAxis.Center = boatOrbit.HorizontalAxis.Value;
        boatOrbit.HorizontalAxis.Recentering = recenter;
        boatOrbit.VerticalAxis.Center = boatOrbit.VerticalAxis.Value;
        boatOrbit.VerticalAxis.Recentering = recenter;
    }

    void BuildCamera()
    {
        // Root level on purpose: if the rig were a child of the swimmer it would inherit their turning and the
        // camera would swing every time they changed direction.
        var rig = new GameObject("PlayerSwimCamera");
        camRig = rig.transform;

        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(camRig, false);
        camGo.transform.localPosition = CameraLocalOffset();
        camGo.transform.localRotation = Quaternion.identity;

        // No Position/Rotation Control components: the camera simply rides this transform.
        pov = camGo.AddComponent<CinemachineCamera>();
        pov.Lens.FieldOfView = fieldOfView;
        pov.Priority = povPriorityActive;

        PlaceCamera();
    }

    Vector3 CameraLocalOffset() =>
        povMode == PovMode.FirstPerson
            ? new Vector3(0f, 0f, 0.15f)
            : new Vector3(0f, 0f, -Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance));

    void OnDestroy()
    {
        if (camRig != null) Destroy(camRig.gameObject);
        ApplyCursorLock(false);
    }

    void ApplyCursorLock(bool locked)
    {
        if (!lockCursor) return;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (IsMounted)
        {
            rawInput = Vector2.zero;
            input = Vector2.zero;
            LookFromBoat();
            return;
        }

        rawInput = IsStunned ? Vector2.zero : InputHelper.Move();
        float step = inputRamp * Time.deltaTime;
        input.x = Mathf.MoveTowards(input.x, rawInput.x, step);
        input.y = Mathf.MoveTowards(input.y, rawInput.y, step);

        Look();
        UpdatePose();

        bool grabbed = Rescue != null && Rescue.InteractConsumedThisFrame;     // E went to the resident in reach
        if (!grabbed && !IsStunned && Time.time >= boardingReadyAt && InputHelper.InteractPressed() && CanMount()) Mount();
    }

    void Look()
    {
        Vector2 d = InputHelper.LookDelta();
        camYaw += d.x * lookSensitivity;
        camPitch += (invertY ? d.y : -d.y) * lookSensitivity;
        camPitch = Mathf.Clamp(camPitch, pitchLimits.x, pitchLimits.y);

        if (povMode == PovMode.ThirdPerson)
        {
            float scroll = InputHelper.ZoomDelta();
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                cameraDistance = Mathf.Clamp(cameraDistance - scroll, minCameraDistance, maxCameraDistance);
                if (pov != null) pov.transform.localPosition = CameraLocalOffset();
            }
        }
    }

    /// <summary>Mouse look while at the helm: swings the boat's chase camera around the boat.</summary>
    void LookFromBoat()
    {
        if (boatOrbit == null) return;

        Vector2 d = InputHelper.LookDelta();
        if (d.sqrMagnitude < 0.0001f) return;   // left alone, Cinemachine recentres it

        float yawStep = d.x * lookSensitivity;
        float pitchStep = (invertY ? d.y : -d.y) * lookSensitivity;
        boatOrbit.HorizontalAxis.Value = boatOrbit.HorizontalAxis.ClampValue(boatOrbit.HorizontalAxis.Value + yawStep);
        boatOrbit.VerticalAxis.Value = boatOrbit.VerticalAxis.ClampValue(boatOrbit.VerticalAxis.Value + pitchStep);
    }

    /// <summary>
    /// Dismount is decided in LateUpdate, after SimpleBoat has had its turn with the same key press. If the boat
    /// used Interact to rescue somebody, the player stays put - otherwise one press would do both.
    /// </summary>
    void LateUpdate()
    {
        PlaceCamera();

        if (!IsMounted || Time.time < boardingReadyAt) return;
        if (!InputHelper.InteractPressed()) return;
        if (boat != null && boat.InteractConsumedThisFrame) return;
        Dismount();
    }

    void PlaceCamera()
    {
        if (camRig == null) return;
        Vector3 anchor = IsMounted && boat != null ? boat.transform.position : transform.position;
        Vector3 eye = anchor + Vector3.up * eyeHeight;

        // First person: look out from the character's own head, which is barely above the water. A swimmer sees
        // very little - which is exactly why the boat is the better place to search from.
        if (!IsMounted && povMode == PovMode.FirstPerson && anim != null)
        {
            var head = anim.Bone("Head");
            if (head != null) eye = head.position + Vector3.up * 0.12f;
        }
        camRig.position = eye;
        camRig.rotation = Quaternion.Euler(camPitch, camYaw, 0f);
    }

    void FixedUpdate()
    {
        if (IsMounted) return;
        Swim();
    }

    // ------------------------------------------------------------------ swimming
    void Swim()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 planar;

        // A swimmer moves through the water, and the water is moving: everything is worked out relative to the
        // flow and the flow is added back, so treading water means being carried downstream.
        // Until the player first moves they are holding on to the boat they started beside; otherwise the flood
        // would carry them off down the street before they had read the first prompt.
        if (rawInput.sqrMagnitude > 0.01f) lettingGo = true;
        Vector3 flow = lettingGo ? FloodCurrent.Sample(rb.position) : Vector3.zero;
        Vector3 throughWater = rb.linearVelocity - flow;

        if (cameraRelativeMovement)
        {
            // Where the player is pointing the stick, in world space, flattened.
            Vector3 camFwd = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
            Vector3 camRight = Quaternion.Euler(0f, camYaw, 0f) * Vector3.right;
            Vector3 wish = camFwd * input.y + camRight * input.x;

            float amount = Mathf.Clamp01(wish.magnitude);
            if (amount > 0.01f)
            {
                Vector3 dir = wish.normalized;
                float wanted = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, wanted, turnSpeed * dt);
            }

            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float current = Vector3.Dot(throughWater, fwd);
            float rate = amount > 0.01f ? acceleration : coastDeceleration;
            float speed = Mathf.MoveTowards(current, amount * swimSpeed * SpeedScale, rate * dt);
            planar = fwd * speed;
        }
        else
        {
            // Tank controls, matching the boat: A/D turn, W/S drive.
            yaw += input.x * turnSpeed * 0.4f * dt;

            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            float fwdSpeed = Vector3.Dot(throughWater, fwd);
            float sideSpeed = Vector3.Dot(throughWater, right);

            float targetSpeed = (input.y >= 0f ? input.y * swimSpeed : input.y * reverseSpeed) * SpeedScale;
            float rate = Mathf.Abs(input.y) > 0.01f ? acceleration : coastDeceleration;
            fwdSpeed = Mathf.MoveTowards(fwdSpeed, targetSpeed, rate * dt);
            sideSpeed = Mathf.MoveTowards(sideSpeed, 0f, sidewaysGrip * dt);

            planar = fwd * fwdSpeed + right * sideSpeed;
        }

        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

        // Ride the surface by steering the vertical velocity towards it, rather than teleporting with
        // MovePosition - a hard set every frame fights the solver and punches through colliders.
        float vy = 0f;
        if (WaterSurface.Instance != null)
        {
            float targetY = WaterSurface.Instance.GetHeight(rb.position) + waterOffset;
            vy = Mathf.Clamp((targetY - rb.position.y) / dt, -6f, 6f);
        }

        var velocity = new Vector3(planar.x + flow.x, vy, planar.z + flow.z);
        if (Time.time < pushUntil) velocity += pushVelocity;                           // thrown clear by a shock
        if (Rescue != null) velocity = Rescue.ApplyTether(rb.position, velocity);      // the line to the boat has a length
        rb.linearVelocity = velocity;
    }

    /// <summary>
    /// The collider follows the body: upright and mostly under the surface while treading water, lying along the
    /// surface while swimming. This object's origin is at the waterline in both cases.
    /// </summary>
    void ShapeBody(bool swimming)
    {
        int direction = swimming ? 2 : 1;       // capsule axis: Z or Y
        if (body.direction == direction && bodyShaped) return;
        bodyShaped = true;
        body.direction = direction;
        body.center = swimming
            ? new Vector3(0f, -bodyRadius * 0.4f, 0f)
            : new Vector3(0f, 0.3f - bodyHeight * 0.5f, 0f);
    }

    void SnapToWater()
    {
        if (WaterSurface.Instance == null) return;
        Vector3 p = transform.position;
        p.y = WaterSurface.Instance.GetHeight(p) + waterOffset;
        transform.position = p;
    }

    void UpdatePose()
    {
        if (anim == null) return;
        float speed = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
        bool moving = speed > swimAnimationThreshold || rawInput.sqrMagnitude > 0.1f;
        anim.Play(moving ? CharacterPose.Swim : CharacterPose.Float);
        ShapeBody(moving);

        // Sink the character so the water meets the right part of the body for the pose it is in.
        anim.transform.localPosition = new Vector3(0f, -anim.Waterline, 0f);
    }

    // ------------------------------------------------------------------ mount / dismount
    bool CanMount()
    {
        if (boat == null || boat.Hull == null) return false;
        Vector3 closest = boat.Hull.ClosestPoint(transform.position);
        return (closest - transform.position).sqrMagnitude <= mountRange * mountRange;
    }

    void Mount()
    {
        if (Rescue != null) Rescue.LetGoOfTow();       // you cannot climb aboard with somebody on a line
        IsMounted = true;
        boardingReadyAt = Time.time + boardingCooldown;
        boat.SuppressInteract(boardingCooldown);   // this press boarded; it must not also rescue somebody

        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        // A child of the boat now. Its own interpolation would keep writing a world pose of its own and drift
        // out of step with the hull; riding the boat's (already interpolated) transform keeps the two locked.
        rb.interpolation = RigidbodyInterpolation.None;
        body.enabled = false;

        transform.SetParent(boat.transform, true);
        transform.localPosition = passengers != null ? passengers.DriverSeatLocalPosition : Vector3.zero;
        transform.localRotation = Quaternion.LookRotation(boat.LocalBow * (anim != null ? anim.ForwardSign() : 1f), Vector3.up);
        transform.localScale = Vector3.one;

        passengers?.SetDriverAboard(true);
        if (anim != null)
        {
            anim.transform.localPosition = Vector3.zero;
            anim.Play(CharacterPose.Idle);
            passengers?.SeatDriver(anim);      // sitting, one hand on the tiller
        }

        boat.IsPlayerAboard = true;
        if (pov != null) pov.Priority = povPriorityInactive;
        // The cursor stays locked: the mouse now looks around from the helm (see LookFromBoat).
    }

    void Dismount()
    {
        boardingReadyAt = Time.time + boardingCooldown;

        if (!TryFindExit(out Vector3 exit))
        {
            boat.SetStatus("No room to step off here - move the boat clear first.");
            return;
        }

        IsMounted = false;
        lettingGo = true;

        // Face the way the bow points - not the boat's +Z, which is the stern on a model built bow-backwards.
        Vector3 heading = boat.HeadingForward;
        yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
        camYaw = yaw;
        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);

        transform.SetParent(null, true);
        transform.SetPositionAndRotation(exit, facing);
        transform.localScale = Vector3.one;

        passengers?.ReleaseDriver();
        passengers?.SetDriverAboard(false);

        rb.isKinematic = false;
        // Move the physics body as well as the transform. Left to the transform alone, the body wakes up where it
        // was last simulated - at the helm - and interpolation drags the swimmer straight back there.
        rb.position = exit;
        rb.rotation = facing;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearVelocity = Vector3.zero;
        body.enabled = true;

        anim?.Play(CharacterPose.Float);

        boat.IsPlayerAboard = false;
        if (pov != null) pov.Priority = povPriorityActive;
        ApplyCursorLock(true);
    }

    /// <summary>
    /// Finds open water beside the boat to step off into: starboard first, then port. A boat nosed up against a
    /// house has a wall on one side, and stepping off that way would put the swimmer inside it.
    /// </summary>
    bool TryFindExit(out Vector3 exit)
    {
        Vector3 side = Vector3.Cross(Vector3.up, boat.HeadingForward).normalized;

        for (int i = 0; i < 2; i++)
        {
            Vector3 p = boat.transform.position + (i == 0 ? side : -side) * (mountRange * 0.8f);
            if (WaterSurface.Instance != null) p.y = WaterSurface.Instance.GetHeight(p) + waterOffset;
            if (!IsClear(p)) continue;

            exit = p;
            return true;
        }

        exit = default;
        return false;
    }

    /// <summary>Would the swimmer's body fit here without overlapping anything solid (other than the boat)?</summary>
    bool IsClear(Vector3 feet)
    {
        // "feet" is a point on the water: the body hangs below it, head and shoulders above.
        Vector3 bottom = feet + Vector3.up * (0.3f - bodyHeight + bodyRadius);
        Vector3 top = feet + Vector3.up * (0.3f - bodyRadius);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, bodyRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < n; i++)
            if (overlaps[i] != boat.Hull && overlaps[i] != body) return false;
        return true;
    }

    // ------------------------------------------------------------------ prompt
    /// <summary>Read by the HUD: what Interact would do right now, or null if it would do nothing.</summary>
    public string InteractPrompt
    {
        get
        {
            if (IsMounted)
                return boat != null && boat.CurrentTarget != null ? null : "Press E to leave the boat";
            return CanMount() ? "Press E to board the boat" : null;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, mountRange);
    }
}
