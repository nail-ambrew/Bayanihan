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
        // The moving storm behind the menu (Scene_MenuCinematic), with a vignette so the words read; the still
        // flooded street if the cinematic is not in this build or never starts.
        if (MenuBackdrop.Load())
        {
            MenuParallax.Vignette(canvas.transform);
            StartCoroutine(FallBackIfNoCinematic(canvas.transform));
        }
        else UiKit.Background(canvas.transform);

        LogoFx.Add(UiKit.Logo(safe, UiKit.TopLeft, new Vector2(92, -28), 164f));
        UiKit.Version(safe);

        var menu = UiKit.MenuColumn(safe, new Vector2(92, -110), 38f, 15f, 174f);
        MenuParallax.Add(menu);
        UiKit.MenuItem(menu, "HOW TO PLAY", () => Open(MenuPages.HowToPlay(safe, ClosePage)), 38f, 174f);
        UiKit.MenuItem(menu, "PLAY", Play, 38f, 174f);
        UiKit.MenuItem(menu, "SETTINGS", () => Open(MenuPages.Settings(safe, ClosePage, SignOut)), 38f, 174f);
        UiKit.MenuItem(menu, "CREDITS", () => Open(MenuPages.Credits(safe, ClosePage)), 38f, 174f);
        UiKit.MenuItem(menu, "QUIT", AskQuit, 38f, 174f);
    }

    /// <summary>The still background after all, if the cinematic has not started within a few seconds.</summary>
    System.Collections.IEnumerator FallBackIfNoCinematic(Transform canvasRoot)
    {
        float until = Time.unscaledTime + 5f;
        while (Time.unscaledTime < until)
        {
            if (MenuCinematic.Current != null && MenuCinematic.Current.Started) yield break;
            yield return null;
        }
        Debug.LogWarning("MainMenuScreen: the menu cinematic did not start; showing the still background.");
        UiKit.Background(canvasRoot);
    }

    void Update()
    {
        if (!InputHelper.BackPressed()) return;
        if (dialog != null) { Destroy(dialog); dialog = null; }
        else if (page != null) ClosePage();
        else AskQuit();
    }

    void Open(GameObject next)
    {
        if (page != null) Destroy(page);
        page = next;
    }

    /// <summary>The page's back arrow, Esc and Android's Back: the page goes and its menu line is no longer lit.</summary>
    void ClosePage()
    {
        if (page != null) Destroy(page);
        page = null;
        UiMenuItem.ClearHighlight();
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

    bool signingOut;

    /// <summary>Settings' SIGN OUT and the quit dialog's Return to Login. A second tap while it runs does nothing.</summary>
    async void SignOut()
    {
        if (signingOut) return;
        signingOut = true;
        try
        {
            await AccountService.SignOutAsync();
            SceneFlow.Load(SceneFlow.Account);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MainMenu] Sign out failed: " + e.Message);
            if (this != null) dialog = UiKit.Notice(safe, "Couldn't sign out", "Check your internet connection and try again.", "OK", () => dialog = null);
        }
        finally { signingOut = false; }
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
