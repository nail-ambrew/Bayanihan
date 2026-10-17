using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows the rescuer crew and the evacuees on the boat.
/// - A crew member (one of the Rescuer characters, C21-C24) minds the helm. When the player takes the tiller they
///   move forward to the bow as lookout, and go back when the player leaves.
/// - Every rescued resident is seated in the next free seat - the same character that was in the water, at the
///   same size - and is given a life vest.
/// - When the boat unloads at the evacuation centre they stand up and step ashore one after another.
/// Added to the boat automatically by SimpleBoat. Select the boat to see the seat positions as cyan/orange gizmos.
/// </summary>
[RequireComponent(typeof(SimpleBoat))]
public class BoatPassengers : MonoBehaviour
{
    [Header("Crew")]
    [Tooltip("Short ids of the rescuers shown on the boat. The first minds the helm; any others take the crew " +
             "spots after the bow lookout's. Empty = pick Rescuer characters at random.")]
    public string[] rescuerIds = { "C21" };
    [Tooltip("Size of the people on the boat. Keep at 1 so nobody changes size coming aboard.")]
    public float characterScale = 1f;

    [Header("Seats")]
    [Tooltip("Optional: your own seat positions in the boat's local space. Empty = generated from the Box Collider.")]
    public Vector3[] passengerSeats;
    [Tooltip("Optional: your own crew positions. First = helm, second = bow lookout (where the helm crew goes " +
             "while the player drives). Empty = generated (stern + bow).")]
    public Vector3[] crewSpots;
    [Tooltip("Evacuees and the lookout sit on the side tubes facing in across the boat, as they do in a real " +
             "inflatable. Off = everybody faces the bow.")]
    public bool faceInboard = true;
    [Tooltip("How far below the box collider's centre the deck is (fraction of the box half-height).")]
    [Range(-1f, 1f)] public float deckLevel = 0.35f;
    [Tooltip("Fine-tune the deck height (metres, boat local space).")]
    public float seatYOffset = 0f;
    public bool sitPassengers = true;
    [Tooltip("Tick if seated legs stick out backwards.")]
    public bool flipSitDirection = false;
    [Tooltip("Tick if people face the stern instead of the bow.")]
    public bool flipFacing = false;

    [Header("Seated pose")]
    [Tooltip("How far the thighs swing up from hanging straight down. 90 = straight out in front.")]
    [Range(0f, 110f)] public float thighBend = 85f;
    [Tooltip("How far the knees bend back down from the thigh. 90 = shins vertical.")]
    [Range(0f, 110f)] public float shinBend = 80f;
    [Tooltip("How high the hips sit above the seat point, in the character's own units.")]
    public float seatHipHeight = 0.09f;

    [Header("Tiller")]
    [Tooltip("Name of the outboard's tiller under the boat model. Whoever is at the helm rests a hand on its grip - " +
             "the end that points at the helm - and the hand follows it as the motor swings with the steering.")]
    public string tillerName = "motor_tiller";

    [Header("Life vests")]
    [Tooltip("Everybody brought aboard is put in a life vest.")]
    public bool giveLifeVests = true;
    public Color vestColor = new Color(1f, 0.42f, 0.05f);

    [Header("Unloading")]
    public float unloadDuration = 0.8f;
    [Tooltip("Seconds between one evacuee stepping off and the next.")]
    public float unloadStagger = 0.35f;
    [Tooltip("Raise/lower where evacuees land on the evacuation centre.")]
    public float landingYOffset = 0f;

    class Rider
    {
        public CharacterAnimator anim;
        public Transform body;                 // the character node, lowered so the hips rest on the seat
        public Vector3 standingLocalPos;
        public bool seated;
        public int seat = -1;                  // which passenger seat, for people rescued from the water
        public Survivor who;                   // the resident this is, for people rescued from the water

        public Transform Root => anim != null ? anim.transform : null;

        public void Stand()
        {
            if (!seated) return;
            if (anim != null)
            {
                anim.Overlays &= ~(PoseOverlay.Seated | PoseOverlay.HoldTiller);
                anim.HandTarget = null;
            }
            if (body != null) body.localPosition = standingLocalPos;
            seated = false;
        }
    }

