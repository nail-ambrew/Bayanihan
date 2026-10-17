using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The boat's side of a throw rescue. Reach, throw, row, go: the safest way to get somebody is the one that keeps
/// the boat and its crew out of the water. Press T and the crew throw the Salbabida or the Lubid - whichever the
/// person needs - to somebody within about 12 m. They take hold, and are brought in to the side of the boat; then E
/// takes them aboard, as it does for anyone within reach.
///
/// A throw needs a clear line to them, a boat that is nearly still beside them, the item they need packed and not
/// already out, and room on the boat for them. When it cannot be done, the prompt says why.
///
/// Added to the boat automatically by SimpleBoat.
/// </summary>
[RequireComponent(typeof(SimpleBoat))]
public class BoatRescue : MonoBehaviour
{
    [Header("Throwing")]
    [Tooltip("How far a ring or a rope can be thrown, in metres from the crew member who throws it.")]
    public float throwRange = 12f;
    [Tooltip("The boat has to be slower than this, relative to the person in the water, to throw accurately (m/s).")]
    public float maxThrowSpeed = 4f;
    [Tooltip("How fast the ring or rope flies (m/s).")]
    public float throwSpeed = 16f;
    [Tooltip("Seconds before another throw, while the crew get the line ready.")]
    public float throwCooldown = 0.8f;

    [Header("Bringing them in")]
    [Tooltip("How fast somebody in the water is pulled to the boat (m/s).")]
    public float pullSpeed = 1.8f;
    [Tooltip("How fast somebody on a roof walks to the edge (m/s).")]
    public float roofWalkSpeed = 1f;
    [Tooltip("How fast somebody comes down the line from a roof (m/s).")]
    public float descendSpeed = 1.5f;
    [Tooltip("In the water, if the boat is further than this from them the line goes taut and they let go (metres).")]
    public float maxLineLength = 16f;
    [Tooltip("In the water, if the boat is going faster than this they lose their grip (m/s).")]
    public float maxLineSpeed = 4f;

    public SimpleBoat Boat { get; private set; }
    /// <summary>Who a throw would go to if T were pressed now.</summary>
    public Survivor Target { get; private set; }
    /// <summary>What the prompt says: how to throw, or why not. Null when there is nobody to throw to.</summary>
    public string Prompt { get; private set; }
    public bool PromptIsWarning { get; private set; }

    /// <summary>Lines thrown and lost, for the report.</summary>
    public int Throws { get; private set; }
    public int LinesLost { get; private set; }
    public IReadOnlyList<RescueLine> Lines => lines;

    public bool LifebuoyOut { get; private set; }
    public bool RopeOut { get; private set; }

    /// <summary>The lifebuoy lying in the boat, if there is one. It is hidden while a copy is out.</summary>
    public Transform DeckLifebuoy { get; private set; }

    /// <summary>Where a throw starts from: the bow lookout's hands.</summary>
    public Vector3 ThrowOrigin
    {
        get
        {
            Vector3 at = passengers != null ? passengers.LookoutWorldPosition : transform.position;
            return at + Vector3.up * 1.4f;
        }
    }

    readonly List<RescueLine> lines = new List<RescueLine>();
    BoatPassengers passengers;
    Camera cam;
    float nextAimAt, nextThrowAt;
    GUIStyle promptStyle;

    void Awake()
    {
        Boat = GetComponent<SimpleBoat>();
        passengers = GetComponent<BoatPassengers>();
    }

    void Start()
    {
        DeckLifebuoy = CharacterRig.FindDeep(transform, "lifebuoy");
    }

    void Update()
    {
        if (!Boat.IsPlayerAboard)
        {
            Target = null;
            Prompt = null;
            return;
        }

        if (Time.time >= nextAimAt)
        {
            nextAimAt = Time.time + 0.1f;
            Aim();
        }

        if (Target != null && InputHelper.ThrowPressed()) Throw(Target);
    }

