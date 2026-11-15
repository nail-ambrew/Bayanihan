using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Plays the flood-rescue mission the way a person would: with a keyboard. It has a virtual keyboard of its own and nothing
/// but key presses reaches the game - W and S for the throttle, A and D to steer, E to board and to take somebody aboard
/// (held for the first-aid treatment), T to throw a line, C to radio for the power to be cut, R on the report. It looks at the
/// game the way a player does: where the residents are, where the boat is, what the prompt says.
///
/// Where to drive comes from BoatReach: the water the boat can get through, flooded out from where it is, and a route across
/// it. So it needs no waypoints written by hand and works on any map whose colliders are solid.
///
/// It is a test helper, and a way to see a whole mission played for real: a test can drive part of it, or all of it
/// (MabuhayBotTests). Whatever it does is written to <see cref="Log"/>, and the latest line to <see cref="Last"/>, which
/// can be read from outside while it runs.
/// </summary>
public sealed class MissionBot
{
    /// <summary>The latest line logged by any bot, for reading from outside while a test runs.</summary>
    public static string Last = "";
    /// <summary>Every line logged since the bot was made, in order.</summary>
    public readonly List<string> Log = new List<string>();
    /// <summary>The same, kept by whichever bot is running, so it can be read from outside.</summary>
    public static List<string> Transcript = new List<string>();

    /// <summary>The part of the world the bot drives in, in metres east and north: it floods the water inside this.</summary>
    public Rect arena;
    /// <summary>Top speed over the ground on a long clear run (m/s).</summary>
    public float cruise = 4.8f;
    /// <summary>Called with each line as it is logged, so a test can print as it goes.</summary>
    public Action<string> onLog;
    /// <summary>
    /// Called at the moments worth a photograph (start, helm, line_nena, alongside_nena, aboard_joy, hall_ashore, hall_registering, report),
    /// and waited for. A test passes <see cref="Shot"/> here, or leaves it empty.
    /// </summary>
    public Func<string, IEnumerator> onMoment;

    SimpleBoat boat;
    PlayerRescuer player;
    BoatRescue rescue;
    Rigidbody body;
    BoatReach reach;

    Keyboard keyboard;
    readonly List<Keyboard> realKeyboards = new List<Keyboard>();
    readonly HashSet<Key> held = new HashSet<Key>();       // keys down until the bot lets go
    readonly HashSet<Key> taps = new HashSet<Key>();       // keys down for this frame only
    float steer, throttle;                                 // what the bot wants, -1 to 1; pulsed onto A/D and W/S
    float steerBank, throttleBank;
    bool stationKeeping;

    float lastMoveAt;
    Vector2 lastMovePosition;

    /// <summary>
    /// The clock every wait runs on. Real time, not game time: when a mission ends the report stops game time, and a bot that
    /// waited on it would wait for ever.
    /// </summary>
    static float Now => Time.realtimeSinceStartup;

    /// <summary>The report is up (or about to be): there is nothing more to do, and game time has stopped.</summary>
    static bool MissionOver => RescueManager.Instance != null && RescueManager.Instance.GameOver;

    // ------------------------------------------------------------------ setting up and letting go

    public MissionBot(Rect arena)
    {
        this.arena = arena;
        Transcript = new List<string>();
    }

    /// <summary>Finds the boat and the swimmer and takes hold of the keyboard. Call once the scene has loaded.</summary>
    public IEnumerator Start()
    {
        boat = UnityEngine.Object.FindFirstObjectByType<SimpleBoat>();
        player = UnityEngine.Object.FindFirstObjectByType<PlayerRescuer>();
        if (boat == null || player == null) throw new InvalidOperationException("MissionBot: no boat or no swimmer in the scene.");
        rescue = boat.GetComponent<BoatRescue>();
        body = boat.GetComponent<Rigidbody>();
        StartKeyboard();
        yield return null;                                  // a frame, so the device exists before the first key
        say("keyboard ready");
    }

