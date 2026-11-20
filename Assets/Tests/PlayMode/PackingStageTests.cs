using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static GameplayTestHelpers;

/// <summary>
/// The loadout's packing stage: packing and unpacking each animate in under 0.6 s, a fifth item is refused with a shake
/// and a red flash, the meter shows how full the pack is, and LAUNCH plays READY (~2 s, skippable) before the mission
/// loads. The cards and buttons are pressed as a player presses them.
/// </summary>
public class PackingStageTests
{
    LoadoutScreen screen;

    [UnitySetUp]
    public IEnumerator Open()
    {
        GameSettings.Reset();
        GameSettings.PlayIntro = false;                 // LAUNCH goes straight to the mission in these tests
        LoadoutState.Clear();
        Time.timeScale = 1f;
        Application.runInBackground = true;
        yield return SceneManager.LoadSceneAsync(SceneFlow.Loadout, LoadSceneMode.Single);
        yield return null;
        screen = Object.FindFirstObjectByType<LoadoutScreen>();
        Assert.IsNotNull(screen.Stage, "the packing stage is on the LOADOUT card");
    }

    [UnityTearDown]
    public IEnumerator Close()
    {
        IntroFlow.Disarm();
        CutsceneHold.Release();
        ScreenFade.Clear();
        GameSettings.Reset();
        LoadoutState.Clear();
        yield return WaitUntil(() => !SceneFlow.IsLoading, 30f, "any load to finish");
    }

    static void Press(string name)
    {
        var go = GameObject.Find(name);
        Assert.IsNotNull(go, name);
        go.GetComponent<Button>().onClick.Invoke();
    }

    [UnityTest]
    public IEnumerator Packing_and_unpacking_each_animate_in_under_six_tenths_of_a_second()
    {
        Assert.IsTrue(screen.Stage.HasModels, "the bag and item models are there");
        Press("Card_" + RescueItemId.Lifebuoy);
        yield return WaitUntil(() => screen.Stage.LastAnimSeconds > 0f, 2f, "the pack animation");
        Assert.Less(screen.Stage.LastAnimSeconds, 0.6f, "packed in under 0.6 s");
        Assert.AreEqual(1, screen.Stage.PipsLit, "one pip");

        yield return WaitSeconds(0.2f);
        float before = screen.Stage.LastAnimSeconds;
        Press("Card_" + RescueItemId.Lifebuoy);         // tapped again: out of the bag
        yield return WaitUntil(() => screen.Stage.LastAnimSeconds != before, 2f, "the unpack animation");
        Assert.Less(screen.Stage.LastAnimSeconds, 0.6f, "unpacked in under 0.6 s");
        Assert.AreEqual(0, screen.Stage.PipsLit);
    }

    [UnityTest]
    public IEnumerator A_fifth_item_is_refused_with_a_shake_and_a_red_flash()
    {
        foreach (var id in new[] { RescueItemId.Lifebuoy, RescueItemId.FirstAid, RescueItemId.Rope, RescueItemId.Radio })
            Press("Card_" + id);
        yield return WaitSeconds(0.7f);
        Assert.AreEqual(4, screen.Stage.PipsLit, "the meter is full");
        Press("Card_" + RescueItemId.Raincoat);
        yield return null;
        Assert.AreEqual(1, screen.Stage.Denied, "refused");
        Assert.Greater(screen.Stage.FlashAlpha, 0.5f, "red outline");
        Assert.AreEqual(4, screen.Stage.PipsLit, "still four");
        yield return WaitSeconds(PackingStage.DenySeconds + 0.1f);
        Assert.Less(screen.Stage.FlashAlpha, 0.05f, "the flash fades");
    }

    [UnityTest]
    public IEnumerator Launch_plays_ready_for_about_two_seconds_then_loads_the_mission()
    {
        Press("Button_RECOMMENDED_PACK");
        yield return null;
        float at = Time.unscaledTime;
        Press("Button_LAUNCH");
        yield return null;
        Assert.IsTrue(screen.Readying, "READY is playing");
        Assert.IsNull(Object.FindFirstObjectByType<CutsceneSkipButton>(), "READY is not a cutscene: no SKIP over the loadout");
        Assert.IsFalse(SceneFlow.IsLoading, "the mission waits for it");
        yield return WaitUntil(() => SceneFlow.IsLoading, 4f, "the mission to load");
        Assert.That(Time.unscaledTime - at, Is.InRange(1.7f, 2.6f), "about two seconds");
    }

    [UnityTest]
    public IEnumerator FinishReady_cuts_ready_short()
    {
        Press("Button_RECOMMENDED_PACK");
        yield return null;
        float launched = Time.unscaledTime;
        Press("Button_LAUNCH");
        yield return WaitSeconds(0.3f);
        Assert.IsFalse(SceneFlow.IsLoading);
        // There is no SKIP button on READY any more; FinishReady stays for code that needs to end it early.
        screen.Stage.FinishReady();
        Assert.IsTrue(SceneFlow.IsLoading, "the mission loads in the same frame as FinishReady");
        Assert.IsFalse(screen.Stage.ReadyRunning);
        Assert.Less(Time.unscaledTime - launched, PackingStage.ReadySeconds - 0.5f, "well before READY would have ended");
    }
}
