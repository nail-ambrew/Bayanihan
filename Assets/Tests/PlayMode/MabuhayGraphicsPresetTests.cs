using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// The GRAPHICS setting on the Mabuhay mission (GameplayQuality): Low shortens the view, thickens the fog, turns the
/// shadows off and thins the undergrowth, and going back to High gives every one of them back.
/// </summary>
public class MabuhayGraphicsPresetTests
{
    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
    }

    [UnityTearDown]
    public IEnumerator Put_everything_back()
    {
        GameSettings.Reset();
        GameSettings.Apply();
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    [UnityTest]
    public IEnumerator Low_draws_less_and_High_gives_it_all_back()
    {
        GameSettings.Quality = 2;
        yield return WaitSeconds(0.5f);
        var cam = Camera.main;
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        float highFar = cam.farClipPlane;
        float highLod = QualitySettings.lodBias;
        bool highShadows = data.renderShadows;
        Assert.Greater(highFar, GameplayQuality.Low.farClip, "High sees further than Low will");

        GameSettings.Quality = 0;
        yield return WaitSeconds(0.5f);
        Assert.LessOrEqual(cam.farClipPlane, GameplayQuality.Low.farClip + 0.01f, "a short view on Low");
        Assert.GreaterOrEqual(RenderSettings.fogDensity, GameplayQuality.Low.fogFloor - 1e-5f, "fog thick enough to hide where it ends");
        Assert.IsFalse(data.renderShadows, "no shadows on Low");
        Assert.AreEqual(GameplayQuality.Low.lodBias, QualitySettings.lodBias, 1e-4f, "houses drop out nearer");
        Assert.AreEqual(GameplayQuality.Low.foliageDensity, InstancedFoliage.DensityScale, 1e-4f, "less undergrowth");

        GameSettings.Quality = 2;
        yield return WaitSeconds(0.5f);
        Assert.AreEqual(highFar, cam.farClipPlane, 0.01f, "High's view distance is back");
        Assert.AreEqual(highLod, QualitySettings.lodBias, 1e-4f, "and its LOD bias");
        Assert.AreEqual(highShadows, data.renderShadows, "and its shadows");
        Assert.AreEqual(1f, InstancedFoliage.DensityScale, "and all the undergrowth");
        Assert.AreEqual(1f, InstancedFoliage.QualityScale);
    }
}
