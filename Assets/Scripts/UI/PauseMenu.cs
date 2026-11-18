using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Figma "Pause" frames: the game stops behind a dark band on the left with the logo and
/// CONTINUE, RESTART, SETTINGS, ABORT. Restart and Abort ask first ("Are you sure to restart the operation?" NO / YES).
/// Opened by the pause button, Esc, or Android's Back. Made by <see cref="GameHud"/>.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    RescueManager manager;
    RectTransform safe;
    GameObject page, dialog;

    public bool IsOpen => gameObject.activeSelf;

    public static PauseMenu Create(Transform canvasRoot, RescueManager manager)
    {
        var root = UiKit.Rect("PauseMenu", canvasRoot);
        UiKit.Stretch(root);
        var menu = root.gameObject.AddComponent<PauseMenu>();
        menu.manager = manager;
        menu.Build(root);
        root.gameObject.SetActive(false);
        return menu;
    }

    void Build(RectTransform root)
    {
        // A dim over the whole game, darker down the left where the menu is (the Figma frame's band).
        var dim = UiKit.Image("Dim", root, new Color(0, 0, 0, 0.35f));
        UiKit.Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        var band = UiKit.Image("Band", root, Color.white, HorizontalFade());
        band.rectTransform.anchorMin = Vector2.zero;
        band.rectTransform.anchorMax = new Vector2(0.42f, 1f);
        band.rectTransform.offsetMin = band.rectTransform.offsetMax = Vector2.zero;

        safe = UiKit.Rect("Safe", root);
        UiKit.Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>().FitDesign = SafeArea.MenuFit;

        UiKit.Logo(safe, UiKit.TopLeft, new Vector2(70, -30), 140f);
        var col = UiKit.MenuColumn(safe, new Vector2(70, -102), 38f, 12f, 170f);
        UiKit.MenuItem(col, "CONTINUE", Close, 38f, 170f);
        UiKit.MenuItem(col, "RESTART", AskRestart, 38f, 170f);
        UiKit.MenuItem(col, "SETTINGS", OpenSettings, 38f, 170f);
        UiKit.MenuItem(col, "ABORT", AskAbort, 38f, 170f);
    }

    static Sprite fade;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => fade = null;

    /// <summary>Black on the left fading out to the right. Made once and shared.</summary>
    static Sprite HorizontalFade()
    {
        if (fade != null) return fade;
        var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, new Color(0, 0, 0, Mathf.Lerp(0.75f, 0f, x / 63f)));
        tex.Apply();
        fade = Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f));
        return fade;
    }

    public void Open()
    {
        if (manager != null && manager.GameOver) return;
        UiSound.Play(UiSoundKind.PauseOpen);
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        GamePause.Pause();
    }

    public void Close()
    {
        if (dialog != null) { Destroy(dialog); dialog = null; }
        ClosePage();
        gameObject.SetActive(false);
        GamePause.Resume();
    }

    /// <summary>Esc / Back: closes the innermost thing open, or pauses.</summary>
    public void Toggle()
    {
        if (!IsOpen) { Open(); return; }
        if (dialog != null) { Destroy(dialog); dialog = null; return; }
        if (page != null) { ClosePage(); return; }
        UiSound.Play(UiSoundKind.PauseClose);
        Close();
    }

    void OpenSettings()
    {
        if (page != null) Destroy(page);
        page = MenuPages.Settings(safe, ClosePage, null, 245f);
    }

    /// <summary>The settings page's back arrow: back to the pause menu, nothing lit.</summary>
    void ClosePage()
    {
        if (page != null) Destroy(page);
        page = null;
        UiMenuItem.ClearHighlight();
    }

    void AskRestart()
    {
        if (dialog != null) return;
        dialog = UiKit.Confirm(safe, "Are you sure to restart the operation?",
            ("NO", () => { dialog = null; UiMenuItem.ClearHighlight(); }),
            ("YES", () => { dialog = null; if (manager != null) manager.Restart(); else SceneFlow.Reload(); }));
    }

    void AskAbort()
    {
        if (dialog != null) return;
        dialog = UiKit.Confirm(safe, "Are you sure to abort the operation?",
            ("NO", () => { dialog = null; UiMenuItem.ClearHighlight(); }),
            ("YES", () => { dialog = null; if (manager != null) manager.ExitToMenu(); else SceneFlow.Load(SceneFlow.Home); }));
    }

    void OnDestroy()
    {
        // Leaving the scene with the game paused (restart, abort) must not leave time stopped.
        if (GamePause.IsPaused) GamePause.Clear();
    }
}
