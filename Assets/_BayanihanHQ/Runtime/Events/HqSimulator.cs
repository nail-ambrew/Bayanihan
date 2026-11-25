using System;
using System.Collections.Generic;

namespace BayanihanHQ
{
    /// <summary>
    /// Simulate mode: a scripted, believable two-minute work session for demos (the capstone defense) when no Claude Code
    /// session is running. It makes the same events the hook writes, tagged sim, so the whole office reacts exactly as it
    /// would live. Each loop is a new session with a little jitter per desk, so it doesn't look canned.
    /// </summary>
    public class HqSimulator
    {
        /// <summary>Seconds from the start of one loop to the next.</summary>
        public const double LoopSeconds = 128;

        const string G = "Game/Assets/";
        readonly Random random;
        List<Step> steps = new List<Step>();
        int next;
        long loopStartMs = -1;
        int loop;

        struct Step
        {
            public double T;
            public HqEvent E;
        }

        public HqSimulator(int seed = 2026) { random = new Random(seed); }

        /// <summary>Seconds into the current loop.</summary>
        public double Elapsed(long nowMs) => loopStartMs < 0 ? 0 : (nowMs - loopStartMs) / 1000.0;

        /// <summary>Starts again from the top (Simulate toggled on, or Reset).</summary>
        public void Restart()
        {
            loopStartMs = -1;
            next = 0;
        }

        /// <summary>Returns the events due by <paramref name="nowMs"/>, stamped with the real time they were due.</summary>
        public List<HqEvent> Update(long nowMs)
        {
            var due = new List<HqEvent>();
            if (loopStartMs < 0) BeginLoop(nowMs);
            while (true)
            {
                if (next >= steps.Count)
                {
                    if (Elapsed(nowMs) < LoopSeconds) break;
                    BeginLoop(loopStartMs + (long)(LoopSeconds * 1000));
                    continue;
                }
                var s = steps[next];
                long at = loopStartMs + (long)(s.T * 1000);
                if (at > nowMs) break;
                s.E.ts = at;
                due.Add(s.E);
                next++;
            }
            return due;
        }

        void BeginLoop(long startMs)
        {
            loopStartMs = startMs;
            loop++;
            steps = Build(random, "sim-" + loop.ToString("000") + "-" + random.Next(1000, 9999));
            next = 0;
        }

        static List<Step> BuildForTest(int seed) => Build(new Random(seed), "sim-test");