    // ------------------------------------------------------------------ who can be thrown to
    void Aim()
    {
        Vector3 origin = ThrowOrigin;
        if (cam == null) cam = Camera.main;

        Survivor best = null;
        float bestScore = float.MaxValue;
        foreach (var s in Survivor.All)
        {
            if (s == null || s.IsRescued || s.IsHeld) continue;
            if (!NeedsThrow(s)) continue;

            Vector3 head = s.SightPoint;
            float distance = Vector3.Distance(origin, head);
            if (distance > throwRange) continue;
            if (!LineOfSight.Clear(origin, head)) continue;

            // Nearer the middle of the screen wins, then nearer to the boat.
            float score = distance;
            if (cam != null)
            {
                float facing = Vector3.Dot(cam.transform.forward, (head - cam.transform.position).normalized);
                score += facing < 0.2f ? 100f : -facing * 6f;
            }
            if (score < bestScore) { bestScore = score; best = s; }
        }

        Target = best;
        BuildPrompt();
    }

    /// <summary>
    /// A line is the way to somebody who cannot be reached by hand: up on a roof, behind debris, or in the water but
    /// out of reach. Somebody within arm's length of the boat, who needs no line, is simply taken aboard.
    /// </summary>
    bool NeedsThrow(Survivor s)
    {
        if (s.RequiresLine) return true;
        if (s.LineDelivered) return false;
        bool wantsGear = (s.needs & (Need.Lifebuoy | Need.Rope)) != 0;
        return wantsGear && !Boat.IsInReach(s);
    }

    /// <summary>
    /// Could the crew have reached this person with a line from where the boat is? In range, in view, and nothing
    /// missing from the pack. A swim rescue where this is true was not the last resort it should be.
    /// </summary>
    public bool CouldThrowTo(Survivor s)
    {
        if (s == null || s.InLiveWire || !s.HasRequiredItems) return false;
        Vector3 origin = ThrowOrigin;
        return Vector3.Distance(origin, s.SightPoint) <= throwRange && LineOfSight.Clear(origin, s.SightPoint);
    }

    /// <summary>What has to go out to them: the ring if they need it, the rope if they need it, a rope if they need nothing but are out of reach.</summary>
    void WhatToThrow(Survivor s, out bool lifebuoy, out bool rope)
    {
        lifebuoy = (s.needs & Need.Lifebuoy) != 0;
        rope = (s.needs & Need.Rope) != 0;
        if (!lifebuoy && !rope) rope = true;        // somebody on a roof who needs nothing in particular still needs a line
    }

    /// <summary>Null if a throw can go ahead; otherwise why it cannot.</summary>
    string ThrowBlock(Survivor s, bool lifebuoy, bool rope)
    {
        if (s.InLiveWire) return "live wire in the water - get the power cut first (C)";

        if (lifebuoy && !LoadoutState.Has(RescueItemId.Lifebuoy)) return "needs a Salbabida - not packed";
        if (rope && !LoadoutState.Has(RescueItemId.Rope)) return "needs a Lubid - not packed";
        if (s.NeedsFirstAidKit && !LoadoutState.Has(RescueItemId.FirstAid)) return "needs a Botika - not packed";

        if (lifebuoy && LifebuoyOut) return "the Salbabida is already out";
        if (rope && RopeOut) return "the Lubid is already out";

        if (Boat.Passengers + lines.Count >= Boat.capacity) return "the boat is full";
        if (Boat.SpeedRelativeTo(s) > maxThrowSpeed) return "too fast to throw - ease off";
        return null;
    }

    void BuildPrompt()
    {
        Prompt = null;
        PromptIsWarning = false;
        if (Target == null) return;

        WhatToThrow(Target, out bool lifebuoy, out bool rope);
        string block = ThrowBlock(Target, lifebuoy, rope);
        string who = Target.DisplayName;
        float distance = Vector3.Distance(ThrowOrigin, Target.SightPoint);

        if (block != null)
        {
            Prompt = who + ": " + block;
            PromptIsWarning = true;
        }
        else if (Time.time < nextThrowAt)
        {
            Prompt = "Getting the line ready...";
        }
        else
        {
            Prompt = "T · throw the " + ItemNames(lifebuoy, rope) + " to " + who + " (" + distance.ToString("0") + " m)";
        }
    }

