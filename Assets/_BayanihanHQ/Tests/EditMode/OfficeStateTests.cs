using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace BayanihanHQ.Tests
{
    /// <summary>The HQ's state model: parsing hook lines, statuses, the whiteboard, the signal and Simulate's story.</summary>
    public class OfficeStateTests
    {
        const long T0 = 1_800_000_000_000;
        OfficeState state;

        [SetUp]
        public void SetUp() => state = new OfficeState(HqRoster.Default());

        static HqEvent Ev(string ev, string agent, long ts, string tool = null, string tuid = null, string file = null, string aid = null) =>
            new HqEvent { ev = ev, agent = agent, ts = ts, tool = tool, tuid = tuid, file = file, aid = aid, sid = "s1" };

        void Apply(HqEvent e, long now = -1) => state.Apply(e, now < 0 ? e.ts : now);

        [Test]
        public void Parses_a_real_hook_line()
        {
            var e = HqEvent.Parse("{\"ts\":1795622605323,\"sid\":\"429e\",\"ev\":\"PreToolUse\",\"agent\":\"qa-tester\",\"via\":\"agent\",\"aid\":\"a82\",\"tuid\":\"toolu_01\",\"tool\":\"Glob\",\"file\":\"Game/Assets/Tests/EditMode\",\"preview\":\"**/*.cs\"}");
            Assert.That(e, Is.Not.Null);
            Assert.That(e.ts, Is.EqualTo(1795622605323));
            Assert.That(e.agent, Is.EqualTo("qa-tester"));
            Assert.That(e.Summary(), Is.EqualTo("Glob → EditMode"));
            Assert.That(e.task == null || e.task.IsEmpty, "an absent task must read as empty");
        }

        [Test]
        public void Broken_lines_are_skipped_not_thrown()
        {
            Assert.That(HqEvent.Parse(""), Is.Null);
            Assert.That(HqEvent.Parse("not json"), Is.Null);
            Assert.That(HqEvent.Parse("{\"no\":1}"), Is.Null);
            Assert.That(HqEvent.Parse("{\"ev\":\"Stop\"}").agent, Is.EqualTo("lead"), "no agent means the lead");
        }

        [Test]
        public void A_tool_is_working_then_lingers_then_goes_idle()
        {
            Apply(Ev("PreToolUse", "gameplay-scripter", T0, "Edit", "t1", "Game/Assets/Scripts/SimpleBoat.cs"));
            var bea = state.Get("gameplay-scripter");
            Assert.That(bea.Status, Is.EqualTo(AgentStatus.Working));
            Assert.That(bea.Activity, Is.EqualTo("Edit → SimpleBoat.cs"));

            Apply(Ev("PostToolUse", "gameplay-scripter", T0 + 500, "Edit", "t1", "Game/Assets/Scripts/SimpleBoat.cs"));
            state.Tick(T0 + 2000);
            Assert.That(bea.Status, Is.EqualTo(AgentStatus.Working), "still shown working just after the tool");
            state.Tick(T0 + 500 + OfficeState.WorkingLingerMs + 1);
            Assert.That(bea.Status, Is.EqualTo(AgentStatus.Idle));
            Assert.That(bea.ToolCount, Is.EqualTo(1));
            Assert.That(bea.FilesTouched, Does.Contain("Game/Assets/Scripts/SimpleBoat.cs"));
        }

        [Test]
        public void A_late_PreToolUse_after_its_Post_does_not_reopen_the_tool()
        {
            Apply(Ev("PostToolUse", "audio", T0 + 10, "Read", "t9"));
            Apply(Ev("PreToolUse", "audio", T0, "Read", "t9"), T0 + 20);
            state.Tick(T0 + 10 + OfficeState.WorkingLingerMs + 1);
            Assert.That(state.Get("audio").Status, Is.EqualTo(AgentStatus.Idle));
        }

        [Test]
        public void A_permission_request_waits_until_the_tool_finishes()
        {
            Apply(Ev("PreToolUse", "qa-tester", T0, "Bash", "t2"));
            Apply(Ev("PermissionRequest", "qa-tester", T0 + 100, "Bash"));
            Apply(new HqEvent { ev = "Notification", agent = "lead", kind = "permission_prompt", ts = T0 + 200, sid = "s1" });
            var nena = state.Get("qa-tester");
            Assert.That(nena.Status, Is.EqualTo(AgentStatus.Waiting));
            Assert.That(state.Lead.Status, Is.Not.EqualTo(AgentStatus.Waiting), "the follow-up notification is the same prompt");
            Assert.That(state.Signal(T0 + 300), Is.EqualTo(4));
            Assert.That(state.Signal(T0 + 100 + 2 * 60 * 1000 + 1), Is.EqualTo(5), "a wait over two minutes is signal 5");

            Apply(Ev("PostToolUse", "qa-tester", T0 + 5000, "Bash", "t2"));
            Assert.That(nena.Status, Is.EqualTo(AgentStatus.Working));
        }

        [Test]
        public void A_failure_is_an_error_but_an_interrupt_is_not()
        {
            Apply(Ev("PreToolUse", "qa-tester", T0, "Bash", "t3"));
            Apply(Ev("PostToolUseFailure", "qa-tester", T0 + 100, "Bash", "t3"));
            Assert.That(state.Get("qa-tester").Status, Is.EqualTo(AgentStatus.Error));
            Assert.That(state.Signal(T0 + 200), Is.EqualTo(5));
            state.Tick(T0 + 100 + OfficeState.ErrorHoldMs + 1);
            Assert.That(state.Get("qa-tester").Status, Is.EqualTo(AgentStatus.Idle));
            Assert.That(state.Signal(T0 + 100 + OfficeState.ErrorHoldMs + 1), Is.EqualTo(3), "a failure in the last 2 minutes");

            var interrupt = Ev("PostToolUseFailure", "audio", T0, "Bash", "t4");
            interrupt.intr = true;
            Apply(interrupt);
            Assert.That(state.Get("audio").Status, Is.Not.EqualTo(AgentStatus.Error));
        }

        [Test]
        public void A_hand_off_puts_a_card_on_the_board_and_ticks_it_when_the_subagent_stops()
        {
            var spawn = Ev("PreToolUse", "map-level-builder", T0, "Agent", "t5");
            spawn.preview = "Fill side streets";
            spawn.to = "map-level-builder";
            var spawned = new List<string>();
            state.Spawned += a => spawned.Add(a.Key);
            Apply(spawn);
            Apply(Ev("SubagentStart", "map-level-builder", T0 + 50, aid: "A1"));
            var lito = state.Get("map-level-builder");
            Assert.That(spawned, Is.EqualTo(new[] { "map-level-builder" }));
            Assert.That(lito.Status, Is.EqualTo(AgentStatus.Thinking));
            Assert.That(lito.CurrentTask, Is.EqualTo("Fill side streets"));
            Assert.That(state.Board.Items[0].Status, Is.EqualTo("in_progress"));

            Apply(Ev("SubagentStop", "map-level-builder", T0 + 9000, aid: "A1"));
            Assert.That(lito.Status, Is.EqualTo(AgentStatus.Done));
            Assert.That(state.Board.Items[0].Status, Is.EqualTo("completed"));
            state.Tick(T0 + 9000 + OfficeState.DoneHoldMs + 1);
            Assert.That(lito.Status, Is.EqualTo(AgentStatus.Idle));
        }

        [Test]
        public void Tasks_are_created_updated_and_completed()
        {
            Apply(new HqEvent { ev = "TaskCreated", agent = "lead", ts = T0, task = new HqTaskInfo { id = "3", text = "Wire the counter", status = "pending" } });
            Apply(new HqEvent { ev = "PostToolUse", agent = "lead", tool = "TaskUpdate", ts = T0 + 1, task = new HqTaskInfo { id = "3", status = "in_progress" } });
            Assert.That(state.Board.Items[0].Status, Is.EqualTo("in_progress"));
            Assert.That(state.Board.Items[0].Text, Is.EqualTo("Wire the counter"));
            Apply(new HqEvent { ev = "TaskCompleted", agent = "lead", ts = T0 + 2, task = new HqTaskInfo { id = "3", text = "Wire the counter", status = "completed" } });
            Assert.That(state.Board.CountDone(), Is.EqualTo(1));
        }

        [Test]
        public void A_lost_Stop_does_not_leave_the_lead_busy_forever()
        {
            Apply(Ev("UserPromptSubmit", "lead", T0));
            Assert.That(state.Lead.Status, Is.EqualTo(AgentStatus.Thinking));
            state.Tick(T0 + OfficeState.EngagedTimeoutMs + 1);
            Assert.That(state.Lead.Status, Is.EqualTo(AgentStatus.Idle));
        }

        [Test]
        public void Replay_reads_only_whole_lines_from_the_tail()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hqtest-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "events.jsonl");
            try
            {
                var lines = new List<string>();
                for (int i = 0; i < 200; i++) lines.Add("{\"ts\":" + (T0 + i * 1000) + ",\"ev\":\"PreToolUse\",\"agent\":\"audio\",\"tool\":\"Read\",\"tuid\":\"x" + i + "\"}");
                lines.Add("garbage line");
                File.WriteAllText(path, string.Join("\n", lines) + "\n");
                var events = HqReplay.ReadTail(path, 2000, System.TimeSpan.FromHours(6));
                Assert.That(events.Count, Is.GreaterThan(5).And.LessThan(40));
                Assert.That(events[events.Count - 1].tuid, Is.EqualTo("x199"));
                var old = HqReplay.ReadTail(path, 1 << 20, System.TimeSpan.FromSeconds(10));
                Assert.That(old.Count, Is.EqualTo(11), "the window keeps only the last 10 seconds");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Simulate_story_shows_every_status_and_finishes_within_two_minutes_and_a_bit()
        {
            var events = HqSimulator.StoryEvents(7, T0);
            var seen = new HashSet<AgentStatus>();
            var desks = new HashSet<string>();
            int maxSignal = 0;
            state.StatusChanged += (a, from, to) => { seen.Add(to); if (a.Key != "lead") desks.Add(a.Key); };
            long last = T0;
            foreach (var e in events)
            {
                Assert.That(e.ts, Is.GreaterThanOrEqualTo(last), "events come in time order");
                last = e.ts;
                Assert.That(e.sim, "simulated events are tagged");
                state.Apply(e, e.ts);
                maxSignal = System.Math.Max(maxSignal, state.Signal(e.ts));
                for (long t = e.ts; t < e.ts + 900; t += 300) state.Tick(t);
            }
            Assert.That(last - T0, Is.LessThan((long)(HqSimulator.LoopSeconds * 1000)));
            foreach (AgentStatus s in System.Enum.GetValues(typeof(AgentStatus)))
                Assert.That(seen.Contains(s) || s == AgentStatus.Idle, "Simulate shows " + s);
            Assert.That(desks.Count, Is.GreaterThanOrEqualTo(6), "most desks get work");
            Assert.That(maxSignal, Is.EqualTo(5));
            Assert.That(state.Board.CountDone(), Is.GreaterThanOrEqualTo(6));
        }
    }
}
