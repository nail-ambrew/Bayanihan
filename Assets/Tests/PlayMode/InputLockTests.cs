using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using static GameplayTestHelpers;

/// <summary>
/// Once the mission is over the crew's keys do nothing. Before this, E still took somebody aboard behind the report
/// and the boat still answered the throttle. R and Esc on the report screen must keep working.
/// A virtual keyboard stands in for the real one.
/// </summary>
public class InputLockTests
{
    Keyboard keyboard;
    Keyboard[] realKeyboards;

    [UnitySetUp]
    public IEnumerator Load()
    {
        LoadoutState.Clear();
        yield return LoadGameplay();

        // Keep listening to a virtual keyboard even when the Editor window is not in front.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

        keyboard = InputSystem.AddDevice<Keyboard>("InputLockTestKeyboard");
        keyboard.MakeCurrent();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        LoadoutState.Clear();
        Time.timeScale = 1f;
        yield return null;
    }

    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
    }

    IEnumerator Release()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    [UnityTest]
    public IEnumerator The_keys_reach_the_game_while_the_mission_is_on()
    {
        Assert.IsFalse(InputHelper.GameplayLocked);

        yield return Press(Key.W);
        Assert.AreEqual(1f, InputHelper.Move().y, "W is the throttle");
        yield return Release();

        yield return Press(Key.E);
        Assert.IsTrue(InputHelper.InteractPressed(), "E is read");
        yield return Release();
    }

    [UnityTest]
    public IEnumerator The_keys_do_nothing_once_the_mission_is_over()
    {
        SetTimeLeft(0.05f);
        yield return WaitUntil(() => RescueManager.Instance.GameOver, 5f, "the clock to run out");
        Assert.IsTrue(InputHelper.GameplayLocked);

        yield return Press(Key.W);
        Assert.AreEqual(Vector2.zero, InputHelper.Move(), "the throttle is dead");
        yield return Release();

        foreach (var key in new[] { Key.E, Key.F, Key.Q, Key.T, Key.C })
        {
            yield return Press(key);
            Assert.IsFalse(InputHelper.InteractPressed() || InputHelper.InteractHeld() || InputHelper.FlashlightPressed() ||
                           InputHelper.RadioPressed() || InputHelper.ThrowPressed() || InputHelper.PowerCutPressed(),
                           key + " should do nothing once the mission is over");
            yield return Release();
        }
    }

    [UnityTest]
    public IEnumerator Restart_and_back_still_work_on_the_report_screen()
    {
        SetTimeLeft(0.05f);
        yield return WaitUntil(() => RescueManager.Instance.ReportVisible, 10f, "the report");

        yield return Press(Key.R);
        Assert.IsTrue(InputHelper.RestartPressed(), "R tries again");
        yield return Release();

        yield return Press(Key.Escape);
        Assert.IsTrue(InputHelper.BackPressed(), "Esc goes back to the loadout");
        yield return Release();
    }
}
