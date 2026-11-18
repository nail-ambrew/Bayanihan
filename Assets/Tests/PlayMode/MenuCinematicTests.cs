using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static GameplayTestHelpers;

/// <summary>
/// The main menu's moving storm: it loads under the menu and takes over the picture, loops every ~20 s, holds a still
/// frame with Reduce Motion, stays inside the triangle budget, and the logo pops in once then shimmers.
/// </summary>
public class MenuCinematicTests
{
    [UnitySetUp]
    public IEnumerator Prepare()
    {
        GameSettings.Reset();
        GamePause.Clear();
        Time.timeScale = 1f;
        Application.runInBackground = true;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        GameSettings.Reset();
        yield return null;
    }

    static IEnumerator OpenMenu()
    {
        Assert.IsTrue(MenuBackdrop.Available, "the menu cinematic is in the build");
        yield return SceneManager.LoadSceneAsync(SceneFlow.MainMenu, LoadSceneMode.Single);
        yield return WaitUntil(() => MenuCinematic.Current != null && MenuCinematic.Current.Started, 15f, "the menu cinematic");
        yield return null;
    }

    static PlayableDirector Director => MenuCinematic.Current.manager.Director;

    [UnityTest]
    public IEnumerator The_storm_plays_behind_the_menu_instead_of_the_still_picture()
    {
        yield return OpenMenu();
        Assert.AreEqual(SceneFlow.MainMenu, SceneManager.GetActiveScene().name, "the menu stays the active scene");
        Assert.AreEqual(MenuCinematic.Current.skybox.shader, RenderSettings.skybox.shader, "its sky is used (the storm works on a copy)");
        var menuScene = SceneManager.GetSceneByName(SceneFlow.MainMenu);
        Assert.IsFalse(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c => c.enabled && c.gameObject.scene == menuScene),
                       "the menu scene's own camera is off");
        Assert.IsFalse(Object.FindObjectsByType<Image>(FindObjectsSortMode.None).Any(i => i.name == "Background" && i.isActiveAndEnabled),
                       "no still background over it");
        Assert.IsNotNull(GameObject.Find("Vignette"), "a vignette keeps the menu readable");
        Assert.IsTrue(MenuCinematic.Current.manager.loop);
        Assert.IsNull(MenuCinematic.Current.manager.Overlay.Skip, "a background has no SKIP");
    }

    [UnityTest]
    public IEnumerator It_loops_about_every_twenty_seconds()
    {
        yield return OpenMenu();
        Assert.That(Director.duration, Is.InRange(18.0, 22.0));
        Assert.AreEqual(DirectorWrapMode.Loop, Director.extrapolationMode);
        Director.time = Director.duration - 0.15;
        yield return WaitSeconds(0.5f);
        Assert.Less(Director.time, 1.0, "it wrapped round to the start");
        Assert.AreEqual(PlayState.Playing, Director.state);
    }

    [UnityTest]
    public IEnumerator Reduce_motion_holds_a_still_frame_and_the_menu_still()
    {
        GameSettings.ReduceMotion = true;
        yield return OpenMenu();
        double held = Director.time;
        yield return WaitSeconds(1f);
        Assert.AreEqual(MenuCinematic.Current.manager.stillFrame, Director.time, 0.01, "on its still frame");
        Assert.AreEqual(held, Director.time, 0.001, "and not moving");
        var logo = Object.FindFirstObjectByType<LogoFx>();
        Assert.AreEqual(Vector3.one, logo.transform.localScale, "no pop");
        Assert.AreEqual(-1f, logo.GlintAt, "no shimmer");
        Assert.AreEqual(Vector2.zero, Object.FindFirstObjectByType<MenuParallax>().Offset, "no drift");
    }

    [UnityTest]
    public IEnumerator The_logo_pops_in_once_then_shimmers()
    {
        yield return SceneManager.LoadSceneAsync(SceneFlow.MainMenu, LoadSceneMode.Single);
        yield return null;
        var logo = Object.FindFirstObjectByType<LogoFx>();
        Assert.IsNotNull(logo);
        Assert.Less(logo.transform.localScale.x, 1.09f);
        yield return WaitSeconds(LogoFx.PopSeconds + 0.1f);
        Assert.IsTrue(logo.Popped);
        Assert.AreEqual(1f, logo.transform.localScale.x, 0.001f, "settled");
        bool shone = false;
        float until = Time.unscaledTime + logo.shimmerEvery + 1f;
        while (Time.unscaledTime < until && !shone) { shone |= logo.GlintAt >= 0f; yield return null; }
        Assert.IsTrue(shone, "a glint crossed it");
    }

    /// <summary>Counted from the index buffers, which works for meshes the game cannot read (static batches).</summary>
    static long Triangles(Mesh m)
    {
        long n = 0;
        for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
        return n;
    }

    [UnityTest]
    public IEnumerator It_stays_inside_the_triangle_budget()
    {
        yield return OpenMenu();
        var scene = SceneManager.GetSceneByName(MenuBackdrop.SceneName);
        long tris = 0;
        var batches = new System.Collections.Generic.HashSet<Mesh>();       // a static batch is one mesh shared by its pieces
        foreach (var root in scene.GetRootGameObjects())
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (!mr.enabled || mf == null || mf.sharedMesh == null) continue;
                if (mr.isPartOfStaticBatch) { if (batches.Add(mf.sharedMesh)) tris += Triangles(mf.sharedMesh); }
                else tris += Triangles(mf.sharedMesh);
            }
        foreach (var root in scene.GetRootGameObjects())
            foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (sk.sharedMesh != null) tris += Triangles(sk.sharedMesh);
        TestContext.WriteLine("menu cinematic: " + tris + " triangles");
        Assert.Less(tris, 150000, "under 150k triangles in the whole set");
    }
}
