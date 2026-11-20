using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

/// <summary>
/// The intro, played inside the real Mabuhay map: Scene_IntroCutscene is loaded on top of the held mission
/// (<see cref="IntroFlow"/>) and this ties its Timeline to the map, which a scene file cannot do across scenes:
/// <list type="bullet">
/// <item>the Cinemachine track to the map's camera brain, the storm track to the map's TyphoonWeather;</item>
/// <item>the boat and residents are held still where the mission starts while the flood rises under them;</item>
/// <item>the quality preset and Reduce Motion are applied (rain, debris, depth of field, film effects, shake);</item>
/// <item>the last shot ends exactly on the gameplay camera, and the storm, rain and flood level end where play begins.</item>
/// </list>
/// When it ends (played through, skipped, or the watchdog), everything it changed is put back, the mission is
/// released (the HUD fades in) and the intro scene unloads.
/// </summary>
public class IntroCutscene : MonoBehaviour
{
    public static IntroCutscene Current { get; private set; }

    public CutsceneManager manager;
    public CutsceneCueReceiver cues;
    [Tooltip("Every shot camera (they are switched off when the intro ends).")]
    public CinemachineCamera[] shots;
    [Tooltip("The last shot: it ends on the gameplay camera's own pose.")]
    public ShotMove settle;
    public IntroDebrisPool debris;
    public SirenLight siren;
    public AudioSource sirenSound;
    [Tooltip("The cutscene's own look: depth of field, film grain, chromatic aberration, a stronger vignette.")]
    public Volume look;
    [Tooltip("The story's cast: one child object per place a character stands in the story (HomeAll, RoofBen, WaterJoy, Hall...), " +
             "each holding CharacterSpots. Cast_Show_<name> / Cast_Hide_<name> cues show and hide them, so the actors can be in " +
             "different places from one shot to the next without anybody walking across the map.")]
    public Transform castRoot;
    [Tooltip("The photo that slips out of Lola Nena's hands (Cue_PhotoDrop).")]
    public IntroPropMotion photo;
    [Tooltip("The neighbour's shanty that gives way (Cue_ShantyFall).")]
    public IntroPropMotion shanty;
    [Tooltip("The tricycle that tips over in the flood (Cue_TricycleTip).")]
    public IntroPropMotion tricycle;
    [Tooltip("The spray when the boat is launched (Cue_Splash).")]
    public ParticleSystem splash;
    [Tooltip("Effects scaled by the quality preset.")]
    public ParticleSystem[] effects;
    [Tooltip("The intro's look fades out over this many seconds before the end, so the hand-over shows no change.")]
    public float lookFadeSeconds = 2f;

    public bool Started { get; private set; }
    public bool Ended { get; private set; }
    /// <summary>The last beat reached (1..5), from the Beat_N signals.</summary>
    public int Beat { get; private set; }
    public readonly List<int> BeatsSeen = new List<int>();

    PlayableDirector director;
    CinemachineBrain brain;
    CinemachineCamera gameplayCamera;
    readonly List<(CinemachineCamera cam, CinemachineVirtualCameraBase.StandbyUpdateMode was)> mapCameras =
        new List<(CinemachineCamera, CinemachineVirtualCameraBase.StandbyUpdateMode)>();
    float camerasFoundAt = -1f;
    TyphoonWeather weather;
    float stormBefore, rainScaleBefore;
    bool lightningBefore;
    readonly List<(Rigidbody body, bool kinematic, Vector3 pos, Quaternion rot)> held = new List<(Rigidbody, bool, Vector3, Quaternion)>();
    SimpleBoat boat;
    Vector3 boatPos;
    Quaternion boatRot;
    CinematicQuality quality;
    DepthOfField dof;
    ChromaticAberration aberration;
    FilmGrain grain;
    VolumeProfile lookProfile;

    void Awake()
    {
        Current = this;
        director = manager != null ? manager.Director : GetComponent<PlayableDirector>();
    }

    IEnumerator Start()
    {
        // Opened on its own in the Editor (no mission under it): just play it.
        if (!CutsceneHold.Active) CutsceneHold.Begin();
        yield return null;                      // the map's Start()s have run: the boat, the weather, the mission
        yield return null;                      // and the rescuer has taken the helm (PlayerRescuer boards a frame after Start)

        director = GetComponent<PlayableDirector>();     // here, not Awake: the manager may wake after this
        FindTheMap();
        Bind();
        Hold();
        ApplySettings();
        if (debris != null) debris.Prepare(quality.debrisCap);
        PrepareCast();
        if (cues != null) cues.Cue += OnCue;

        manager.Finished += OnFinished;
        manager.Play();
        Started = true;
        ScreenFade.In(0.6f);
    }

