using System.Collections;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// A mission starts with the rescuer at the helm, seen through the boat's chase camera, not in the water in the
/// swimmer's own view. (Most other tests ask for the water start, to test swimming; this one checks the game's own.)
/// </summary>
public class StartsAboardTests
{
    bool? before;

    [UnitySetUp]
    public IEnumerator Prepare()
    {
        before = PlayerRescuer.StartAboardOverride;
        PlayerRescuer.StartAboardOverride = null;           // the game's own default
        GameSettings.Reset();
        LoadoutState.Clear();
        Time.timeScale = 1f;
        Application.runInBackground = true;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        PlayerRescuer.StartAboardOverride = before;
        IntroFlow.Disarm();
        CutsceneHold.Release();
        FloodLevel.Set(0f);
        ScreenFade.Clear();
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    static IEnumerator Load(string scene)
    {
        yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        yield return WaitSeconds(0.3f);
    }

    static void AssertAtTheHelm()
    {
        var player = Player();
        var boat = Boat();
        Assert.IsTrue(player.IsMounted, "the rescuer starts aboard");
        Assert.IsTrue(boat.IsPlayerAboard, "at the helm");
        Assert.AreEqual(boat.transform, player.transform.parent, "riding the boat");
        var live = Camera.main.GetComponent<CinemachineBrain>().ActiveVirtualCamera as CinemachineCamera;
        Assert.IsNotNull(live);
        Assert.IsNotNull(live.GetComponent<CinemachineOrbitalFollow>(), "the boat's chase camera is live, not the swimmer's view");
    }

    [UnityTest]
    public IEnumerator A_mabuhay_mission_starts_at_the_helm()
    {
        MapChoice.Select(MapChoice.MabuhayId);
        yield return Load(MapChoice.MabuhayScene);
        AssertAtTheHelm();
    }

    [UnityTest]
    public IEnumerator The_manila_mission_starts_at_the_helm_too()
    {
        MapChoice.Select(MapChoice.ManilaId);
        yield return Load(MapChoice.ManilaScene);
        AssertAtTheHelm();
    }

    [UnityTest]
    public IEnumerator After_the_intro_the_mission_is_at_the_helm()
    {
        MapChoice.Select(MapChoice.MabuhayId);
        IntroFlow.Arm();
        yield return SceneManager.LoadSceneAsync(MapChoice.MabuhayScene, LoadSceneMode.Single);
        yield return WaitUntil(() => IntroCutscene.Current != null && IntroCutscene.Current.Started, 20f, "the intro");
        yield return WaitSeconds(1f);
        IntroCutscene.Current.manager.Skip();
        yield return WaitUntil(() => !CutsceneHold.Active, 2f, "the hand-over");
        yield return WaitSeconds(0.5f);
        AssertAtTheHelm();
    }

    [UnityTest]
    public IEnumerator Stepping_off_still_puts_the_rescuer_in_the_water()
    {
        MapChoice.Select(MapChoice.MabuhayId);
        yield return Load(MapChoice.MabuhayScene);
        var player = Player();
        yield return WaitSeconds(player.boardingCooldown + 0.1f);
        StepOff(player);
        yield return null;
        Assert.IsFalse(player.IsMounted, "E steps off, as before");
    }
}
