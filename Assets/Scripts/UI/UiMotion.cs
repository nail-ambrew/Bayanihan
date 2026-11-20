using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The motion of the menus, in one component: a button grows a little under a hovering pointer and shrinks while it is
/// pressed, and a menu, page or dialog fades and springs in when it opens. It runs on unscaled time, so it still moves
/// while the game is paused (the pause menu sets the time scale to zero). With Reduce Motion on, nothing scales or slides:
/// the colour changes of the buttons stay, and everything appears at once.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UiMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    /// <summary>How far a button grows under the pointer.</summary>
    public const float HoverScale = 1.04f;
    /// <summary>How far a button shrinks while it is held down.</summary>
    public const float PressScale = 0.95f;
    /// <summary>The scale a menu opens from, before it springs to full size.</summary>
    public const float EnterFrom = 0.9f;
    /// <summary>How long an entrance takes, in seconds.</summary>
    public const float EnterDuration = 0.28f;
    /// <summary>How quickly the hover and press scale follows the pointer. Higher is snappier.</summary>
    const float Responsiveness = 18f;

    Vector3 baseScale = Vector3.one;
    Vector3 basePosition;
    Selectable selectable;

    bool hovered, pressed;
    float pointScale = 1f;

    // The entrance: progress 0..1 after its delay, eased out, the scale it springs from, and the slide it travels from.
    bool entering;
    float enterDelay, enterClock, enter = 1f, enterFrom = EnterFrom;
    Vector2 slide;
    bool hasSlide;

    /// <summary>The motion on <paramref name="go"/>, added when it is not there yet.</summary>
    public static UiMotion Of(GameObject go) => go.TryGetComponent(out UiMotion m) ? m : go.AddComponent<UiMotion>();

    void Awake()
    {
        baseScale = transform.localScale;
        basePosition = transform.localPosition;
        selectable = GetComponent<Selectable>();
    }

    void OnEnable()
    {
        // A menu that comes back (the pause menu, opened again) has its own entrance; the hover state starts clear.
        hovered = pressed = false;
    }

    void OnDisable()
    {
        // Put it back at rest, so a button that is hidden under the pointer does not stay grown.
        hovered = pressed = false;
        pointScale = 1f;
        if (transform != null) transform.localScale = baseScale;
    }

    /// <summary>
    /// Fades and springs this object in from <see cref="EnterFrom"/> of its size, after <paramref name="delay"/> seconds.
    /// <paramref name="slideFrom"/> moves it in from an offset, for objects no layout group places (a board, a dialog
    /// card): a child of a layout group keeps its place, so pass none there. <paramref name="from"/> = 1 fades without the
    /// spring, for a dim that covers the screen.
    /// </summary>
    public UiMotion Enter(float delay = 0f, Vector2? slideFrom = null, float from = EnterFrom)
    {
        // Looked up each time, not cached: a cached CanvasGroup can outlive its component (the pause menu's Safe object threw
        // MissingComponentException on its first Open in a mission), and then the fade has nothing to set.
        var group = Group();
        if (GameSettings.ReduceMotion)
        {
            entering = false;
            enter = 1f;
            group.alpha = 1f;
            return this;
        }
        entering = true;
        enterDelay = Mathf.Max(0f, delay);
        enterClock = 0f;
        enter = 0f;
        enterFrom = Mathf.Clamp01(from);
        hasSlide = slideFrom.HasValue;
        slide = slideFrom ?? Vector2.zero;
        group.alpha = 0f;
        return this;
    }

    /// <summary>This object's CanvasGroup: found, or added the first time.</summary>
    CanvasGroup Group()
    {
        var g = GetComponent<CanvasGroup>();
        return g != null ? g : gameObject.AddComponent<CanvasGroup>();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool motion = !GameSettings.ReduceMotion;

        if (entering)
        {
            enterClock += dt;
            float t = Mathf.Clamp01((enterClock - enterDelay) / EnterDuration);
            enter = Ease(t);
            if (t >= 1f) entering = false;
        }

        float target = 1f;
        if (motion && IsInteractable())
            target = pressed ? PressScale : hovered ? HoverScale : 1f;
        pointScale = Mathf.Lerp(pointScale, target, 1f - Mathf.Exp(-Responsiveness * dt));

        float spring = Mathf.Lerp(enterFrom, 1f, enter);
        transform.localScale = baseScale * (pointScale * spring);
        var g = GetComponent<CanvasGroup>();
        if (g != null) g.alpha = enter;
        if (hasSlide) transform.localPosition = basePosition + (Vector3)(slide * (1f - enter));
    }

    bool IsInteractable() => selectable == null || selectable.IsInteractable();

    public void OnPointerEnter(PointerEventData e)
    {
        hovered = true;
    }

    public void OnPointerExit(PointerEventData e)
    {
        hovered = false;
        pressed = false;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (IsInteractable()) pressed = true;
    }

    public void OnPointerUp(PointerEventData e)
    {
        pressed = false;
    }

    /// <summary>The ease that starts quick and settles, so a menu lands rather than stops.</summary>
    public static float Ease(float t)
    {
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }
}
