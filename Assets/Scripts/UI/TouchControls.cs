using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The on-screen controls for phones and tablets: a joystick to steer or swim, a big RESCUE button and the item buttons
/// in the other corner, and the rest of the screen to drag (look) and pinch (zoom). Buttons only show while they can do
/// something: THROW when the crew is aboard, LIGHT and RADIO when packed, POWER CUT when a live wire is in reach.
/// Everything they do goes through <see cref="TouchInput"/>, so the game reads it exactly as it reads the keyboard.
/// Made by <see cref="GameHud"/>; shown when <see cref="GameSettings.ShowTouchControls"/> says so.
/// </summary>
public class TouchControls : MonoBehaviour
{
    static readonly List<TouchControls> all = new List<TouchControls>();

    CanvasGroup group;
    RectTransform root, joystickRoot, buttonsRoot;
    TouchButton rescue, throwButton, light, radio, powerCut;
    BoatRescue boatRescue;
    BoatTools tools;
    PlayerRescuer player;

    public static void RefreshAll()
    {
        foreach (var t in all) if (t != null) t.Layout();
    }

    public static TouchControls Create(RectTransform parent)
    {
        var rt = UiKit.Rect("TouchControls", parent);
        UiKit.Stretch(rt);
        var c = rt.gameObject.AddComponent<TouchControls>();
        c.Build(rt);
        return c;
    }

    void OnEnable() => all.Add(this);
    void OnDisable()
    {
        all.Remove(this);
        TouchInput.ReleaseAll();
    }

    void Build(RectTransform rt)
    {
        root = rt;
        group = rt.gameObject.AddComponent<CanvasGroup>();

        // Behind everything: drag to look, pinch to zoom.
        var look = UiKit.Image("LookArea", rt, new Color(0, 0, 0, 0));
        UiKit.Stretch(look.rectTransform);
        look.raycastTarget = true;
        look.gameObject.AddComponent<LookArea>();

        joystickRoot = UiKit.Rect("Joystick", rt);
        joystickRoot.sizeDelta = new Vector2(110, 110);
        joystickRoot.gameObject.AddComponent<Joystick>().Build();

        buttonsRoot = UiKit.Rect("Buttons", rt);
        buttonsRoot.sizeDelta = new Vector2(200, 170);
        rescue = TouchButton.Create(buttonsRoot, "RESCUE", TouchAction.Interact, 62f, UiTheme.Orange);
        throwButton = TouchButton.Create(buttonsRoot, "THROW", TouchAction.Throw, 44f, UiTheme.OrangeDeep);
        light = TouchButton.Create(buttonsRoot, "LIGHT", TouchAction.Flashlight, 36f, UiTheme.BrownDark);
        radio = TouchButton.Create(buttonsRoot, "RADIO", TouchAction.Radio, 36f, UiTheme.BrownDark);
        powerCut = TouchButton.Create(buttonsRoot, "POWER\nCUT", TouchAction.PowerCut, 40f, UiTheme.Error);

        Layout();
    }

    /// <summary>Puts the joystick and the buttons in the corners the settings ask for.</summary>
    void Layout()
    {
        if (root == null) return;
        bool joyLeft = GameSettings.Side == ControlSide.JoystickLeft;
        Vector2 joyCorner = joyLeft ? UiKit.BottomLeft : UiKit.BottomRight;
        Vector2 btnCorner = joyLeft ? UiKit.BottomRight : UiKit.BottomLeft;
        float s = joyLeft ? 1f : -1f;      // mirror x when swapped

        UiKit.Place(joystickRoot, joyCorner, new Vector2(28 * s, 22), new Vector2(110, 110));
        UiKit.Place(buttonsRoot, btnCorner, new Vector2(-18 * s, 18), new Vector2(200, 170));

        // Inside the button cluster, positions measured from its outer corner.
        Vector2 a = btnCorner;
        Place(rescue, a, new Vector2(-8 * s, 8));
        Place(throwButton, a, new Vector2(-80 * s, 4));
        Place(powerCut, a, new Vector2(-132 * s, 6));
        Place(light, a, new Vector2(-6 * s, 82));
        Place(radio, a, new Vector2(-52 * s, 96));
    }

    static void Place(TouchButton b, Vector2 anchor, Vector2 pos) =>
        UiKit.Place((RectTransform)b.transform, anchor, pos, ((RectTransform)b.transform).sizeDelta);

    void Update()
    {
        bool show = GameSettings.ShowTouchControls && !InputHelper.GameplayLocked;
        group.alpha = show ? 1f : 0f;
        group.blocksRaycasts = show;
        group.interactable = show;
        if (!show) { TouchInput.ReleaseAll(); return; }

        if (player == null) player = FindFirstObjectByType<PlayerRescuer>();
        if (boatRescue == null) boatRescue = FindFirstObjectByType<BoatRescue>();
        if (tools == null) tools = FindFirstObjectByType<BoatTools>();

        bool aboard = player == null || player.IsMounted;
        throwButton.Show(boatRescue != null && aboard);
        light.Show(LoadoutState.Has(RescueItemId.Flashlight) && aboard);
        radio.Show(LoadoutState.Has(RescueItemId.Radio) && aboard);
        powerCut.Show(tools != null && tools.ZoneToCut != null && aboard);
        light.SetOn(tools != null && tools.FlashlightOn);
        radio.SetCooldown(tools != null ? tools.RadioCooldown01 : 0f);
    }
}