    SimpleBoat boat;
    Vector3[] seats, crew;
    readonly List<Rider> riders = new List<Rider>();
    readonly List<Rider> crewRiders = new List<Rider>();
    Rider helmCrew;                 // minds the helm, and moves to the bow while the player drives
    Rider driver;                   // the player's own character while they are at the helm
    bool driverAboard;
    readonly HashSet<int> reservedSeats = new HashSet<int>();   // seats held for people still on their way over the side
    Transform tillerGrip;           // marker on the end of the tiller that the helmsman holds
    static Material vestMaterial;

    /// <summary>Where the player sits to drive: the helm, i.e. the first crew spot.</summary>
    public Vector3 DriverSeatLocalPosition => crew != null && crew.Length > 0 ? crew[0] : Vector3.zero;

    /// <summary>Where the bow lookout sits, in the world: where a line is thrown from. The boat's centre if there is no crew.</summary>
    public Vector3 LookoutWorldPosition
    {
        get
        {
            if (crew == null || crew.Length == 0) return transform.position;
            return transform.TransformPoint(crew.Length > 1 ? crew[1] : crew[0]);
        }
    }

    /// <summary>Crew members visibly on the boat right now. They are weight the hull carries.</summary>
    public int CrewAboard
    {
        get
        {
            int n = 0;
            foreach (var c in crewRiders)
                if (c.Root != null && c.Root.gameObject.activeSelf) n++;
            return n;
        }
    }

    /// <summary>True while a crew member is at the helm, so somebody can hold the boat where the player left it.</summary>
    public bool HasHelmCrew => !driverAboard && helmCrew != null && helmCrew.Root != null;

    /// <summary>
    /// The player has taken (or given up) the tiller. The crew member who was minding it goes forward to the bow
    /// as lookout rather than vanishing, and comes back aft when the player leaves.
    /// </summary>
    public void SetDriverAboard(bool aboard)
    {
        driverAboard = aboard;
        if (helmCrew == null || helmCrew.Root == null || crew == null || crew.Length == 0) return;

        if (crew.Length < 2)
        {
            helmCrew.Root.gameObject.SetActive(!aboard);    // nowhere else to sit
            return;
        }
        Place(helmCrew, aboard ? 1 : 0, true);
        SetTiller(helmCrew, !aboard);
    }

    /// <summary>Sits the player's character at the helm with a hand on the tiller.</summary>
    public void SeatDriver(CharacterAnimator anim)
    {
        if (anim == null) return;
        driver = MakeRider(anim);
        var t = anim.transform;
        t.localPosition = Vector3.zero;                     // the player object itself is already at the helm
        Sit(driver);
        SetTiller(driver, true);
    }

    /// <summary>Stands the player's character back up as they leave the helm.</summary>
    public void ReleaseDriver()
    {
        if (driver == null) return;
        driver.Stand();
        driver = null;
    }

    void Awake() { boat = GetComponent<SimpleBoat>(); }

    void Start()
    {
        if (CharacterLibrary.Instance == null)
        {
            Debug.LogWarning("BoatPassengers: no character library. Run Bayanihan > Rebuild Character Library.", this);
            enabled = false;
            return;
        }

        Layout(out seats, out crew);
        FindTillerGrip();
        SpawnCrew();
        ShowPackedDeckGear();
    }

