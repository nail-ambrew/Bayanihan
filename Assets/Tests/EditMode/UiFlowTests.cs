using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>The logic behind the Figma screens: the Mission Complete numbers, the sign-in card's "last login",
/// the loading tips, the settings, the item names on the loadout, and the objectives under Dispatch.</summary>
public class UiFlowTests
{
    // ------------------------------------------------------------------ mission stats
    [Test]
    public void Residents_close_together_are_one_area_and_it_is_cleared_only_when_all_of_them_are_safe()
    {
        var positions = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(10, 0, 0),          // one junction
            new Vector3(100, 0, 0),                                // a roof on its own
            new Vector3(200, 0, 0), new Vector3(205, 3, 6)         // a side street (height does not count)
        };
        var safe = new List<bool> { true, true, false, true, false };
        MissionStats.Areas(positions, safe, 18f, out int cleared, out int total);
        Assert.AreEqual(3, total);
        Assert.AreEqual(1, cleared, "only the junction has everybody out");
    }

    [Test]
    public void Neighbours_of_neighbours_join_the_same_area()
    {
        var positions = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(15, 0, 0), new Vector3(30, 0, 0) };
        MissionStats.Areas(positions, new List<bool> { true, true, true }, 18f, out int cleared, out int total);
        Assert.AreEqual(1, total, "a chain of residents along a street is one area");
        Assert.AreEqual(1, cleared);
    }

    [Test]
    public void Stats_read_as_the_Figma_card_writes_them()
    {
        var s = new MissionStats { timeTaken = 263f, timeLimit = 480f, saved = 7, total = 10, areasCleared = 2, areas = 4, itemsUsed = 3, itemsPacked = 4, uses = 9 };
        Assert.AreEqual("4:23 / 8:00", s.TimeText);
        Assert.AreEqual("7 / 10", s.SavedText);
        Assert.AreEqual("2 / 4", s.AreasText);
        Assert.AreEqual("3 / 4 items · 9 uses", s.SuppliesText);
        Assert.AreEqual("None packed", new MissionStats().SuppliesText);
    }

    // ------------------------------------------------------------------ sign-in
    [Test]
    public void Last_login_reads_as_time_ago()
    {
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual("just now", AccountRules.TimeAgo(now.AddSeconds(-20).ToString("o"), now));
        Assert.AreEqual("1 minute ago", AccountRules.TimeAgo(now.AddMinutes(-1.5).ToString("o"), now));
        Assert.AreEqual("1 hour ago", AccountRules.TimeAgo(now.AddMinutes(-70).ToString("o"), now));
        Assert.AreEqual("3 days ago", AccountRules.TimeAgo(now.AddDays(-3.2).ToString("o"), now));
        Assert.IsNull(AccountRules.TimeAgo(null, now));
        Assert.IsNull(AccountRules.TimeAgo("not a date", now));
    }

    [Test]
    public void An_email_in_the_username_box_gets_a_clear_message()
    {
        StringAssert.Contains("username", AccountRules.CheckLogin("juan@example.com", "Secret#2026"));
        Assert.IsNull(AccountRules.CheckLogin("juan", "Secret#2026"));
    }

    // ------------------------------------------------------------------ loading
    [Test]
    public void The_loading_tips_include_the_three_from_Figma_and_never_repeat_back_to_back()
    {
        StringAssert.Contains("no one is left behind", string.Join("\n", LoadingTips.All));
        StringAssert.Contains("go-bag", string.Join("\n", LoadingTips.All));
        StringAssert.Contains("Saving a family is better", string.Join("\n", LoadingTips.All));
        string last = null;
        for (int i = 0; i < 50; i++)
        {
            string tip = LoadingTips.Next();
            Assert.AreNotEqual(last, tip);
            last = tip;
        }
    }

    // ------------------------------------------------------------------ settings
    [Test]
    public void Low_medium_and_high_pick_the_lowest_middle_and_highest_quality_level()
    {
        Assert.AreEqual(0, GameSettings.QualityLevelFor(0, 6));
        Assert.That(GameSettings.QualityLevelFor(1, 6), Is.InRange(2, 3), "a middle level");
        Assert.AreEqual(5, GameSettings.QualityLevelFor(2, 6));
        Assert.AreEqual(0, GameSettings.QualityLevelFor(2, 1), "a project with one level always uses it");
    }

    [Test]
    public void Settings_are_kept_and_clamped()
    {
        float volume = GameSettings.Volume, look = GameSettings.LookSensitivity;
        var touch = GameSettings.Touch;
        try
        {
            GameSettings.LookSensitivity = 99f;
            Assert.AreEqual(GameSettings.MaxSensitivity, GameSettings.LookSensitivity);
            GameSettings.Touch = TouchMode.On;
            Assert.IsTrue(GameSettings.ShowTouchControls);
            GameSettings.Touch = TouchMode.Off;
            Assert.IsFalse(GameSettings.ShowTouchControls);
        }
        finally
        {
            GameSettings.Volume = volume;
            GameSettings.LookSensitivity = look;
            GameSettings.Touch = touch;
        }
    }

    // ------------------------------------------------------------------ loadout
    [Test]
    public void Items_use_the_Figma_names_with_the_Filipino_name_kept_as_a_subtitle()
    {
        var expected = new Dictionary<RescueItemId, (string name, string filipino)>
        {
            { RescueItemId.FirstAid, ("First-Aid Kit", "Botika") },
            { RescueItemId.Flashlight, ("Flashlight", "Flashlight") },
            { RescueItemId.Lifebuoy, ("Floatie", "Salbabida") },
            { RescueItemId.Radio, ("Radio", "Radyo") },
            { RescueItemId.Raincoat, ("Raincoat", "Kapote") },
            { RescueItemId.Rope, ("Rope", "Lubid") },
        };
        foreach (var pair in expected)
        {
            var def = RescueItems.Get(pair.Key);
            Assert.AreEqual(pair.Value.name, def.name);
            Assert.AreEqual(pair.Value.filipino, def.filipino);
            Assert.IsFalse(string.IsNullOrWhiteSpace(def.description));
            Assert.LessOrEqual(def.description.Length, 95, def.name + "'s description is too long for its card");
        }
        CollectionAssert.AreEquivalent(RescueItems.All.Select(d => d.id), RescueItems.DisplayOrder, "the grid shows every item once");
    }

    [Test]
    public void Objectives_come_from_the_mission_and_say_what_to_pack()
    {
        var mission = ScriptableObject.CreateInstance<MissionDefinition>();
        mission.timeLimit = 480f;
        mission.residents = new List<ResidentSpec>
        {
            new ResidentSpec { id = "a", situation = Situation.OpenWater, needs = Need.Lifebuoy },
            new ResidentSpec { id = "b", situation = Situation.Rooftop, needs = Need.Rope },
            new ResidentSpec { id = "c", situation = Situation.Trapped, needs = Need.Rope | Need.FirstAid },
        };
        string text = LoadoutScreen.Objectives(mission);
        StringAssert.Contains("Rescue all 3 residents: 1 in the water, 1 on rooftops, 1 behind debris.", text);
        StringAssert.Contains("1 need a Floatie, 2 need a Rope, 1 are hurt (First-Aid Kit).", text);
        StringAssert.Contains("evacuation center", text);
        StringAssert.Contains("within 8:00", text);
        UnityEngine.Object.DestroyImmediate(mission);

        StringAssert.Contains("time limit", LoadoutScreen.Objectives(null), "with no mission, the Figma objectives");
    }
}
