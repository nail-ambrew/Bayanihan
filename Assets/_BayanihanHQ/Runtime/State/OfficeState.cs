using System;
using System.Collections.Generic;

namespace BayanihanHQ
{
    /// <summary>
    /// The whole office as data: every desk, the whiteboard, sessions, the event log and the typhoon signal. It has no
    /// Unity objects in it, so live events, the replayed log and Simulate mode all go through the same <see cref="Apply"/>,
    /// and EditMode tests can drive it directly.
    /// </summary>
    public class OfficeState
    {
        /// <summary>A tool shows as working for this long after it finishes, so quick Reads are still visible.</summary>
        public const long WorkingLingerMs = 3000;
        public const long ErrorHoldMs = 10000;
        public const long DoneHoldMs = 3000;
        /// <summary>A desk that has heard nothing for this long is no longer busy (covers a lost Stop or SubagentStop).</summary>
        public const long EngagedTimeoutMs = 10 * 60 * 1000;
        /// <summary>A tool with no PostToolUse after this long is treated as finished.</summary>
        public const long OpenToolTimeoutMs = 10 * 60 * 1000;
        /// <summary>A wait older than this is assumed answered (e.g. replaying yesterday's log).</summary>
        public const long WaitingTimeoutMs = 20 * 60 * 1000;
        public const long FailureWindowMs = 2 * 60 * 1000;
        public const long SessionTimeoutMs = 30 * 60 * 1000;
        public const int LogMax = 300;

        readonly List<AgentState> agents = new List<AgentState>();
        readonly Dictionary<string, AgentState> byKey = new Dictionary<string, AgentState>();
        readonly Dictionary<string, SessionInfo> sessions = new Dictionary<string, SessionInfo>();
        readonly List<long> failures = new List<long>();
        readonly HashSet<string> finishedTools = new HashSet<string>();
        readonly Queue<string> finishedOrder = new Queue<string>();

        /// <summary>A Claude Code session the office has heard from.</summary>
        public class SessionInfo
        {
            public string Id;
            public long StartMs;
            public long LastMs;
            public bool Ended;
        }

        public OfficeState(HqRoster roster)
        {
            Roster = roster ?? HqRoster.Default();
            foreach (var entry in Roster.agents)
            {
                if (byKey.ContainsKey(entry.key)) continue;
                var a = new AgentState(entry);
                a.Clear();
                agents.Add(a);
                byKey[entry.key] = a;
            }
            Lead = Get(HqRoster.LeadKey) ?? agents[0];
        }

        public HqRoster Roster { get; }
        public IReadOnlyList<AgentState> Agents => agents;
        public AgentState Lead { get; }
        public TodoBoardState Board { get; } = new TodoBoardState();
        /// <summary>Everything applied, newest last, at most 300.</summary>
        public List<HqEvent> Log { get; } = new List<HqEvent>();
        public int EventsApplied { get; private set; }
        public long LastEventMs { get; private set; }

        /// <summary>A desk changed status: (desk, from, to).</summary>
        public event Action<AgentState, AgentStatus, AgentStatus> StatusChanged;
        /// <summary>A subagent of this role was started: its character walks in from the entrance.</summary>
        public event Action<AgentState> Spawned;
        /// <summary>Fired after each event is applied.</summary>
        public event Action<HqEvent> Applied;

        public AgentState Get(string key) => key != null && byKey.TryGetValue(key, out var a) ? a : null;

        /// <summary>The desk an event belongs to; unknown keys (an edited roster) go to the lead.</summary>
        AgentState DeskFor(HqEvent e) => Get(e.agent) ?? Lead;