    /// <summary>
    /// Hangs a marker on the end of the outboard's tiller that points at the helm. It is parented to the tiller, so it
    /// swings with the steering and so does the helmsman's hand.
    /// </summary>
    void FindTillerGrip()
    {
        var tiller = CharacterRig.FindDeep(transform, tillerName);
        if (tiller == null) return;

        Vector3 bow = boat != null ? boat.LocalBow : Vector3.forward;
        Vector3 grip = Vector3.zero;
        bool found = false;

        var filter = tiller.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable)
        {
            // The grip is the part of the tiller that reaches furthest towards the bow.
            var vertices = filter.sharedMesh.vertices;
            var points = new Vector3[vertices.Length];
            float furthest = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                points[i] = transform.InverseTransformPoint(tiller.TransformPoint(vertices[i]));
                furthest = Mathf.Max(furthest, Vector3.Dot(points[i], bow));
            }

            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (Vector3.Dot(points[i], bow) < furthest - 0.04f) continue;
                sum += points[i];
                n++;
            }
            if (n > 0) { grip = sum / n; found = true; }
        }

        if (!found)
        {
            var renderer = tiller.GetComponent<Renderer>();
            if (renderer == null) return;
            grip = transform.InverseTransformPoint(renderer.bounds.center);
        }

        var marker = new GameObject("TillerGrip").transform;
        marker.SetParent(tiller, false);
        marker.position = transform.TransformPoint(grip);
        tillerGrip = marker;
    }

    /// <summary>
    /// "Supply slots are physical space on the deck": the lifebuoy lying in the boat is only there if it was packed.
    /// </summary>
    void ShowPackedDeckGear()
    {
        var lifebuoy = CharacterRig.FindDeep(transform, "lifebuoy");
        if (lifebuoy != null) lifebuoy.gameObject.SetActive(LoadoutState.Has(RescueItemId.Lifebuoy));
    }

    // ------------------------------------------------------------------ layout
    void Layout(out Vector3[] seatPositions, out Vector3[] crewPositions)
    {
        var sb = boat != null ? boat : GetComponent<SimpleBoat>();
        var box = GetComponent<BoxCollider>();
        Vector3 c = box != null ? box.center : Vector3.zero;
        Vector3 e = box != null ? box.size * 0.5f : new Vector3(0.9f, 0.4f, 1.8f);
        Vector3 bow = sb != null ? sb.LocalBow : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, bow);

        float halfLen = Mathf.Abs(Vector3.Dot(e, bow));
        float halfWid = Mathf.Abs(Vector3.Dot(e, side));
        float y = c.y - e.y * deckLevel + seatYOffset;

        // passengers: two columns, rows running from amidships towards the stern
        if (passengerSeats != null && passengerSeats.Length > 0) seatPositions = passengerSeats;
        else
        {
            int n = Mathf.Max(1, sb != null ? sb.capacity : 4);
            seatPositions = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                int row = i / 2, col = i % 2;
                float along = 0.20f - row * 0.32f;
                float across = (col == 0 ? -1f : 1f) * 0.38f * halfWid;
                Vector3 p = c + bow * (along * halfLen) + side * across;
                p.y = y;
                seatPositions[i] = p;
            }
        }

        // crew: stern (by the motor) and bow
        if (crewSpots != null && crewSpots.Length > 0) crewPositions = crewSpots;
        else
        {
            Vector3 stern = c + bow * (-0.72f * halfLen);
            Vector3 front = c + bow * (0.66f * halfLen) + side * (0.20f * halfWid);
            stern.y = y; front.y = y;
            crewPositions = new[] { stern, front };
        }
    }

    /// <summary>Which way somebody in this spot faces, in the boat's local space.</summary>
    Quaternion SeatFacing(Vector3 localPos, bool helm, float forwardSign)
    {
        if (flipFacing) forwardSign = -forwardSign;
        Vector3 bow = boat.LocalBow;
        Vector3 look = bow;

        if (faceInboard && !helm)
        {
            // Towards the centreline, from whichever tube they are sitting on.
            Vector3 across = Vector3.Cross(Vector3.up, bow);
            var box = GetComponent<BoxCollider>();
            float centre = box != null ? Vector3.Dot(box.center, across) : 0f;
            float offset = Vector3.Dot(localPos, across) - centre;
            if (Mathf.Abs(offset) > 0.15f) look = across * -Mathf.Sign(offset);
        }
        return Quaternion.LookRotation(look * forwardSign, Vector3.up);
    }

    /// <summary>World position and rotation of a passenger seat right now (the boat is moving).</summary>
    public void SeatPose(int index, out Vector3 position, out Quaternion rotation)
    {
        Vector3 local = SeatLocal(index);
        float sign = CharacterLibrary.Instance != null && CharacterLibrary.Instance.forwardSign < 0f ? -1f : 1f;
        position = transform.TransformPoint(local);
        rotation = transform.rotation * SeatFacing(local, false, sign);
    }

    Vector3 SeatLocal(int index)
    {
        if (seats == null || seats.Length == 0) return Vector3.zero;
        return index < seats.Length
            ? seats[index]
            : seats[seats.Length - 1] + Vector3.up * 0.15f * (index - seats.Length + 1);   // fewer seats than capacity
    }

    // ------------------------------------------------------------------ spawning
    void SpawnCrew()
    {
        var library = CharacterLibrary.Instance;
        int ids = rescuerIds != null ? rescuerIds.Length : 0;
        int count = Mathf.Max(ids, 1);

        for (int i = 0; i < count; i++)
        {
            // Spot 0 is the helm. Spot 1 is kept free for the helm crew to move to, so extra crew start at 2.
            int spot = i == 0 ? 0 : i + 1;
            if (spot >= crew.Length) break;

            CharacterEntry entry = i < ids ? library.Find(rescuerIds[i]) : null;
            if (entry == null) entry = library.RandomRescuer();
            if (entry == null) break;

            var anim = CharacterRig.Spawn(entry, transform, CharacterPose.Idle);
            if (anim == null) continue;
            anim.transform.localScale = Vector3.one * characterScale;

            var rider = MakeRider(anim);
            crewRiders.Add(rider);
            Place(rider, spot, true);
            if (i == 0) { helmCrew = rider; SetTiller(rider, true); }
        }
    }

    Rider MakeRider(CharacterAnimator anim)
    {
        var body = anim.Character != null ? anim.Character : anim.transform;
        return new Rider { anim = anim, body = body, standingLocalPos = body.localPosition };
    }

    /// <summary>Puts a crew member in a crew spot, seated and facing the right way.</summary>
    void Place(Rider r, int crewSpot, bool sit)
    {
        var t = r.Root;
        Vector3 local = crew[Mathf.Clamp(crewSpot, 0, crew.Length - 1)];
        t.localPosition = local;
        t.localRotation = SeatFacing(local, crewSpot == 0, r.anim.ForwardSign());
        r.Stand();
        if (sit && sitPassengers) Sit(r);
    }

    void SetTiller(Rider r, bool holding)
    {
        if (r == null || r.anim == null) return;
        if (holding) r.anim.Overlays |= PoseOverlay.HoldTiller;
        else r.anim.Overlays &= ~PoseOverlay.HoldTiller;
        r.anim.HandTarget = holding ? tillerGrip : null;
        r.anim.Refresh();
    }

    /// <summary>
    /// Sits somebody down: the seated overlay bends the legs after every clip sample (so the upper body keeps
    /// breathing and shifting), and the body is lowered until the hips rest on the seat point.
    /// </summary>
    void Sit(Rider r)
    {
        var anim = r.anim;
        if (anim == null || anim.Bone("ThighL") == null) return;

        anim.SeatedThighBend = thighBend * (flipSitDirection ? -1f : 1f);
        anim.SeatedShinBend = shinBend * (flipSitDirection ? -1f : 1f);
        anim.Overlays |= PoseOverlay.Seated;
        anim.Play(CharacterPose.Idle);
        anim.Refresh();

        var hips = anim.Bone("Hips");
        if (hips != null)
        {
            r.body.localPosition = r.standingLocalPos;
            Vector3 hipsLocal = anim.transform.InverseTransformPoint(hips.position);
            r.body.localPosition += Vector3.down * (hipsLocal.y - seatHipHeight);
        }
        r.seated = true;
    }

    // ------------------------------------------------------------------ boarding
    /// <summary>
    /// Holds a seat for somebody who is on their way aboard, and says which one it is. The seat is on whichever
    /// tube has fewer people on it - counting the crew and the person at the helm, not only the evacuees - so the
    /// boat is not sat with everyone on one side. Between equal tubes the seats are filled in the order listed.
    /// </summary>
    public int ReserveSeat()
    {
        if (seats == null || seats.Length == 0) return riders.Count + reservedSeats.Count;

        int count = Mathf.Max(seats.Length, boat != null ? boat.capacity : 0);

        // How many people are on each tube already.
        int lower = 0, upper = 0;       // the two sides of the centreline
        foreach (var c in crewRiders)
            if (c.Root != null && c.Root.gameObject.activeSelf) CountTube(TubeOf(c.Root.localPosition), ref lower, ref upper);
        if (driverAboard) CountTube(TubeOf(DriverSeatLocalPosition), ref lower, ref upper);
        foreach (var r in riders) CountTube(TubeOf(SeatLocal(r.seat)), ref lower, ref upper);
        foreach (int s in reservedSeats) CountTube(TubeOf(SeatLocal(s)), ref lower, ref upper);

        int best = -1, bestScore = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (SeatTaken(i)) continue;
            int tube = TubeOf(SeatLocal(i));
            int load = tube < 0 ? lower : tube > 0 ? upper : 0;
            int score = load * 100 + i;
            if (score < bestScore) { bestScore = score; best = i; }
        }

        if (best < 0)
        {
            // More people than seats (SimpleBoat should never allow it): stack them above the last seat.
            best = count;
            while (SeatTaken(best)) best++;
        }

        reservedSeats.Add(best);
        return best;
    }

    bool SeatTaken(int index)
    {
        if (reservedSeats.Contains(index)) return true;
        foreach (var r in riders) if (r.seat == index) return true;
        return false;
    }

    static void CountTube(int tube, ref int lower, ref int upper)
    {
        if (tube < 0) lower++;
        else if (tube > 0) upper++;
    }

    /// <summary>-1 or +1 for the tube a spot in the boat's local space is on, 0 along the centreline.</summary>
    int TubeOf(Vector3 local)
    {
        Vector3 across = Vector3.Cross(Vector3.up, boat != null ? boat.LocalBow : Vector3.forward);
        var box = GetComponent<BoxCollider>();
        float centre = box != null ? Vector3.Dot(box.center, across) : 0f;
        float offset = Vector3.Dot(local, across) - centre;
        return Mathf.Abs(offset) < 0.15f ? 0 : (offset > 0f ? 1 : -1);
    }

    /// <summary>
    /// Takes over a character that has just been hauled aboard (see Survivor.BoardBoat) and sits them down in the
    /// seat that was held for them.
    /// </summary>
    /// <param name="boardingEpoch">The boat's BoardingEpoch when this person was picked up. If the boat has
    /// unloaded since (they were still coming over the side), they belong on shore, not on an emptied deck.</param>
    public void SeatExisting(CharacterAnimator anim, int seat, int boardingEpoch, Survivor who = null)
    {
        bool stale = boat != null && boardingEpoch != boat.BoardingEpoch;
        if (!stale) reservedSeats.Remove(seat);         // an unload has already let go of every seat held before it
        if (anim == null) return;

        if (!enabled || stale)
        {
            Destroy(anim.gameObject);
            return;
        }

        var t = anim.transform;
        Vector3 local = SeatLocal(seat);                // the seat they were hauled towards, so nobody jumps at the end
        t.SetParent(transform, false);
        t.localPosition = local;
        t.localRotation = SeatFacing(local, false, anim.ForwardSign());
        t.localScale = Vector3.one * characterScale;

        var r = MakeRider(anim);
        r.seat = seat;
        r.who = who;
        if (sitPassengers) Sit(r);
        if (giveLifeVests) PutOnVest(anim);
        riders.Add(r);
    }

    /// <summary>Called when a resident is counted aboard without a character of their own to bring.</summary>
    /// <param name="entry">Who they are, so the same person appears on the boat.</param>
    public void AddPassenger(CharacterEntry entry, int boardingEpoch, Survivor who = null)
    {
        if (!enabled) return;
        if (boat != null && boardingEpoch != boat.BoardingEpoch) return;

        int cap = boat != null ? Mathf.Max(1, boat.capacity) : seats.Length;
        if (riders.Count + reservedSeats.Count >= cap)
        {
            // Should be unreachable - SimpleBoat refuses to pick up when IsFull - but never stack bodies.
            Debug.LogWarning("BoatPassengers: refused a passenger, boat already at capacity " + cap + ".", this);
            boat?.SetStatus("Boat is full - drop residents at the evacuation centre.");
            return;
        }

        if (entry == null) entry = CharacterLibrary.Instance?.RandomCivilian();
        if (entry == null) return;

        var anim = CharacterRig.Spawn(entry, transform, CharacterPose.Idle);
        if (anim == null) return;
        SeatExisting(anim, ReserveSeat(), boardingEpoch, who);   // no hop to show: straight into a seat
    }

    /// <summary>
    /// A blocky orange life vest over the chest. Nobody rides a rescue boat without one, and it also makes the
    /// people who have been rescued easy to tell from the people who have not.
    /// </summary>
    void PutOnVest(CharacterAnimator anim)
    {
        var chest = anim.Bone("Chest");
        var spine = anim.Bone("Spine");
        var armL = anim.Bone("UpperArmL");
        var armR = anim.Bone("UpperArmR");
        if (chest == null || chest.Find("LifeVest") != null) return;

        // Sized from this person's own skeleton, so it fits a child as well as an adult.
        float width = armL != null && armR != null ? Vector3.Distance(armL.position, armR.position) * 0.95f : 0.38f;
        float height = spine != null ? Vector3.Distance(spine.position, chest.position) * 2.1f : 0.32f;

        if (vestMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            vestMaterial = new Material(shader) { name = "LifeVest" };
        }
        vestMaterial.color = vestColor;

        var vest = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vest.name = "LifeVest";
        Destroy(vest.GetComponent<Collider>());
        vest.GetComponent<MeshRenderer>().sharedMaterial = vestMaterial;

        var t = vest.transform;
        t.SetParent(chest, false);
        t.localPosition = new Vector3(0f, 0.02f, 0f);
        t.localRotation = Quaternion.identity;
        Vector3 scale = chest.lossyScale;
        t.localScale = new Vector3(width / Mathf.Max(scale.x, 0.001f), height / Mathf.Max(scale.y, 0.001f),
                                   width * 0.72f / Mathf.Max(scale.z, 0.001f));
    }

    // ------------------------------------------------------------------ unloading
    int unloading;                  // people who have not yet finished stepping ashore

    /// <summary>True while anybody is still on their way ashore. The mission report waits for this to be over.</summary>
    public bool IsUnloading => unloading > 0;

    /// <summary>
    /// Called by SimpleBoat.DropOffAll. Everyone stands up and steps ashore, one after another.
    /// </summary>
    /// <param name="residents">Filled with the residents who are getting off (those rescued from the water).</param>
    /// <param name="onLanded">Called for each of them, with the character they are, once they are standing ashore.</param>
    public void UnloadAll(Collider destination, List<Survivor> residents = null, System.Action<Survivor, Transform> onLanded = null)
    {
        for (int i = 0; i < riders.Count; i++)
        {
            if (riders[i].who != null && residents != null) residents.Add(riders[i].who);
            StartCoroutine(StepAshore(riders[i], destination, i * unloadStagger, onLanded));
        }
        riders.Clear();
        reservedSeats.Clear();
    }

    IEnumerator StepAshore(Rider r, Collider dest, float wait, System.Action<Survivor, Transform> onLanded)
    {
        unloading++;
        try { yield return StartCoroutine(Disembark(r, dest, wait)); }
        finally
        {
            // Landed (or lost on the way): either way, the desk is told so it is not left waiting for somebody.
            if (r.who != null && onLanded != null) onLanded(r.who, r.Root);
            unloading--;
        }
    }

    IEnumerator Disembark(Rider r, Collider dest, float wait)
    {
        if (wait > 0f) yield return new WaitForSeconds(wait);

        Transform c = r.Root;
        if (c == null) yield break;

        // stand up again, and get moving
        r.Stand();
        r.anim?.Play(CharacterPose.Walk);

        c.SetParent(null, true);

        Vector3 start = c.position;
        Vector3 end = LandingPoint(dest);
        Vector3 heading = end - start;
        heading.y = 0f;
        Quaternion startRotation = Quaternion.Euler(0f, c.eulerAngles.y, 0f);
        Quaternion walkRotation = heading.sqrMagnitude > 0.01f && r.anim != null
            ? Quaternion.LookRotation(heading * r.anim.ForwardSign())
            : startRotation;

        float dur = unloadDuration + heading.magnitude * 0.12f;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Vector3 p = Vector3.Lerp(start, end, k);
            p.y += Mathf.Sin(k * Mathf.PI) * 0.35f;         // a step up and over the tube, not a leap
            c.SetPositionAndRotation(p, Quaternion.Slerp(startRotation, walkRotation, Mathf.Clamp01(t * 3f)));
            yield return null;
        }
        c.position = end;
        r.anim?.Play(CharacterPose.Idle);   // safe and sound
    }

    Vector3 LandingPoint(Collider dest)
    {
        if (dest == null)
            return transform.position + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));

        // An evacuation centre knows where its own landing is (a porch, a dock) better than its trigger box does.
        var centre = dest.GetComponent<EvacuationCenter>();
        if (centre != null) return centre.RandomLandingPoint(landingYOffset);

        Bounds b = dest.bounds;
        float mx = Mathf.Min(0.6f, b.extents.x * 0.5f);
        float mz = Mathf.Min(0.6f, b.extents.z * 0.5f);
        return new Vector3(
            Random.Range(b.min.x + mx, b.max.x - mx),
            b.max.y + landingYOffset,
            Random.Range(b.min.z + mz, b.max.z - mz));
    }

    // ------------------------------------------------------------------ gizmos
    void OnDrawGizmosSelected()
    {
        Layout(out var s, out var c);
        Gizmos.color = Color.cyan;
        foreach (var p in s) Gizmos.DrawWireSphere(transform.TransformPoint(p), 0.15f);
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        foreach (var p in c) Gizmos.DrawWireSphere(transform.TransformPoint(p), 0.18f);
    }
}
