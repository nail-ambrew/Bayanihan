using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Bayanihan > Cutscenes > Build Audio: the AudioMixer (Master > Music, SFX, Ambience, VO, each with its volume exposed
/// for the Settings sliders), import settings for the generated placeholder sounds, and the
/// <see cref="CutsceneAssets"/> registry that lists them. Safe to run again; an existing mixer is kept.
///
/// Unity has no public API to make a mixer, so this uses the editor's own (the one the Create menu uses). If a Unity
/// update breaks that, it says so: make Assets/Audio/Bayanihan.mixer by hand (Create > Audio Mixer), add the four
/// groups, expose their Volume as MusicVolume, SfxVolume, AmbienceVolume and VoVolume, and run this again.
/// </summary>
public static class CutsceneAudioBuilder
{
    public const string MixerPath = "Assets/Audio/Bayanihan.mixer";
    public const string AssetsPath = "Assets/Resources/" + CutsceneAssets.ResourcePath + ".asset";
    public const string Root = "Assets/Cutscenes";
    static readonly string[] Groups = { "Music", "SFX", "Ambience", "VO" };
    static readonly string[] Params = { CutsceneAudio.MusicParam, CutsceneAudio.SfxParam, CutsceneAudio.AmbienceParam, CutsceneAudio.VoParam };

    [MenuItem("Bayanihan/Cutscenes/Build Audio", false, 60)]
    public static void BuildFromMenu() => Debug.Log("CutsceneAudioBuilder: " + Build());

    public static string Build()
    {
        foreach (var f in new[] { "Assets/Audio", "Assets/Resources/Cutscenes", Root + "/Shared/Audio", Root + "/Loadout/Audio", Root + "/Intro/Audio" })
            EnsureFolder(f);
        AssetDatabase.Refresh();
        int imported = ConfigureImports();

        string mixerNote;
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null)
        {
            try { mixer = CreateMixer(); mixerNote = "mixer made"; }
            catch (Exception e)
            {
                Debug.LogError("CutsceneAudioBuilder: could not make the mixer (" + e.Message + "). Make " + MixerPath +
                               " by hand: see this class's notes.");
                mixerNote = "NO MIXER";
            }
        }
        else mixerNote = "mixer kept";

        var a = AssetDatabase.LoadAssetAtPath<CutsceneAssets>(AssetsPath);
        if (a == null)
        {
            a = ScriptableObject.CreateInstance<CutsceneAssets>();
            AssetDatabase.CreateAsset(a, AssetsPath);
        }
        a.mixer = mixer;
        if (mixer != null)
        {
            a.music = Group(mixer, "Music");
            a.sfx = Group(mixer, "SFX");
            a.ambience = Group(mixer, "Ambience");
            a.vo = Group(mixer, "VO");
        }
        a.rainLight = Clip("Shared", "Amb_RainLight_Loop");
        a.rainHeavy = Clip("Shared", "Amb_RainHeavy_Loop");
        a.tinDrips = Clip("Shared", "Amb_TinDrips_Loop");
        a.waterRush = Clip("Shared", "Amb_WaterRush_Loop");
        a.wind = Clip("Shared", "Amb_Wind_Loop");
        a.thunder = new[] { Clip("Shared", "Sfx_Thunder_01"), Clip("Shared", "Sfx_Thunder_02") }.Where(c => c != null).ToArray();
        a.siren = Clip("Shared", "Sfx_Siren_Loop");
        a.whoosh = Clip("Shared", "Sfx_Whoosh");
        a.splash = Clip("Shared", "Sfx_Splash");
        a.packRustle = Clip("Loadout", "Sfx_Pack_Rustle");
        a.packZip = Clip("Loadout", "Sfx_Pack_Zip");
        a.packBuckle = Clip("Loadout", "Sfx_Pack_Buckle");
        a.packDenied = Clip("Loadout", "Sfx_Pack_Denied");
        a.readyStamp = Clip("Loadout", "Sfx_Ready_Stamp");
        a.radioBulletin = Clip("Intro", "VO_Radio_Bulletin");
        // The story intro's spoken lines: every VO_Intro_<name> file, by name.
        a.introVoices = AssetDatabase.FindAssets("t:AudioClip VO_Intro_", new[] { Root + "/Intro/Audio" })
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith("VO_Intro_"))
            .OrderBy(p => p)
            .Select(p => new CutsceneAssets.NamedClip
            {
                name = System.IO.Path.GetFileNameWithoutExtension(p).Substring("VO_Intro_".Length),
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p)
            }).ToArray();
        a.creak = Clip("Intro", "Sfx_Creak");
        a.collapse = Clip("Intro", "Sfx_Collapse");
        a.dogBark = Clip("Intro", "Sfx_DogBark");
        a.powerOut = Clip("Intro", "Sfx_PowerOut");
        a.musicBed = Clip("Intro", "Music_Bed_Loop");
        a.musicCue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Music/To the Ends.mp3");
        // the loadout's models (GLB from Source Assets/Loadout/make_loadout_models.py)
        a.backpack = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Loadout/Models/Pack_DryBag.glb");
        a.itemModels = System.Enum.GetNames(typeof(RescueItemId))
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Loadout/Models/Item_" + n + ".glb")).Where(g => g != null).ToArray();
        EditorUtility.SetDirty(a);
        AssetDatabase.SaveAssets();

        int missing = new UnityEngine.Object[] { a.rainLight, a.rainHeavy, a.tinDrips, a.waterRush, a.wind, a.siren, a.whoosh, a.splash,
            a.packRustle, a.packZip, a.packBuckle, a.packDenied, a.readyStamp, a.radioBulletin, a.musicCue,
            a.creak, a.collapse, a.dogBark, a.powerOut, a.musicBed }.Count(o => o == null) + (a.introVoices.Length < 9 ? 1 : 0);
        return mixerNote + "; " + imported + " clips set up; " + (missing == 0 ? "every slot filled" : missing + " slots EMPTY") +
               "; thunder x" + a.thunder.Length + ".";
    }

    static AudioClip Clip(string folder, string name) =>
        AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/" + folder + "/Audio/" + name + ".wav");

    static AudioMixerGroup Group(AudioMixer mixer, string name) =>
        mixer.FindMatchingGroups(name).FirstOrDefault(g => g.name == name);

    /// <summary>Loops stream-light (compressed in memory); short one-shots decompress on load so they start at once.</summary>
    static int ConfigureImports()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) continue;
            bool loop = path.Contains("_Loop") || path.Contains("/VO_");
            var s = imp.defaultSampleSettings;
            s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = loop ? 0.6f : 0.8f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.loadInBackground = loop;
            imp.SaveAndReimport();
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ the mixer, through the editor's own API
    static AudioMixer CreateMixer()
    {
        var asm = typeof(Editor).Assembly;
        var controllerType = asm.GetType("UnityEditor.Audio.AudioMixerController", true);
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var controller = controllerType.GetMethod("CreateMixerControllerAtPath", any).Invoke(null, new object[] { MixerPath });
        var master = controllerType.GetProperty("masterGroup", any).GetValue(controller);
        var create = controllerType.GetMethod("CreateNewGroup", any);
        var addChild = controllerType.GetMethod("AddChildToParent", any);
        var addExposed = controllerType.GetMethod("AddExposedParameter", any);
        var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath", true);
        var exposedProp = controllerType.GetProperty("exposedParameters", any);

        for (int i = 0; i < Groups.Length; i++)
        {
            var group = create.Invoke(controller, new object[] { Groups[i], false });
            addChild.Invoke(controller, new[] { group, master });
            var volumeGuid = group.GetType().GetMethod("GetGUIDForVolume", any).Invoke(group, null);
            var path = Activator.CreateInstance(pathType, group, volumeGuid);
            addExposed.Invoke(controller, new[] { path });

            // Name the parameter just exposed (Unity calls it "MyExposedParam" until renamed).
            var array = (Array)exposedProp.GetValue(controller);
            for (int j = 0; j < array.Length; j++)
            {
                var p = array.GetValue(j);
                var guidField = p.GetType().GetField("guid", any);
                if (!guidField.GetValue(p).Equals(volumeGuid)) continue;
                p.GetType().GetField("name", any).SetValue(p, Params[i]);
                array.SetValue(p, j);
            }
            exposedProp.SetValue(controller, array);
        }
        EditorUtility.SetDirty((UnityEngine.Object)controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(MixerPath);
        return AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
