using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes Assets/Resources/Maps/MabuhayMission.asset: the mission played on the Barangay Mabuhay map
/// (Assets/Scenes/Maps/MabuhayGameplay.unity). It sits next to MissionAssetBuilder, which writes the Manila one, and does
/// the same job: everything about the mission that is not a place is here - the briefing, the clock, the weather, who is out
/// there and what each of them needs - so the loadout screen and the scene always agree. The places are the Anchor_* objects
/// in the scene, put there by MabuhayLevelBuilder.
///
/// It is deliberately not in Resources/Missions: MissionDefinition.LoadShipped takes the first asset in that folder, and that
/// has to stay the Manila mission. MapChoice knows where this one is.
///
/// The ten residents, their households, their needs and their situations are the same as in Manila; only where they wait
/// differs. Edit the numbers and names below, then run Bayanihan > Rebuild Mabuhay Mission Asset, or edit the asset by hand
/// in the Inspector; running this again puts it back to what is written here.
/// </summary>
public static class MabuhayMissionBuilder
{
    public const string Folder = "Assets/Resources/Maps";
    public const string AssetPath = Folder + "/MabuhayMission.asset";

    [MenuItem("Bayanihan/Rebuild Mabuhay Mission Asset")]
    public static void Rebuild()
    {
        var def = Build();
        Selection.activeObject = def;
        EditorGUIUtility.PingObject(def);
        Debug.Log("Mabuhay mission asset rebuilt: " + AssetPath + " (" + def.residents.Count + " residents)", def);
    }

    public static MissionDefinition Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Maps");

        var def = AssetDatabase.LoadAssetAtPath<MissionDefinition>(AssetPath);
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<MissionDefinition>();
            AssetDatabase.CreateAsset(def, AssetPath);
        }

        Fill(def);
        EditorUtility.SetDirty(def);
        AssetDatabase.SaveAssets();
        return def;
    }

    static void Fill(MissionDefinition d)
    {
        d.title = "Pagbaha sa Mabuhay · The Water Rises";
        // The loadout screen has room for about 200 characters.
        d.briefing =
            "Floodwater is running through Barangay Mabuhay, Carmona. Families cling to posts and wait on roofs on Bagumbayan Street. " +
            "Take everyone to the Barangay Hall, where the clerk will write their names.";
        d.setting = "Daytime · Flood";
        d.reports = new[]
        {
            "A cable is down across Bagumbayan Street, west of the hall. The water there is live, and only the Radyo can have the power cut.",
            "Wreckage jams the lane of A. Mapalad Street behind the corner houses: whoever is behind it can only be reached by a line thrown over.",
            "The water is rising, about ten centimetres a minute."
        };

        d.timeLimit = 480f;
        d.stormIntensity = 0.7f;
        d.waterRisePerMinute = 0.1f;
        d.maxWaterRise = 0.6f;
        d.rules = new RescueRules();

        d.residents = new List<ResidentSpec>
        {
            // The Dela Cruz family, near where the boat starts: a girl adrift, a man on a post, her grandmother on a roof.
            Spec("joy",   "Joy Dela Cruz",    "Dela Cruz",   CharacterCategory.Kid,   "C01", Situation.OpenWater, Need.Lifebuoy,                 0.85f),
            Spec("ben",   "Ben Dela Cruz",    "Dela Cruz",   CharacterCategory.Adult, "C16", Situation.Clinging,  Need.Lifebuoy,                 1.00f),
            Spec("nena",  "Nena Dela Cruz",   "Dela Cruz",   CharacterCategory.Elder, "C26", Situation.Rooftop,   Need.Rope,                     0.90f),

            // The Bautistas, at the pole with the live cable.
            Spec("celia", "Celia Bautista",   "Bautista",    CharacterCategory.Adult, "C20", Situation.Clinging,  Need.Lifebuoy,                 1.00f),
            Spec("jun",   "Jun Bautista",     "Bautista",    CharacterCategory.Teen,  "C09", Situation.Clinging,  Need.Lifebuoy,                 1.00f),

            // The Villanuevas, nearer the hall: a grandfather on a post, his granddaughter on the roof by the hall.
            Spec("ador",  "Ador Villanueva",  "Villanueva",  CharacterCategory.Elder, "C27", Situation.Clinging,  Need.Lifebuoy,                 0.85f),
            Spec("tessa", "Tessa Villanueva", "Villanueva",  CharacterCategory.Teen,  "C08", Situation.Rooftop,   Need.Rope,                     1.00f),

            // One man alone and hurt, in the open water.
            Spec("lito",  "Lito Reyes",       "Reyes",       CharacterCategory.Adult, "C13", Situation.OpenWater, Need.Lifebuoy | Need.FirstAid, 0.90f),

            // A mother and her boy behind the debris in the lane. She is hurt.
            Spec("rosa",  "Rosa Santos",      "Santos",      CharacterCategory.Adult, "C14", Situation.Trapped,   Need.Rope | Need.FirstAid,     0.80f),
            Spec("migo",  "Migo Santos",      "Santos",      CharacterCategory.Kid,   "C06", Situation.Trapped,   Need.Rope,                     0.80f)
        };
    }

    static ResidentSpec Spec(string id, string name, string household, CharacterCategory category, string character,
                             Situation situation, Need needs, float condition)
    {
        return new ResidentSpec
        {
            id = id,
            displayName = name,
            household = household,
            category = category,
            characterId = character,
            situation = situation,
            needs = needs,
            startCondition = condition,
            anchor = "Anchor_" + id
        };
    }
}
