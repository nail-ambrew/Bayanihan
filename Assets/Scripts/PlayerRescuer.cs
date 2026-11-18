using System.Collections.Generic;
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
    [Tooltip("The body the swimmer wears, put on the character above. Empty = the water rescuer in Resources/Characters.")]
    public CharacterSkin skin;

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
    [Tooltip("The view while swimming. Follows the SWIM VIEW setting (and V) in play; this is only what the scene starts on.")]
    public PovMode povMode = PovMode.ThirdPerson;
    [Tooltip("Height the camera pivots around, above the swimmer's origin.")]
    public float eyeHeight = 1.1f;
    [Tooltip("Third person only: how far behind the swimmer the camera sits.")]
    public float cameraDistance = 4.5f;
    [Tooltip("First person, if the head could not be measured: where the eye sits, from the head bone - x = up, y = forward along the way you look (metres).")]
    public Vector2 firstPersonEye = new Vector2(0.22f, 0.22f);
    [Tooltip("First person: how far in front of the head the eye sits, past its outer edge, so no part of the head is ever in view (metres).")]
    public float firstPersonClearance = 0.08f;
    [Tooltip("First person: the furthest you can look down, in degrees. Further, and the eye is over the swimmer's own vest and arms.")]
    public float firstPersonMaxDown = 25f;
    [Tooltip("Third person: how far clear of walls the camera keeps, and how far above the water.")]
    public float thirdPersonClearance = 0.25f;
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

    [Header("Start")]
    [Tooltip("Start the mission at the helm, with the boat's chase camera, rather than in the water beside the boat in the " +
             "swimmer's view. E (RESCUE) steps off as usual.")]
    public bool startAboard = true;

    /// <summary>
    /// Overrides <see cref="startAboard"/> for every rescuer that starts from now on (null = use the field). Tests of
    /// swimming set it to false so they still begin in the water.
    /// </summary>
    public static bool? StartAboardOverride;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStartOverride() => StartAboardOverride = null;

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
    // The head, measured once from the mesh: its middle in the head bone's own space, and how far its furthest point is from that.
    Vector3 headCentreLocal;
    float headRadius;
    float shownDistance;             // third person: how far back the camera is now, after pulling in from walls
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
        else
        {
            var body = skin != null ? skin : CharacterSkin.Load();
            if (body != null && !body.ApplyTo(anim))
                Debug.LogWarning("PlayerRescuer: the water rescuer's body would not go on " + playerCharacterId + ".", this);
        }

        // Swimming through the hull beats shoving the boat around every time the player brushes against it.
        if (boat != null && boat.Hull != null) Physics.IgnoreCollision(body, boat.Hull, true);

        povMode = GameSettings.SwimView;
        MeasureHead();
        BuildCamera();
        SetUpBoatCamera();
        SnapToWater();
        ApplyCursorLock(true);

        // The mission begins at the helm: seated, hand on the tiller, the boat's camera live. A frame from now, once the
        // boat and its crew have set themselves up, so the crew member steps forward from the helm as when boarding.
        if (StartAboardOverride ?? startAboard) StartCoroutine(BoardAtStart());
    }

    System.Collections.IEnumerator BoardAtStart()
    {
        yield return null;
        if (!IsMounted && boat != null && boat.Hull != null) Mount();
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
        pov.Lens.NearClipPlane = 0.05f;             // close enough to the face that a shoulder or an arm is not clipped away
        pov.Priority = povPriorityActive;

        PlaceCamera();
    }

    Vector3 CameraLocalOffset() =>
        povMode == PovMode.FirstPerson
            ? Vector3.zero                          // the eye point already carries the forward offset (PlaceCamera)
            : new Vector3(0f, 0f, -(shownDistance > 0f ? shownDistance : Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance)));

    /// <summary>
    /// Finds the swimmer's head in their mesh: the vertices the head bone moves, where their middle is and how far the furthest
    /// is from it. First person puts the eye just outside that sphere, in the direction of the look, so the head stays on (its
    /// shadow, its reflection, the view from the boat) and yet nothing of it can come between the eye and the world.
    /// </summary>
    void MeasureHead()
    {
        headRadius = 0f;
        if (anim == null) return;
        var head = anim.Bone("Head");
        var skin = anim.GetComponentInChildren<SkinnedMeshRenderer>();
        if (head == null || skin == null || skin.sharedMesh == null || !skin.sharedMesh.isReadable) return;
        int index = System.Array.IndexOf(skin.bones, head);
        var weights = skin.sharedMesh.boneWeights;
        if (index < 0 || weights.Length == 0) return;

        anim.SetHeadHidden(false);
        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        var vertices = baked.vertices;
        var points = new List<Vector3>();
        for (int i = 0; i < vertices.Length && i < weights.Length; i++)
        {
            var w = weights[i];
            float onHead = (w.boneIndex0 == index ? w.weight0 : 0f) + (w.boneIndex1 == index ? w.weight1 : 0f) +
                           (w.boneIndex2 == index ? w.weight2 : 0f) + (w.boneIndex3 == index ? w.weight3 : 0f);
            if (onHead > 0.2f) points.Add(skin.transform.TransformPoint(vertices[i]));
        }
        Destroy(baked);
        if (points.Count == 0) return;

        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) bounds.Encapsulate(p);
        foreach (var p in points) headRadius = Mathf.Max(headRadius, Vector3.Distance(p, bounds.center));
        headCentreLocal = head.InverseTransformPoint(bounds.center);
    }

    /// <summary>Where the middle of the head is now, and how big it is (zero if it could not be measured).</summary>
    public bool HeadSphere(out Vector3 centre, out float radius)
    {
        var head = anim != null ? anim.Bone("Head") : null;
        centre = head != null ? head.TransformPoint(headCentreLocal) : transform.position;
        radius = headRadius;
        return head != null && headRadius > 0f;
    }

    /// <summary>The camera the swimmer sees through (null before the game starts).</summary>
    public Transform SwimCamera => pov != null ? pov.transform : null;

    /// <summary>Switches between first and third person, and keeps the setting.</summary>
    public void SetView(PovMode mode)
    {
        GameSettings.SwimView = mode;
        ApplyView(mode);
    }

    /// <summary>Third person: the camera never goes lower than this over the water, so a low look-up does not skim the surface.</summary>
    const float ThirdPersonAboveWater = 0.6f;

    float MaxPitch => povMode == PovMode.FirstPerson ? Mathf.Min(pitchLimits.y, firstPersonMaxDown) : pitchLimits.y;

    void ApplyView(PovMode mode)
    {
        if (povMode == mode) return;
        povMode = mode;
        shownDistance = 0f;
        camPitch = Mathf.Clamp(camPitch, pitchLimits.x, MaxPitch);
        if (pov != null) pov.transform.localPosition = CameraLocalOffset();
        PlaceCamera();
    }

    void OnDestroy()
    {
        if (camRig != null) Destroy(camRig.gameObject);
        ApplyCursorLock(false);
    }

    /// <summary>What the player last asked for (locked while playing); the pause menu and the end screen free it for a while.</summary>
    bool wantCursorLocked;

    void ApplyCursorLock(bool locked)
    {
        if (!lockCursor) return;
        wantCursorLocked = locked;
        SetCursor(locked && !InputHelper.GameplayLocked);
    }

    static void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    /// <summary>
    /// A locked cursor sits in the middle of the screen, so every click lands there and no menu button can be pressed.
    /// While the game is paused or over the cursor is free; back in play it is locked again for mouse look.
    /// </summary>
    void KeepCursorFreeForMenus()
    {
        if (!lockCursor) return;
        bool locked = wantCursorLocked && !InputHelper.GameplayLocked;
        if ((Cursor.lockState == CursorLockMode.Locked) != locked) SetCursor(locked);
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        KeepCursorFreeForMenus();
        if (InputHelper.ViewTogglePressed()) SetView(povMode == PovMode.FirstPerson ? PovMode.ThirdPerson : PovMode.FirstPerson);
        else ApplyView(GameSettings.SwimView);          // the settings page may have changed it
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
        camPitch = Mathf.Clamp(camPitch, pitchLimits.x, MaxPitch);

        if (povMode == PovMode.ThirdPerson)
        {
            float scroll = InputHelper.ZoomDelta();
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                cameraDistance = Mathf.Clamp(cameraDistance - scroll, minCameraDistance, maxCameraDistance);
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
        Quaternion look = Quaternion.Euler(camPitch, camYaw, 0f);
        float water = WaterSurface.Instance != null ? WaterSurface.Instance.GetHeight(anchor) : anchor.y;

        // The head is always on: from the boat, in the swimmer's shadow and in third person. In first person the eye sits just
        // outside the head, on the side the player is looking: every part of the head is then behind the eye, whichever way
        // they look, so it never fills the view.
        if (anim != null) anim.SetHeadHidden(false);
        bool firstPerson = povMode == PovMode.FirstPerson && !IsMounted && anim != null;
        if (firstPerson)
        {
            if (HeadSphere(out Vector3 centre, out float radius))
                eye = centre + look * Vector3.forward * (radius + firstPersonClearance);
            else
            {
                var head = anim.Bone("Head");
                if (head != null)
                {
                    Vector3 lookingAlong = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
                    eye = head.position + Vector3.up * firstPersonEye.x + lookingAlong * firstPersonEye.y;
                }
            }
            eye.y = Mathf.Max(eye.y, water + 0.12f);           // never looking out from under the surface
        }
        camRig.position = eye;
        camRig.rotation = look;

        if (pov == null) return;
        if (firstPerson || IsMounted)
        {
            pov.transform.localPosition = Vector3.zero;
            shownDistance = 0f;
            return;
        }

        // Third person: back along the look, but in front of any wall behind the swimmer and above the water.
        float wanted = Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance);
        float room = ClearDistanceBehind(eye, look * Vector3.back, wanted);
        Vector3 back = look * Vector3.back;
        if (back.y < -0.01f)
            room = Mathf.Min(room, Mathf.Max(0.3f, (eye.y - (water + ThirdPersonAboveWater)) / -back.y));
        // Pull in at once (a wall must never come between), ease back out.
        shownDistance = shownDistance <= 0f || room < shownDistance ? room : Mathf.MoveTowards(shownDistance, room, 6f * Time.deltaTime);
        pov.transform.localPosition = new Vector3(0f, 0f, -shownDistance);
    }

    /// <summary>
    /// How far the camera can go back from <paramref name="eye"/> before it would touch something fixed: a wall, a roof, a post.
    /// Anything with a body (the swimmer, the boat, residents, floating debris) is not in the way.
    /// </summary>
    float ClearDistanceBehind(Vector3 eye, Vector3 back, float wanted)
    {
        int mask = Physics.DefaultRaycastLayers & ~(1 << FloatingDebris.Layer);
        float best = wanted;
        foreach (var hit in Physics.SphereCastAll(eye, thirdPersonClearance, back, wanted, mask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody != null || hit.collider.GetComponent<WaterSurface>() != null) continue;
            if (hit.distance <= 0f) continue;                   // the cast starts inside it: the eye is not, so it is round the swimmer
            best = Mathf.Min(best, hit.distance);
        }
        return Mathf.Max(0.3f, best);
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
            if (povMode == PovMode.FirstPerson)
            {
                // First person: the body faces where the player looks, as their head would, and the stick swims any way
                // from there - forward, back, sideways. The eye then always sits at the front of the face, never over a
                // shoulder or the pack.
                yaw = Mathf.MoveTowardsAngle(yaw, camYaw, turnSpeed * dt);
                Vector3 flat = new Vector3(throughWater.x, 0f, throughWater.z);
                Vector3 along = amount > 0.01f ? wish.normalized
                              : flat.sqrMagnitude > 0.0001f ? flat.normalized : Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                bool backing = amount > 0.01f && input.y < -0.01f && Mathf.Abs(input.x) < Mathf.Abs(input.y);
                float top = (backing ? reverseSpeed : swimSpeed) * SpeedScale;
                float now = Vector3.Dot(throughWater, along);
                float rate1 = amount > 0.01f ? acceleration : coastDeceleration;
                planar = along * Mathf.MoveTowards(now, amount * top, rate1 * dt);
            }
            else
            {
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
        {
            var c = overlaps[i];
            if (c == boat.Hull || c == body) continue;
            // The street under shallow flood water is not in the way: the legs just reach the bottom. Anything that
            // stands up out of it (a wall, a post, a parked jeepney) still is.
            if (c.bounds.max.y < feet.y - 0.5f) continue;
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ prompt
    /// <summary>Read by the HUD: what Interact would do right now, or null if it would do nothing.</summary>
    public string InteractPrompt
    {
        get
        {
            if (IsMounted)
                return boat != null && boat.CurrentTarget != null ? null : InputHelper.Press("E", "RESCUE") + " to leave the boat";
            return CanMount() ? InputHelper.Press("E", "RESCUE") + " to board the boat" : null;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, mountRange);
    }
}
