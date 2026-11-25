using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>One desk in the office: who sits there and in what colour. Edited in Config/roster.json.</summary>
    [Serializable]
    public class HqRosterEntry
    {
        /// <summary>The subagent name from .claude/agents (or "lead" for the main session).</summary>
        public string key;
        /// <summary>The Filipino first name on the nameplate.</summary>
        public string name;
        public string role;
        /// <summary>Hex colour of the vest, nameplate and cards, e.g. #4E9C2C.</summary>
        public string color;
        /// <summary>Desk slot in the office; 0 is the lead's big desk.</summary>
        public int desk;

        /// <summary>The parsed colour, grey if the hex is wrong.</summary>
        public Color Colour => ColorUtility.TryParseHtmlString(color, out var c) ? c : new Color(0.6f, 0.6f, 0.6f);
    }

    /// <summary>The office's team list. Falls back to a built-in roster if roster.json is missing or broken.</summary>
    [Serializable]
    public class HqRoster
    {
        /// <summary>The main Claude Code session's desk key.</summary>
        public const string LeadKey = "lead";

        public List<HqRosterEntry> agents = new List<HqRosterEntry>();

        /// <summary>Config/roster.json under the HQ folder.</summary>
        public static string DefaultPath(string dataPath) => Path.Combine(dataPath, "_BayanihanHQ", "Config", "roster.json");

        /// <summary>Reads roster.json; the office never fails to open because of a typo in it.</summary>
        public static HqRoster Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var r = JsonUtility.FromJson<HqRoster>(File.ReadAllText(path));
                    if (r != null && r.agents != null && r.agents.Count > 0)
                    {
                        if (r.Find(LeadKey) == null) r.agents.Insert(0, Default().agents[0]);
                        return r;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bayanihan HQ] roster.json could not be read, using the built-in team: " + e.Message);
            }
            return Default();
        }

        public HqRosterEntry Find(string key)
        {
            foreach (var a in agents) if (a.key == key) return a;
            return null;
        }

        /// <summary>The same team as Config/roster.json ships with.</summary>
        public static HqRoster Default()
        {
            var r = new HqRoster();
            void Add(string key, string name, string role, string color, int desk) =>
                r.agents.Add(new HqRosterEntry { key = key, name = name, role = role, color = color, desk = desk });
            Add(LeadKey, "Kap. Dado", "Lead / Orchestrator", "#F09A47", 0);
            Add("map-level-builder", "Lito", "Map & Level Builder", "#4E9C2C", 1);
            Add("gameplay-scripter", "Bea", "Gameplay Scripter", "#2E6CC2", 2);
            Add("physics-colliders", "Jun", "Physics & Colliders", "#7A4FC2", 3);
            Add("vfx-environment", "Tala", "VFX & Environment", "#2BB3C0", 4);
            Add("ui-frontend", "Mika", "UI / Front-end", "#E05A9A", 5);
            Add("backend-data", "Rey", "Backend & Data", "#E9C12F", 6);
            Add("audio", "Lorna", "Audio", "#E07A2E", 7);
            Add("animation-characters", "Kiko", "Animation & Characters", "#C23C32", 8);
            Add("qa-tester", "Nena", "QA / Tester", "#6B8A99", 9);
            return r;
        }
    }
}
