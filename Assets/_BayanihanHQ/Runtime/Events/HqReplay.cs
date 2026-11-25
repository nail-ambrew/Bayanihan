using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BayanihanHQ
{
    /// <summary>
    /// Reads the end of events.jsonl when the office opens, so it shows what is going on even if Unity was started after
    /// the Claude Code session. Only the tail is read: the log can be 5 MB and the office only needs recent history.
    /// </summary>
    public static class HqReplay
    {
        /// <summary>The log the hook writes: ~/Documents/Bayanihan/.claude/office/events.jsonl, two levels above Assets.</summary>
        public static string DefaultLogPath(string dataPath) =>
            Path.GetFullPath(Path.Combine(dataPath, "..", "..", ".claude", "office", "events.jsonl"));

        /// <summary>
        /// Events from the last <paramref name="maxBytes"/> of the log (and the rotated file if the log is short), keeping
        /// only those within <paramref name="window"/> of the newest one, oldest first.
        /// </summary>
        public static List<HqEvent> ReadTail(string path, int maxBytes, TimeSpan window)
        {
            var lines = new List<string>();
            var rotated = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + ".1.jsonl");
            long mainSize = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (mainSize < maxBytes && File.Exists(rotated)) lines.AddRange(TailLines(rotated, (int)(maxBytes - mainSize)));
            if (mainSize > 0) lines.AddRange(TailLines(path, maxBytes));

            var events = new List<HqEvent>();
            foreach (var line in lines)
            {
                var e = HqEvent.Parse(line);
                if (e != null && !e.sim) events.Add(e);
            }
            if (events.Count == 0) return events;

            // Async hooks can land a few milliseconds out of order; sort, then drop anything older than the window.
            events.Sort((a, b) => a.ts.CompareTo(b.ts));
            long newest = events[events.Count - 1].ts;
            long cutoff = newest - (long)window.TotalMilliseconds;
            events.RemoveAll(e => e.ts < cutoff);
            return events;
        }

        /// <summary>The complete lines in the last maxBytes of a file. The first, probably cut, line is dropped.</summary>
        public static List<string> TailLines(string path, int maxBytes)
        {
            var result = new List<string>();
            if (maxBytes <= 0) return result;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - maxBytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[fs.Length - start];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = fs.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    var text = Encoding.UTF8.GetString(buffer, 0, read);
                    var parts = text.Split('\n');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (i == 0 && start > 0) continue;
                        var line = parts[i].Trim();
                        if (line.Length > 0) result.Add(line);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }
    }
}
