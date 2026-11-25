using System;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// One line of .claude/office/events.jsonl, as written by .claude/office/hook.mjs. The field names are short because
    /// every tool call writes one. Missing fields come back as their defaults from JsonUtility.
    /// </summary>
    [Serializable]
    public class HqEvent
    {
        /// <summary>Unix time in milliseconds when the hook ran.</summary>
        public long ts;
        /// <summary>Claude Code session id.</summary>
        public string sid;
        /// <summary>Hook event name, e.g. PreToolUse, SubagentStart, PermissionRequest.</summary>
        public string ev;
        /// <summary>Roster key of the desk this belongs to (e.g. "lead", "qa-tester").</summary>
        public string agent;
        /// <summary>How the desk was chosen: agent, spawn, path, cmd or lead.</summary>
        public string via;
        /// <summary>A built-in helper subagent (e.g. Explore) whose work was routed to this desk.</summary>
        public string sub;
        /// <summary>Opaque subagent id, so two runs of the same role can be told apart.</summary>
        public string aid;
        /// <summary>Opaque tool-use id, used to pair PreToolUse with PostToolUse.</summary>
        public string tuid;
        public string tool;
        /// <summary>Project-relative path, e.g. Game/Assets/Scripts/SimpleBoat.cs.</summary>
        public string file;
        /// <summary>At most 100 characters, already redacted by the hook.</summary>
        public string preview;
        /// <summary>True on PostToolUse, false on PostToolUseFailure.</summary>
        public bool ok;
        /// <summary>The failure was the user interrupting, not an error.</summary>
        public bool intr;
        /// <summary>For Agent calls: the subagent type the work was handed to.</summary>
        public string to;
        /// <summary>For Notification: notification_type.</summary>
        public string kind;
        public HqTaskInfo task;
        public HqTaskInfo[] todos;
        /// <summary>Made by Simulate mode. Never written to the log.</summary>
        public bool sim;

        /// <summary>Parses one JSON line. Returns null for blank or broken lines instead of throwing.</summary>
        public static HqEvent Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                var e = JsonUtility.FromJson<HqEvent>(line);
                if (e == null || string.IsNullOrEmpty(e.ev)) return null;
                if (string.IsNullOrEmpty(e.agent)) e.agent = HqRoster.LeadKey;
                return e;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The file name without folders, for bubbles and cards.</summary>
        public string FileName => string.IsNullOrEmpty(file) ? "" : file.Substring(file.LastIndexOf('/') + 1);

        /// <summary>A short "Edit → SimpleBoat.cs" style line for bubbles, monitors and the log.</summary>
        public string Summary()
        {
            var target = !string.IsNullOrEmpty(file) ? FileName : preview;
            if (string.IsNullOrEmpty(tool)) return string.IsNullOrEmpty(preview) ? ev : ev + ": " + preview;
            return string.IsNullOrEmpty(target) ? tool : tool + " → " + target;
        }
    }

    /// <summary>A task or todo item as the hook sends it: an id (tasks only), its text and its state.</summary>
    [Serializable]
    public class HqTaskInfo
    {
        public string id;
        public string text;
        public string status;

        /// <summary>JsonUtility fills absent objects with an empty instance, so "present" means it has content.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(id) && string.IsNullOrEmpty(text) && string.IsNullOrEmpty(status);
    }
}
