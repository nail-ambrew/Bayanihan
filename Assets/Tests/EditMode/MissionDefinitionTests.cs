using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The mission asset: what the briefing says is worked out from the roster, so it cannot disagree with who is out
/// there; and the mission that ships makes sense - everybody can be reached with the suggested pack, nobody is
/// described twice, every character exists.
/// </summary>
public class MissionDefinitionTests
{
    static ResidentSpec Spec(string id, string household, Situation situation, Need needs,
                             CharacterCategory category = CharacterCategory.Adult)
    {
        return new ResidentSpec
        {
            id = id,
            displayName = id.ToUpperInvariant() + " " + household,
            household = household,
            situation = situation,
            needs = needs,
            category = category,
            anchor = "Anchor_" + id
        };
    }

    static MissionDefinition Mission(params ResidentSpec[] residents)
    {
        var def = ScriptableObject.CreateInstance<MissionDefinition>();
        def.residents = new List<ResidentSpec>(residents);
        return def;
    }

    // ------------------------------------------------------------------ what the briefing says

    [Test]
    public void The_roster_summary_counts_who_is_where_and_what_they_need()
    {
        var def = Mission(
            Spec("a", "X", Situation.OpenWater, Need.Lifebuoy),
            Spec("b", "X", Situation.OpenWater, Need.Lifebuoy | Need.FirstAid),
            Spec("c", "Y", Situation.Rooftop, Need.Rope),
            Spec("d", "Y", Situation.Trapped, Need.Rope | Need.FirstAid),
            Spec("e", "Z", Situation.Clinging, Need.Lifebuoy));
        def.reports = new string[0];

        Assert.AreEqual(2, def.Count(Situation.OpenWater));
        Assert.AreEqual(1, def.Count(Situation.Rooftop));
        Assert.AreEqual(3, def.CountNeeding(Need.Lifebuoy));
        Assert.AreEqual(2, def.CountNeeding(Need.Rope));
        Assert.AreEqual(2, def.CountNeeding(Need.FirstAid));

        string intel = def.Intel();
        StringAssert.Contains("2 in open water", intel);
        StringAssert.Contains("1 clinging to posts", intel);
        StringAssert.Contains("1 on rooftops", intel);
        StringAssert.Contains("1 trapped behind debris", intel);
        StringAssert.Contains("3 need a Salbabida", intel);
        StringAssert.Contains("2 need a Lubid", intel);
        StringAssert.Contains("2 hurt, need a Botika", intel);
    }

    [Test]
    public void Things_nobody_has_are_not_mentioned()
    {
        var def = Mission(Spec("a", "X", Situation.OpenWater, Need.Lifebuoy));
        def.reports = new string[0];
        string intel = def.Intel();
        StringAssert.DoesNotContain("rooftops", intel);
        StringAssert.DoesNotContain("trapped", intel);
        StringAssert.DoesNotContain("Lubid", intel);
        StringAssert.DoesNotContain("Botika", intel);
    }

    [Test]
    public void Dispatch_reports_follow_the_counts_and_blank_ones_are_left_out()
    {
        var def = Mission(Spec("a", "X", Situation.OpenWater, Need.Lifebuoy));
        def.reports = new[] { "A cable is down.", "   ", "", "The water is rising." };
        var lines = def.Intel().Split('\n').Select(l => l.Trim()).ToList();

        Assert.AreEqual(4, lines.Count, string.Join(" | ", lines));
        Assert.AreEqual("• A cable is down.", lines[2]);
        Assert.AreEqual("• The water is rising.", lines[3]);
    }

    [Test]
    public void The_one_line_summary_says_how_many_residents_and_how_many_the_boat_holds()
    {
        var def = Mission(Spec("a", "X", Situation.OpenWater, Need.None), Spec("b", "X", Situation.OpenWater, Need.None));
        def.setting = "Daytime · Flood";
        Assert.AreEqual("Daytime · Flood · 2 residents on the roster · boat holds 4 at a time", def.Meta(4));

        var one = Mission(Spec("a", "X", Situation.OpenWater, Need.None));
        StringAssert.Contains("1 resident on the roster", one.Meta(3));
    }

    [Test]
    public void Households_are_counted_by_family_name()
    {
        var def = Mission(
            Spec("a", "Reyes", Situation.OpenWater, Need.None), Spec("b", "Reyes", Situation.OpenWater, Need.None),
            Spec("c", "Cruz", Situation.OpenWater, Need.None), Spec("d", "", Situation.OpenWater, Need.None));
        var households = def.Households();
        Assert.AreEqual(2, households["Reyes"]);
        Assert.AreEqual(1, households["Cruz"]);
        Assert.AreEqual(2, households.Count, "somebody with no family name is not a household");
    }