    /// <summary>Lets go of every key, and gives the real keyboard back.</summary>
    public void Stop()
    {
        if (keyboard != null)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.RemoveDevice(keyboard);
            keyboard = null;
        }
        foreach (var k in realKeyboards) if (k != null) InputSystem.EnableDevice(k);
        realKeyboards.Clear();
    }

    void StartKeyboard()
    {
        // Keep listening to a virtual keyboard even when the Editor window is not in front.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

        // Devices outlive Play mode: one left over from before would swallow the keys.
        foreach (var d in InputSystem.devices.ToArray())
            if (d.name.StartsWith("Virtual", StringComparison.Ordinal)) InputSystem.RemoveDevice(d);

        keyboard = InputSystem.AddDevice<Keyboard>("VirtualKeyboard");
        keyboard.MakeCurrent();
        foreach (var device in InputSystem.devices.ToArray())
        {
            if (!(device is Keyboard real) || real == keyboard || !real.enabled) continue;
            realKeyboards.Add(real);
            InputSystem.DisableDevice(real);
        }
    }

    void say(string line)
    {
        string stamped = "[" + (RescueManager.Instance != null ? RescueManager.Instance.Elapsed.ToString("0") : "-") + " s] " + line;
        Log.Add(stamped);
        Transcript.Add(stamped);
        Last = stamped;
        onLog?.Invoke(stamped);
    }

    // ------------------------------------------------------------------ keys

    /// <summary>Ends the frame: puts this frame's keys on the keyboard and lets the game see them.</summary>
    IEnumerator Frame()
    {
        var keys = new List<Key>(held);
        keys.AddRange(taps);
        taps.Clear();

        // The throttle and the wheel are pulsed: a key that is down one frame in four is a quarter of full, to a boat
        // whose controls smooth the keys out like a stick.
        throttleBank += Mathf.Abs(throttle);
        if (throttleBank >= 1f) { throttleBank -= 1f; keys.Add(throttle >= 0f ? Key.W : Key.S); }
        steerBank += Mathf.Abs(steer);
        if (steerBank >= 1f) { steerBank -= 1f; keys.Add(steer >= 0f ? Key.D : Key.A); }

        if (keyboard != null) InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
        yield return null;
    }

    /// <summary>
    /// Saves what is on the screen (the game, and the HUD drawn over it) as a PNG under Temp/MabuhayBot, to be looked at. The
    /// file appears once the frame has been drawn.
    /// </summary>
    public IEnumerator Shot(string name)
    {
        string folder = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "MabuhayBot");
        System.IO.Directory.CreateDirectory(folder);
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, name + ".png"));
        yield return Frame();
        yield return Frame();
        say("photographed " + name);
    }

    /// <summary>One press of a key, over a frame, and a frame to let go.</summary>
    IEnumerator Tap(Key key)
    {
        taps.Add(key);
        yield return Frame();
        yield return Frame();
    }

    IEnumerator Wait(float seconds)
    {
        float end = Now + seconds;
        while (Now < end) yield return Frame();
    }

    // ------------------------------------------------------------------ the boat's state, as a player would read it

    Vector2 Position => new Vector2(boat.transform.position.x, boat.transform.position.z);
    Vector2 Heading { get { var h = boat.HeadingForward; return new Vector2(h.x, h.z); } }

    /// <summary>How far the bow is to the right of the way to a point, in degrees (negative = to the left).</summary>
    float BearingTo(Vector2 point) => -Vector2.SignedAngle(Heading, point - Position);

    /// <summary>The resident the mission calls this (joy, ben...), found among those still in the world.</summary>
    public static Survivor Resident(string id)
    {
        var s = TryResident(id);
        if (s == null) throw new InvalidOperationException("MissionBot: " + id + " is not in the scene.");
        return s;
    }

    /// <summary>
    /// The resident, if they are still in the world. Somebody who has been taken aboard has left the world (their object is switched
    /// off once they are seated), so null means they were already brought in, perhaps by an E press meant for somebody else.
    /// </summary>
    public static Survivor TryResident(string id)
    {
        var spec = MissionDefinition.Current.residents.FirstOrDefault(r => r.id == id);
        if (spec == null) throw new InvalidOperationException("MissionBot: the mission has nobody called '" + id + "'.");
        return Survivor.All.FirstOrDefault(r => r.residentName == spec.displayName);
    }

    // ------------------------------------------------------------------ driving

    /// <summary>Floods the water from where the boat is now. Cheap after the first time: the cells already asked about are remembered.</summary>
    void ReadTheWater()
    {
        if (reach == null)
        {
            reach = new BoatReach(arena, 1f, BoatReach.DefaultRadius, 0f);
            // Live water is not somewhere to drive: it stalls the engine.
            reach.Avoid = xz =>
            {
                var at = new Vector3(xz.x, 0f, xz.y);
                foreach (var zone in LiveWireZone.All)
                    if (zone != null && zone.IsLive && zone.DistanceToEdge(at) < 2f) return true;
                // Wreckage is pushed aside, not driven through: keep a boat's width from each piece.
                foreach (var piece in FloatingDebris.All)
                    if (piece != null && (new Vector2(piece.transform.position.x, piece.transform.position.z) - xz).sqrMagnitude < 2.6f * 2.6f) return true;
                return false;
            };
        }
        Physics.SyncTransforms();
        reach.Flood(new Vector3(Position.x, 0f, Position.y));
    }

    /// <summary>The way from where the boat is now to somewhere: the corners of a path through the water it can get through.</summary>
    List<Vector2> RouteTo(Vector2 target, float search)
    {
        ReadTheWater();
        return reach.RouteTo(target, search);
    }

    /// <summary>Steers for a point and works the throttle towards a speed over the ground, for this frame.</summary>
    void Aim(Vector2 at, float groundSpeed)
    {
        float error = BearingTo(at);
        float yawRate = body.angularVelocity.y * Mathf.Rad2Deg;                  // positive = swinging right
        steer = Mathf.Clamp((error - 0.2f * yawRate) / 22f, -1f, 1f);

        float wrong = Mathf.Abs(error);
        if (wrong > 70f) groundSpeed = Mathf.Min(groundSpeed, 1.0f);
        else if (wrong > 35f) groundSpeed = Mathf.Min(groundSpeed, 2.8f);
        Speed(groundSpeed);
    }

    /// <summary>
    /// Works the throttle towards a speed over the ground along the bow. The motor pushes against the water and the water is
    /// moving, so against a current of a metre a second a crawl over the ground needs a metre a second through the water.
    /// </summary>
    void Speed(float groundSpeed)
    {
        float flowAlong = Vector3.Dot(FloodCurrent.Sample(boat.transform.position), boat.HeadingForward);
        float through = groundSpeed - flowAlong;
        float difference = through - boat.ForwardSpeed;
        throttle = difference > 0.2f ? Mathf.Clamp(difference / 1.5f, 0f, 1f) :
                   difference < -0.8f ? -Mathf.Clamp(-difference / 3f, 0.2f, 1f) : 0f;
    }

    void LetGoOfTheWheel() { steer = 0f; throttle = 0f; }

    /// <summary>Holds the boat where it is against the current: a little throttle upstream, as a helmsman does.</summary>
    void KeepStation()
    {
        Speed(0f);
        steer = 0f;
    }

    /// <summary>
    /// Follows a route (the corners of a path through the water): steers for the furthest corner it can see in a straight run,
    /// slows for the end and for sharp turns, and gets itself out when it is stuck. Done when <paramref name="done"/> says so.
    /// </summary>
    IEnumerator Follow(Func<List<Vector2>> plan, Func<bool> done, float stopWithin, float timeout, string what, Func<float> limit = null)
    {
        var route = plan();
        if (route.Count < 2) { say("no way to " + what); yield break; }
        float end = Now + timeout;
        float replanAt = Now + 3f;
        int progress = 0;
        lastMovePosition = Position;
        lastMoveAt = Now;

        while (Now < end)
        {
            if (MissionOver || (done != null && done())) { yield break; }

            // Wreckage drifts and the other things in the water move: look again at the water every few seconds.
            if (Now >= replanAt)
            {
                replanAt = Now + 3f;
                var fresh = plan();
                if (fresh.Count >= 2) { route = fresh; progress = 0; }
            }

            Vector2 here = Position;
            while (progress + 1 < route.Count && Vector2.Distance(here, route[progress + 1]) < Vector2.Distance(here, route[progress])) progress++;

            // The furthest corner in plain view along the way, within a boat's reach of a turn.
            Vector2 aim = route[Mathf.Min(progress + 1, route.Count - 1)];
            for (int i = route.Count - 1; i > progress; i--)
            {
                if (Vector2.Distance(here, route[i]) > 18f) continue;
                if (reach.StraightRunIsClear(here, route[i])) { aim = route[i]; break; }
            }

            float remaining = RouteLength(route, progress, here);

            float speed = Mathf.Min(cruise, Mathf.Sqrt(2f * 2.2f * Mathf.Max(0f, remaining - stopWithin)) + 0.5f);
            if (limit != null) speed = Mathf.Min(speed, limit());
            Aim(aim, speed);

            if (Now - lastMoveAt > 4f)
            {
                if (Vector2.Distance(here, lastMovePosition) < 0.8f && speed > 0.8f)
                {
                    yield return Unstick(route[Mathf.Min(progress + 1, route.Count - 1)]);
                }
                lastMovePosition = Position;
                lastMoveAt = Now;
            }
            yield return Frame();
        }
        say("gave up getting to " + what + " after " + timeout + " s");
    }

    static float RouteLength(List<Vector2> route, int progress, Vector2 here)
    {
        float length = Vector2.Distance(here, route[Mathf.Min(progress + 1, route.Count - 1)]);
        for (int i = progress + 1; i + 1 < route.Count; i++) length += Vector2.Distance(route[i], route[i + 1]);
        return length;
    }

    /// <summary>Wedged against something: back off, turned a little, and have another go.</summary>
    IEnumerator Unstick(Vector2 towards)
    {
        say("stuck at " + Position.x.ToString("0.0") + ", " + Position.y.ToString("0.0") + ": backing off");
        float bearing = BearingTo(towards);
        float end = Now + 2.2f;
        while (Now < end)
        {
            steer = bearing >= 0f ? -1f : 1f;          // stern swings towards where the bow was wanted
            throttle = -1f;
            yield return Frame();
        }
        LetGoOfTheWheel();
    }

    /// <summary>Brakes to nearly a stop, and holds there.</summary>
    IEnumerator Halt(float seconds = 6f)
    {
        float end = Now + seconds;
        while (Now < end && boat.GroundSpeed > 0.9f && !MissionOver)
        {
            KeepStation();
            yield return Frame();
        }
        LetGoOfTheWheel();
    }

    // ------------------------------------------------------------------ the mission, step by step

    /// <summary>E from the water beside the boat: climbs aboard and takes the helm.</summary>
    public IEnumerator Board()
    {
        for (int tries = 0; tries < 6 && !player.IsMounted; tries++)
        {
            yield return Tap(Key.E);
            yield return Wait(0.5f);
        }
        say(player.IsMounted ? "at the helm" : "could not climb aboard");
        yield return Wait(0.6f);
        yield return Moment("helm");
    }

    /// <summary>C: asks for the power to be cut at any live wire in range of the Radyo.</summary>
    public IEnumerator RadioForThePowerCut()
    {
        foreach (var zone in LiveWireZone.All.ToArray())
        {
            if (zone == null || !zone.IsLive || zone.CutRequested) continue;
            if (zone.DistanceToEdge(boat.transform.position) > zone.requestRange) { say("too far from the live wire to radio it in"); continue; }
            yield return Tap(Key.C);
            say(zone.CutRequested ? "radioed for the power to be cut" : "the radio call did not go through");
        }
    }

    /// <summary>Brings the boat up to somebody in the water or on a post and takes them aboard.</summary>
    public IEnumerator Pick(Survivor s)
    {
        say("picking up " + s.DisplayName + " (" + s.situation + (s.Injured ? ", hurt" : "") + ")");
        yield return WaitUntilSafe(s);
        if (s.IsRescued) yield break;

        for (int attempt = 0; attempt < 4 && !s.IsRescued && !MissionOver; attempt++)
        {
            yield return Follow(() => RouteTo(new Vector2(s.transform.position.x, s.transform.position.z), 10f),
                                () => boat.IsInReach(s) && boat.SpeedRelativeTo(s) < 2.2f, 1.5f, 90f, s.DisplayName,
                                () => Mathf.Clamp(Vector3.Distance(boat.transform.position, s.transform.position) * 0.5f, 1.0f, cruise));
            yield return Halt(4f);
            yield return TakeAboard(s);
            if (s.IsRescued) break;
            say(s.DisplayName + " is not aboard yet: " + (boat.CurrentTarget == s ? Why(s) : "not in reach") + ", trying again");
        }
        yield return WaitUntilHauled(s);
        yield return Moment("aboard_" + IdOf(s));
    }

    IEnumerator Moment(string name)
    {
        if (onMoment != null) yield return onMoment(name);
    }

    static string IdOf(Survivor s) => s.DisplayName.Split(' ')[0].ToLowerInvariant();

    string Why(Survivor s) { s.CanBeRescued(out string reason); return reason ?? "?"; }

    /// <summary>Waits while the water round them is live: nobody is taken out of a live wire.</summary>
    IEnumerator WaitUntilSafe(Survivor s)
    {
        float end = Now + 60f;
        while (s != null && s.InLiveWire && !s.IsRescued && Now < end && !MissionOver)
        {
            KeepStation();
            yield return Frame();
        }
        LetGoOfTheWheel();
    }

    /// <summary>E, with the boat nearly still beside them: a press, or - for somebody hurt - a hold of two seconds.</summary>
    IEnumerator TakeAboard(Survivor s)
    {
        float end = Now + 12f;
        while (!s.IsRescued && Now < end && !MissionOver)
        {
            if (!boat.IsInReach(s)) yield break;
            bool hold = s.RequiresStabilise;
            if (hold)
            {
                held.Add(Key.E);
                float holdEnd = Now + 3f;
                while (!s.IsRescued && Now < holdEnd && boat.IsInReach(s) && !MissionOver)
                {
                    KeepStation();
                    yield return Frame();
                }
                held.Remove(Key.E);
                yield return Frame();
            }
            else
            {
                yield return Tap(Key.E);
                yield return Wait(0.6f);
            }
        }
        LetGoOfTheWheel();
    }

    /// <summary>Until the person who has been taken aboard has been hauled up and sits in the boat.</summary>
    IEnumerator WaitUntilHauled(Survivor s)
    {
        float end = Now + 10f;
        while (s != null && s.gameObject.activeSelf && Now < end && !MissionOver)
        {
            KeepStation();
            yield return Frame();
        }
        LetGoOfTheWheel();
        if (s != null && s.IsRescued) say(s.DisplayName + " is aboard (" + boat.Passengers + "/" + boat.capacity + ")");
    }

    /// <summary>
    /// Somebody on a roof or behind wreckage: finds water the boat can reach from which a line can be thrown to them, goes there,
    /// faces them, throws (T), waits for them to come alongside, and takes them aboard.
    /// </summary>
    public IEnumerator Throw(Survivor s)
    {
        say("throwing a line to " + s.DisplayName + " (" + s.situation + ")");
        yield return WaitUntilSafe(s);

        var triedAlready = new List<Vector2>();
        for (int attempt = 0; attempt < 6 && !s.IsRescued && !MissionOver; attempt++)
        {
            ReadTheWater();
            Vector2 where;
            if (!StandOffFor(s, triedAlready, out where)) { say("nowhere to throw to " + s.DisplayName + " from"); yield break; }
            Vector2 them = new Vector2(s.transform.position.x, s.transform.position.z);
            yield return Follow(() => RouteTo(where, 4f), () => Vector2.Distance(Position, where) < 2.2f, 1.2f, 90f, "the place to throw to " + s.DisplayName);
            yield return Halt(5f);

            // Face them (the throw goes to whoever is nearest the middle of the view), then throw.
            float faceEnd = Now + 5f;
            while (Now < faceEnd && Mathf.Abs(BearingTo(them)) > 12f && !MissionOver)
            {
                KeepStation();
                steer = Mathf.Clamp(BearingTo(them) / 20f, -1f, 1f);
                yield return Frame();
            }

            float throwEnd = Now + 25f;
            while (!s.IsHeld && !s.LineDelivered && !s.IsRescued && Now < throwEnd && !MissionOver)
            {
                KeepStation();
                steer = Mathf.Clamp(BearingTo(them) / 25f, -1f, 1f);
                if (rescue.Target == s && !rescue.PromptIsWarning) { yield return Tap(Key.T); yield return Wait(0.8f); }
                else yield return Frame();
            }
            if (!s.IsHeld && !s.LineDelivered && !s.IsRescued)
            {
                say("no line went to " + s.DisplayName + " (" + (rescue.Prompt ?? "no prompt") + ") from " + where.x.ToString("0") + ", " + where.y.ToString("0") + ": trying somewhere else");
                triedAlready.Add(where);
                continue;
            }

            yield return Moment("line_" + IdOf(s));

            // They come in on the line: walk to the roof's edge and down, or across the water behind the wreckage.
            float pullEnd = Now + 60f;
            while (!s.LineDelivered && !s.IsRescued && Now < pullEnd && !MissionOver)
            {
                KeepStation();
                yield return Frame();
            }
            if (s.LineDelivered || s.IsRescued) break;
            say("the line to " + s.DisplayName + " was lost: again");
        }

        if (!s.IsRescued && !MissionOver)
        {
            say(s.DisplayName + " is alongside");
            yield return Moment("alongside_" + IdOf(s));
            for (int attempt = 0; attempt < 5 && !s.IsRescued && !MissionOver; attempt++)
            {
                yield return TakeAboard(s);
                if (!s.IsRescued) yield return Wait(0.8f);
            }
        }
        yield return WaitUntilHauled(s);
        yield return Moment("aboard_" + IdOf(s));
    }

    /// <summary>
    /// The water, reachable and near, from which a line can go out to them: in range of the lookout's hands and in plain view of
    /// their head. The nearest such place to where the boat is.
    /// </summary>
    bool StandOffFor(Survivor s, List<Vector2> notThere, out Vector2 where)
    {
        float hands = boat.transform.position.y + 0.66f + 1.4f;            // where the lookout's hands will be, over the water
        return reach.ThrowPointFor(s.SightPoint, hands, 10.8f, out where, reject: xz => notThere.Any(bad => Vector2.Distance(bad, xz) < 3.5f));
    }

    /// <summary>Drives to the middle of a zone (a trigger box) and stops there.</summary>
    public IEnumerator DriveToThe(Collider zone)
    {
        Vector2 centre = new Vector2(zone.bounds.center.x, zone.bounds.center.z);
        yield return Follow(() => RouteTo(centre, 8f), () => Vector2.Distance(Position, centre) < 2f, 1.0f, 150f, zone.name);
        yield return Halt(5f);
    }

    /// <summary>Takes whoever is aboard to the Barangay Hall, slowly enough to let them off, and waits for the desk to write them down.</summary>
    public IEnumerator DeliverToTheHall()
    {
        var evac = UnityEngine.Object.FindFirstObjectByType<EvacuationCenter>();
        var zone = evac.GetComponent<Collider>();
        Vector2 centre = new Vector2(zone.bounds.center.x, zone.bounds.center.z);
        int aboard = boat.Passengers;
        say("taking " + aboard + " to the hall");

        for (int attempt = 0; attempt < 3 && boat.Passengers > 0 && !MissionOver; attempt++)
        {
            yield return Follow(() => RouteTo(centre, 8f), () => boat.Passengers == 0, 1.0f, 150f, "the hall",
                                () => Vector2.Distance(Position, centre) < 14f ? 1.8f : cruise);
            float unloadEnd = Now + 12f;
            while (boat.Passengers > 0 && Now < unloadEnd && !MissionOver)
            {
                // Inside the zone and slow: they step off. Ease up to the middle of it.
                Aim(centre, Vector2.Distance(Position, centre) > 2.5f ? 1.0f : 0.3f);
                yield return Frame();
            }
            LetGoOfTheWheel();
        }

        // They are stepping ashore one after another; then the desk writes them down.
        float ashoreAt = Now + 2.5f;
        while (Now < ashoreAt) { KeepStation(); yield return Frame(); }
        yield return Moment("hall_ashore");

        float registerEnd = Now + 60f;
        bool photographed = false;
        while (evac.Desk != null && evac.Desk.Pending > 0 && Now < registerEnd && !MissionOver)
        {
            KeepStation();
            if (!photographed && evac.Desk.Current != null) { photographed = true; yield return Moment("hall_registering"); }
            yield return Frame();
        }
        LetGoOfTheWheel();
        var manager = RescueManager.Instance;
        say(aboard + " delivered; " + manager.Evacuated + "/" + manager.TotalSurvivors + " safe");
    }

    /// <summary>The whole mission: board, radio, and each trip in turn, until the report is on the screen.</summary>
    public IEnumerator PlayMission(string[][] trips)
    {
        yield return Moment("start");
        yield return Board();
        yield return RadioForThePowerCut();
        foreach (var trip in trips)
        {
            foreach (var id in trip)
            {
                if (MissionOver) break;
                var s = TryResident(id);
                if (s == null || s.IsRescued) { say(id + " is already aboard"); continue; }
                if (s.RequiresLine) yield return Throw(s);
                else yield return Pick(s);
            }
            yield return DeliverToTheHall();
            if (MissionOver) break;
        }

        var manager = RescueManager.Instance;
        float reportBy = Now + 90f;
        while (!manager.ReportVisible && Now < reportBy) yield return Frame();
        yield return Moment("report");
        say("mission over: " + manager.Evacuated + "/" + manager.TotalSurvivors + " safe, score " + manager.Score);
    }

    /// <summary>Presses R, as a player does on the report screen.</summary>
    public IEnumerator PressR()
    {
        yield return Tap(Key.R);
    }
}
