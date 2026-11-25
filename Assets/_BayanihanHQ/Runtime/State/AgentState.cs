using System.Collections.Generic;

namespace BayanihanHQ
{
    /// <summary>What a desk is doing right now, in the order the office shows it.</summary>
    public enum AgentStatus
    {
        Idle,
        Thinking,
        Working,
        /// <summary>Claude Code needs the owner: a permission prompt or a notification.</summary>
        Waiting,
        Error,
        Done
    }

    /// <summary>
    /// One desk's live state. OfficeState writes the inputs (flags and timers) from events; the status itself is always
    /// worked out from them in <see cref="OfficeState.Tick"/>, so a lost event can only leave a timer to run out, never a
    /// desk stuck in the wrong state.
    /// </summary>
    public class AgentState
    {
        public const int RecentMax = 20;

        public AgentState(HqRosterEntry entry) { Entry = entry; }

        public HqRosterEntry Entry { get; }
        public string Key => Entry.key;
        public string Name => Entry.name;
        public bool IsLead => Entry.key == HqRoster.LeadKey;

        public AgentStatus Status { get; internal set; } = AgentStatus.Idle;
        /// <summary>When the current status started (Unix ms).</summary>
        public long StatusSince { get; internal set; }

        /// <summary>The job it was handed (the Agent tool's description), or the lead's current task.</summary>
        public string CurrentTask { get; internal set; } = "";
        public string Tool { get; internal set; } = "";
        public string File { get; internal set; } = "";
        public string Preview { get; internal set; } = "";
        /// <summary>The last tool line, e.g. "Edit → SimpleBoat.cs", for bubbles and monitors.</summary>
        public string Activity { get; internal set; } = "";
        /// <summary>A built-in helper (e.g. Explore) whose work is shown at this desk.</summary>
        public string Helper { get; internal set; } = "";
        /// <summary>Why it is waiting: permission_prompt, idle_prompt...</summary>
        public string WaitingKind { get; internal set; } = "";

        public int ToolCount { get; internal set; }
        public HashSet<string> FilesTouched { get; } = new HashSet<string>();
        /// <summary>The last 20 events at this desk, oldest first.</summary>
        public List<HqEvent> Recent { get; } = new List<HqEvent>();

        /// <summary>Has done anything this session; desks with no one at them stay empty until then (the lead always sits).</summary>
        public bool Present { get; internal set; }
        /// <summary>When a subagent of this role was last started (Unix ms), for the walk-in from the entrance.</summary>
        public long SpawnedAt { get; internal set; }
        public long LastEventMs { get; internal set; }

        // Inputs the status is worked out from.
        internal readonly HashSet<string> ActiveAgentIds = new HashSet<string>();
        internal readonly Dictionary<string, long> OpenTools = new Dictionary<string, long>();
        internal bool Busy;            // the lead between a prompt and Stop
        internal long WaitingSince;
        internal long WorkingUntil;
        internal long ErrorUntil;
        internal long DoneUntil;
        internal long IdleSince;

        /// <summary>How long it has been idle (ms), for wandering to the break area.</summary>
        public long IdleFor(long nowMs) => Status == AgentStatus.Idle ? nowMs - IdleSince : 0;

        /// <summary>How long it has been waiting for the owner (ms).</summary>
        public long WaitingFor(long nowMs) => Status == AgentStatus.Waiting ? nowMs - WaitingSince : 0;

        internal void Remember(HqEvent e)
        {
            Recent.Add(e);
            if (Recent.Count > RecentMax) Recent.RemoveAt(0);
        }

        internal void Clear()
        {
            Status = AgentStatus.Idle;
            StatusSince = IdleSince = 0;
            CurrentTask = Tool = File = Preview = Activity = Helper = WaitingKind = "";
            ToolCount = 0;
            FilesTouched.Clear();
            Recent.Clear();
            Present = IsLead;
            SpawnedAt = LastEventMs = 0;
            ActiveAgentIds.Clear();
            OpenTools.Clear();
            Busy = false;
            WaitingSince = WorkingUntil = ErrorUntil = DoneUntil = 0;
        }
    }
}
