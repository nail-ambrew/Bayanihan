using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The loadout screen's packing stage: a little 3D set (a rescuer and an open dry-bag on a crate) drawn into a window on
/// the LOADOUT card, so packing is something you watch happen.
/// <list type="bullet">
/// <item>Pack: the item lifts in from the equipment side, arcs into the bag stretching as it flies and squashing as it
/// lands, and the bag bulges and settles, with a rustle. Under 0.6 s.</item>
/// <item>Unpack: the same, backwards.</item>
/// <item>Denied (the bag is full): the bag shakes, the window flashes a red outline, a thump.</item>
/// <item>The pack meter: four pips that pop as they fill (the game counts slots, not weight).</item>
/// <item>READY (LAUNCH): zip, buckle click, the rescuer slings the bag, a READY stamp, about 2 s; skippable.</item>
/// </list>
/// The set lives far below everything on its own layer, seen only by its own camera. Models come from
/// <see cref="CutsceneAssets"/> (placeholders made by Source Assets/Loadout/make_loadout_models.py).
/// </summary>
public class PackingStage : MonoBehaviour
{
    public const int Layer = 30;
    public const float PackSeconds = 0.38f, BulgeSeconds = 0.12f, UnpackSeconds = 0.32f, DenySeconds = 0.4f;
    public const float ReadySeconds = 2.0f;
    static readonly Vector3 SetOrigin = new Vector3(0f, -500f, 0f);
    static readonly Vector3 EnterFrom = new Vector3(1.3f, 1.45f, 0.1f);     // screen left: the equipment side

    Transform world, bag, bagBody, flap, rescuerSpot;
    CharacterAnimator rescuer;
    Camera cam;
    RenderTexture target;
    RawImage view;
    Image frame, flash;
    readonly List<Image> pips = new List<Image>();
    TMP_Text meterLabel;
    RectTransform stamp;
    readonly Dictionary<RescueItemId, Transform> inBag = new Dictionary<RescueItemId, Transform>();
    readonly List<RescueItemId> order = new List<RescueItemId>();
    Vector3 bagHome;
    int shown = -1;

    /// <summary>How long the last pack or unpack animation took (seconds), for tests.</summary>
    public float LastAnimSeconds { get; private set; }
    public int Denied { get; private set; }
    public bool ReadyRunning { get; private set; }
    public float FlashAlpha => flash != null ? flash.color.a : 0f;
    public int PipsLit { get; private set; }
    public RawImage View => view;
    public bool HasModels { get; private set; }

    // ------------------------------------------------------------------ building
    /// <summary>Makes the stage and its window, placed in <paramref name="parent"/> at <paramref name="pos"/>.</summary>
    public static PackingStage Create(Transform parent, Vector2 pos, Vector2 size)
    {
        var holder = UiKit.Panel("PackingStage", parent, UiTheme.BrownDark, UiTheme.RadiusSmall);
        UiKit.Place(holder.rectTransform, UiKit.TopLeft, pos, size);
        holder.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var stage = holder.gameObject.AddComponent<PackingStage>();
        stage.Build(holder.rectTransform, size);
        return stage;
    }

