using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GameplayTestHelpers;
using static MabuhayTestHelpers;

/// <summary>
/// The Barangay Mabuhay mission played with a keyboard, by the MissionBot: a short drive to the hall to prove the boat can
/// get there under its own power, and - run by name, because it takes minutes - the whole mission from the loadout's pack to
/// the report. Nothing is teleported: every metre is driven, every rescue is E or T pressed on a virtual keyboard.
/// </summary>
public class MabuhayBotTests
{
    MissionBot bot;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return LoadMabuhay();
        yield return WaitSeconds(0.3f);
        bot = new MissionBot(Arena) { onLog = line => Debug.Log("[bot] " + line) };
        yield return bot.Start();
    }

    [UnityTearDown]
    public IEnumerator Restore_the_keyboard_and_the_statics()
    {
        bot?.Stop();
        MabuhayTestHelpers.Restore();
        yield return null;
    }

    [UnityTest]
    [Timeout(240000)]
    public IEnumerator The_boat_can_be_driven_from_its_start_to_the_hall_with_the_keyboard()
    {
        var boat = Boat();
        var hall = Hall();
        yield return bot.Board();
        Assert.IsTrue(Player().IsMounted, "E from the water beside the boat should climb aboard");

        Vector3 start = boat.transform.position;
        var zone = hall.GetComponent<Collider>();
        yield return bot.DriveToThe(zone);

        Vector3 flat = new Vector3(boat.transform.position.x, zone.bounds.center.y, boat.transform.position.z);
        Assert.IsTrue(zone.bounds.Contains(flat), "the boat did not get into the hall's drop-off zone: it is at " + boat.transform.position + ", the zone is " + zone.bounds);
        Assert.Greater(Vector3.Distance(start, boat.transform.position), 60f, "the hall is a long way from the start");
        Assert.IsFalse(RescueManager.Instance.GameOver);
    }

    /// <summary>The moments of a mission worth a photograph; the pictures are saved under Temp/MabuhayBot.</summary>
    static readonly string[] Moments =
    {
        "start", "helm", "aboard_joy", "line_nena", "alongside_nena", "line_rosa", "alongside_rosa", "aboard_lito",
        "hall_ashore", "hall_registering", "report"
    };

    [UnityTest]
    [Explicit("Plays the whole mission in real time, about four minutes: run it by name, with the Game view showing.")]
    [Timeout(1500000)]
    public IEnumerator The_bot_rescues_all_ten_residents_with_real_key_input_and_the_report_appears()
    {
        LoadoutState.Set(RescueItems.Suggested);               // the pack the loadout screen's "Suggested pack" button makes

        var taken = new HashSet<string>();
        bot.onMoment = name => System.Array.IndexOf(Moments, name) >= 0 && taken.Add(name) ? bot.Shot(name) : null;

        string folder = Path.Combine(Application.dataPath, "..", "Temp", "MabuhayBot");
        Directory.CreateDirectory(folder);
        try
        {
            yield return bot.PlayMission(new[]
            {
                new[] { "joy", "ben", "celia", "jun" },
                new[] { "nena", "rosa", "migo", "lito" },
                new[] { "ador", "tessa" }
            });
        }
        finally
        {
            // Whatever happened, the bot's account of it is kept for reading.
            File.WriteAllLines(Path.Combine(folder, "log.txt"), bot.Log);
        }

        var mission = RescueManager.Instance;

        Assert.AreEqual(mission.TotalSurvivors, mission.Evacuated, "everybody should be registered at the hall");
        Assert.IsTrue(mission.GameOver);
        Assert.IsFalse(mission.TimeIsUp, "the mission was finished within its clock of " + mission.timeLimit + " s: it took " + mission.Elapsed.ToString("0") + " s");
        yield return WaitUntil(() => mission.ReportVisible, 60f, "the report");
        Debug.Log("[bot] score " + mission.Score + " in " + mission.Elapsed.ToString("0") + " s, hull strikes " + Boat().HullStrikes);

        // R on the report: the map plays again from the beginning.
        yield return bot.PressR();
        yield return WaitUntil(() => RescueManager.Instance != null && RescueManager.Instance != mission && !RescueManager.Instance.GameOver && Time.timeScale == 1f, 30f, "the mission to start again after R");
        Assert.AreEqual(MabuhayTestHelpers.SceneName, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        Assert.AreEqual(0, RescueManager.Instance.Evacuated);
        Assert.AreEqual(10, RescueManager.Instance.TotalSurvivors);
        Assert.AreEqual(0, Boat().Passengers);
    }
}