        /// <summary>Applies one event. <paramref name="nowMs"/> is the wall clock, used to settle the statuses afterwards.</summary>
        public void Apply(HqEvent e, long nowMs)
        {
            if (e == null || string.IsNullOrEmpty(e.ev)) return;
            EventsApplied++;
            LastEventMs = Math.Max(LastEventMs, e.ts);
            Log.Add(e);
            if (Log.Count > LogMax) Log.RemoveAt(0);
            TouchSession(e);

            var desk = DeskFor(e);
            bool isSpawn = e.tool == "Agent" || e.tool == "Task";

            switch (e.ev)
            {
                case "SessionStart":
                    Lead.Present = true;
                    break;

                case "SessionEnd":
                    EndSession(e);
                    break;

                case "UserPromptSubmit":
                    // The owner is at the keyboard, so nothing is waiting on them any more.
                    foreach (var a in agents) ClearWaiting(a);
                    Lead.Busy = true;
                    Lead.DoneUntil = 0;
                    Lead.Activity = "Planning";
                    Touch(Lead, e);
                    break;

                case "PreCompact":
                    Lead.Activity = "Compacting notes";
                    Touch(Lead, e);
                    break;

                case "Stop":
                    ClearWaiting(Lead);
                    Lead.Busy = false;
                    Lead.OpenTools.Clear();
                    Lead.DoneUntil = e.ts + DoneHoldMs;
                    Lead.Activity = "Done";
                    Touch(Lead, e);
                    break;

                case "PreToolUse":
                    if (isSpawn) { HandOff(desk, e); break; }
                    ClearWaiting(desk);
                    if (!string.IsNullOrEmpty(e.tuid) && finishedTools.Contains(e.tuid)) { Touch(desk, e); break; } // arrived after its Post
                    if (!string.IsNullOrEmpty(e.tuid)) desk.OpenTools[e.tuid] = e.ts;
                    desk.ToolCount++;
                    desk.ErrorUntil = 0;
                    ShowTool(desk, e);
                    break;

                case "PostToolUse":
                    if (isSpawn) { Touch(Lead, e); break; }
                    ClearWaiting(desk);
                    FinishTool(desk, e);
                    desk.ErrorUntil = 0;
                    desk.WorkingUntil = Math.Max(desk.WorkingUntil, e.ts + WorkingLingerMs);
                    if (IsEdit(e.tool) && !string.IsNullOrEmpty(e.file)) desk.FilesTouched.Add(e.file);
                    ShowTool(desk, e);
                    break;

                case "PostToolUseFailure":
                    ClearWaiting(desk);
                    FinishTool(desk, e);
                    if (isSpawn) Board.FailAssignment(e.tuid, e.ts);
                    if (e.intr)
                    {
                        desk.WorkingUntil = Math.Max(desk.WorkingUntil, e.ts + 500);
                    }
                    else
                    {
                        desk.ErrorUntil = e.ts + ErrorHoldMs;
                        failures.Add(e.ts);
                    }
                    ShowTool(desk, e);
                    break;

                case "PermissionRequest":
                    // The Notification that follows a permission prompt has no file, so it lands on the lead's desk.
                    // The request names the real desk, so move the wait there.
                    if (desk != Lead && Lead.WaitingKind == "permission_prompt" && e.ts - Lead.WaitingSince < 3000) ClearWaiting(Lead);
                    StartWaiting(desk, e, "permission");
                    ShowTool(desk, e);
                    break;

                case "Notification":
                    if (IsWaitingKind(e.kind))
                    {
                        // Skip the duplicate when the PermissionRequest for the same prompt already put a desk on hold.
                        if (e.kind == "permission_prompt" && AnyWaitingSince(e.ts - 3000)) { Touch(desk, e); break; }
                        StartWaiting(desk, e, e.kind);
                    }
                    else Touch(desk, e);
                    break;

                case "SubagentStart":
                    desk.ActiveAgentIds.Add(e.aid ?? "");
                    desk.DoneUntil = 0;
                    desk.SpawnedAt = e.ts;
                    if (desk != Lead) Board.StartAssignment(desk.Key, e.aid, e.ts);
                    desk.Activity = "Arriving";
                    Touch(desk, e);
                    if (desk != Lead) Spawned?.Invoke(desk);
                    break;

                case "SubagentStop":
                    desk.ActiveAgentIds.Remove(e.aid ?? "");
                    ClearWaiting(desk);
                    if (desk != Lead)
                    {
                        Board.FinishAssignment(desk.Key, e.aid, e.ts);
                        if (desk.ActiveAgentIds.Count == 0)
                        {
                            desk.OpenTools.Clear();
                            desk.DoneUntil = e.ts + DoneHoldMs;
                            desk.Activity = "Done";
                        }
                    }
                    Touch(desk, e);
                    break;

                case "TaskCreated":
                case "TaskCompleted":
                    if (e.task != null) Board.UpsertTask(e.task, desk.Key, e.ts);
                    Touch(desk, e);
                    break;

                default:
                    Touch(desk, e);
                    break;
            }

            // TaskUpdate and TodoWrite arrive as ordinary PostToolUse events with a task or todo list attached.
            if (e.ev == "PostToolUse" && e.task != null && !e.task.IsEmpty && e.tool == "TaskUpdate") Board.UpsertTask(e.task, desk.Key, e.ts);
            if (e.ev == "PostToolUse" && e.todos != null && e.todos.Length > 0) Board.ReplaceTodos(e.todos, desk.Key, e.ts);

            Tick(nowMs);
            Applied?.Invoke(e);
        }

