using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The pages the Figma file has a menu item for but no frame of its own: How to Play, Settings and Credits. They are
/// drawn on the same orange board and paper card as Mission Complete, to the right of the menu, so the main menu and
/// the pause menu can both open them.
/// </summary>
public static class MenuPages
{
    // ------------------------------------------------------------------ how to play
    /// <param name="close">The board's back arrow: every page can be closed with a tap (iOS has no Back key).</param>
    public static GameObject HowToPlay(Transform root, UnityAction close, float left = 290f)
    {
        var body = UiKit.Board(root, "HOW TO PLAY", "RESCUE GUIDE", null, out _, out _, left, close);
        var board = body.parent.parent.gameObject;
        var content = UiKit.Scroll(body, out _, 5f);

        bool touch = InputHelper.TouchActive;
        Section(content, "THE MISSION",
            "A typhoon has flooded the barangay. Take the rescue boat out, find the residents who are stranded, and bring " +
            "them to the evacuation center before time runs out. Nobody is safe until their name is written down at the " +
            "registration desk in the hall.");

        Section(content, "CONTROLS", touch
            ? "• Joystick: steer the boat, or swim.\n" +
              "• Drag the screen: look around. Pinch: zoom.\n" +
              "• RESCUE: pull someone aboard, board or leave the boat. Hold it to treat the injured.\n" +
              "• THROW: throw a Floatie or Rope to someone out of reach.\n" +
              "• LIGHT, RADIO, POWER CUT: use the Flashlight and the Radio, when packed.\n" +
              "• Pause button: pause, restart, settings or abort."
            : "• W A S D or arrows: steer the boat, or swim.  Mouse: look around.  Scroll: zoom.\n" +
              "• E: pull someone aboard, board or leave the boat. Hold E to treat the injured.\n" +
              "• T: throw a Floatie or Rope to someone out of reach.\n" +
              "• F: Flashlight.  Q: Radio (find the nearest resident).  C: ask for a live wire to be cut.\n" +
              "• Esc: pause, restart, settings or abort.");

        Section(content, "REACHING PEOPLE",
            "• In the water or clinging to a post: slow down, come alongside and rescue them.\n" +
            "• On a roof or behind debris: the boat cannot reach them. Throw them a line, or step off and swim to them, " +
            "take hold and tow them back to the boat.\n" +
            "• The boat holds four. Unload at the evacuation center to make room.");

        Section(content, "TRIAGE",
            "A tag shows over everyone you can see. RED is most urgent, then YELLOW, then GREEN. Children, elders and the " +
            "hurt weaken fastest, and the open water is the worst place to wait. Someone left too long becomes critical: " +
            "they can only board with the First-Aid Kit, and count for half.");

        Section(content, "PACKING",
            "You pack four of the six items before launch. A resident who needs an item cannot be rescued without it: " +
            "the Floatie for open water, the Rope for roofs and debris, the First-Aid Kit for the injured. The Radio " +
            "finds people and has live wires cut. Check Dispatch before you pack.");

        Section(content, "HAZARDS",
            "• Live wires in the water stop the engine and stun swimmers. Radio for the power to be cut, then wait.\n" +
            "• Floating debris dents the hull if you hit it fast.\n" +
            "• The water keeps rising, and everyone in it weakens faster.");

        Section(content, "SCORE",
            "100 points for each person registered, times their tag when you took them (Red x1.5, Yellow x1.25, Green " +
            "x1), half if they were critical. Bring a whole family in for 50 more each. Up to 100 for taking the most " +
            "urgent first, and 100 for a clean run with no hull strikes or unsafe swims.");
        return board;
    }

