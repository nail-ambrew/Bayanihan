using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes Assets/Resources/Missions/Mission.asset: the mission the game ships with. Everything about it that is not a
/// place is here - the briefing, the clock, the weather, who is out there and what each of them needs - so the loadout
/// screen and the Gameplay scene always agree. The places are anchor objects in the scene, named after each resident's
/// "anchor" (see MissionBuilder).
///
/// Edit the numbers and names below, then run Bayanihan > Rebuild Mission Asset. Or edit the asset by hand in the
/// Inspector; running this again puts it back to what is written here.
/// </summary>
public static class MissionAssetBuilder
{
    static string Folder => "Assets/Resources/" + MissionDefinition.ResourceFolder;
    static string AssetPath => Folder + "/Mission.asset";

    [MenuItem("Bayanihan/Rebuild Mission Asset")]
    public static void Rebuild()
    {
        var def = Build();
        Selection.activeObject = def;
        EditorGUIUtility.PingObject(def);
        Debug.Log("Mission asset rebuilt: " + AssetPath + " (" + def.residents.Count + " residents)", def);
    }

    public static MissionDefinition Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", MissionDefinition.ResourceFolder);

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
        d.title = "Pagbaha sa Barangay · The Water Rises";
        d.briefing =
            "The creek jumped its banks before dawn. Families are on their roofs and clinging to the posts. " +
            "Take everyone to the Barangay Hall at the west end: nobody is safe until the clerk has written their name.";
        d.setting = "Daytime · Flood";
        d.reports = new[]
        {
            "A cable is down at the second junction west. The water there is live, and only the Radyo can have the power cut.",
            "Wreckage jams a side street to the north: whoever is behind it can only be reached by a line thrown over.",
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
            Spec("joy",   "Joy Dela Cruz",    "Dela Cruz",   CharacterCategory.Kid,   "C01", Situation.OpenWater, Need.Lifebuoy,              0.85f),
            Spec("ben",   "Ben Dela Cruz",    "Dela Cruz",   CharacterCategory.Adult, "C16", Situation.Clinging,  Need.Lifebuoy,              1.00f),
            Spec("nena",  "Nena Dela Cruz",   "Dela Cruz",   CharacterCategory.Elder, "C26", Situation.Rooftop,   Need.Rope,                  0.90f),

            // The Bautistas, at the junction with the live cable.
            Spec("celia", "Celia Bautista",   "Bautista",    CharacterCategory.Adult, "C20", Situation.Clinging,  Need.Lifebuoy,              1.00f),
            Spec("jun",   "Jun Bautista",     "Bautista",    CharacterCategory.Teen,  "C09", Situation.Clinging,  Need.Lifebuoy,              1.00f),

            // The Villanuevas, further west: a grandfather on a post, his granddaughter on the roof of the shop by the hall.
            Spec("ador",  "Ador Villanueva",  "Villanueva",  CharacterCategory.Elder, "C27", Situation.Clinging,  Need.Lifebuoy,              0.85f),
            Spec("tessa", "Tessa Villanueva", "Villanueva",  CharacterCategory.Teen,  "C08", Situation.Rooftop,   Need.Rope,                  1.00f),

            // One man alone and hurt, in the open street.
            Spec("lito",  "Lito Reyes",       "Reyes",       CharacterCategory.Adult, "C13", Situation.OpenWater, Need.Lifebuoy | Need.FirstAid, 0.90f),

            // A mother and her boy behind the debris in the side street. She is hurt.
            Spec("rosa",  "Rosa Santos",      "Santos",      CharacterCategory.Adult, "C14", Situation.Trapped,   Need.Rope | Need.FirstAid,  0.80f),
            Spec("migo",  "Migo Santos",      "Santos",      CharacterCategory.Kid,   "C06", Situation.Trapped,   Need.Rope,                  0.80f)
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
