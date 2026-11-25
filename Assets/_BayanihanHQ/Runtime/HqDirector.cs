using System;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// Runs the office: opens the receiver, replays the log, feeds live or simulated events into <see cref="OfficeState"/>
    /// on the main thread, and owns the Simulate / Pause / Reset toggles. Everything visual reads from <see cref="State"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HqDirector : MonoBehaviour
    {
        /// <summary>How the office is being fed, for the top bar.</summary>
        public enum Feed { Offline, Listening, Live, Simulating }

        [Tooltip("Must match PORT in .claude/office/hook.mjs.")]
        public int port = 47821;
        [Tooltip("Empty: ~/Documents/Bayanihan/.claude/office/events.jsonl (two folders above Assets).")]
        public string logPathOverride = "";
        [Tooltip("How much of the log to read when the office opens.")]
        public int replayBytes = 512 * 1024;
        [Tooltip("Only replay events this many hours older than the newest one.")]
        public float replayWindowHours = 6f;
        [Tooltip("Start in Simulate mode (for demos).")]
        public bool simulateOnStart;

        /// <summary>The one director in the scene (the office has one).</summary>
        public static HqDirector Instance { get; private set; }

        public OfficeState State { get; private set; }
        public HqReceiver Receiver { get; private set; }
        public bool Simulating { get; private set; }
        public bool Paused { get; private set; }
        public int Replayed { get; private set; }
        public string LogPath { get; private set; }

        /// <summary>Unix time in ms, the clock every status timer uses.</summary>
        public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Raised after Reset or a Simulate switch rebuilds the state, so views can start fresh.</summary>
        public event Action StateRebuilt;

        HqSimulator simulator;
        long pausedAt;
        long simClockOffset;   // simulated time stops while paused

        void Awake()
        {
            Instance = this;
            // The office is a monitor, not a game: 30 fps is plenty, and an uncapped Editor eats the CPU that Claude
            // Code's hooks need to start quickly. Runtime only; nothing is saved.
            Application.targetFrameRate = 30;
            State =new OfficeState(HqRoster.Load(HqRoster.DefaultPath(Application.dataPath)));
            simulator = new HqSimulator(Environment.TickCount);
            LogPath = string.IsNullOrEmpty(logPathOverride) ? HqReplay.DefaultLogPath(Application.dataPath) : logPathOverride;

            Receiver = new HqReceiver();
            if (!Receiver.Start(port))
                Debug.LogWarning($"[Bayanihan HQ] Couldn't listen on 127.0.0.1:{port} ({Receiver.Error}). The log replay and Simulate still work.");

            if (simulateOnStart) SetSimulate(true);
            else ReplayLog();
        }

        void OnDisable()
        {
            // Covers leaving Play mode and a domain reload: the port must be free for the next run.
            Receiver?.Stop();
        }

        void OnEnable()
        {
            if (Receiver != null && !Receiver.Listening && State != null) Receiver.Start(port);
        }

        void OnDestroy()
        {
            Receiver?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            long now = NowMs;
            if (Paused) return;   // the receiver keeps queueing; nothing moves until Pause is turned off

            while (Receiver.TryDequeue(out var json))
            {
                if (Simulating) continue;   // live events are in the log too; turning Simulate off replays them
                var e = HqEvent.Parse(json);
                if (e != null) State.Apply(e, now);
            }

            if (Simulating)
                foreach (var e in simulator.Update(now - simClockOffset))
                {
                    e.ts += simClockOffset;
                    State.Apply(e, now);
                }

            State.Tick(now);
        }

        /// <summary>How the top bar should describe the feed.</summary>
        public Feed CurrentFeed
        {
            get
            {
                if (Simulating) return Feed.Simulating;
                if (Receiver == null || !Receiver.Listening) return Feed.Offline;
                return NowMs - State.LastEventMs < 60_000 ? Feed.Live : Feed.Listening;
            }
        }

        /// <summary>Simulate on: an empty office plays the scripted demo. Off: back to the real log and live events.</summary>
        public void SetSimulate(bool on)
        {
            Simulating = on;
            State.Reset();
            simulator.Restart();
            simClockOffset = 0;
            if (Paused) pausedAt = NowMs;
            if (!on) ReplayLog();
            StateRebuilt?.Invoke();
        }

        /// <summary>Freezes the office. Live events wait in the receiver's queue and are applied on resume.</summary>
        public void SetPaused(bool paused)
        {
            if (paused == Paused) return;
            Paused = paused;
            if (paused) pausedAt = NowMs;
            else simClockOffset += NowMs - pausedAt;
        }

        /// <summary>Clears the office. Live mode starts empty and fills from the next event; Simulate starts its story again.</summary>
        public void ResetOffice()
        {
            State.Reset();
            if (Simulating)
            {
                simulator.Restart();
                simClockOffset = 0;
                if (Paused) pausedAt = NowMs;
            }
            StateRebuilt?.Invoke();
        }

        void ReplayLog()
        {
            try
            {
                var events = HqReplay.ReadTail(LogPath, replayBytes, TimeSpan.FromHours(replayWindowHours));
                long now = NowMs;
                foreach (var e in events) State.Apply(e, now);
                Replayed = events.Count;
                Debug.Log($"[Bayanihan HQ] Replayed {events.Count} events from {LogPath}; listening on 127.0.0.1:{port}: {Receiver.Listening}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bayanihan HQ] Couldn't replay the log: " + e.Message);
            }
        }
    }
}