        /// <summary>Works out every desk's status from its flags and timers. Call every frame (cheap).</summary>
        public void Tick(long nowMs)
        {
            foreach (var a in agents)
            {
                ExpireStale(a, nowMs);
                var next = Decide(a, nowMs);
                if (next == a.Status) continue;
                var prev = a.Status;
                a.Status = next;
                a.StatusSince = nowMs;
                if (next == AgentStatus.Idle) a.IdleSince = nowMs;
                StatusChanged?.Invoke(a, prev, next);
            }
            failures.RemoveAll(t => nowMs - t > FailureWindowMs);
        }

        static AgentStatus Decide(AgentState a, long now)
        {
            if (a.WaitingSince > 0) return AgentStatus.Waiting;
            if (now < a.ErrorUntil) return AgentStatus.Error;
            if (a.OpenTools.Count > 0 || now < a.WorkingUntil) return AgentStatus.Working;
            if (now < a.DoneUntil) return AgentStatus.Done;
            if (a.Busy || a.ActiveAgentIds.Count > 0) return AgentStatus.Thinking;
            return AgentStatus.Idle;
        }

        /// <summary>Drops inputs whose closing event never came, so a lost hook can't freeze a desk.</summary>
        static void ExpireStale(AgentState a, long now)
        {
            if (a.WaitingSince > 0 && now - a.WaitingSince > WaitingTimeoutMs) { a.WaitingSince = 0; a.WaitingKind = ""; }
            if (a.OpenTools.Count > 0)
            {
                List<string> stale = null;
                foreach (var kv in a.OpenTools) if (now - kv.Value > OpenToolTimeoutMs) (stale ??= new List<string>()).Add(kv.Key);
                if (stale != null) foreach (var k in stale) a.OpenTools.Remove(k);
            }
            if ((a.Busy || a.ActiveAgentIds.Count > 0) && now - a.LastEventMs > EngagedTimeoutMs)
            {
                a.Busy = false;
                a.ActiveAgentIds.Clear();
            }
        }

        /// <summary>
        /// The PAGASA-style wall board. 1: nobody working. 2: one or two busy. 3: three or more busy, or a failure in the
        /// last 2 minutes. 4: someone is waiting for the owner. 5: a desk in Error, 3+ failures in 2 minutes, or a wait over
        /// 2 minutes.
        /// </summary>
        public int Signal(long nowMs)
        {
            int busy = 0, waiting = 0, errors = 0;
            bool longWait = false;
            foreach (var a in agents)
            {
                switch (a.Status)
                {
                    case AgentStatus.Working:
                    case AgentStatus.Thinking: busy++; break;
                    case AgentStatus.Waiting:
                        waiting++;
                        if (a.WaitingFor(nowMs) > 2 * 60 * 1000) longWait = true;
                        break;
                    case AgentStatus.Error: errors++; break;
                }
            }
            int recentFailures = RecentFailures(nowMs);
            if (errors > 0 || recentFailures >= 3 || longWait) return 5;
            if (waiting > 0) return 4;
            if (busy >= 3 || recentFailures >= 1) return 3;
            if (busy >= 1) return 2;
            return 1;
        }

        public int RecentFailures(long nowMs)
        {
            int n = 0;
            foreach (var t in failures) if (nowMs - t <= FailureWindowMs) n++;
            return n;
        }

        public int CountStatus(AgentStatus status)
        {
            int n = 0;
            foreach (var a in agents) if (a.Status == status) n++;
            return n;
        }

        /// <summary>The desks waiting for the owner, longest wait first.</summary>
        public List<AgentState> WaitingAgents()
        {
            var list = new List<AgentState>();
            foreach (var a in agents) if (a.Status == AgentStatus.Waiting) list.Add(a);
            list.Sort((x, y) => x.WaitingSince.CompareTo(y.WaitingSince));
            return list;
        }

        /// <summary>Sessions not ended and heard from in the last 30 minutes.</summary>
        public int ActiveSessions(long nowMs)
        {
            int n = 0;
            foreach (var s in sessions.Values) if (!s.Ended && nowMs - s.LastMs < SessionTimeoutMs) n++;
            return n;
        }

