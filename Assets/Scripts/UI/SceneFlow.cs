using TMPro;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The scenes of the game, and the loading screen between them: the flooded street, a tip at the bottom, the orange
/// bar and the percentage, as in the Figma "Loading" frames.
/// </summary>
public static class SceneFlow
{
    public const string Account = "Account";
    public const string MainMenu = "MainMenu";
    public const string Loadout = "Loadout";

    /// <summary>The loading screen stays up at least this long, so a fast load still shows its tip.</summary>
    public const float MinimumShow = 1.4f;

    static LoadingScreen current;

    public static bool IsLoading => current != null;

    /// <summary>
    /// Loads a scene behind the loading screen. If that scene is not in the build, the player is told so on screen
    /// (a button that silently does nothing looks broken) and stays where they are.
    /// </summary>
    public static void Load(string scene)
    {
        if (string.IsNullOrEmpty(scene) || !Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogWarning("SceneFlow: scene '" + scene + "' is not in Build Settings.");
            var safe = UiKit.NewCanvas("SceneProblem", 1001, out var canvas);
            UiKit.EnsureEventSystem();
            UiKit.Notice(safe, "Couldn't open that", "'" + scene + "' is missing from this build. Tell the team, and try another option.",
                         "OK", () => Object.Destroy(canvas.gameObject));
            return;
        }
        if (current != null) return;
        Time.timeScale = 1f;
        current = LoadingScreen.Create();
        current.StartCoroutine(Run(scene, current));
    }

    /// <summary>The scene that is open now, again (Restart).</summary>
    public static void Reload() => Load(SceneManager.GetActiveScene().name);

    /// <summary>The main menu if the build has one, otherwise the loadout screen (older builds).</summary>
    public static string Home => Application.CanStreamedLevelBeLoaded(MainMenu) ? MainMenu : Loadout;

    static IEnumerator Run(string scene, LoadingScreen screen)
    {
        float shownAt = Time.realtimeSinceStartup;
        var op = SceneManager.LoadSceneAsync(scene);
        op.allowSceneActivation = false;

        // Unity stops at 0.9 until activation is allowed.
        while (op.progress < 0.9f || Time.realtimeSinceStartup - shownAt < MinimumShow)
        {
            float load = Mathf.Clamp01(op.progress / 0.9f);
            float time = Mathf.Clamp01((Time.realtimeSinceStartup - shownAt) / MinimumShow);
            screen.SetProgress(Mathf.Min(load, time));
            yield return null;
        }
        screen.SetProgress(1f);
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;
        yield return null;
        Object.Destroy(screen.gameObject);
        current = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = null; }
}

/// <summary>The Figma loading frame. Made by <see cref="SceneFlow"/>, kept across the scene change, then destroyed.</summary>
public class LoadingScreen : MonoBehaviour
{
    Image fill;
    TMP_Text percent;

    public static LoadingScreen Create()
    {
        var safe = UiKit.NewCanvas("LoadingScreen", 1000, out var canvas);
        DontDestroyOnLoad(canvas.gameObject);
        var screen = canvas.gameObject.AddComponent<LoadingScreen>();
        screen.Build(canvas.transform, safe, LoadingTips.Next());
        return screen;
    }

    public void Build(Transform canvasRoot, RectTransform safe, string tip)
    {
        var bg = UiKit.Background(canvasRoot);
        bg.raycastTarget = true;        // nothing behind it can be tapped while loading
        UiKit.Logo(safe, UiKit.TopLeft, new Vector2(14, -10));
        UiKit.Version(safe);

        var tipText = UiKit.Label("Tip", safe, tip, UiTheme.BodySize + 1, UiTheme.White, TextAnchor.LowerCenter, heading: true);
        UiKit.Place(tipText.rectTransform, UiKit.Bottom, new Vector2(0, 62), new Vector2(620, 28));
        tipText.TextShadow(new Color(0, 0, 0, 0.6f));

        // a 4-high bar, rounded at half its height
        var track = UiKit.Panel("Bar", safe, UiTheme.Fade(UiTheme.White, 0.25f), UiKit.Fit(UiTheme.RadiusSmall, new Vector2(660, 4)));
        UiKit.Place(track.rectTransform, UiKit.Bottom, new Vector2(0, 50), new Vector2(660, 4));
        fill = UiKit.Panel("Fill", track.transform, UiTheme.Orange, UiKit.Fit(UiTheme.RadiusSmall, new Vector2(660, 4)));
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0, 1);
        fill.rectTransform.pivot = new Vector2(0, 0.5f);
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;

        percent = UiKit.Label("Percent", safe, "0%", UiTheme.BodySize, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
        UiKit.Place(percent.rectTransform, UiKit.Bottom, new Vector2(0, 30), new Vector2(80, 14));
        SetProgress(0f);
    }

    public void SetProgress(float t)
    {
        t = Mathf.Clamp01(t);
        if (fill != null) fill.rectTransform.anchorMax = new Vector2(t, 1);
        if (percent != null) percent.text = Mathf.RoundToInt(t * 100f) + "%";
    }
}

/// <summary>The lines shown while a scene loads. The first three are the Figma ones; the rest are true to how the game plays.</summary>
public static class LoadingTips
{
    public static readonly string[] All =
    {
        "Bayanihan: when a community lifts together, no one is left behind.",
        "Keep a go-bag ready: water, flashlight, radio, first aid, important documents.",
        "Saving one is good. Saving a family is better.",
        "Red tags first: children, elders and the hurt weaken fastest in the water.",
        "Slow down to come alongside. Nobody can be pulled aboard from a speeding boat.",
        "A live wire in the water stops your engine. Pack the Radio and call for the power to be cut.",
        "People on roofs and behind debris need a line. Throw the Floatie or the Rope.",
        "Nobody is safe until their name is written at the evacuation centre.",
        "Without the Rope packed, a swimmer has no tether back to the boat."
    };

    static int last = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { last = -1; }

    /// <summary>A tip, never the same one twice in a row.</summary>
    public static string Next()
    {
        int i = Random.Range(0, All.Length);
        if (All.Length > 1 && i == last) i = (i + 1) % All.Length;
        last = i;
        return All[i];
    }
}
