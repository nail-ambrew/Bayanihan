using UnityEngine;

/// <summary>
/// The Figma "Main Menu" frame: the logo, and HOW TO PLAY, PLAY, SETTINGS, CREDITS, QUIT down the left over the
/// flooded street. PLAY opens the loadout; the other pages open on the board to the right; QUIT asks
/// Return to Login / Exit Game / CANCEL.
/// The whole UI is built in code: the only setup is this component on an empty object in the MainMenu scene.
/// </summary>
public class MainMenuScreen : MonoBehaviour
{
    RectTransform safe;
    GameObject page, dialog;

    void Start()
    {
        Time.timeScale = 1f;
        GamePause.Clear();
        UiKit.EnsureEventSystem();
        safe = UiKit.NewCanvas("MainMenuCanvas", 0, out var canvas);
        UiKit.Background(canvas.transform);

        UiKit.Logo(safe, UiKit.TopLeft, new Vector2(92, -28), 164f);
        UiKit.Version(safe);

        var menu = UiKit.MenuColumn(safe, new Vector2(92, -110), 38f, 15f, 174f);
        UiKit.MenuItem(menu, "HOW TO PLAY", () => Open(MenuPages.HowToPlay(safe)), 38f, 174f);
        UiKit.MenuItem(menu, "PLAY", Play, 38f, 174f);
        UiKit.MenuItem(menu, "SETTINGS", () => Open(MenuPages.Settings(safe, SignOut)), 38f, 174f);
        UiKit.MenuItem(menu, "CREDITS", () => Open(MenuPages.Credits(safe)), 38f, 174f);
        UiKit.MenuItem(menu, "QUIT", AskQuit, 38f, 174f);
    }

    void Update()
    {
        if (!InputHelper.BackPressed()) return;
        if (dialog != null) { Destroy(dialog); dialog = null; }
        else if (page != null) { Destroy(page); page = null; }
        else AskQuit();
    }

    void Open(GameObject next)
    {
        if (page != null) Destroy(page);
        page = next;
    }

    void Play()
    {
        if (page != null) { Destroy(page); page = null; }
        SceneFlow.Load(SceneFlow.Loadout);
    }

    void AskQuit()
    {
        if (dialog != null) return;
        dialog = UiKit.Choices(safe,
            ("Return to Login", ButtonStyle.Secondary, () => { dialog = null; SignOut(); }),
            ("Exit Game", ButtonStyle.Secondary, () => { dialog = null; Exit(); }),
            ("CANCEL", ButtonStyle.Primary, () => dialog = null));
    }

    async void SignOut()
    {
        await AccountService.SignOutAsync();
        SceneFlow.Load(SceneFlow.Account);
    }

    static void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
