using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The mission for the Barangay Mabuhay map, held to what MissionDefinitionTests holds the Manila one to - a roster with nobody
/// listed twice, every situation and need, everybody reachable with the suggested pack, every character real - and to being the same
/// ten people in the same trouble, only somewhere else.
/// </summary>
public class MabuhayMissionTests
{
    static MissionDefinition Mabuhay()
    {
        var def = MapChoice.Find(MapChoice.MabuhayId).LoadMission();
        Assert.IsNotNull(def, "there is no Mabuhay mission in Resources/Maps. Run Bayanihan > Rebuild Mabuhay Mission Asset.");
        return def;
    }

    [Test]
    public void The_briefing_says_where_it_is_and_fits_the_loadout_screen()
    {
        var def = Mabuhay();
        StringAssert.Contains("Mabuhay", def.title);
        StringAssert.Contains("Barangay Mabuhay, Carmona", def.briefing);
        StringAssert.Contains("Barangay Hall", def.briefing);
        Assert.LessOrEqual(def.briefing.Length, 210, "the loadout screen's briefing box holds about 200 characters; the rest is cut off");
        Assert.IsFalse(string.IsNullOrWhiteSpace(def.setting));
    }

    [Test]
    public void The_reports_name_the_streets_the_hazards_are_on()
    {
        var def = Mabuhay();
        string reports = string.Join("\n", def.reports);
        StringAssert.Contains("Bagumbayan Street", reports, "the live wire is on Bagumbayan Street");
        StringAssert.Contains("A. Mapalad Street", reports, "the jam is in A. Mapalad Street");
        StringAssert.Contains("Radyo", reports);
    }

    [Test]
    public void The_roster_has_ten_people_with_nobody_listed_twice()
    {
        var def = Mabuhay();
        Assert.AreEqual(10, def.residents.Count);
        Assert.AreEqual(def.residents.Count, def.residents.Select(r => r.id).Distinct().Count(), "ids are unique");
        Assert.AreEqual(def.residents.Count, def.residents.Select(r => r.displayName).Distinct().Count(), "names are unique");
        Assert.AreEqual(def.residents.Count, def.residents.Select(r => r.anchor).Distinct().Count(), "each has a place of their own");
        foreach (var r in def.residents)
        {
            Assert.AreEqual("Anchor_" + r.id, r.anchor, r.displayName + "'s anchor is named after them, as MabuhayLevelBuilder makes it");
            Assert.That(r.startCondition, Is.InRange(0.3f, 1f), r.displayName + " starts in a strange state");
        }
    }

    [Test]
    public void It_is_the_same_ten_people_in_the_same_trouble_as_the_Manila_mission()
    {
        var manila = MissionDefinition.LoadShipped();
        var here = Mabuhay();
        foreach (var m in manila.residents)
        {
            var r = here.residents.FirstOrDefault(x => x.id == m.id);
            Assert.IsNotNull(r, m.displayName + " is not in the Mabuhay mission");
            Assert.AreEqual(m.displayName, r.displayName);
            Assert.AreEqual(m.household, r.household, m.displayName);
            Assert.AreEqual(m.category, r.category, m.displayName);
            Assert.AreEqual(m.characterId, r.characterId, m.displayName);
            Assert.AreEqual(m.situation, r.situation, m.displayName);
            Assert.AreEqual(m.needs, r.needs, m.displayName);
            Assert.AreEqual(m.startCondition, r.startCondition, 1e-4f, m.displayName);
        }
        Assert.AreEqual(manila.residents.Count, here.residents.Count);
    }

    [Test]
    public void It_mixes_every_situation_and_every_need_and_keeps_the_families_together()
    {
        var def = Mabuhay();
        foreach (Situation situation in System.Enum.GetValues(typeof(Situation)))
            Assert.Greater(def.Count(situation), 0, "nobody is " + situation);
        Assert.Greater(def.CountNeeding(Need.Lifebuoy), 0);
        Assert.Greater(def.CountNeeding(Need.Rope), 0);
        Assert.Greater(def.CountNeeding(Need.FirstAid), 0);
        Assert.Greater(def.Households().Count(h => h.Value > 1), 1, "more than one family to keep together");
    }

    [Test]
    public void Everybody_can_be_reached_with_the_suggested_pack_and_those_out_of_the_boats_reach_need_a_line()
    {
        var def = Mabuhay();
        var pack = RescueItems.Suggested.ToList();
        foreach (var r in def.residents)
        {
            foreach (Need need in new[] { Need.Lifebuoy, Need.Rope, Need.FirstAid })
            {
                if ((r.needs & need) == 0) continue;
                var item = RescueItems.All.First(i => RescueItems.NeedFor(i.id) == need).id;
                Assert.IsTrue(pack.Contains(item), r.displayName + " needs " + need + ", which the suggested pack does not carry");
            }
            if (r.situation == Situation.Rooftop || r.situation == Situation.Trapped)
                Assert.IsTrue((r.needs & Need.Rope) != 0, r.displayName + " is out of the boat's reach but does not need a line");
        }
        Assert.IsTrue(pack.Contains(RescueItemId.Radio), "the live wire needs the Radyo");
    }

    [Test]
    public void It_asks_for_three_trips_of_a_boat_that_holds_four()
    {
        var def = Mabuhay();
        int boat = 4;
        Assert.AreEqual(3, Mathf.CeilToInt(def.residents.Count / (float)boat), "ten residents and a boat for four make three trips");
    }

    [Test]
    public void Every_character_it_names_is_in_the_library_and_is_the_right_kind_of_person()
    {
        var library = CharacterLibrary.Instance;
        Assert.IsNotNull(library, "no character library in Resources");
        foreach (var r in Mabuhay().residents)
        {
            var entry = library.Find(r.characterId);
            Assert.IsNotNull(entry, r.displayName + " is " + r.characterId + ", who is not in the library");
            Assert.AreEqual(r.category, entry.category, r.displayName);
        }
    }

    [Test]
    public void The_clock_and_the_weather_are_sensible()
    {
        var def = Mabuhay();
        Assert.That(def.timeLimit, Is.InRange(300f, 720f), "a mission is a few minutes long");
        Assert.That(def.stormIntensity, Is.InRange(0.3f, 1f));
        Assert.That(def.waterRisePerMinute, Is.GreaterThan(0f), "the briefing says the water rises");
        Assert.IsNotNull(def.rules);
    }
}
