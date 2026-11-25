using System.Collections.Generic;
using UnityEngine;

namespace BayanihanHQ
{
    /// <summary>
    /// The office room: binds each roster entry to its desk, seats a responder there, and drives every responder from the
    /// state model each frame (walk-ins, wandering to the break corner, poses). It also hands out break-corner spots and
    /// plays a soft ping when someone starts waiting for the owner. The furniture and references come from OfficeSceneBuilder.
    /// </summary>
    public class HqOffice : MonoBehaviour
    {
        [Tooltip("Desks by slot; slot 0 is the lead's big desk.")]
        public DeskStation[] desks;
        [Tooltip("Inside the entrance: where newly started subagents walk in from.")]
        public Transform door;
        [Tooltip("Places in the kubo / sari-sari corner where idle responders go.")]
        public Transform[] breakSpots;
        public Transform mapTable;
        [Tooltip("Played when a responder starts waiting for you (the game's Kenney interface sound).")]
        public AudioClip waitingPing;
        [Range(0, 1)] public float pingVolume = 0.6f;

        readonly Dictionary<string, Responder> responders = new Dictionary<string, Responder>();
        bool[] spotTaken;
        HqDirector director;
        AudioSource audioSource;
        float lastPing = -10;

        /// <summary>All responders by roster key.</summary>
        public IReadOnlyDictionary<string, Responder> Responders => responders;

        /// <summary>The middle of the break corner, which people there face.</summary>
        public Vector3 BreakCentre
        {
            get
            {
                if (breakSpots == null || breakSpots.Length == 0) return transform.position;
                var sum = Vector3.zero;
                foreach (var s in breakSpots) sum += s.position;
                return sum / breakSpots.Length;
            }
        }

        void Start()
        {
            director = HqDirector.Instance;
            if (director == null) { Debug.LogWarning("[Bayanihan HQ] No HqDirector in the scene."); return; }
            spotTaken = new bool[breakSpots != null ? breakSpots.Length : 0];
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            // The lead wears the game's own rescuer gear; everyone else is a resident volunteer in a role-colour vest
            // (on the rescuer models the vest would be hidden under their orange jacket).
            var library = CharacterLibrary.Instance;
            var rescuers = new List<CharacterEntry>();
            var volunteers = new List<CharacterEntry>();
            var extras = new List<CharacterEntry>();
            if (library != null)
                foreach (var c in library.characters)
                {
                    if (c == null || !c.IsComplete) continue;
                    if (c.category == CharacterCategory.Rescuer) rescuers.Add(c);
                    else if (c.category == CharacterCategory.Adult) volunteers.Add(c);
                    else if (c.category == CharacterCategory.Teen || c.category == CharacterCategory.Elder) extras.Add(c);
                }
            volunteers.AddRange(extras);

            int n = 0;
            foreach (var agent in director.State.Agents)
            {
                var desk = DeskFor(agent.Entry.desk);
                if (desk == null) { Debug.LogWarning($"[Bayanihan HQ] No desk {agent.Entry.desk} for {agent.Name}; check roster.json."); continue; }
                desk.Bind(agent);

                // The same person always sits at the same desk, so the team is recognisable from day to day.
                CharacterEntry entry = null;
                if (agent.IsLead && rescuers.Count > 0) entry = rescuers[1 % rescuers.Count];
                else if (volunteers.Count > 0) entry = volunteers[n++ % volunteers.Count];
                else if (rescuers.Count > 0) entry = rescuers[n++ % rescuers.Count];
                if (entry == null) continue;

                var r = Responder.Create(agent, desk, this, entry);
                if (r != null) responders[agent.Key] = r;
            }
            if (responders.Count == 0)
                Debug.LogWarning("[Bayanihan HQ] No characters could be made (is Resources/CharacterLibrary.asset built?). Desks still update.");

            director.State.Spawned += OnSpawned;
            director.State.StatusChanged += OnStatusChanged;
            director.StateRebuilt += OnRebuilt;
            gameObject.AddComponent<WaitingIndicators>();
        }

        void OnDestroy()
        {
            if (director == null || director.State == null) return;
            director.State.Spawned -= OnSpawned;
            director.State.StatusChanged -= OnStatusChanged;
            director.StateRebuilt -= OnRebuilt;
        }

        DeskStation DeskFor(int slot)
        {
            if (desks == null) return null;
            foreach (var d in desks) if (d != null && d.slot == slot) return d;
            return null;
        }

        void Update()
        {
            if (director == null) return;
            long now = HqDirector.NowMs;
            foreach (var r in responders.Values) r.Tick(now);
        }

        void OnSpawned(AgentState agent)
        {
            if (responders.TryGetValue(agent.Key, out var r)) r.OnSpawned();
        }

        void OnStatusChanged(AgentState agent, AgentStatus from, AgentStatus to)
        {
            // One ping per wait (never more than one a second, so a burst of prompts doesn't nag).
            if (to != AgentStatus.Waiting || waitingPing == null || director.Paused) return;
            if (Time.unscaledTime - lastPing < 1f) return;
            lastPing = Time.unscaledTime;
            audioSource.PlayOneShot(waitingPing, pingVolume);
        }

        void OnRebuilt()
        {
            for (int i = 0; i < spotTaken.Length; i++) spotTaken[i] = false;
            foreach (var r in responders.Values) r.ResetToDesk();
        }

        /// <summary>A free spot in the break corner (random among the free ones), or -1 when it is full.</summary>
        public int ClaimBreakSpot()
        {
            var free = new List<int>();
            for (int i = 0; i < spotTaken.Length; i++) if (!spotTaken[i]) free.Add(i);
            if (free.Count == 0) return -1;
            int pick = free[Random.Range(0, free.Count)];
            spotTaken[pick] = true;
            return pick;
        }

        public void ReleaseBreakSpot(int index)
        {
            if (index >= 0 && index < spotTaken.Length) spotTaken[index] = false;
        }
    }
}