    [Test]
    public void A_resident_is_called_by_their_first_name()
    {
        var spec = new ResidentSpec { id = "x", displayName = "Nena Dela Cruz" };
        Assert.AreEqual("Nena", spec.CallName);
        Assert.AreEqual("Madonna", new ResidentSpec { id = "x", displayName = "Madonna" }.CallName);
        Assert.AreEqual("x", new ResidentSpec { id = "x" }.CallName);
    }

    // ------------------------------------------------------------------ the mission that ships

    MissionDefinition Shipped()
    {
        var def = MissionDefinition.LoadShipped();
        Assert.IsNotNull(def, "there is no mission in Resources/Missions. Run Bayanihan > Rebuild Mission Asset.");
        return def;
    }

    [Test]
    public void The_shipped_mission_has_a_roster_with_nobody_listed_twice()
    {
        var def = Shipped();
        Assert.AreEqual(10, def.residents.Count);
        Assert.AreEqual(def.residents.Count, def.residents.Select(r => r.id).Distinct().Count(), "ids are unique");
        Assert.AreEqual(def.residents.Count, def.residents.Select(r => r.displayName).Distinct().Count(), "names are unique");
        Assert.AreEqual(def.residents.Count, def.residents.Select(r => r.anchor).Distinct().Count(), "each has a place of their own");
        foreach (var r in def.residents)
        {
            Assert.IsFalse(string.IsNullOrEmpty(r.displayName), r.id + " has no name");
            Assert.IsFalse(string.IsNullOrEmpty(r.household), r.displayName + " has no household");
            Assert.IsFalse(string.IsNullOrEmpty(r.anchor), r.displayName + " has no anchor");
            Assert.That(r.startCondition, Is.InRange(0.3f, 1f), r.displayName + " starts in a strange state");
        }
    }

    [Test]
    public void The_shipped_mission_mixes_every_situation_and_every_need()
    {
        var def = Shipped();
        foreach (Situation situation in System.Enum.GetValues(typeof(Situation)))
            Assert.Greater(def.Count(situation), 0, "nobody is " + situation);
        Assert.Greater(def.CountNeeding(Need.Lifebuoy), 0);
        Assert.Greater(def.CountNeeding(Need.Rope), 0);
        Assert.Greater(def.CountNeeding(Need.FirstAid), 0);
        Assert.Greater(def.Households().Count(h => h.Value > 1), 1, "more than one family to keep together");
        foreach (CharacterCategory category in new[] { CharacterCategory.Kid, CharacterCategory.Teen, CharacterCategory.Adult, CharacterCategory.Elder })
            Assert.IsTrue(def.residents.Any(r => r.category == category), "no " + category + " on the roster");
    }

    [Test]
    public void Everybody_on_the_shipped_mission_can_be_reached_with_the_suggested_pack()
    {
        var def = Shipped();
        var pack = RescueItems.Suggested.ToList();
        foreach (var r in def.residents)
        {
            foreach (Need need in new[] { Need.Lifebuoy, Need.Rope, Need.FirstAid })
            {
                if ((r.needs & need) == 0) continue;
                var item = RescueItems.All.First(i => RescueItems.NeedFor(i.id) == need).id;
                Assert.IsTrue(pack.Contains(item), r.displayName + " needs " + need + ", which the suggested pack does not carry");
            }
        }
        Assert.IsTrue(pack.Contains(RescueItemId.Radio), "the live wire needs the Radyo, and the suggested pack leaves it behind");
    }

    [Test]
    public void Somebody_the_boat_cannot_reach_is_always_somebody_a_line_can()
    {
        // A roof and a debris jam are out of reach from the water: those residents have to need the Lubid.
        foreach (var r in Shipped().residents)
            if (r.situation == Situation.Rooftop || r.situation == Situation.Trapped)
                Assert.IsTrue((r.needs & Need.Rope) != 0, r.displayName + " is out of the boat's reach but does not need a line");
    }

    [Test]
    public void Every_character_the_shipped_mission_names_is_in_the_library_and_is_the_right_kind_of_person()
    {
        var library = CharacterLibrary.Instance;
        Assert.IsNotNull(library, "no character library in Resources");
        foreach (var r in Shipped().residents)
        {
            if (string.IsNullOrEmpty(r.characterId)) continue;
            var entry = library.Find(r.characterId);
            Assert.IsNotNull(entry, r.displayName + " is " + r.characterId + ", who is not in the library");
            Assert.AreEqual(r.category, entry.category, r.displayName + " is listed as " + r.category + " but " + r.characterId + " is " + entry.category);
        }
    }

    [Test]
    public void The_shipped_mission_names_its_hazards_in_the_briefing()
    {
        var def = Shipped();
        string intel = def.Intel();
        StringAssert.Contains("Radyo", intel, "the live wire is the reason to pack it");
        StringAssert.Contains("rising", intel);
        Assert.Greater(def.timeLimit, 300f, "ten residents and three trips need more than five minutes");
        Assert.Greater(def.waterRisePerMinute, 0f);
    }
}
