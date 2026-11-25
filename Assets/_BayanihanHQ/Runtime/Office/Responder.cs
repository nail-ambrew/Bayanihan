using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BayanihanHQ
{
    /// <summary>
    /// One responder in the office: a character from the game's own CharacterLibrary (Filipino rescuers and residents),
    /// in a vest and hard hat of their role colour. It acts out its desk's status:
    /// Working types (hands on the keyboard), Thinking looks at the monitor, Waiting raises a hand under a pulsing marker,
    /// Error slumps, Done waves with a little confetti. After a minute idle it walks to the break corner, and a newly
    /// started subagent walks in from the door. Poses reuse the game's CharacterPoses overlays; walking uses the NavMesh.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class Responder : MonoBehaviour
    {
        /// <summary>Where the body is.</summary>
        public enum Mode { Hidden, Desk, WalkingToDesk, WalkingToBreak, OnBreak }

        public const float WanderAfterIdleSeconds = 60f;

        public AgentState Agent { get; private set; }
        public DeskStation Desk { get; private set; }
        public CharacterAnimator Anim { get; private set; }
        /// <summary>The character's model; moved down when seated so the hips rest on the chair.</summary>
        public Transform Body { get; private set; }
        public NavMeshAgent Nav { get; private set; }
        public SpeechBubble Bubble { get; private set; }
        public Mode Where { get; private set; } = Mode.Hidden;
        public bool Seated { get; private set; }
        /// <summary>World position just above the head, for bubbles, markers and screen indicators.</summary>
        public Vector3 HeadTop => (head != null ? head.position : transform.position + Vector3.up * 1.3f) + Vector3.up * 0.32f;

        static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        static Material confettiMaterial;

        HqOffice office;
        Transform head;
        Transform marker;
        ParticleSystem confetti;
        Vector3 walkGoal;
        Quaternion walkFacing;
        int breakSpot = -1;
        bool glideToGoal;
        float glideT;
        Vector3 glideFrom;
        float appearAt = -1;
        float typingPhase;
        AgentStatus lastStatus;
        string lastActivity = "";
        float doneWaveUntil;

        /// <summary>Makes the responder for one desk. Returns null if the character library can't build anyone.</summary>
        public static Responder Create(AgentState agent, DeskStation desk, HqOffice office, CharacterEntry entry)
        {
            var root = new GameObject("Responder " + agent.Name);
            root.transform.SetParent(office.transform, false);
            root.transform.SetPositionAndRotation(desk.seat.position, desk.seat.rotation);
            var r = root.AddComponent<Responder>();
            r.Agent = agent;
            r.Desk = desk;
            r.office = office;
            r.typingPhase = Random.value * 10f;

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            r.Body = body;
            r.Anim = CharacterRig.Spawn(entry, body);
            if (r.Anim == null)
            {
                Destroy(root);
                return null;
            }
            r.Anim.animateWithinDistance = 1000f;   // the office camera sits far back; keep everyone moving
            r.Anim.speedRange = new Vector2(0.95f, 1.05f);
            // The rig faces along its own +Z or -Z depending on the model; turn the body so it faces the desk.
            if (r.Anim.ForwardSign() < 0) body.localRotation = Quaternion.Euler(0, 180, 0);
            r.head = r.Anim.Bone("Head");
            r.Dress(agent.Entry.Colour);
            r.MakeMarker();
            r.Bubble = SpeechBubble.Create(root.transform);

            r.Nav = root.AddComponent<NavMeshAgent>();
            r.Nav.radius = 0.28f;
            r.Nav.height = 1.7f;
            r.Nav.speed = 3.2f;   // a brisk walk: the room is 24 m across and a demo should not wait on it
            r.Nav.angularSpeed = 600f;
            r.Nav.acceleration = 10f;
            r.Nav.stoppingDistance = 0.05f;
            r.Nav.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            r.Nav.enabled = false;   // switched on only while walking, so a seated body isn't pushed off its chair

            r.SitDown();
            r.Where = Mode.Desk;
            return r;
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Called by HqOffice every frame with the desk's current state.</summary>
        public void Tick(long nowMs)
        {
            var a = Agent;
            bool shouldShow = a.Present || a.IsLead;

            if (!shouldShow)
            {
                if (Where != Mode.Hidden) Hide();
                return;
            }
            if (Where == Mode.Hidden)
            {
                // Wait a moment before appearing: the hand-off and the SubagentStart arrive a few hundred ms apart, and a
                // freshly started subagent should walk in from the door rather than pop into its chair.
                if (appearAt < 0) appearAt = Time.time + 0.6f;
                if (Time.time < appearAt) return;
                appearAt = -1;
                gameObject.SetActive(true);
                if (nowMs - a.SpawnedAt < 8000) WalkInFromDoor();
                else { SitDown(); Where = Mode.Desk; }
            }

            bool wantDesk = a.Status != AgentStatus.Idle;
            bool wantBreak = a.Status == AgentStatus.Idle && a.IdleFor(nowMs) >= WanderAfterIdleSeconds * 1000;
            switch (Where)
            {
                case Mode.Desk:
                    if (wantBreak) GoToBreak();
                    break;
                case Mode.OnBreak:
                case Mode.WalkingToBreak:
                    if (wantDesk) GoToDesk();
                    break;
            }

            if (Where == Mode.WalkingToDesk || Where == Mode.WalkingToBreak) StepWalk();

            if (a.Status == AgentStatus.Done && lastStatus != AgentStatus.Done) Celebrate();
            Bubble.Follow(HeadTop + Vector3.up * 0.25f);
            UpdateBubble(nowMs);
            lastStatus = a.Status;
        }

        /// <summary>Poses go on after the clip and the game's own overlays have been applied this frame.</summary>
        void LateUpdate()
        {
            if (Where == Mode.Hidden || Anim == null) return;
            var a = Agent;
            var overlays = PoseOverlay.None;

            if (Seated)
            {
                overlays |= PoseOverlay.Seated;
                switch (a.Status)
                {
                    case AgentStatus.Working:
                        overlays |= PoseOverlay.HoldOn;
                        Type();
                        break;
                    case AgentStatus.Thinking:
                        overlays |= PoseOverlay.HeadLook;
                        Anim.LookTarget = Desk.screenAnchor != null ? Desk.screenAnchor.position : transform.position + transform.forward;
                        break;
                    case AgentStatus.Waiting:
                        overlays |= PoseOverlay.ShoutCall;
                        break;
                    case AgentStatus.Error:
                        overlays |= PoseOverlay.Limp;
                        break;
                }
                if (Time.time < doneWaveUntil) overlays = PoseOverlay.Seated | PoseOverlay.WaveForHelp;
            }
            else if (Where == Mode.OnBreak)
            {
                if (Time.time < doneWaveUntil) overlays |= PoseOverlay.WaveForHelp;
            }

            if (Anim.Overlays != overlays)
            {
                Anim.Overlays = overlays;
                Anim.Refresh();
            }

            // The waiting marker: a yellow diamond that bobs and pulses above the head.
            bool waiting = a.Status == AgentStatus.Waiting;
            if (marker != null)
            {
                if (marker.gameObject.activeSelf != waiting) marker.gameObject.SetActive(waiting);
                if (waiting)
                {
                    float t = Time.time;
                    marker.position = HeadTop + Vector3.up * (0.95f + 0.08f * Mathf.Sin(t * 4f));
                    marker.rotation = Quaternion.Euler(0, t * 120f, 45f) * Quaternion.Euler(45f, 0, 0);
                    marker.localScale = Vector3.one * (0.26f + 0.05f * Mathf.Sin(t * 8f));
                }
            }
        }

        /// <summary>Both hands on the keyboard, tapping out of step with each other.</summary>
        void Type()
        {
            var keyboard = Desk.transform.TransformPoint(new Vector3(0, 0.79f, -0.14f));
            var right = Desk.transform.right;
            float t = Time.time * 14f + typingPhase;
            Anim.RightHandPoint = keyboard + right * 0.11f + Vector3.up * (0.02f * Mathf.Max(0, Mathf.Sin(t)));
            Anim.LeftHandPoint = keyboard - right * 0.11f + Vector3.up * (0.02f * Mathf.Max(0, Mathf.Sin(t * 1.3f + 1.7f)));
        }

        void UpdateBubble(long nowMs)
        {
            var a = Agent;
            var activity = a.Activity ?? "";
            switch (a.Status)
            {
                case AgentStatus.Waiting:
                    Bubble.Show(HqUi.Display(string.IsNullOrEmpty(activity) ? "Needs your approval" : activity), SpeechBubble.Tone.Waiting, 9999f);
                    break;
                case AgentStatus.Error:
                    Bubble.Show(HqUi.Display(activity), SpeechBubble.Tone.Error, 9999f);
                    break;
                case AgentStatus.Working:
                case AgentStatus.Thinking:
                    if (activity != lastActivity || lastStatus == AgentStatus.Waiting || lastStatus == AgentStatus.Error)
                        Bubble.Show(HqUi.Display(activity), SpeechBubble.Tone.Normal, a.Status == AgentStatus.Working ? 6f : 3f);
                    break;
                case AgentStatus.Done:
                    if (lastStatus != AgentStatus.Done) Bubble.Show("Tapos na!  <size=70%>(done)</size>", SpeechBubble.Tone.Done, 2.5f);
                    break;
                default:
                    if (lastStatus == AgentStatus.Waiting || lastStatus == AgentStatus.Error) Bubble.Hide();
                    break;
            }
            lastActivity = activity;
        }

        // ------------------------------------------------------------------ moving

        /// <summary>A newly started subagent: appears at the entrance and walks to its desk.</summary>
        public void WalkInFromDoor()
        {
            gameObject.SetActive(true);
            if (office.door != null) transform.SetPositionAndRotation(office.door.position, office.door.rotation);
            Where = Mode.Hidden;
            GoToDesk();
        }

        /// <summary>A subagent of this role was started while the responder is already in the room.</summary>
        public void OnSpawned()
        {
            if (Where == Mode.OnBreak || Where == Mode.WalkingToBreak) GoToDesk();
        }

        void GoToDesk()
        {
            office.ReleaseBreakSpot(breakSpot);
            breakSpot = -1;
            WalkTo(Desk.seat.position, Desk.seat.rotation, Mode.WalkingToDesk);
        }

        void GoToBreak()
        {
            breakSpot = office.ClaimBreakSpot();
            if (breakSpot < 0) return;   // the corner is full; stay put
            var spot = office.breakSpots[breakSpot].position;
            var lookAt = office.BreakCentre - spot;
            lookAt.y = 0;
            WalkTo(spot, lookAt.sqrMagnitude > 0.01f ? Quaternion.LookRotation(lookAt) : Quaternion.identity, Mode.WalkingToBreak);
        }

        void WalkTo(Vector3 goal, Quaternion facing, Mode mode)
        {
            StandUp();
            walkGoal = goal;
            walkFacing = facing;
            Where = mode;
            glideToGoal = false;
            Anim.SpeedScale = 1.7f;   // the walk cycle keeps up with the faster pace
            Anim.Play(CharacterPose.Walk);

            // Chairs and desks are cut out of the NavMesh, so start and end on the nearest walkable point and glide the
            // last few centimetres.
            if (NavMesh.SamplePosition(transform.position, out var from, 2f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(goal, out var to, 2f, NavMesh.AllAreas))
            {
                Nav.enabled = true;
                Nav.Warp(from.position);
                Nav.SetDestination(to.position);
            }
            else
            {
                Nav.enabled = false;   // no NavMesh (not baked): walk straight there
            }
        }

        void StepWalk()
        {
            if (glideToGoal)
            {
                glideT += Time.deltaTime / 0.35f;
                transform.position = Vector3.Lerp(glideFrom, walkGoal, glideT);
                transform.rotation = Quaternion.Slerp(transform.rotation, walkFacing, glideT);
                if (glideT >= 1f) Arrive();
                return;
            }

            bool arrived;
            if (Nav.enabled)
            {
                arrived = !Nav.pathPending && Nav.remainingDistance <= 0.12f;
                if (!Nav.pathPending && Nav.pathStatus == NavMeshPathStatus.PathInvalid) arrived = true;
            }
            else
            {
                var to = walkGoal - transform.position;
                to.y = 0;
                float step = 3.2f * Time.deltaTime;
                if (to.magnitude > 0.05f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 10f * Time.deltaTime);
                transform.position = Vector3.MoveTowards(transform.position, walkGoal, step);
                arrived = to.magnitude <= step;
            }
            if (!arrived) return;
            if (Nav.enabled) Nav.enabled = false;
            glideToGoal = true;
            glideT = 0;
            glideFrom = transform.position;
        }

        void Arrive()
        {
            glideToGoal = false;
            Anim.SpeedScale = 1f;
            transform.SetPositionAndRotation(walkGoal, walkFacing);
            if (Where == Mode.WalkingToDesk)
            {
                SitDown();
                Where = Mode.Desk;
            }
            else
            {
                Anim.Play(CharacterPose.Idle);
                Where = Mode.OnBreak;
            }
        }

        /// <summary>Back to the chair at once (Reset, Simulate on or off).</summary>
        public void ResetToDesk()
        {
            office.ReleaseBreakSpot(breakSpot);
            breakSpot = -1;
            Bubble.Hide();
            lastStatus = AgentStatus.Idle;
            lastActivity = "";
            doneWaveUntil = 0;
            appearAt = -1;
            gameObject.SetActive(true);
            SitDown();
            Where = Mode.Desk;
            if (!Agent.Present && !Agent.IsLead) Hide();
        }

        void Hide()
        {
            if (Nav.enabled) Nav.enabled = false;
            office.ReleaseBreakSpot(breakSpot);
            breakSpot = -1;
            Where = Mode.Hidden;
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ poses

        /// <summary>Sits on the desk chair: the seated overlay bends the legs, then the body drops until the hips rest on the seat.</summary>
        public void SitDown()
        {
            if (Nav != null && Nav.enabled) Nav.enabled = false;
            transform.SetPositionAndRotation(Desk.seat.position, Desk.seat.rotation);
            Anim.Overlays = PoseOverlay.Seated;
            Anim.Play(CharacterPose.Idle);
            Anim.Refresh();
            Body.localPosition = Vector3.zero;
            var hips = Anim.Bone("Hips");
            if (hips != null)
            {
                float hipsHeight = transform.InverseTransformPoint(hips.position).y;
                Body.localPosition = Vector3.down * (hipsHeight - (Desk.seatHeight + 0.09f));
            }
            Seated = true;
        }

        /// <summary>Stands up where they are (for walking).</summary>
        public void StandUp()
        {
            Anim.Overlays = PoseOverlay.None;
            Anim.Refresh();
            Body.localPosition = Vector3.zero;
            Seated = false;
        }

        /// <summary>The "Done" moment: a wave and a small burst of confetti in the team's colours.</summary>
        void Celebrate()
        {
            doneWaveUntil = Time.time + 2.5f;
            if (confetti == null) confetti = MakeConfetti();
            confetti.transform.position = HeadTop + Vector3.up * 0.2f;
            confetti.Emit(45);
        }

        // ------------------------------------------------------------------ making things

        /// <summary>
        /// A vest and hard hat in the role colour, sized from the person's own skeleton (the same idea as the boat's life
        /// vests in BoatPassengers.PutOnVest), plus a reflective stripe so it reads as a rescue vest.
        /// </summary>
        void Dress(Color colour)
        {
            var chest = Anim.Bone("Chest");
            var spine = Anim.Bone("Spine");
            var armL = Anim.Bone("UpperArmL");
            var armR = Anim.Bone("UpperArmR");
            if (chest != null)
            {
                float width = armL != null && armR != null ? Vector3.Distance(armL.position, armR.position) * 0.95f : 0.38f;
                float height = spine != null ? Vector3.Distance(spine.position, chest.position) * 2.1f : 0.32f;
                var vest = Box("Vest", chest, colour, new Vector3(width, height, width * 0.62f), Vector3.down * height * 0.28f, transform.forward);
                Box("Stripe", vest.transform, new Color(0.92f, 0.92f, 0.82f), new Vector3(1.02f, 0.12f, 1.02f), Vector3.zero, Vector3.forward, local: true);
            }
            if (head != null)
            {
                // A hard hat on top of the head: placed from the model's own height, since the head bone sits at the neck.
                float top = head.position.y + 0.3f;
                var skins = GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skins.Length > 0)
                {
                    var bounds = skins[0].bounds;
                    foreach (var sk in skins) bounds.Encapsulate(sk.bounds);
                    top = bounds.max.y;
                }
                var hat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hat.name = "HardHat";
                Destroy(hat.GetComponent<Collider>());
                hat.GetComponent<MeshRenderer>().sharedMaterial = Lit(Color.Lerp(colour, Color.white, 0.15f));
                hat.transform.SetParent(head, true);
                hat.transform.position = new Vector3(head.position.x, top - 0.07f, head.position.z);
                hat.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
                var ls = head.lossyScale;
                hat.transform.localScale = new Vector3(0.27f / Mathf.Max(0.0001f, ls.x), 0.16f / Mathf.Max(0.0001f, ls.y), 0.30f / Mathf.Max(0.0001f, ls.z));
                var brim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                brim.name = "Brim";
                Destroy(brim.GetComponent<Collider>());
                brim.GetComponent<MeshRenderer>().sharedMaterial = hat.GetComponent<MeshRenderer>().sharedMaterial;
                brim.transform.SetParent(hat.transform, false);
                brim.transform.localPosition = new Vector3(0, -0.12f, 0.06f);
                brim.transform.localScale = new Vector3(1.15f, 0.04f, 1.2f);
            }
        }

        void MakeMarker()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Waiting Marker";
            Destroy(go.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HQ Waiting Marker" };
            m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.15f) * 2.5f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            marker = go.transform;
        }

        ParticleSystem MakeConfetti()
        {
            var go = new GameObject("Confetti");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.82f, 0.25f), Agent.Entry.Colour) { mode = ParticleSystemGradientMode.TwoColors };
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90, 0, 0);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (confettiMaterial == null)
            {
                confettiMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "HQ Confetti" };
                confettiMaterial.SetColor("_BaseColor", Color.white);
            }
            renderer.sharedMaterial = confettiMaterial;
            return ps;
        }

        static GameObject Box(string name, Transform parent, Color colour, Vector3 size, Vector3 offset, Vector3 facing, bool local = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(colour);
            go.transform.SetParent(parent, false);
            if (local)
            {
                go.transform.localScale = size;
                go.transform.localPosition = offset;
            }
            else
            {
                // Sizes are in metres whatever the bone's scale.
                var ls = parent.lossyScale;
                go.transform.localScale = new Vector3(size.x / Mathf.Max(0.0001f, ls.x), size.y / Mathf.Max(0.0001f, ls.y), size.z / Mathf.Max(0.0001f, ls.z));
                go.transform.position = parent.position + offset;
                go.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
            return go;
        }

        /// <summary>One shared URP/Lit material per colour.</summary>
        public static Material Lit(Color colour)
        {
            if (materials.TryGetValue(colour, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HQ " + ColorUtility.ToHtmlStringRGB(colour) };
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", 0.25f);
            materials[colour] = m;
            return m;
        }
    }
}