    static string ItemNames(bool lifebuoy, bool rope)
    {
        if (lifebuoy && rope) return "Salbabida and Lubid";
        return lifebuoy ? "Salbabida" : "Lubid";
    }

    // ------------------------------------------------------------------ the throw
    /// <summary>Throws what they need to them, if that can be done. Returns whether a line went out.</summary>
    public bool Throw(Survivor s)
    {
        if (Time.time < nextThrowAt) return false;

        WhatToThrow(s, out bool lifebuoy, out bool rope);
        string block = ThrowBlock(s, lifebuoy, rope);
        if (block != null)
        {
            Boat.SetStatus(s.DisplayName + ": " + block, 2.5f);
            return false;
        }

        var line = RescueLine.Launch(this, s, lifebuoy, rope);
        lines.Add(line);
        LifebuoyOut |= lifebuoy;
        RopeOut |= rope;
        if (lifebuoy && DeckLifebuoy != null) DeckLifebuoy.gameObject.SetActive(false);

        Throws++;
        nextThrowAt = Time.time + throwCooldown;
        Boat.SetStatus("Throwing the " + ItemNames(lifebuoy, rope) + " to " + s.DisplayName + "...", 2f);
        return true;
    }

    // ------------------------------------------------------------------ told by the lines
    public void OnLineHeld(RescueLine line)
    {
        Boat.SetStatus(line.Target.DisplayName + " has the line - hold the boat steady and bring them in.", 3f);
    }

    public void OnLineAlongside(RescueLine line)
    {
        Boat.SetStatus(line.Target.DisplayName + " is alongside - press E to bring them aboard.", 3f);
    }

    public void OnLineLost(RescueLine line, string why)
    {
        LinesLost++;
        Boat.SetStatus(line.Target.DisplayName + ": " + why, 3f);
    }

    /// <summary>A line is finished with, one way or another: the ring and the rope are back aboard.</summary>
    public void OnLineDone(RescueLine line)
    {
        lines.Remove(line);

        bool lifebuoy = false, rope = false;
        foreach (var l in lines) { lifebuoy |= l.CarriesLifebuoy; rope |= l.CarriesRope; }
        LifebuoyOut = lifebuoy;
        RopeOut = rope;

        if (!lifebuoy && DeckLifebuoy != null) DeckLifebuoy.gameObject.SetActive(LoadoutState.Has(RescueItemId.Lifebuoy));
    }

    // ------------------------------------------------------------------ screen
    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (Prompt == null || Target == null || !Boat.IsPlayerAboard) return;
        if (RescueManager.Instance != null && RescueManager.Instance.GameOver) return;

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        promptStyle.normal.textColor = PromptIsWarning ? new Color(1f, 0.6f, 0.35f) : new Color(0.7f, 0.95f, 1f);
        GUI.Label(new Rect(0f, Screen.height - 190f, Screen.width, 40f), Prompt, promptStyle);

        // A frame round the person a throw would go to.
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(Target.SightPoint);
        if (sp.z <= 0f) return;

        var box = new Rect(sp.x - 24f, Screen.height - sp.y - 24f, 48f, 48f);
        var colour = PromptIsWarning ? new Color(1f, 0.6f, 0.35f) : new Color(0.7f, 0.95f, 1f);
        var prior = GUI.color;
        GUI.color = colour;
        const float t = 3f, c = 12f;      // line weight and corner length
        GUI.DrawTexture(new Rect(box.x, box.y, c, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.x, box.y, t, c), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMax - c, box.y, c, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMax - t, box.y, t, c), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.x, box.yMax - t, c, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.x, box.yMax - c, t, c), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMax - c, box.yMax - t, c, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMax - t, box.yMax - c, t, c), Texture2D.whiteTexture);
        GUI.color = prior;
    }
}