    // ------------------------------------------------------------------ settings
    /// <param name="onSignOut">Shown as a Sign Out button when given (the main menu, not the pause menu).</param>
    public static GameObject Settings(Transform root, UnityAction close, Action onSignOut = null, float left = 290f)
    {
        var body = UiKit.Board(root, "SETTINGS", "OPTIONS", null, out _, out _, left, close);
        var board = body.parent.parent.gameObject;
        // Tightly packed; the last settings (and SIGN OUT, on the main menu) scroll into view.
        var content = UiKit.Scroll(body, out _, 2f, new RectOffset(6, 6, 2, 4));

        // Sound: everything, then the music and the sound effects under it. Each is heard at once as its slider moves.
        Row(UiKit.Slider(content, "MASTER VOLUME", GameSettings.Volume, v => GameSettings.Volume = v).transform.parent);
        Row(UiKit.Slider(content, "MUSIC", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v).transform.parent);
        Row(UiKit.Slider(content, "SOUND EFFECTS", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v).transform.parent);
        Row(UiKit.Choice(content, "GRAPHICS", new[] { "LOW", "MEDIUM", "HIGH" }, GameSettings.Quality, i => GameSettings.Quality = i).transform);
        // The mission's colour grade: changes the picture at once when opened from the pause menu.
        Row(UiKit.Choice(content, "COLOR", ColorGrade.Names, GameSettings.ColorGrade, i => GameSettings.ColorGrade = i).transform);
        Row(UiKit.Slider(content, "LOOK SPEED", GameSettings.LookSensitivity, v => GameSettings.LookSensitivity = v,
                         GameSettings.MinSensitivity, GameSettings.MaxSensitivity).transform.parent);
        // How the swimmer is seen (V switches it in play): from their own eyes, or from behind with the whole body in view.
        Row(UiKit.Choice(content, "SWIM VIEW", new[] { "1ST PERSON", "3RD PERSON" },
                         GameSettings.SwimView == PlayerRescuer.PovMode.FirstPerson ? 0 : 1,
                         i => GameSettings.SwimView = i == 0 ? PlayerRescuer.PovMode.FirstPerson : PlayerRescuer.PovMode.ThirdPerson).transform);
        // Cinematics: the flood intro before a Mabuhay mission, calmer motion, and subtitles for the radio.
        Row(UiKit.Choice(content, "PLAY INTRO", new[] { "ON", "OFF" }, GameSettings.PlayIntro ? 0 : 1,
                         i => GameSettings.PlayIntro = i == 0).transform);
        Row(UiKit.Choice(content, "REDUCE MOTION", new[] { "OFF", "ON" }, GameSettings.ReduceMotion ? 1 : 0,
                         i => { GameSettings.ReduceMotion = i == 1; CinematicMotion.Changed(); }).transform);
        Row(UiKit.Choice(content, "SUBTITLES", new[] { "ON", "OFF" }, GameSettings.Subtitles ? 0 : 1,
                         i => GameSettings.Subtitles = i == 0).transform);
        Row(UiKit.Choice(content, "TOUCH CONTROLS", new[] { "AUTO", "ON", "OFF" }, (int)GameSettings.Touch, i =>
        {
            GameSettings.Touch = (TouchMode)i;
            TouchControls.RefreshAll();
        }).transform);
        Row(UiKit.Choice(content, "JOYSTICK", new[] { "LEFT", "RIGHT" }, (int)GameSettings.Side, i =>
        {
            GameSettings.Side = (ControlSide)i;
            TouchControls.RefreshAll();
        }).transform);

        // The account, so a player can find their UID for support: the same caption-and-value row as the settings above.
        var a = AccountService.Current;
        if (a != null)
        {
            var row = UiKit.Rect("Account", content);
            Row(row, 30);
            var cap = UiKit.Label("Caption", row, "ACCOUNT", UiTheme.BodySize + 1, UiTheme.Ink, TextAnchor.MiddleLeft, heading: true);
            UiKit.Place(cap.rectTransform, UiKit.Left, Vector2.zero, new Vector2(110, 22));
            var value = UiKit.Label("Value", row, a.username + "    UID " + a.uid, UiTheme.BodySize + 2, UiTheme.OrangeBurnt, TextAnchor.MiddleLeft, heading: true);
            value.rectTransform.anchorMin = new Vector2(0, 0);
            value.rectTransform.anchorMax = new Vector2(1, 1);
            value.rectTransform.offsetMin = new Vector2(118, 0);
            value.rectTransform.offsetMax = Vector2.zero;
        }
        if (onSignOut != null && a != null)
        {
            var holder = UiKit.Rect("SignOutRow", content);
            Row(holder, 32);
            var b = UiKit.Button(holder, "SIGN OUT", ButtonStyle.Secondary, () => onSignOut());
            UiKit.Place((RectTransform)b.transform, UiKit.Left, Vector2.zero, new Vector2(110, 28));
        }
        return board;
    }

    // ------------------------------------------------------------------ credits
    public static GameObject Credits(Transform root, UnityAction close, float left = 290f)
    {
        var body = UiKit.Board(root, "CREDITS", "BAYANIHAN", null, out _, out _, left, close);
        var board = body.parent.parent.gameObject;
        var content = UiKit.Scroll(body, out _, 5f);

        Section(content, "DISASTER RESCUE GAME", "A capstone project by ELEV8 GAMES. Version " + Application.version + ".");
        Section(content, "THE TEAM",
            "Christian Mchail Dela Cruz - Lead Developer, Unity Integration\n" +
            "Neil Andrew Velandrez - Asset Developer and Concept Art\n" +
            "John Benedict Villamor - UI/UX Designer and Asset References\n" +
            "Shiereen Francince Santos - Lead Researcher and Documentation\n" +
            "Joaquin Andrei Singayan - System Researcher, Mobile Implementation");
        Section(content, "THE PLACE",
            "Barangay Mabuhay, City of Carmona, Cavite. Map data © OpenStreetMap contributors, available under the Open " +
            "Database License (ODbL).");
        Section(content, "MUSIC",
            "\"To the Ends\" and \"Stay the Course\" by Kevin MacLeod (incompetech.com). Licensed under Creative Commons: " +
            "By Attribution 4.0 License, http://creativecommons.org/licenses/by/4.0/");
        Section(content, "SOUND",
            "Interface sounds: \"Interface Sounds\" by Kenney (kenney.nl), CC0. Outboard engine: \"River boat with outboard " +
            "engine (bow breaking through water)\" by nomadas, freesound.org, CC0.");
        Section(content, "MADE WITH", "Unity, Unity Gaming Services, Blender.");
        Section(content, "THANK YOU",
            "To the rescuers, barangay officials and volunteers whose work during typhoons inspired this game. " +
            "Bayanihan: when a community lifts together, no one is left behind.");
        return board;
    }

    // ------------------------------------------------------------------ helpers
    static void Section(Transform content, string heading, string text)
    {
        UiKit.Paragraph(content, heading, UiTheme.BodySize + 3, UiTheme.OrangeBurnt, heading: true);
        UiKit.Paragraph(content, text, UiTheme.BodySize + 1, UiTheme.Ink);
    }

    static void Row(Transform row, float height = 26f)     // choice buttons this tall still take a fingertip
    {
        var le = row.gameObject.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }
}