    void FindTheMap()
    {
        var main = Camera.main;
        brain = main != null ? main.GetComponent<CinemachineBrain>() : null;
        FindMapCameras();
        weather = FindFirstObjectByType<TyphoonWeather>();
        boat = FindFirstObjectByType<SimpleBoat>();
        quality = CinematicQuality.Current;
        // Each shot keeps its own draw distance (the builder's), capped by the quality preset; the last shot is left
        // at the gameplay camera's, so the hand-over has nothing pop in.
        foreach (var s in shots)
        {
            if (s == null || (settle != null && s.gameObject == settle.gameObject)) continue;
            var lens = s.Lens;
            lens.FarClipPlane = Mathf.Min(lens.FarClipPlane, quality.farClip);
            s.Lens = lens;
        }
    }

    void Bind()
    {
        if (!(director.playableAsset is TimelineAsset timeline)) return;
        foreach (var track in timeline.GetOutputTracks())
        {
            if (track is CinemachineTrack && brain != null) director.SetGenericBinding(track, brain);
            else if (track is StormTrack && weather != null) director.SetGenericBinding(track, weather);
        }
    }

    /// <summary>
    /// The map's own cameras (the boat's chase camera, and the swimmer's, which the rescuer adds when they start in the
    /// water). They keep updating while the intro is live, and the one that will be live after it - the highest
    /// priority - is the pose the last shot lands on.
    /// </summary>
    void FindMapCameras()
    {
        camerasFoundAt = Time.unscaledTime;
        foreach (var cam in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
        {
            if (cam.gameObject.scene == gameObject.scene || mapCameras.Exists(c => c.cam == cam)) continue;
            mapCameras.Add((cam, cam.StandbyUpdate));
            cam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
        }
        gameplayCamera = null;
        foreach (var c in mapCameras)
            if (c.cam != null && c.cam.isActiveAndEnabled && (gameplayCamera == null || c.cam.Priority.Value >= gameplayCamera.Priority.Value))
                gameplayCamera = c.cam;
    }

    /// <summary>The camera the mission will be seen through when the intro hands over.</summary>
    public CinemachineCamera GameplayCamera => gameplayCamera;

    /// <summary>The boat and the residents keep their starting places while the water comes up under them.</summary>
    void Hold()
    {
        foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (body.gameObject.scene == gameObject.scene) continue;
            bool mission = body.GetComponentInParent<SimpleBoat>() != null || body.GetComponentInParent<Survivor>() != null ||
                           body.GetComponentInParent<PlayerRescuer>() != null;
            if (!mission) continue;
            held.Add((body, body.isKinematic, body.position, body.rotation));
            body.isKinematic = true;
        }
        if (boat != null) { boatPos = boat.transform.position; boatRot = boat.transform.rotation; }
        if (weather != null)
        {
            stormBefore = weather.intensity;
            lightningBefore = weather.autoLightning;
            rainScaleBefore = weather.rainRateScale;
            weather.autoLightning = false;
        }
    }

    void ApplySettings()
    {
        if (weather != null) weather.rainRateScale = quality.rainRate / Mathf.Max(1f, weather.rainMaxRate);
        bool calm = GameSettings.ReduceMotion;
        foreach (var s in shots)
        {
            if (s == null) continue;
            var noise = s.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (noise != null) noise.enabled = !calm;
        }
        foreach (var e in effects)
        {
            if (e == null) continue;
            var main = e.main;
            main.maxParticles = Mathf.Max(8, Mathf.RoundToInt(main.maxParticles * quality.effectScale));
            var em = e.emission;
            em.rateOverTimeMultiplier *= quality.effectScale;
        }
        if (look != null && look.sharedProfile != null)
        {
            lookProfile = Instantiate(look.sharedProfile);         // a copy: the asset is never changed at runtime
            look.profile = lookProfile;
            lookProfile.TryGet(out dof);
            lookProfile.TryGet(out aberration);
            lookProfile.TryGet(out grain);
            if (dof != null) dof.active = quality.depthOfField;
            if (grain != null) grain.active = quality.filmEffects;
            if (aberration != null) aberration.active = quality.filmEffects && !calm;
        }
    }

    /// <summary>
    /// Every actor is made while the screen is still black (so no shot has a hitch when somebody first appears), then each
    /// group is hidden until its Cast_Show cue.
    /// </summary>
    void PrepareCast()
    {
        if (castRoot == null) return;
        foreach (Transform group in castRoot)
        {
            group.gameObject.SetActive(true);
            foreach (var spot in group.GetComponentsInChildren<CharacterSpot>(true)) spot.Spawn();
            group.gameObject.SetActive(false);
        }
    }