        /// <summary>Start of the oldest active session (Unix ms), or 0 when none is active.</summary>
        public long SessionStartMs(long nowMs)
        {
            long start = 0;
            foreach (var s in sessions.Values)
                if (!s.Ended && nowMs - s.LastMs < SessionTimeoutMs && (start == 0 || s.StartMs < start)) start = s.StartMs;
            return start;
        }

        /// <summary>Empties the office (the Reset toggle).</summary>
        public void Reset()
        {
            foreach (var a in agents) a.Clear();
            Board.Clear();
            Log.Clear();
            sessions.Clear();
            failures.Clear();
            finishedTools.Clear();
            finishedOrder.Clear();
            EventsApplied = 0;
            LastEventMs = 0;
        }

        // --- helpers -------------------------------------------------------------------------------------------------

        void HandOff(AgentState target, HqEvent e)
        {
            // The lead is the one calling Agent; the event names the desk it hands work to.
            Lead.ToolCount++;
            Lead.Tool = e.tool;
            Lead.Preview = e.preview ?? "";
            Lead.Activity = target == Lead
                ? "Agent → " + (string.IsNullOrEmpty(e.to) ? "helper" : e.to)
                : "Assign → " + target.Name;
            Touch(Lead, e);
            var owner = target == Lead ? Lead.Key : target.Key;
            var text = target == Lead && !string.IsNullOrEmpty(e.to) ? e.to + ": " + e.preview : e.preview;
            Board.AddAssignment(e.tuid, owner, text ?? "", e.ts);
            if (target != Lead)
            {
                target.Present = true;
                target.CurrentTask = e.preview ?? "";
            }
        }

        void ShowTool(AgentState desk, HqEvent e)
        {
            desk.Tool = e.tool ?? "";
            desk.File = e.file ?? "";
            desk.Preview = e.preview ?? "";
            desk.Activity = e.Summary();
            if (!string.IsNullOrEmpty(e.sub)) desk.Helper = e.sub;
            Touch(desk, e);
        }

        void Touch(AgentState desk, HqEvent e)
        {
            desk.Present = true;
            desk.LastEventMs = Math.Max(desk.LastEventMs, e.ts);
            desk.Remember(e);
        }

        void FinishTool(AgentState desk, HqEvent e)
        {
            if (string.IsNullOrEmpty(e.tuid)) { desk.OpenTools.Clear(); return; }
            desk.OpenTools.Remove(e.tuid);
            if (finishedTools.Add(e.tuid))
            {
                finishedOrder.Enqueue(e.tuid);
                if (finishedOrder.Count > 500) finishedTools.Remove(finishedOrder.Dequeue());
            }
        }

        void StartWaiting(AgentState desk, HqEvent e, string kind)
        {
            if (desk.WaitingSince == 0) desk.WaitingSince = e.ts;
            desk.WaitingKind = kind ?? "";
            desk.Activity = e.ev == "Notification" && !string.IsNullOrEmpty(e.preview) ? e.preview : "Needs approval: " + e.Summary();
            Touch(desk, e);
        }

        static void ClearWaiting(AgentState a)
        {
            a.WaitingSince = 0;
            a.WaitingKind = "";
        }

        bool AnyWaitingSince(long ts)
        {
            foreach (var a in agents) if (a.WaitingSince > 0 && a.WaitingSince >= ts) return true;
            return false;
        }

        void EndSession(HqEvent e)
        {
            foreach (var a in agents)
            {
                ClearWaiting(a);
                a.OpenTools.Clear();
                a.ActiveAgentIds.Clear();
                a.Busy = false;
            }
            Touch(Lead, e);
        }

        void TouchSession(HqEvent e)
        {
            if (string.IsNullOrEmpty(e.sid)) return;
            if (!sessions.TryGetValue(e.sid, out var s))
            {
                s = new SessionInfo { Id = e.sid, StartMs = e.ts };
                sessions[e.sid] = s;
            }
            s.StartMs = Math.Min(s.StartMs, e.ts);
            s.LastMs = Math.Max(s.LastMs, e.ts);
            if (e.ev == "SessionEnd") s.Ended = true;
            else if (e.ev == "SessionStart") s.Ended = false;
        }

        static bool IsWaitingKind(string kind) =>
            kind == "permission_prompt" || kind == "idle_prompt" || kind == "agent_needs_input" || kind == "elicitation_dialog";

        static bool IsEdit(string tool) => tool == "Edit" || tool == "Write" || tool == "MultiEdit" || tool == "NotebookEdit";
    }
}
