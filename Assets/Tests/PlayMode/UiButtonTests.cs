using TMPro;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static GameplayTestHelpers;

/// <summary>
/// Every button on the menus does what its label says, the way a tap would (through the button's onClick): the main
/// menu's pages open and close with their back arrow, the quit dialog cancels, settings change, the loadout packs and
/// unpacks, the pause menu's pages and confirms close, and FULL REPORT swaps with STATS. Buttons that go to Unity
/// Authentication (log in, register, sign out) are left to a person: they need a real password.
/// </summary>
public class UiButtonTests
{
    int qualityBefore;

    [SetUp]
    public void Remember()
    {
        qualityBefore = GameSettings.Quality;
        LoadoutState.Clear();
    }

    [TearDown]
    public void Restore()
    {
        GameSettings.Quality = qualityBefore;
        GamePause.Clear();
        LoadoutState.Clear();
        Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------ helpers
    static Transform Find(string name, Transform under = null)
    {
        var roots = under != null ? new[] { under } : SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.transform).ToArray();
        foreach (var r in roots)
            foreach (var t in r.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        return null;
    }

    static void Tap(string name, Transform under = null)
    {
        var t = Find(name, under);
        Assert.IsNotNull(t, "there is a '" + name + "' to tap");
        var b = t.GetComponent<Button>();
        Assert.IsNotNull(b, "'" + name + "' is a button");
        Assert.IsTrue(b.IsInteractable(), "'" + name + "' can be tapped");
        b.onClick.Invoke();
    }

    static Transform Safe(string canvas) => GameObject.Find(canvas + "/SafeArea").transform;

    /// <summary>
    /// What a real mouse or finger does, not what onClick.Invoke does: for every button showing under <paramref name="root"/>,
    /// a click at its centre must reach that button and not something on top of it, or behind it showing through.
    /// </summary>
    /// <summary>A button in a scroll view that is out of sight is scrolled to the middle of the view, as a player would.</summary>
    static void ScrollIntoView(RectTransform button)
    {
        var scroll = button.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.viewport == null || scroll.content == null) return;
        Canvas.ForceUpdateCanvases();
        var view = new Vector3[4];
        var at = new Vector3[4];
        scroll.viewport.GetWorldCorners(view);
        button.GetWorldCorners(at);
        if (at[0].y >= view[0].y && at[1].y <= view[1].y) return;                 // already in sight
        float shift = (view[0].y + view[1].y) * 0.5f - (at[0].y + at[1].y) * 0.5f;
        scroll.content.position += new Vector3(0f, shift, 0f);
        Canvas.ForceUpdateCanvases();
    }