        static List<Step> Build(Random random, string sid)
        {
            var s = new Story(sid, random);

            // The owner asks for a round of work; the lead reads the notes and plans it on the board.
            s.Lead(0.0, "SessionStart", preview: "startup");
            s.Lead(0.8, "UserPromptSubmit");
            s.Tool("lead", 1.6, "Read", "Game/Docs/Home.md", null, 0.4);
            s.Tool("lead", 2.4, "Read", "Game/Docs/Systems/Map and Props.md", null, 0.5);
            s.Task(3.4, "1", "Fill Bagumbayan side streets with sari-sari rows", "pending", "TaskCreated");
            s.Task(3.6, "2", "Boat wake reacts to speed", "pending", "TaskCreated");
            s.Task(3.8, "3", "Rain ambience follows typhoon strength", "pending", "TaskCreated");
            s.Task(4.0, "4", "Show rescued / total on the HUD", "pending", "TaskCreated");
            s.Task(4.2, "5", "Run Mabuhay and boat tests", "pending", "TaskCreated");
            s.Task(4.4, "6", "Update the Docs notes", "pending", "TaskCreated");

            // Lito, the level builder: row generation away from WalterMart and Macaria, then a full map rebuild.
            s.Spawn(5.0, "map-level-builder", "Fill Bagumbayan side streets with sari-sari rows");
            s.Task(5.3, "1", null, "in_progress");
            s.Tool("map-level-builder", 6.2, "Read", G + "Scripts/Editor/RowBuilder.cs", null, 0.6);
            s.Tool("map-level-builder", 7.4, "Grep", G + "Scripts/Editor", "ProtectedBuffer", 0.4);
            s.Tool("map-level-builder", 8.6, "Read", G + "Scripts/Map/BarangayMockPlanner.cs", null, 0.5);
            s.Tool("map-level-builder", 10.5, "Edit", G + "Scripts/Editor/RowBuilder.cs", null, 1.2);
            s.Tool("map-level-builder", 13.5, "Edit", G + "Scripts/Map/BarangayMockPlanner.cs", null, 1.0);
            s.Tool("map-level-builder", 16.0, "Bash", null, "unity cmd menu --path \"Bayanihan/Build Mabuhay Gameplay Scene\"", 15.0);
            s.Tool("map-level-builder", 33.0, "Read", G + "Generated/MabuhayGameplay/BuildReport.txt", null, 0.5);
            s.Tool("map-level-builder", 35.0, "Edit", "Game/Docs/Systems/Map and Props.md", null, 1.0);
            s.Stop(38.0, "map-level-builder");
            s.Task(38.4, "1", null, "completed", "TaskCompleted");

            // Bea, the gameplay scripter: the boat's wake.
            s.Spawn(6.0, "gameplay-scripter", "Boat wake reacts to speed");
            s.Task(6.3, "2", null, "in_progress");
            s.Tool("gameplay-scripter", 7.2, "Read", G + "Scripts/SimpleBoat.cs", null, 0.5);
            s.Tool("gameplay-scripter", 8.4, "Read", G + "Scripts/BoatEffects.cs", null, 0.4);
            s.Tool("gameplay-scripter", 11.0, "Edit", G + "Scripts/SimpleBoat.cs", null, 1.5);
            s.Tool("gameplay-scripter", 14.5, "Edit", G + "Scripts/BoatEffects.cs", null, 1.2);
            s.Tool("gameplay-scripter", 18.0, "Bash", null, "unity cmd recompile", 4.0);
            s.Tool("gameplay-scripter", 23.0, "Bash", null, "unity cmd console --type error", 0.8);
            s.Stop(26.0, "gameplay-scripter");
            s.Task(26.4, "2", null, "completed", "TaskCompleted");

            // Lorna, audio: rain ambience.
            s.Spawn(7.0, "audio", "Rain ambience follows typhoon strength");
            s.Task(7.3, "3", null, "in_progress");
            s.Tool("audio", 8.0, "Glob", G + "Audio/Weather", "**/*.ogg", 0.3);
            s.Tool("audio", 9.5, "Read", G + "Scripts/TyphoonWeather.cs", null, 0.6);
            s.Tool("audio", 12.0, "Write", G + "Scripts/RainAmbience.cs", null, 1.0);
            s.Tool("audio", 16.5, "Edit", G + "Scripts/TyphoonWeather.cs", null, 1.0);
            s.Tool("audio", 21.0, "Edit", "Game/README.md", null, 0.6);
            s.Stop(34.0, "audio");
            s.Task(34.4, "3", null, "completed", "TaskCompleted");

            // Tala, VFX: needs the owner's OK to capture the Game view (~45 s): the "waiting for me" moment.
            s.Spawn(28.0, "vfx-environment", "Lightning stays under 3 flashes a second");
            s.Tool("vfx-environment", 29.5, "Read", G + "Scripts/Cutscenes/FlashLimiter.cs", null, 0.5);
            s.Tool("vfx-environment", 31.5, "Edit", G + "Scripts/TyphoonWeather.cs", null, 1.0);
            s.Permission("vfx-environment", 44.0, "Bash", "unity cmd capture_game_view --path Temp/lightning.png", waitSeconds: 11.0);
            s.Tool("vfx-environment", 58.0, "Read", "Game/Temp/lightning.png", null, 0.4);
            s.Stop(61.0, "vfx-environment");

            // Mika, UI: the HUD counter.
            s.Spawn(40.0, "ui-frontend", "Show rescued / total on the HUD");
            s.Task(40.3, "4", null, "in_progress");
            s.Tool("ui-frontend", 41.0, "Read", G + "Scripts/UI/UiKit.cs", null, 0.6);
            s.Tool("ui-frontend", 42.5, "Read", G + "Scripts/UI/UiTheme.cs", null, 0.4);
            s.Tool("ui-frontend", 46.0, "Edit", G + "Scripts/HudMarker.cs", null, 1.2);
            s.Tool("ui-frontend", 50.0, "Edit", G + "Scripts/RescueManager.cs", null, 1.0);
            s.Tool("ui-frontend", 55.0, "Edit", "Game/Docs/Systems/UI and Settings.md", null, 0.8);
            s.Stop(64.0, "ui-frontend");
            s.Task(64.4, "4", null, "completed", "TaskCompleted");

            // Nena, QA: a test fails at ~70 s, Bea fixes it, the rerun passes.
            s.Spawn(62.0, "qa-tester", "Run Mabuhay and boat tests");
            s.Task(62.3, "5", null, "in_progress");
            s.Tool("qa-tester", 63.0, "Bash", null, "unity cmd run_tests --mode editor --filter BarangayLayout --format json", 4.0);
            s.Tool("qa-tester", 68.0, "Bash", null, "unity cmd run_tests --mode editor --filter BoatTests --format json", 3.0, ok: false);
            s.Spawn(75.0, "gameplay-scripter", "Fix BoatTests: wake speed reads 0 at the helm");
            s.Tool("gameplay-scripter", 76.0, "Read", G + "Tests/EditMode/BoatTests.cs", null, 0.5);
            s.Tool("gameplay-scripter", 77.5, "Read", G + "Scripts/PlayerRescuer.cs", null, 0.6);
            s.Tool("gameplay-scripter", 80.0, "Edit", G + "Scripts/SimpleBoat.cs", null, 1.2);
            s.Tool("gameplay-scripter", 83.0, "Bash", null, "unity cmd recompile", 3.0);
            s.Stop(87.0, "gameplay-scripter");
            s.Tool("qa-tester", 89.0, "Bash", null, "unity cmd run_tests --mode editor --filter BoatTests --format json", 3.0);
            s.Tool("qa-tester", 93.0, "Bash", null, "unity cmd run_tests --mode playmode --filter MabuhaySmokeTests --async_tests true", 1.0);
            s.Tool("qa-tester", 95.0, "Bash", null, "unity cmd test_status", 6.0);
            s.Tool("qa-tester", 102.0, "Bash", null, "unity cmd console", 0.6);
            s.Stop(104.0, "qa-tester");
            s.Task(104.4, "5", null, "completed", "TaskCompleted");

            // The lead writes up, finishes, and later sits waiting for the next prompt.
            s.Task(105.0, "6", null, "in_progress");
            s.Tool("lead", 106.0, "Edit", "Game/Docs/Home.md", null, 1.0);
            s.Tool("lead", 109.0, "Bash", null, "git status --short", 0.5);
            s.Task(111.0, "6", null, "completed", "TaskCompleted");
            s.Lead(112.0, "Stop");
            s.Lead(120.0, "Notification", preview: "Claude is waiting for your input", kind: "idle_prompt");

            return s.Sorted();
        }