    void Build(RectTransform holder, Vector2 size)
    {
        // window
        target = new RenderTexture(Mathf.RoundToInt(size.x * 3), Mathf.RoundToInt(size.y * 3), 24, RenderTextureFormat.ARGB32) { name = "PackingStageRT", antiAliasing = 2 };
        view = new GameObject("View", typeof(RectTransform)).AddComponent<RawImage>();
        view.transform.SetParent(holder, false);
        view.texture = target;
        view.raycastTarget = false;
        UiKit.Stretch(view.rectTransform);

        frame = UiKit.Image("Frame", holder.parent, new Color(1, 1, 1, 0.9f), FrameSprite());
        UiKit.Place(frame.rectTransform, UiKit.TopLeft, holder.anchoredPosition + new Vector2(-2, 2), size + new Vector2(4, 4));
        frame.raycastTarget = false;
        flash = UiKit.Image("DeniedFlash", holder.parent, new Color(UiTheme.Error.r, UiTheme.Error.g, UiTheme.Error.b, 0f), FrameSprite());
        UiKit.Place(flash.rectTransform, UiKit.TopLeft, holder.anchoredPosition + new Vector2(-3, 3), size + new Vector2(6, 6));
        flash.raycastTarget = false;

        // pack meter, bottom right of the window
        var meter = UiKit.Rect("Meter", holder);
        UiKit.Place(meter, UiKit.BottomRight, new Vector2(-5, 5), new Vector2(84, 12));
        var back = UiKit.Panel("Back", meter, new Color(0, 0, 0, 0.45f), 6f);
        UiKit.Stretch(back.rectTransform);
        meterLabel = UiKit.Label("Label", meter, "PACK 0/4", UiTheme.SmallSize - 2, Color.white, TextAnchor.MiddleLeft, heading: true);
        UiKit.Place(meterLabel.rectTransform, UiKit.Left, new Vector2(5, 0), new Vector2(40, 12));
        for (int i = 0; i < RescueItems.Slots; i++)
        {
            var pip = UiKit.Image("Pip" + i, meter, new Color(1, 1, 1, 0.25f), UiKit.Circle());
            UiKit.Place(pip.rectTransform, UiKit.Right, new Vector2(-5 - (RescueItems.Slots - 1 - i) * 9, 0), new Vector2(7, 7));
            pips.Add(pip);
        }

        // READY stamp, hidden until LAUNCH
        var s = UiKit.Label("ReadyStamp", holder, "READY", 30, UiTheme.Error, TextAnchor.MiddleCenter, heading: true);
        s.TextOutline(new Color(0.5f, 0.05f, 0.05f), 0.15f);
        stamp = s.rectTransform;
        UiKit.Place(stamp, UiKit.Center, new Vector2(-28, 6), new Vector2(150, 44));
        stamp.localEulerAngles = new Vector3(0, 0, 12f);
        stamp.gameObject.SetActive(false);

        BuildSet();
    }