/// <summary>One round on-screen button. Down = press, up or slide off = release.</summary>
public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    TouchAction action;
    Image disc, cooldown;
    Text label;
    Color colour;
    bool down, on;
    CanvasGroup group;

    public static TouchButton Create(Transform parent, string text, TouchAction action, float size, Color colour)
    {
        var rt = UiKit.Rect("Touch_" + action, parent);
        rt.sizeDelta = new Vector2(size, size);
        var b = rt.gameObject.AddComponent<TouchButton>();
        b.action = action;
        b.colour = colour;
        b.group = rt.gameObject.AddComponent<CanvasGroup>();

        var ring = UiKit.Image("Ring", rt, UiTheme.Fade(UiTheme.White, 0.85f), UiKit.Circle());
        UiKit.Stretch(ring.rectTransform);
        b.disc = UiKit.Image("Disc", rt, colour, UiKit.Circle());
        UiKit.Inset(b.disc.rectTransform, 2f);
        b.disc.raycastTarget = true;
        b.cooldown = UiKit.Image("Cooldown", rt, new Color(0, 0, 0, 0.45f), UiKit.Circle());
        UiKit.Inset(b.cooldown.rectTransform, 2f);
        b.cooldown.type = Image.Type.Filled;
        b.cooldown.fillMethod = Image.FillMethod.Radial360;
        b.cooldown.fillOrigin = (int)Image.Origin360.Top;
        b.cooldown.fillAmount = 0f;
        b.label = UiKit.Label("Label", rt, text, Mathf.RoundToInt(size * 0.2f), UiTheme.White, TextAnchor.MiddleCenter, heading: true);
        UiKit.Stretch(b.label.rectTransform);
        b.label.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.4f);
        return b;
    }

    public void Show(bool visible)
    {
        group.alpha = visible ? 0.92f : 0f;
        group.blocksRaycasts = visible;
        if (!visible && down) { down = false; TouchInput.Release(action); Paint(); }
    }

    public void SetOn(bool value)
    {
        if (on == value) return;
        on = value;
        Paint();
    }

    public void SetCooldown(float t01) => cooldown.fillAmount = Mathf.Clamp01(t01);

    public void OnPointerDown(PointerEventData e)
    {
        down = true;
        TouchInput.Press(action);
        Paint();
    }

    public void OnPointerUp(PointerEventData e) => Let();
    public void OnPointerExit(PointerEventData e) { if (down) Let(); }

    void Let()
    {
        down = false;
        TouchInput.Release(action);
        Paint();
    }

    void Paint()
    {
        disc.color = down ? Color.Lerp(colour, Color.black, 0.25f) : on ? UiTheme.Notice : colour;
        label.color = on && !down ? UiTheme.Ink : UiTheme.White;
        transform.localScale = down ? Vector3.one * 0.94f : Vector3.one;
    }

    void OnDisable()
    {
        if (down) TouchInput.Release(action);
        down = false;
    }
}

/// <summary>A fixed joystick: drag the knob inside the ring; the farther out, the harder.</summary>
public class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    RectTransform knob, area;
    const float Radius = 40f, DeadZone = 0.12f;

    public void Build()
    {
        area = (RectTransform)transform;
        var hit = UiKit.Image("Hit", area, new Color(0, 0, 0, 0));
        UiKit.Inset(hit.rectTransform, -16f);
        hit.raycastTarget = true;
        var ring = UiKit.Image("Ring", area, UiTheme.Fade(UiTheme.White, 0.55f), UiKit.Ring(0.06f));
        UiKit.Stretch(ring.rectTransform);
        var plate = UiKit.Image("Plate", area, UiTheme.Fade(UiTheme.BrownDark, 0.35f), UiKit.Circle());
        UiKit.Inset(plate.rectTransform, 4f);
        var k = UiKit.Image("Knob", area, UiTheme.Orange, UiKit.Circle());
        knob = k.rectTransform;
        UiKit.Place(knob, UiKit.Center, Vector2.zero, new Vector2(44, 44));
        k.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.35f);
    }

    public void OnPointerDown(PointerEventData e) => OnDrag(e);

    public void OnDrag(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out var local)) return;
        Vector2 v = Vector2.ClampMagnitude(local / Radius, 1f);
        knob.anchoredPosition = v * Radius;
        float m = v.magnitude;
        TouchInput.Stick = m < DeadZone ? Vector2.zero : v.normalized * ((m - DeadZone) / (1f - DeadZone));
    }

    public void OnPointerUp(PointerEventData e)
    {
        knob.anchoredPosition = Vector2.zero;
        TouchInput.Stick = Vector2.zero;
    }

    void OnDisable()
    {
        TouchInput.Stick = Vector2.zero;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }
}

/// <summary>The empty part of the screen: one finger drags the camera round, two fingers pinch to zoom.</summary>
public class LookArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();
    float pinchDistance = -1f;
    /// <summary>Screen pixels of pinch for one zoom step (one notch of a mouse wheel).</summary>
    const float PixelsPerZoomStep = 90f;

    public void OnPointerDown(PointerEventData e)
    {
        fingers[e.pointerId] = e.position;
        pinchDistance = -1f;
    }

    public void OnPointerUp(PointerEventData e)
    {
        fingers.Remove(e.pointerId);
        pinchDistance = -1f;
    }

    public void OnDrag(PointerEventData e)
    {
        fingers[e.pointerId] = e.position;
        if (fingers.Count >= 2)
        {
            var it = fingers.Values.GetEnumerator();
            it.MoveNext(); var a = it.Current;
            it.MoveNext(); var b = it.Current;
            float d = Vector2.Distance(a, b);
            if (pinchDistance > 0f) TouchInput.AddZoom((d - pinchDistance) / PixelsPerZoomStep);
            pinchDistance = d;
            return;
        }
        // Drag deltas arrive in screen pixels, like the mouse, so the look speed carries over.
        TouchInput.AddLook(e.delta * 0.5f);
    }

    void OnDisable()
    {
        fingers.Clear();
        pinchDistance = -1f;
    }
}