    static void AssertEveryButtonTakesAClickAtItsCentre(Transform root, string screen)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        Assert.IsNotNull(es, screen + " has an EventSystem");
        int checkedCount = 0;
        foreach (var b in root.GetComponentsInChildren<Button>())
        {
            if (!b.IsInteractable()) continue;
            var rt = (RectTransform)b.transform;
            ScrollIntoView(rt);                 // as a player would scroll down to it on a long page
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var centre = (corners[0] + corners[2]) * 0.5f;      // screen-space overlay: world units are screen pixels
            var data = new UnityEngine.EventSystems.PointerEventData(es) { position = centre };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(data, hits);
            Assert.IsNotEmpty(hits, screen + ": a click on '" + b.name + "' hits nothing");
            var handler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(hits[0].gameObject);
            Assert.AreSame(b.gameObject, handler, screen + ": a click on '" + b.name + "' reaches '" + (handler != null ? handler.name : hits[0].gameObject.name) + "' instead");
            checkedCount++;
        }
        Assert.Greater(checkedCount, 0, screen + " has buttons to click");
    }

    [UnityTest]
    public IEnumerator Every_button_on_every_menu_takes_a_real_click()
    {
        yield return SceneManager.LoadSceneAsync(SceneFlow.MainMenu, LoadSceneMode.Single);
        yield return null;
        var safe = Safe("MainMenuCanvas");
        AssertEveryButtonTakesAClickAtItsCentre(safe, "Main menu");
        foreach (var item in new[] { "Menu_HOW TO PLAY", "Menu_SETTINGS", "Menu_CREDITS" })
        {
            Tap(item, safe);
            yield return null;
            AssertEveryButtonTakesAClickAtItsCentre(safe, item.Substring(5) + " page");
        }
        Tap("Close", Find("Board", safe));
        Tap("Menu_QUIT", safe);
        yield return null;
        AssertEveryButtonTakesAClickAtItsCentre(Find("Choices", safe), "Quit dialog");

        yield return SceneManager.LoadSceneAsync(SceneFlow.Loadout, LoadSceneMode.Single);
        yield return null;
        AssertEveryButtonTakesAClickAtItsCentre(Safe("LoadoutCanvas"), "Loadout");

        yield return LoadGameplay();
        yield return null;
        var menu = Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
        menu.Open();
        yield return null;
        AssertEveryButtonTakesAClickAtItsCentre(menu.transform, "Pause menu");
        Tap("Menu_SETTINGS", menu.transform);
        yield return null;
        AssertEveryButtonTakesAClickAtItsCentre(menu.transform, "Pause settings");
        Tap("Close", Find("Board", menu.transform));
        Tap("Menu_ABORT", menu.transform);
        yield return null;
        AssertEveryButtonTakesAClickAtItsCentre(Find("Dialog", menu.transform), "Abort question");
        Tap("Button_NO", Find("Dialog", menu.transform));
        menu.Close();

        SetTimeLeft(0.05f);
        yield return WaitUntil(() => RescueManager.Instance.ReportVisible, 15f, "the end screen");
        yield return null;
        AssertEveryButtonTakesAClickAtItsCentre(GameObject.Find("GameHud/MissionComplete/Safe").transform, "Mission Complete");
    }

    // ------------------------------------------------------------------ main menu
    [UnityTest]
    public IEnumerator Every_main_menu_page_opens_and_its_back_arrow_closes_it()
    {
        yield return SceneManager.LoadSceneAsync(SceneFlow.MainMenu, LoadSceneMode.Single);
        yield return null;
        var safe = Safe("MainMenuCanvas");

        foreach (var (item, title) in new[] { ("Menu_HOW TO PLAY", "HOW TO PLAY"), ("Menu_SETTINGS", "SETTINGS"), ("Menu_CREDITS", "CREDITS") })
        {
            Tap(item, safe);
            yield return null;
            var board = Find("Board", safe);
            Assert.IsNotNull(board, item + " opens a page");
            Assert.AreEqual(title, board.Find("Title").GetComponent<TMP_Text>().text);
            Assert.IsNotNull(board.Find("Close"), title + " has a back arrow, so it can be closed without a Back key");

            Tap("Close", board);
            yield return null;
            Assert.IsNull(Find("Board", safe), "the back arrow closes " + title);
            Assert.IsNull(UiMenuItem.Highlighted, "and its menu line is no longer lit");
        }
    }

    [UnityTest]
    public IEnumerator Opening_a_second_page_replaces_the_first()
    {
        yield return SceneManager.LoadSceneAsync(SceneFlow.MainMenu, LoadSceneMode.Single);
        yield return null;
        var safe = Safe("MainMenuCanvas");
        Tap("Menu_HOW TO PLAY", safe);
        yield return null;
        Tap("Menu_CREDITS", safe);
        yield return null;
        var boards = safe.GetComponentsInChildren<Transform>().Where(t => t.name == "Board").ToList();
        Assert.AreEqual(1, boards.Count, "one page at a time");
        Assert.AreEqual("CREDITS", boards[0].Find("Title").GetComponent<TMP_Text>().text);
    }

    [UnityTest]
    public IEnumerator The_settings_choices_change_the_settings()
    {
        yield return SceneManager.LoadSceneAsync(SceneFlow.MainMenu, LoadSceneMode.Single);
        yield return null;
        var safe = Safe("MainMenuCanvas");
        Tap("Menu_SETTINGS", safe);
        yield return null;
        var graphics = Find("Choice_GRAPHICS", safe);
        Tap("Button_LOW", graphics);
        Assert.AreEqual(0, GameSettings.Quality, "LOW");
        Tap("Button_HIGH", graphics);
        Assert.AreEqual(2, GameSettings.Quality, "HIGH");

        int colourBefore = GameSettings.ColorGrade;
        var colour = Find("Choice_COLOR", safe);
        Assert.IsNotNull(colour, "Settings offers the colour grade");
        Tap("Button_VIBRANT", colour);
        Assert.AreEqual(1, GameSettings.ColorGrade, "VIBRANT");
        Tap("Button_STORMY", colour);
        Assert.AreEqual(2, GameSettings.ColorGrade, "STORMY");
        GameSettings.ColorGrade = colourBefore;

        // The music and the sound effects have their own volume, under the master one.
        float musicBefore = GameSettings.MusicVolume, sfxBefore = GameSettings.SfxVolume;
        try
        {
            var music = Find("Slider_MUSIC", safe)?.GetComponentInChildren<Slider>();
            var sfx = Find("Slider_SOUND EFFECTS", safe)?.GetComponentInChildren<Slider>();
            Assert.IsNotNull(music, "Settings has a Music volume");
            Assert.IsNotNull(sfx, "Settings has a Sound Effects volume");
            music.value = 0.3f;
            sfx.value = 0.6f;
            Assert.AreEqual(0.3f, GameSettings.MusicVolume, 1e-4f, "the Music slider sets the music volume");
            Assert.AreEqual(0.6f, GameSettings.SfxVolume, 1e-4f, "the Sound Effects slider sets the sound effects volume");
        }
        finally
        {
            GameSettings.MusicVolume = musicBefore;
            GameSettings.SfxVolume = sfxBefore;
        }
    }

    [UnityTest]
    public IEnumerator Quit_asks_first_and_cancel_keeps_the_player_in_the_menu()
    {
        yield return SceneManager.LoadSceneAsync(SceneFlow.MainMenu, LoadSceneMode.Single);
        yield return null;
        var safe = Safe("MainMenuCanvas");
        Tap("Menu_QUIT", safe);
        yield return null;
        var dialog = Find("Choices", safe);
        Assert.IsNotNull(dialog, "Quit asks Return to Login / Exit Game / CANCEL");
        foreach (var answer in new[] { "Button_Return to Login", "Button_Exit Game", "Button_CANCEL" })
            Assert.IsNotNull(Find(answer, dialog), answer);
        Tap("Button_CANCEL", dialog);
        yield return null;
        Assert.IsNull(Find("Choices", safe), "CANCEL closes the dialog");
        Assert.AreEqual(SceneFlow.MainMenu, SceneManager.GetActiveScene().name, "and the player is still in the menu");
    }

    // ------------------------------------------------------------------ loadout
    [UnityTest]
    public IEnumerator The_loadout_cards_slots_and_pack_buttons_all_work()
    {
        yield return SceneManager.LoadSceneAsync(SceneFlow.Loadout, LoadSceneMode.Single);
        yield return null;
        var safe = Safe("LoadoutCanvas");
        bool Filled(int slot) => Find("Slot" + slot, safe).GetComponent<Image>().color == UiTheme.Green;

        Tap("Card_" + RescueItemId.Rope, safe);
        yield return null;
        Assert.IsTrue(Filled(1), "tapping a card packs it in the first slot");
        Assert.AreEqual(UiTheme.Green, Find("Card_" + RescueItemId.Rope, safe).GetComponent<Image>().color, "and the card shows it is packed");

        Tap("Slot1", safe);
        yield return null;
        Assert.IsFalse(Filled(1), "tapping a filled slot unpacks it");

        Tap("Button_RECOMMENDED_PACK", safe);
        yield return null;
        for (int i = 1; i <= RescueItems.Slots; i++) Assert.IsTrue(Filled(i), "RECOMMENDED PACK fills slot " + i);

        Tap("Button_CLEAR", safe);
        yield return null;
        for (int i = 1; i <= RescueItems.Slots; i++) Assert.IsFalse(Filled(i), "CLEAR empties slot " + i);

        Assert.IsNotNull(Find("Back", safe), "there is a back arrow to the main menu");
        Assert.IsNotNull(Find("Button_LAUNCH", safe), "and LAUNCH");
    }

    // ------------------------------------------------------------------ in the mission
    [UnityTest]
    public IEnumerator The_pause_menu_pages_and_confirms_close_and_keep_the_game_paused()
    {
        yield return LoadGameplay();
        yield return null;
        var menu = Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
        menu.Open();
        yield return null;
        var root = menu.transform;

        Tap("Menu_SETTINGS", root);
        yield return null;
        Assert.IsNotNull(Find("Board", root), "SETTINGS opens the settings page");
        Tap("Close", Find("Board", root));
        yield return null;
        Assert.IsNull(Find("Board", root), "its back arrow closes it");
        Assert.IsTrue(GamePause.IsPaused, "the game stays paused");

        foreach (var item in new[] { "Menu_RESTART", "Menu_ABORT" })
        {
            Tap(item, root);
            yield return null;
            var dialog = Find("Dialog", root);
            Assert.IsNotNull(dialog, item + " asks first");
            Tap("Button_NO", dialog);
            yield return null;
            Assert.IsNull(Find("Dialog", root), "NO closes the question");
            Assert.IsTrue(GamePause.IsPaused, "and the mission carries on, paused");
        }

        Tap("Menu_CONTINUE", root);
        yield return null;
        Assert.IsFalse(GamePause.IsPaused, "CONTINUE resumes");
    }

    [UnityTest]
    public IEnumerator After_ABORT_the_main_menu_buttons_can_be_clicked()
    {
        // Abort from the pause menu leaves the mission for the main menu. Everything there must be reachable at once:
        // no question, pause or overlay left over from the mission may sit on the buttons.
        yield return MabuhayTestHelpers.LoadMabuhay();
        yield return null;
        var menu = Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
        menu.Open();
        yield return null;
        Tap("Menu_ABORT", menu.transform);
        yield return null;
        Tap("Button_YES", Find("Dialog", menu.transform));

        yield return WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.MainMenu && !SceneFlow.IsLoading, 30f, "the main menu");
        yield return null;
        yield return null;

        Assert.IsFalse(GamePause.IsPaused, "the game is not left paused");
        Assert.AreEqual(1f, Time.timeScale, 1e-4f, "time runs");
        Assert.IsFalse(AudioListener.pause, "the sound is not left paused");
        Assert.IsFalse(InputHelper.GameplayLocked, "the menu is not locked");
        Assert.IsFalse(SceneFlow.IsLoading, "the loading screen has gone");
        AssertEveryButtonTakesAClickAtItsCentre(Safe("MainMenuCanvas"), "Main menu after abort");
    }

    [UnityTest]
    public IEnumerator Full_report_and_stats_swap_back_and_forth()
    {
        yield return LoadGameplay();
        yield return null;
        SetTimeLeft(0.05f);
        yield return WaitUntil(() => RescueManager.Instance.ReportVisible, 15f, "the end screen");
        yield return null;
        var root = GameObject.Find("GameHud/MissionComplete/Safe").transform;
        TMP_Text ReportLabel() => Find("Menu_FULL REPORT", root).Find("Label").GetComponent<TMP_Text>();

        Tap("Menu_FULL REPORT", root);
        yield return null;
        Assert.IsNotNull(Find("Report", root), "FULL REPORT shows the report");
        Assert.IsFalse(Find("Board", root).gameObject.activeSelf, "in place of the stats");
        Assert.AreEqual("STATS", ReportLabel().text, "and the line now says how to get back");

        Tap("Close", Find("Report", root));
        yield return null;
        Assert.IsNull(Find("Report", root), "the report's back arrow closes it");
        Assert.IsTrue(Find("Board", root).gameObject.activeSelf, "the stats are back");
        Assert.AreEqual("FULL REPORT", ReportLabel().text);
    }
}