    void BuildSet()
    {
        world = new GameObject("PackingStageSet").transform;
        world.position = SetOrigin;

        var camGo = new GameObject("StageCamera");
        camGo.transform.SetParent(world, false);
        cam = camGo.AddComponent<Camera>();
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.11f, 0.08f);
        cam.fieldOfView = 34f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20f;
        cam.targetTexture = target;
        camGo.transform.localPosition = new Vector3(-0.12f, 1.15f, 2.75f);
        camGo.transform.LookAt(SetOrigin + new Vector3(-0.12f, 0.92f, 0f));
        // keep the menu's own cameras off this layer
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c != cam) c.cullingMask &= ~(1 << Layer);

        var key = new GameObject("Key").AddComponent<Light>();
        key.transform.SetParent(world, false);
        key.transform.localEulerAngles = new Vector3(40f, 160f, 0f);
        key.type = LightType.Directional; key.intensity = 1.6f; key.color = new Color(1f, 0.93f, 0.82f);
        key.cullingMask = 1 << Layer; key.shadows = LightShadows.Soft;
        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.transform.SetParent(world, false);
        fill.transform.localPosition = new Vector3(-1.2f, 1.6f, 1.6f);
        fill.type = LightType.Point; fill.range = 6f; fill.intensity = 2.2f; fill.color = new Color(0.7f, 0.8f, 1f);
        fill.cullingMask = 1 << Layer;

        var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.36f, 0.24f, 0.15f) };
        var wall = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.25f, 0.2f, 0.17f) };
        var crate = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.55f, 0.4f, 0.24f) };
        Prop(PrimitiveType.Cube, new Vector3(0f, -0.05f, 0f), new Vector3(6f, 0.1f, 4f), wood);
        Prop(PrimitiveType.Cube, new Vector3(0f, 1.5f, -1.2f), new Vector3(6f, 3f, 0.1f), wall);
        Prop(PrimitiveType.Cube, new Vector3(0.2f, 0.22f, 0f), new Vector3(0.62f, 0.44f, 0.5f), crate);

        var a = CutsceneAssets.Current;
        HasModels = a.backpack != null;
        bag = new GameObject("Bag").transform;
        bag.SetParent(world, false);
        bag.localPosition = bagHome = new Vector3(0.2f, 0.44f, 0f);
        if (a.backpack != null)
        {
            var model = Instantiate(a.backpack, bag);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 14f, 0f);
            bagBody = Find(model.transform, "Bag_Body") ?? model.transform;
            flap = Find(model.transform, "Bag_Flap");
        }
        else Prop(PrimitiveType.Cube, Vector3.zero, new Vector3(0.34f, 0.46f, 0.22f), crate, bag).name = "Bag_Body";
        if (flap != null) flap.localRotation = Quaternion.Euler(-110f, 0f, 0f);      // open

        rescuerSpot = new GameObject("Rescuer").transform;
        rescuerSpot.SetParent(world, false);
        rescuerSpot.localPosition = new Vector3(-0.5f, 0f, -0.1f);
        rescuerSpot.localRotation = Quaternion.Euler(0f, 20f, 0f);
        var entry = CharacterLibrary.Instance != null ? CharacterLibrary.Instance.Find("C24") : null;
        if (entry != null)
        {
            rescuer = CharacterRig.Spawn(entry, rescuerSpot, CharacterPose.Idle);
            if (rescuer != null)
            {
                rescuer.animateWithinDistance = 1000f;
                rescuer.Overlays |= PoseOverlay.HeadLook;
                rescuer.LookTarget = world.TransformPoint(bagHome + Vector3.up * 0.3f);
            }
        }
        SetLayer(world.gameObject);
    }

    Transform Prop(PrimitiveType type, Vector3 pos, Vector3 scale, Material m, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent != null ? parent : world, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static void SetLayer(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
    }

    void OnDestroy()
    {
        if (world != null) Destroy(world.gameObject);
        if (target != null) { target.Release(); Destroy(target); }
    }

    // ------------------------------------------------------------------ items
    Transform MakeItem(RescueItemId id)
    {
        var prefab = CutsceneAssets.Current.ItemModel(id);
        Transform t;
        if (prefab != null) t = Instantiate(prefab, world).transform;
        else
        {
            t = new GameObject(id.ToString()).transform;
            t.SetParent(world, false);
            Prop(PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.12f, new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = UiTheme.Orange }, t);
        }
        t.name = "Packed_" + id;
        SetLayer(t.gameObject);
        return t;
    }

    /// <summary>Where the n-th packed item sits: in the bag's open mouth, a little of each showing.</summary>
    Vector3 SlotPosition(int n)
    {
        float x = (n % 2 == 0 ? -0.07f : 0.07f), z = (n < 2 ? 0.03f : -0.04f);
        return bagHome + new Vector3(x, 0.40f + n * 0.015f, z);
    }

    static Quaternion SlotRotation(int n) => Quaternion.Euler(-60f + n * 12f, n * 47f, n % 2 == 0 ? 15f : -15f);

    /// <summary>Shows exactly these items in the bag, with no animation (opening the screen, RECOMMENDED, CLEAR).</summary>
    public void Sync(IList<RescueItemId> picked)
    {
        StopAllCoroutines();
        foreach (var kv in inBag) if (kv.Value != null) Destroy(kv.Value.gameObject);
        inBag.Clear();
        order.Clear();
        for (int i = 0; i < picked.Count; i++)
        {
            var t = MakeItem(picked[i]);
            t.localPosition = SlotPosition(i);
            t.localRotation = SlotRotation(i);
            t.localScale = Vector3.one * 0.8f;
            inBag[picked[i]] = t;
            order.Add(picked[i]);
        }
        if (bagBody != null) bagBody.localScale = Vector3.one;
        bag.localPosition = bagHome;
        bag.localRotation = Quaternion.identity;
        SetMeter(picked.Count, false);
    }

    public void Pack(RescueItemId id)
    {
        if (inBag.ContainsKey(id)) return;
        var t = MakeItem(id);
        int n = order.Count;
        inBag[id] = t;
        order.Add(id);
        SetMeter(order.Count, true);
        StartCoroutine(PackRoutine(t, n));
    }

    IEnumerator PackRoutine(Transform t, int n)
    {
        float started = Time.unscaledTime;
        var from = EnterFrom;
        var to = SlotPosition(n);
        var apex = (from + to) * 0.5f + Vector3.up * 0.45f;
        var rotTo = SlotRotation(n);
        CutsceneAudio.PlayOneShot(CutsceneAssets.Current.packRustle, CutsceneAudio.Bus.Sfx, 0.7f);
        for (float e = 0f; e < PackSeconds; e += Time.unscaledDeltaTime)
        {
            float k = e / PackSeconds;
            float u = 1f - (1f - k) * (1f - k);                                  // fast out of the hand, easing into the bag
            t.localPosition = Bezier(from, apex, to, u);
            t.localRotation = Quaternion.Slerp(Quaternion.Euler(0f, 40f, -25f), rotTo, u);
            // stretch along the flight, squash on landing
            float stretch = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.8f)) * 0.22f;
            float land = k > 0.8f ? Mathf.Sin(Mathf.PI * (k - 0.8f) / 0.2f) * 0.25f : 0f;
            t.localScale = 0.8f * new Vector3(1f - stretch * 0.5f + land, 1f + stretch - land, 1f - stretch * 0.5f + land);
            yield return null;
        }
        t.localPosition = to;
        t.localRotation = rotTo;
        t.localScale = Vector3.one * 0.8f;
        CutsceneAudio.PlayOneShot(CutsceneAssets.Current.packZip, CutsceneAudio.Bus.Sfx, 0.35f);
        yield return Bulge(1.07f);
        LastAnimSeconds = Time.unscaledTime - started;
    }

    public void Unpack(RescueItemId id)
    {
        if (!inBag.TryGetValue(id, out var t)) return;
        inBag.Remove(id);
        order.Remove(id);
        SetMeter(order.Count, true);
        // the rest settle into their places
        for (int i = 0; i < order.Count; i++)
            if (inBag.TryGetValue(order[i], out var other) && other != null) { other.localPosition = SlotPosition(i); other.localRotation = SlotRotation(i); }
        StartCoroutine(UnpackRoutine(t));
    }

    IEnumerator UnpackRoutine(Transform t)
    {
        float started = Time.unscaledTime;
        var from = t.localPosition;
        var to = EnterFrom;
        var apex = (from + to) * 0.5f + Vector3.up * 0.45f;
        var rotFrom = t.localRotation;
        CutsceneAudio.PlayOneShot(CutsceneAssets.Current.packRustle, CutsceneAudio.Bus.Sfx, 0.6f);
        StartCoroutine(Bulge(0.95f));
        for (float e = 0f; e < UnpackSeconds; e += Time.unscaledDeltaTime)
        {
            float k = e / UnpackSeconds;
            float u = k * k;                                                    // out of the bag, then away
            t.localPosition = Bezier(from, apex, to, u);
            t.localRotation = Quaternion.Slerp(rotFrom, Quaternion.Euler(0f, 40f, -25f), u);
            float pop = k < 0.2f ? Mathf.Sin(Mathf.PI * k / 0.2f) * 0.2f : 0f;
            t.localScale = 0.8f * Mathf.Lerp(1f, 0.5f, u) * new Vector3(1f + pop, 1f - pop, 1f + pop);
            yield return null;
        }
        Destroy(t.gameObject);
        LastAnimSeconds = Time.unscaledTime - started;
    }

    IEnumerator Bulge(float peak)
    {
        if (bagBody == null) yield break;
        for (float e = 0f; e < BulgeSeconds; e += Time.unscaledDeltaTime)
        {
            float s = 1f + (peak - 1f) * Mathf.Sin(Mathf.PI * e / BulgeSeconds);
            bagBody.localScale = new Vector3(s, 2f - s, s);
            yield return null;
        }
        bagBody.localScale = Vector3.one;
    }

    /// <summary>The bag is full: it shakes, the window flashes red, a thump.</summary>
    public void Deny()
    {
        Denied++;
        StartCoroutine(DenyRoutine());
    }

    IEnumerator DenyRoutine()
    {
        CutsceneAudio.PlayOneShot(CutsceneAssets.Current.packDenied, CutsceneAudio.Bus.Sfx, 0.9f);
        bool calm = GameSettings.ReduceMotion;
        for (float e = 0f; e < DenySeconds; e += Time.unscaledDeltaTime)
        {
            float k = e / DenySeconds;
            if (!calm) bag.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(e * 55f) * 9f * (1f - k));
            var c = flash.color; c.a = 1f - k; flash.color = c;
            yield return null;
        }
        bag.localRotation = Quaternion.identity;
        var end = flash.color; end.a = 0f; flash.color = end;
    }

    void SetMeter(int count, bool animate)
    {
        PipsLit = Mathf.Clamp(count, 0, pips.Count);
        meterLabel.text = "PACK " + PipsLit + "/" + RescueItems.Slots;
        for (int i = 0; i < pips.Count; i++)
        {
            bool on = i < PipsLit;
            pips[i].color = on ? (PipsLit == pips.Count ? UiTheme.Orange : UiTheme.Green) : new Color(1, 1, 1, 0.25f);
            if (animate && on && i == PipsLit - 1 && i != shown) StartCoroutine(Pop(pips[i].rectTransform));
        }
        shown = PipsLit - 1;
    }

    IEnumerator Pop(RectTransform r)
    {
        if (GameSettings.ReduceMotion) yield break;
        for (float e = 0f; e < 0.22f; e += Time.unscaledDeltaTime)
        {
            float s = 1f + 0.6f * Mathf.Sin(Mathf.PI * e / 0.22f);
            r.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        r.localScale = Vector3.one;
    }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) =>
        Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

    // ------------------------------------------------------------------ READY
    /// <summary>Zip, buckle, the rescuer slings the bag, READY: about 2 s, then <paramref name="done"/>.</summary>
    public Coroutine Ready(Action done)
    {
        ReadyRunning = true;
        return StartCoroutine(ReadyRoutine(done));
    }

    /// <summary>Jumps to the end (SKIP).</summary>
    public void FinishReady()
    {
        if (!ReadyRunning) return;
        StopAllCoroutines();
        ReadyRunning = false;
        readyDone?.Invoke();
        readyDone = null;
    }

    Action readyDone;

    IEnumerator ReadyRoutine(Action done)
    {
        readyDone = done;
        var a = CutsceneAssets.Current;
        // zip: the flap closes over the packed things
        CutsceneAudio.PlayOneShot(a.packZip, CutsceneAudio.Bus.Sfx);
        var open = flap != null ? flap.localRotation : Quaternion.identity;
        for (float e = 0f; e < 0.38f; e += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / 0.38f);
            if (flap != null) flap.localRotation = Quaternion.Slerp(open, Quaternion.identity, k);
            foreach (var kv in inBag) if (kv.Value != null) kv.Value.localScale = Vector3.one * 0.8f * (1f - k);
            yield return null;
        }
        if (flap != null) flap.localRotation = Quaternion.identity;
        yield return Wait(0.06f);
        CutsceneAudio.PlayOneShot(a.packBuckle, CutsceneAudio.Bus.Sfx);
        yield return Wait(0.12f);

        // slung onto the rescuer's back
        var from = bag.localPosition;
        var to = rescuerSpot.localPosition + rescuerSpot.localRotation * new Vector3(0f, 1.0f, -0.2f);
        var apex = (from + to) * 0.5f + Vector3.up * 0.5f;
        var fromRot = bag.localRotation;
        var toRot = rescuerSpot.localRotation * Quaternion.Euler(0f, 180f, 0f);
        CutsceneAudio.PlayOneShot(a.whoosh, CutsceneAudio.Bus.Sfx, 0.6f);
        for (float e = 0f; e < 0.55f; e += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, e / 0.55f);
            bag.localPosition = Bezier(from, apex, to, k);
            bag.localRotation = Quaternion.Slerp(fromRot, toRot, k);
            if (rescuer != null) rescuer.LookTarget = cam.transform.position;
            yield return null;
        }
        bag.localPosition = to;
        bag.localRotation = toRot;
        yield return Wait(0.12f);

        // READY
        stamp.gameObject.SetActive(true);
        CutsceneAudio.PlayOneShot(a.readyStamp, CutsceneAudio.Bus.Sfx);
        bool calm = GameSettings.ReduceMotion;
        for (float e = 0f; e < 0.18f; e += Time.unscaledDeltaTime)
        {
            float s = calm ? 1f : Mathf.Lerp(2.2f, 1f, e / 0.18f);
            stamp.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        stamp.localScale = Vector3.one;
        yield return Wait(Mathf.Max(0f, ReadySeconds - 1.53f));
        ReadyRunning = false;
        readyDone = null;
        done?.Invoke();
    }

    static IEnumerator Wait(float s)
    {
        for (float e = 0f; e < s; e += Time.unscaledDeltaTime) yield return null;
    }

    static Sprite frameSprite;

    static Sprite FrameSprite()
    {
        if (frameSprite != null) return frameSprite;
        const int n = 32, t = 3, r = 8;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float outer = UiKit.RoundedAlpha(x + 0.5f, y + 0.5f, n, n, r, r, r, r);
            bool inside = x >= t && y >= t && x < n - t && y < n - t;
            float inner = inside ? UiKit.RoundedAlpha(x + 0.5f - t, y + 0.5f - t, n - 2 * t, n - 2 * t, r - t, r - t, r - t, r - t) : 0f;
            tex.SetPixel(x, y, new Color(1, 1, 1, outer * (1f - inner)));
        }
        tex.Apply();
        frameSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return frameSprite;
    }
}
