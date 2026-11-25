using System.Collections.Generic;

namespace BayanihanHQ
{
    /// <summary>A line on the whiteboard.</summary>
    public class BoardItem
    {
        /// <summary>"assign" (a hand-off to a subagent), "task" (TaskCreate) or "todo" (TodoWrite).</summary>
        public string Kind;
        public string Id;
        /// <summary>Desk key it belongs to.</summary>
        public string Owner;
        public string Text;
        /// <summary>pending, in_progress, completed or failed.</summary>
        public string Status;
        /// <summary>Subagent id that took an assignment, so its SubagentStop can tick it.</summary>
        public string AgentId;
        public long CreatedMs;
        public long UpdatedMs;

        public bool IsDone => Status == "completed" || Status == "failed";
    }

    /// <summary>
    /// The whiteboard. Recent Claude Code models don't use the task tools unless told to, so the board always shows the
    /// lead's hand-offs to subagents, and adds TaskCreate/TodoWrite items when Claude does use them.
    /// </summary>
    public class TodoBoardState
    {
        public const int MaxItems = 24;

        public List<BoardItem> Items { get; } = new List<BoardItem>();

        public void Clear() => Items.Clear();

        /// <summary>A hand-off: the Agent tool's description, owned by the desk it went to.</summary>
        public void AddAssignment(string id, string owner, string text, long ts)
        {
            if (!string.IsNullOrEmpty(id) && FindById("assign", id) != null) return;
            Add(new BoardItem { Kind = "assign", Id = id, Owner = owner, Text = text, Status = "pending", CreatedMs = ts, UpdatedMs = ts });
        }

        /// <summary>A subagent started: the oldest waiting hand-off for that desk is now in progress.</summary>
        public void StartAssignment(string owner, string agentId, long ts)
        {
            foreach (var item in Items)
            {
                if (item.Kind != "assign" || item.Owner != owner || item.Status != "pending") continue;
                item.Status = "in_progress";
                item.AgentId = agentId;
                item.UpdatedMs = ts;
                return;
            }
        }

        /// <summary>A subagent stopped: tick its hand-off (by id, else the oldest one in progress at that desk).</summary>
        public void FinishAssignment(string owner, string agentId, long ts)
        {
            BoardItem match = null;
            foreach (var item in Items)
            {
                if (item.Kind != "assign" || item.IsDone) continue;
                if (!string.IsNullOrEmpty(agentId) && item.AgentId == agentId) { match = item; break; }
                if (match == null && item.Owner == owner && item.Status == "in_progress") match = item;
            }
            if (match == null) return;
            match.Status = "completed";
            match.UpdatedMs = ts;
        }

        public void FailAssignment(string id, long ts)
        {
            var item = FindById("assign", id);
            if (item == null || item.IsDone) return;
            item.Status = "failed";
            item.UpdatedMs = ts;
        }

        /// <summary>TaskCreated / TaskUpdate / TaskCompleted. "deleted" takes it off the board.</summary>
        public void UpsertTask(HqTaskInfo info, string owner, long ts)
        {
            if (info == null || info.IsEmpty) return;
            var item = string.IsNullOrEmpty(info.id) ? null : FindById("task", info.id);
            if (info.status == "deleted") { if (item != null) Items.Remove(item); return; }
            if (item == null)
            {
                item = new BoardItem { Kind = "task", Id = info.id, Owner = owner, Text = info.text ?? "", Status = "pending", CreatedMs = ts };
                Add(item);
            }
            if (!string.IsNullOrEmpty(info.text)) item.Text = info.text;
            if (!string.IsNullOrEmpty(info.status)) item.Status = info.status;
            item.UpdatedMs = ts;
        }

        /// <summary>TodoWrite sends the whole list each time, so it replaces the previous todos.</summary>
        public void ReplaceTodos(HqTaskInfo[] todos, string owner, long ts)
        {
            Items.RemoveAll(i => i.Kind == "todo");
            for (int i = 0; i < todos.Length; i++)
            {
                if (todos[i] == null || string.IsNullOrEmpty(todos[i].text)) continue;
                Add(new BoardItem { Kind = "todo", Id = "todo" + i, Owner = owner, Text = todos[i].text, Status = string.IsNullOrEmpty(todos[i].status) ? "pending" : todos[i].status, CreatedMs = ts, UpdatedMs = ts });
            }
        }

        public int CountDone()
        {
            int n = 0;
            foreach (var i in Items) if (i.IsDone) n++;
            return n;
        }

        BoardItem FindById(string kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var item in Items) if (item.Kind == kind && item.Id == id) return item;
            return null;
        }

        void Add(BoardItem item)
        {
            Items.Add(item);
            // The board has room for 24 lines: finished ones go first, then the oldest.
            while (Items.Count > MaxItems)
            {
                int drop = Items.FindIndex(i => i.IsDone);
                Items.RemoveAt(drop >= 0 ? drop : 0);
            }
        }
    }
}