        /// <summary>Builds the event list. Every desk gets its own small time offset so each loop looks different.</summary>
        class Story
        {
            readonly string sid;
            readonly Random random;
            readonly Dictionary<string, double> jitter = new Dictionary<string, double>();
            readonly Dictionary<string, int> agentRuns = new Dictionary<string, int>();
            readonly List<Step> steps = new List<Step>();
            int toolIds;

            public Story(string sid, Random random)
            {
                this.sid = sid;
                this.random = random;
            }

            double J(string agent)
            {
                if (agent == HqRoster.LeadKey) return 0;
                if (!jitter.TryGetValue(agent, out var j)) jitter[agent] = j = random.NextDouble() * 1.6 - 0.8;
                return j;
            }

            HqEvent E(string agent, string ev, string via) =>
                new HqEvent { sid = sid, agent = agent, ev = ev, via = via, sim = true };

            void Add(double t, HqEvent e) => steps.Add(new Step { T = Math.Max(0, t), E = e });

            string AgentId(string agent) => "sim-" + agent + "-" + (agentRuns.TryGetValue(agent, out var n) ? n : 0);

            public void Lead(double t, string ev, string preview = null, string kind = null)
            {
                var e = E(HqRoster.LeadKey, ev, "lead");
                e.preview = preview;
                e.kind = kind;
                Add(t, e);
            }

