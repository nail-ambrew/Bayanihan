using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static GameplayTestHelpers;

/// <summary>
/// The mission's screens: the HUD is up, the on-screen controls reach the game exactly as keys do, the pause menu stops
/// the game and its controls, and the Mission Complete screen comes up with the stats when the mission ends.
/// </summary>
public class GameUiTests
{
    TouchMode touchBefore;

    [UnitySetUp]
    public IEnumerator Load()
    {
        touchBefore = GameSettings.Touch;
        LoadoutState.Clear();
        TouchInput.Reset();
        yield return LoadGameplay();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        GameSettings.Touch = touchBefore;
        GamePause.Clear();
        TouchInput.Reset();
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_HUD_is_built_on_its_own_canvas()
    {
        Assert.IsNotNull(GameHud.Instance, "the rescue manager adds the HUD");
        Assert.IsNotNull(GameObject.Find("GameHud/SafeArea/Hud/Stats"), "the clock, safe, boat and score chips");
        Assert.IsNotNull(GameObject.Find("GameHud/SafeArea/Hud/Roster"), "the roster card");
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_joystick_and_buttons_reach_the_game_like_keys()
    {
        TouchInput.Stick = new Vector2(0.3f, 0.8f);
        Assert.AreEqual(new Vector2(0.3f, 0.8f), InputHelper.Move(), "the joystick steers when no key is held");
        TouchInput.Stick = new Vector2(3f, 4f);
        Assert.AreEqual(1f, InputHelper.Move().magnitude, 1e-4f, "never faster than a full push, however far the thumb goes");
        TouchInput.Stick = Vector2.zero;

        TouchInput.Press(TouchAction.Throw);
        Assert.IsFalse(InputHelper.ThrowPressed(), "a tap is seen the frame after it arrives");
        yield return null;
        Assert.IsTrue(InputHelper.ThrowPressed(), "the throw button throws");
        yield return null;
        Assert.IsFalse(InputHelper.ThrowPressed(), "and only for one frame");
        TouchInput.Release(TouchAction.Throw);

        TouchInput.Press(TouchAction.Interact);
        Assert.IsTrue(InputHelper.InteractHeld(), "holding RESCUE treats the injured");
        TouchInput.Release(TouchAction.Interact);
        Assert.IsFalse(InputHelper.InteractHeld());
    }

    [UnityTest]
    public IEnumerator The_joystick_reads_the_direction_the_thumb_moves_from_its_centre()
    {
        // Real pointer events, as a finger sends them, on the real on-screen stick: on the left and mirrored on the right.
        var sideBefore = GameSettings.Side;
        GameSettings.Touch = TouchMode.On;
        try
        {
            foreach (var side in new[] { ControlSide.JoystickLeft, ControlSide.JoystickRight })
            {
                GameSettings.Side = side;
                TouchControls.RefreshAll();
                yield return null;
                Canvas.ForceUpdateCanvases();

                var stick = Object.FindFirstObjectByType<Joystick>();
                Assert.IsNotNull(stick, "the HUD has an on-screen joystick");
                var area = (RectTransform)stick.transform;
                var corners = new Vector3[4];
                area.GetWorldCorners(corners);                          // screen-space overlay: world units are screen pixels
                Vector2 centre = (corners[0] + corners[2]) * 0.5f;
                float reach = (corners[2].x - corners[0].x);            // a full push, past the ring's edge

                var events = UnityEngine.EventSystems.EventSystem.current;
                UnityEngine.EventSystems.PointerEventData Finger(int id, Vector2 at) => new UnityEngine.EventSystems.PointerEventData(events) { pointerId = id, position = at };

                stick.OnPointerDown(Finger(1, centre));
                Assert.AreEqual(Vector2.zero, TouchInput.Stick, side + ": pressing the middle of the stick does not move it");

                foreach (var dir in new[] { Vector2.down, Vector2.up, Vector2.left, Vector2.right,
                                            new Vector2(1, -1).normalized, new Vector2(-1, -1).normalized, new Vector2(1, 1).normalized, new Vector2(-1, 1).normalized })
                {
                    stick.OnDrag(Finger(1, centre + dir * reach));
                    Assert.Greater(Vector2.Dot(TouchInput.Stick, dir), 0.97f, side + ": dragging " + dir + " steers " + TouchInput.Stick);
                    Assert.AreEqual(1f, TouchInput.Stick.magnitude, 0.02f, side + ": a full push is full speed");
                }

                // A small push down is a little down, not up: the bug was every touch inside the ring reading as "up".
                stick.OnDrag(Finger(1, centre + Vector2.down * reach * 0.25f));
                Assert.Less(TouchInput.Stick.y, -0.2f, side + ": a small push down steers down, gently");
                Assert.AreEqual(0f, TouchInput.Stick.x, 0.05f);

                // Another finger passing over the stick changes nothing; the first one still owns it.
                var held = TouchInput.Stick;
                stick.OnPointerDown(Finger(2, centre + Vector2.up * reach));
                stick.OnDrag(Finger(2, centre + Vector2.up * reach));
                stick.OnPointerUp(Finger(2, centre + Vector2.up * reach));
                Assert.AreEqual(held, TouchInput.Stick, side + ": a second finger does not take the stick");
                Assert.IsTrue(stick.IsHeld);

                stick.OnPointerUp(Finger(1, centre + Vector2.down * reach));
                Assert.AreEqual(Vector2.zero, TouchInput.Stick, side + ": letting go stops");
                Assert.IsFalse(stick.IsHeld);
            }
        }
        finally
        {
            GameSettings.Side = sideBefore;
            TouchControls.RefreshAll();
        }
    }

    [UnityTest]
    public IEnumerator Prompts_name_the_buttons_on_a_phone_and_the_keys_on_a_keyboard()
    {
        GameSettings.Touch = TouchMode.Off;
        Assert.AreEqual("Press E", InputHelper.Press("E", "RESCUE"));
        GameSettings.Touch = TouchMode.On;
        Assert.AreEqual("Tap RESCUE", InputHelper.Press("E", "RESCUE"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator Pausing_stops_time_and_the_crew_and_continuing_gives_both_back()
    {
        var menu = Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
        Assert.IsNotNull(menu, "the HUD makes a pause menu");
        menu.Open();
        Assert.IsTrue(GamePause.IsPaused);
        Assert.AreEqual(0f, Time.timeScale);
        Assert.IsTrue(InputHelper.GameplayLocked, "nothing the crew presses gets through while paused");

        TouchInput.Stick = Vector2.up;
        Assert.AreEqual(Vector2.zero, InputHelper.Move());
        TouchInput.Stick = Vector2.zero;

        menu.Close();
        Assert.IsFalse(GamePause.IsPaused);
        Assert.AreEqual(1f, Time.timeScale);
        Assert.IsFalse(InputHelper.GameplayLocked);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Pausing_frees_the_mouse_so_the_menu_can_be_clicked_and_continuing_locks_it_again()
    {
        // A locked cursor sits in the middle of the screen: every click lands there and no menu button can be pressed.
        var player = Object.FindFirstObjectByType<PlayerRescuer>();
        Assert.IsNotNull(player);
        if (!player.lockCursor) Assert.Ignore("this scene's player does not lock the cursor");
        yield return null;

        var menu = Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
        menu.Open();
        yield return null;
        Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "the pause menu frees the cursor");
        Assert.IsTrue(Cursor.visible);

        menu.Close();
        yield return null;
        Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState, "back in play the cursor is locked for mouse look again");

        SetTimeLeft(0.05f);
        yield return WaitUntil(() => RescueManager.Instance.ReportVisible, 15f, "the end screen");
        yield return null;
        Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "Mission Complete frees the cursor too");
    }

    [UnityTest]
    public IEnumerator The_end_of_the_mission_shows_Mission_Complete_with_the_stats()
    {
        SetTimeLeft(0.05f);
        yield return WaitUntil(() => RescueManager.Instance.ReportVisible, 15f, "the end screen");
        yield return null;
        yield return null;

        var heading = GameObject.Find("GameHud/MissionComplete/Safe/Board/Title");
        Assert.IsNotNull(heading, "the Mission Complete board");
        Assert.AreEqual("TIME'S UP!", heading.GetComponent<Text>().text, "the clock ran out with nobody registered");
        foreach (var row in new[] { "Time Taken:", "Survivors Saved:", "Areas Cleared:", "Supplies Used:" })
            Assert.IsNotNull(GameObject.Find("GameHud/MissionComplete/Safe/Board/Card/Body/Rows/Row_" + row), row);

        var stats = MissionStats.From(RescueManager.Instance);
        Assert.AreEqual(RescueManager.Instance.TotalSurvivors, stats.total);
        Assert.AreEqual(RescueManager.Instance.Evacuated, stats.saved);
        Assert.Greater(stats.areas, 0);
    }
}