    /// <summary>Shows or hides one of the cast's places (by the child object's name).</summary>
    public void SetCast(string group, bool show)
    {
        if (castRoot == null) return;
        var g = castRoot.Find(group);
        if (g != null) g.gameObject.SetActive(show);
    }

    /// <summary>Is this cast group on screen now (for tests)?</summary>
    public bool CastShown(string group)
    {
        var g = castRoot != null ? castRoot.Find(group) : null;
        return g != null && g.gameObject.activeSelf;
    }

    // The power cutting out: every light the map has goes down, so the street goes dark.
    readonly List<(Light light, float intensity)> dimmed = new List<(Light, float)>();
    float powerOutAt = -1f;

    void PowerOut()
    {
        if (powerOutAt >= 0f) return;
        powerOutAt = Time.unscaledTime;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type == LightType.Directional || l.gameObject.scene == gameObject.scene) continue;
            if (l.GetComponentInParent<SimpleBoat>() != null || l.GetComponentInParent<PlayerRescuer>() != null) continue;
            dimmed.Add((l, l.intensity));
        }
    }

    void RestoreLights()
    {
        foreach (var d in dimmed) if (d.light != null) d.light.intensity = d.intensity;
        dimmed.Clear();
        powerOutAt = -1f;
    }

    void OnCue(string cue)
    {
        if (cue.StartsWith("Beat_") && int.TryParse(cue.Substring(5), out int b)) { Beat = b; BeatsSeen.Add(b); }
        var a = CutsceneAssets.Current;
        if (cue.StartsWith("Cast_Show_")) { SetCast(cue.Substring("Cast_Show_".Length), true); return; }
        if (cue.StartsWith("Cast_Hide_")) { SetCast(cue.Substring("Cast_Hide_".Length), false); return; }
        switch (cue)
        {
            case "Cue_Debris": if (debris != null) debris.Release(); break;
            case "Cue_Splash": if (splash != null) splash.Play(true); break;
            case "Cue_Siren_On": if (siren != null) siren.SetOn(true); if (sirenSound != null && !sirenSound.isPlaying) sirenSound.Play(); break;
            case "Cue_Siren_Off": if (siren != null) siren.SetOn(false); if (sirenSound != null) sirenSound.Stop(); break;
            case "Cue_PowerOut": PowerOut(); CutsceneAudio.PlayOneShot(a.powerOut, CutsceneAudio.Bus.Sfx, 0.9f); break;
            case "Cue_PhotoDrop": if (photo != null) photo.Play(); break;
            case "Cue_ShantyFall": if (shanty != null) shanty.Play(); CutsceneAudio.PlayOneShot(a.collapse, CutsceneAudio.Bus.Sfx, 1f); break;
            case "Cue_TricycleTip": if (tricycle != null) tricycle.Play(); break;
            case "Cue_JoySplash": CutsceneAudio.PlayOneShot(a.splash, CutsceneAudio.Bus.Sfx, 1f); break;
            case "Cue_Creak": CutsceneAudio.PlayOneShot(a.creak, CutsceneAudio.Bus.Sfx, 0.8f); break;
            case "Cue_DogBark": CutsceneAudio.PlayOneShot(a.dogBark, CutsceneAudio.Bus.Sfx, 0.8f); break;
        }
    }

    // The mission's residents are held where the mission starts and not drawn while the film plays.
    readonly List<(Renderer r, bool was)> hidden = new List<(Renderer, bool)>();
    bool residentsShown = true;

    void ShowResidents(bool show)
    {
        if (show == residentsShown) return;
        residentsShown = show;
        if (!show)
        {
            hidden.Clear();
            foreach (var s in FindObjectsByType<Survivor>(FindObjectsSortMode.None))
            {
                foreach (var r in s.GetComponentsInChildren<Renderer>()) { hidden.Add((r, r.enabled)); r.enabled = false; }
            }
        }
        else
        {
            foreach (var h in hidden) if (h.r != null) h.r.enabled = h.was;
            hidden.Clear();
        }
    }

    void LateUpdate()
    {
        if (!Started || Ended) return;
        double t = director.time, length = director.duration;
        // The mission's own residents wait out the film, unseen: the story's cast stand in for them, and they appear at the
        // hand-over exactly where the cast ended (Nena on her roof, Ben at his post, Joy adrift).
        ShowResidents(false);
        if (powerOutAt >= 0f)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - powerOutAt) / 0.7f);
            foreach (var d in dimmed) if (d.light != null) d.light.intensity = d.intensity * (1f - k);
        }

        // The boat sits where the mission starts, on the water (or the street, before the water is that high).
        if (boat != null)
        {
            var water = WaterSurface.Instance;
            float y = water != null ? water.GetHeight(boatPos) + (boatPos.y - water.transform.position.y + FloodLevel.Offset) : boatPos.y;
            boat.transform.SetPositionAndRotation(new Vector3(boatPos.x, Mathf.Max(y, -0.9f), boatPos.z), boatRot);
        }

        // The last shot ends on the gameplay camera, wherever its rig has put it.
        if (Time.unscaledTime - camerasFoundAt > 0.5f) FindMapCameras();
        if (settle != null && gameplayCamera != null)
        {
            settle.overrideEnd = true;
            settle.endPosition = gameplayCamera.State.GetFinalPosition();
            settle.endRotation = gameplayCamera.State.GetFinalOrientation();
        }

        // Towards the end everything eases to how the mission looks: the rain's density, the cutscene's own grade.
        float toEnd = (float)(length - t);
        float handover = Mathf.Clamp01(1f - toEnd / Mathf.Max(0.01f, lookFadeSeconds));
        if (weather != null)
            weather.rainRateScale = Mathf.Lerp(quality.rainRate / Mathf.Max(1f, weather.rainMaxRate), rainScaleBefore, handover);
        if (look != null) look.weight = 1f - handover;
        InstancedFoliage.DistanceScale = Mathf.Lerp(quality.foliageDistance, 1f, handover);

        // Depth of field on whatever the live shot looks at; chromatic aberration only with a lightning flash.
        if (dof != null && dof.active && brain != null && brain.ActiveVirtualCamera is CinemachineCamera live)
        {
            var mover = live.GetComponent<ShotMove>();
            float focus = mover != null && mover.lookAt != null ? Vector3.Distance(live.transform.position, mover.lookAt.position) : 12f;
            dof.focusDistance.Override(Mathf.Max(0.3f, focus));
        }
        if (aberration != null && aberration.active && weather != null) aberration.intensity.Override(Mathf.Clamp01(weather.Flash) * 0.8f);
    }

    void OnFinished(bool skipped)
    {
        if (Ended) return;
        Ended = true;

        // Back exactly as the mission starts.
        if (weather != null)
        {
            weather.SnapIntensity(stormBefore);
            weather.autoLightning = lightningBefore;
            weather.rainRateScale = rainScaleBefore;
        }
        InstancedFoliage.DistanceScale = 1f;
        FloodLevel.Set(0f);
        foreach (var h in held)
        {
            if (h.body == null) continue;
            h.body.transform.SetPositionAndRotation(h.pos, h.rot);
            h.body.isKinematic = h.kinematic;
            if (!h.kinematic) { h.body.linearVelocity = Vector3.zero; h.body.angularVelocity = Vector3.zero; }
        }
        if (boat != null) boat.transform.SetPositionAndRotation(boatPos, boatRot);
        if (debris != null) debris.Clear();
        if (siren != null) siren.SetOn(false);
        RestoreLights();
        if (castRoot != null) foreach (Transform group in castRoot) group.gameObject.SetActive(false);
        foreach (var prop in new[] { photo, shanty, tricycle }) if (prop != null) prop.RestoreStart();
        ShowResidents(true);

        foreach (var s in shots) if (s != null) s.gameObject.SetActive(false);
        if (skipped && brain != null) brain.ResetState();      // a cut, under the black, not a blend
        foreach (var c in mapCameras) if (c.cam != null) c.cam.StandbyUpdate = c.was;

        CutsceneHold.Release();
        MusicPlayer.Unduck(skipped ? 1f : 2.5f);
        if (skipped) ScreenFade.In(0.4f);
        else ScreenFade.Clear();
        Unload();
    }

    /// <summary>The intro scene goes (asynchronously; nothing here has to wait for it). Not while the game is
    /// switching scenes anyway, which takes it with everything else.</summary>
    void Unload()
    {
        var scene = gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded || SceneManager.sceneCount < 2 || !gameObject.activeInHierarchy) return;
        SceneManager.UnloadSceneAsync(scene);
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (lookProfile != null) Destroy(lookProfile);
        if (!Ended && Started)
        {
            InstancedFoliage.DistanceScale = 1f;
            // Unloaded mid-way (a test, the editor): never leave the mission held.
            FloodLevel.Set(0f);
            CutsceneHold.Release();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Current = null;
}