            public void Spawn(double t, string agent, string description)
            {
                agentRuns[agent] = (agentRuns.TryGetValue(agent, out var n) ? n : 0) + 1;
                t += J(agent);
                var tuid = "sim-tool-" + (++toolIds);
                var pre = E(agent, "PreToolUse", "spawn");
                pre.tool = "Agent"; pre.preview = description; pre.to = agent; pre.tuid = tuid;
                Add(t, pre);
                var start = E(agent, "SubagentStart", "agent");
                start.aid = AgentId(agent); start.preview = agent;
                Add(t + 0.2, start);
                var post = E(agent, "PostToolUse", "spawn");
                post.tool = "Agent"; post.preview = description; post.to = agent; post.tuid = tuid; post.ok = true;
                Add(t + 0.3, post);   // subagents run in the background, so the Agent call returns at once
            }

            public void Stop(double t, string agent)
            {
                var e = E(agent, "SubagentStop", "agent");
                e.aid = AgentId(agent); e.preview = agent;
                Add(t + J(agent), e);
            }

            public void Tool(string agent, double t, string tool, string file, string preview, double duration, bool ok = true)
            {
                t += J(agent);
                var tuid = "sim-tool-" + (++toolIds);
                var via = agent == HqRoster.LeadKey ? "lead" : "agent";
                var pre = E(agent, "PreToolUse", via);
                pre.tool = tool; pre.file = file; pre.preview = preview; pre.tuid = tuid;
                if (via == "agent") pre.aid = AgentId(agent);
                Add(t, pre);
                var post = E(agent, ok ? "PostToolUse" : "PostToolUseFailure", via);
                post.tool = tool; post.file = file; post.preview = preview; post.tuid = tuid; post.ok = ok; post.aid = pre.aid;
                Add(t + duration, post);
            }

            public void Permission(string agent, double t, string tool, string preview, double waitSeconds)
            {
                t += J(agent);
                var tuid = "sim-tool-" + (++toolIds);
                var pre = E(agent, "PreToolUse", "agent");
                pre.tool = tool; pre.preview = preview; pre.tuid = tuid; pre.aid = AgentId(agent);
                Add(t, pre);
                var ask = E(agent, "PermissionRequest", "agent");
                ask.tool = tool; ask.preview = preview; ask.aid = pre.aid;
                Add(t + 0.2, ask);
                var note = E(HqRoster.LeadKey, "Notification", "lead");
                note.preview = "Claude needs your permission to use " + tool; note.kind = "permission_prompt";
                Add(t + 0.4, note);
                var post = E(agent, "PostToolUse", "agent");
                post.tool = tool; post.preview = preview; post.tuid = tuid; post.ok = true; post.aid = pre.aid;
                Add(t + waitSeconds + 1.5, post);
            }

            public void Task(double t, string id, string text, string status, string ev = null)
            {
                var e = E(HqRoster.LeadKey, ev ?? "PostToolUse", "lead");
                e.task = new HqTaskInfo { id = id, text = text, status = status };
                if (ev == null) { e.tool = "TaskUpdate"; e.ok = true; e.preview = "#" + id + " → " + status; e.tuid = "sim-tool-" + (++toolIds); }
                else e.preview = text;
                Add(t, e);
            }

            public List<Step> Sorted()
            {
                // A stable sort, so events at the same moment keep the order they were written in.
                var indexed = new List<KeyValuePair<int, Step>>();
                for (int i = 0; i < steps.Count; i++) indexed.Add(new KeyValuePair<int, Step>(i, steps[i]));
                indexed.Sort((a, b) => a.Value.T != b.Value.T ? a.Value.T.CompareTo(b.Value.T) : a.Key.CompareTo(b.Key));
                var result = new List<Step>(indexed.Count);
                foreach (var kv in indexed) result.Add(kv.Value);
                return result;
            }
        }

        /// <summary>For tests: the events of one loop in order, with ts set as if the loop began at <paramref name="startMs"/>.</summary>
        public static List<HqEvent> StoryEvents(int seed, long startMs)
        {
            var list = new List<HqEvent>();
            foreach (var s in BuildForTest(seed))
            {
                s.E.ts = startMs + (long)(s.T * 1000);
                list.Add(s.E);
            }
            return list;
        }
    }
}
