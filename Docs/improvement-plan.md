# Bayanihan improvement plan — handoff

Read this first in a new session. It replaces the long conversation that produced it.
The full audit and original plan text is at `~/.claude/plans/learn-and-understand-the-merry-blossom.md`.

## Ground rules

- Unity 6000.3.9f1, URP. Team capstone; Unity Version Control repo `Capstone/Bayanihan`, branch `/main`. A teammate also commits.
- Never check in. Leave changes pending for the owner. `cm status --short` is safe to read.
- Change scenes only through the connected Editor (`unity command ...`), never by hand-editing `Assets/Scenes/Gameplay.unity` (22 MB, cannot be merged).
- Game code is in the `Bayanihan` assembly (`Assets/Scripts/Bayanihan.asmdef`). Tests are in `Assets/Tests/EditMode` and `Assets/Tests/PlayMode`.
- Assumptions agreed with the owner: PC keyboard and mouse first; survivors never die; the item-gating rule stays; new tuning values are Inspector fields.

## Status

| Milestone | State |
|---|---|
| M1 fixes | Done, verified in Play mode, tests passed |
| M2 physics and boat handling | Done, verified in Play mode, tests passed |
| M3 animation | Done. Verified in Play mode with real key input and by tests (EditMode 12, PlayMode 27, all passing, run fresh on 2026-10-09). Changes from the close-out are pending for the owner — see below |
| M4 rescue mechanics | Done. Owner gave the go-ahead on 2026-10-09 ("continue m4"). Verified by tests (EditMode 71, PlayMode 101, run fresh on 2026-10-11) and by a bot that played the whole mission with real key input. Everything is pending for the owner — see below |
| M5 backlog | Not started |

## First thing to do next

1. The owner play-tests M4 and checks in. Nothing was checked in. Pending (`cm status --short`):
   - Scene: `Assets/Scenes/Gameplay.unity` (about 1,100 changed lines in 22 MB, so check it in before the teammate touches the scene). It carries both the M3 `crewSpots` change and the M4 level: a "Mission Anchors" group (10 `Anchor_<id>` objects), a "Hazards" group (live wire, debris jam, three stretches of wreckage), `MissionBuilder` on `Manager`, and the four hand-placed survivors removed (the mission asset builds the residents now).
   - New asset: `Assets/Resources/Missions/Mission.asset` (and the folder `.meta`). Regenerate with Bayanihan > Rebuild Mission Asset.
   - New code: everything in `Assets/Scripts/Rescue/` (with `Rescue.meta`) and `Assets/Scripts/Editor/MissionAssetBuilder.cs`.
   - Changed code: `BoatEffects`, `BoatPassengers`, `BoatTools`, `EvacuationCenter`, `InputHelper`, `LoadoutScreen`, `PlayerRescuer`, `RescueItems`, `RescueManager`, `SimpleBoat`, `Survivor`, `SurvivorVisual`, `Characters/CharacterAnimator`, `Characters/CharacterPoses`.
   - Tests: `Assets/Tests/EditMode` (`RescueRulesTests`, `ScoreSheetTests`, `AfterActionReportTests`, `MissionDefinitionTests`) and `Assets/Tests/PlayMode` (`ResidentTests`, `ThrowRescueTests`, `SwimRescueTests`, `HazardTests`, `RegistrationTests`, `MissionLevelTests`, `InputLockTests`, plus the M3 files and the changed `MissionFlowTests`, `BoatPassengerTests`, `GameplayTestHelpers`).
   - `Docs/improvement-plan.md` (this file).
   - Not real changes: `ProjectSettings/ProjectSettings.asset` and `EditorBuildSettings.asset` show as changed but are byte-identical (Unity re-saved them); `Bayanihan.slnx` only has a line reordered; `.git/**` shows as private files (not in `ignore.conf`).
2. Look at these in a normal play-through (the bot cannot judge feel): the pack choice on the loadout screen and the new Dispatch panel; C at the start (power cut in 10 s, the Bautistas at the second junction); the throw to Nena on the roof and to the Santos over the jam; the swimmer's tether; registration at the hall; the report.
3. Ask the owner for the go-ahead on M5 before starting it. Do not start M5 without it.
4. Quick wins done on 2026-10-11 (after the M4 report; owner chose them over starting M5):
   - The swimmer now starts at (2.2, 0.8), 1.4 m off the boat's starboard side with "Press E to board the boat" showing, instead of inside the hull. Scene change on the `Player` object.
   - Once the mission is over (`RescueManager.GameOver`) the crew's keys do nothing: `InputHelper.GameplayLocked` gates move, E, F, Q, T and C. R and Esc on the report, and looking round, still work. Before, E could still take somebody aboard behind the report. `InputLockTests` (3) uses a virtual keyboard, so `Bayanihan.Tests.PlayMode.asmdef` now references `Unity.InputSystem`.
   - The parked `scooter_05` that sat at (-95, -3.5), on the straight line from the main street to the hall doors, is moved to the kerb at (-95, -1.9). It is one of dozens of scooters parked across the map; the rest are left alone (scene change).
   - The roster panel squeezes its rows to the room above the prompts (21 px down to 12 px, smaller font) and folds to "N / 10 safe" in a window too short for that. The Editor's Game view is often under 600 px tall, where it was covering the prompts.
5. Still open, found while verifying M4:
   - Other half-sunk props snag the hull and pin a hands-off autopilot: `scooter_03` (-35.3, -10) and the planters near x -30 on the south kerb, beside Celia's post. A human approaches from the centre of the street and is fine.
   - The wreckage fields drift east with the flow and pile against walls. A field that started in the plaza in front of the hall pinned the bot for two minutes, so it now starts at (-84, -7).
   - Only the report page scales with the window. The cards over residents, prompts and the top-left readout are fixed-pixel IMGUI (that is the M5 Canvas HUD).
   - Carried over from M3 and still true: the tiller swings about 15 cm past the helm arm at full lock; survivors facing away look over their shoulder rather than turning.

## What changed in the M3 close-out (2026-10-09)

- Tests added (all passing): waterline per pose for a kid and an adult and for every character in the library; a rescued survivor ends up seated at scale 1 on a seat, in a life vest; the crew goes to the bow when the player boards and returns when they step off; the hand is on the tiller and follows it; seats fill the lighter tube first; a full mission (four rescues, unload, report, retry).
- `BoatEffects`: the bow spray was invisible because its emitter sat under the water. Emitters now ride the water surface; the wake is flat soft foam that spreads and fades, the spray is soft round droplets thrown up at the bow. New Inspector fields: `wakeHeight`, `sprayHeight`, `sprayLift`; `wakeRate` 40 to 70, `sprayRate` 30 to 60.
- Tiller: `CharacterPoses.Reach` is a two-bone solve, so the right hand lands on the tiller wherever the helmsman sits. `BoatPassengers` finds the tiller (`tillerName`, default `motor_tiller`) and hangs a `TillerGrip` marker on its bow-most end; the hand follows it as the motor swings.
- Helm side: the tiller and motor are on the +X side of the boat, and the old helm seat was on the -X tube, 1.3 m from the grip. `crewSpots` is now helm (0.95, 0.66, 1.15) and bow lookout (-0.85, 0.66, -1.8). To undo, mirror them back; the helm arm then stretches towards a tiller it cannot reach.
- Seats: `ReserveSeat` picks the free seat on the tube with fewer people, counting the crew and whoever is at the helm. Before, the first person rescued sat on the same tube as the helm crew. Each person now sits in the seat they were hauled towards; before, they took whichever was free when they arrived, so two people arriving out of order jumped across the boat.
- Report: it waited a fixed 1.5 s, but the unload staggers each person by 0.35 s, so the last residents were frozen mid-stride under the report. `BoatPassengers.IsUnloading` is new; `RescueManager.ShowReport` waits for it (capped at 8 s).
- Checked and fine: head-look (head ends within about 4 degrees of the rescuer when the rescuer is within the neck's 70 degrees); life vests and the whole loop on a second run after retry.

## What exists now (beyond the original project)

- `WaterSurface`: follows the main camera, far skirt, accumulated wave phase, survives script reload.
- `FloodCurrent` (new): flow east (+X) at 0.6 m/s, scaled by storm, slack water upstream of walls.
- `SimpleBoat`: mass from people aboard, load-dependent handling, moves relative to the water, crew hold station while the player is away, hull strikes, unsafe passes, pick-up speed limit (3 m/s), exposes `Steer`, `Throttle`, `GroundSpeed`.
- `Buoyancy`: lift sized for a design mass, so load lowers the hull; rights itself from a capsize.
- `EvacuationCenter`: moved to the Barangay Hall porch (west end of the main street); optional `landingArea`; unloads only below 2.5 m/s.
- `RescueManager`: EVAC distance marker, a report that waits for the unload and the registration desk, score from `ScoreSheet`, use counts reset on retry.
- `BoatTools`: discovery is 18 m plus line of sight; tools only work aboard.
- `CharacterAnimator`: cross-fade, pose overlays, per-pose waterline, `HandTarget`. `CharacterPoses` (new): Seated, WaveForHelp, HoldTiller (reaches `HandTarget`), Limp, HeadLook.
- `SurvivorVisual`: automatic waterline, waves at rescuers, drifts with the current (open-water survivors, 25 m leash).
- `Survivor`: swim-to-hull and haul-aboard sequence; kinematic rigidbody.
- `BoatPassengers`: full-size riders on the side tubes, life vests, helm crew moves to the bow, balanced seat choice, tiller grip, staggered unload with `IsUnloading`.
- `BoatEffects` (new): outboard swing, wake, bow spray.
- `PlayerRescuer`: mouse look from the helm, safe step-off, seated helm pose, capsule follows pose, holds beside the boat until first input.
- `HudMarker`, `LineOfSight` (new helpers).
- `InputHelper.GameplayLocked`: the crew's keys are dead once the mission is over; R, Esc and mouse look still work.
- M4 additions (see "M4 — what was built"): residents with situations, condition and triage; `MissionHud`; `BoatRescue`/`RescueLine` throw; `SwimRescue`; `LiveWireZone`, `FloatingDebris`, `DebrisField`, `DebrisBarrier`, `FloodRise`; `RegistrationDesk`; `ScoreSheet`, `AfterActionReport`; `MissionDefinition`, `MissionBuilder`, `MissionAssetBuilder`.

Tuning changed from the original: swim speed 4.5 to 2.8; waves from one 0.3 m swell to two small ones (0.08 m at 14 m, 0.05 m at 9 m); character scale on the boat 0.65 to 1.

## M4 — what was built (2026-10-09 to 2026-10-10)

Order: a foundations, b markers and roster, c situations, d throw, e swim, f hazards, g evacuation and score, h report, i mission asset and level, j balance. Each step ended with the whole suite green.

- [x] a. Resident data, situation, condition timer, triage
- [x] b. Assessment markers and the roster panel
- [x] c. Situations look right: open water, clinging (hands on a post), rooftop, trapped
- [x] d. Throw rescue
- [x] e. Swim rescue with tow and tether
- [x] f. Hazards: live wire and the power-cut request, floating debris, debris jam, rising water
- [x] g. Evacuation centre: registration desk, named roster, reunited-household bonus, priority-weighted score
- [x] h. After-action report
- [x] i. `MissionDefinition` asset feeding the briefing and the level; 10 residents placed in the scene
- [x] j. Balance by playing it; this document

Where things live (all in `Assets/Scripts/Rescue` unless noted):

- Data: `Situation.cs` (`Situation`, `Triage`, `ResidentStatus`, `TriageStyle`), `RescueRules.cs` (every tuning number for drain, triage and score), `ResidentSpec.cs`, `MissionDefinition.cs` (the asset), `Roster.cs` (`ResidentRecord`, `Roster`), `ScoreSheet.cs` (`ScoreSheet`, `SafetyRecord`, `HouseholdResult`), `AfterActionReport.cs`.
- Building the level: `MissionBuilder.cs` (on `Manager`; sets the clock, storm and rise, and spawns each resident at the scene object named by its `anchor`), `ResidentSpawner.cs`, `Editor/MissionAssetBuilder.cs` (the shipped mission is defined in code there and written to the asset).
- Rescuing: `SurvivorCondition.cs`, `BoatRescue.cs` and `RescueLine.cs` (throw), `SwimRescue.cs` (grab, tow, tether), `Survivor.cs` (`CanBeRescued`, `Rescue`, `Dislodge`).
- Hazards: `LiveWireZone.cs`, `FloatingDebris.cs`, `DebrisField.cs`, `DebrisBarrier.cs` (with `DebrisProps`), `FloodRise.cs`, `ProceduralArt.cs`.
- Hall: `RegistrationDesk.cs` (added to the `EvacuationCenter` by itself); `EvacuationCenter.cs`, `BoatPassengers.cs` (`UnloadAll` now returns who landed).
- Screens: `MissionHud.cs` (cards over residents, roster, live-wire labels, who is being registered), `RescueManager.cs` (HUD, score, report), `LoadoutScreen.cs` (reads the mission asset; Dispatch panel).

How a rescue works now:

- Situations. OpenWater and Clinging can be reached by the boat or by a thrown line. Rooftop and Trapped can only be reached by a thrown line or a swimmer (they `RequiresLine`). Needs are unchanged (Salbabida, Lubid, Botika), and so is the rule that the item has to be packed.
- Condition runs 1 to 0 and drains per minute by situation (open water 0.28, clinging 0.20, trapped 0.16, rooftop 0.08), x1.5 for kids, x1.4 for elders, x1.5 if hurt, x2 in a live wire, x0.5 while held on a line, up to x1.35 for the storm, plus 0.8 per metre the water has risen (not on roofs). At 0 they are critical: they cannot board without the Botika hold, and they score half. Nobody dies.
- Triage: urgency = condition lost + vulnerability (kid 0.25, elder 0.2, hurt 0.2, open water 0.1). Red from 0.6 (or critical), Yellow from 0.3. The tag and condition are read live above anybody the crew can see (20 m, line of sight, 1.5x with the flashlight).
- Throw (T): range 12 m, line of sight, boat under 4 m/s relative to them, 0.8 s between throws; one Salbabida and one Lubid out at a time. The person takes hold and is pulled alongside at 1.8 m/s; somebody in the water lets go if the line goes taut (over 16 m or the boat above 4 m/s). Somebody on a roof or behind debris cannot lose the line. A rooftop resident walks to the roof edge and comes down into the water beside the hull. Behind debris the pull is 20% slower.
- Swim (E, in the water): grab within 2.6 m, tow at 0.6x speed, the crew take them within 4.5 m of the hull (treating them for 2 s first if hurt or critical). With the Lubid packed the swimmer is tethered 35 m from the boat; without it, swimming further than 12 m for 3 s is an "untethered swim". Swimming to somebody a throw would have reached is an "unneeded swim". Both are marks in the safety record.
- Live wire: `LiveWireZone`. The boat in it loses its engine for 3 s, the swimmer is thrown clear and stunned for 3 s, repeating every 4 s while they stay; nobody in it can be rescued (and a throw into it is refused). With the Radyo packed, C asks for the power to be cut (35 m from the zone's edge); 10 s later it goes dead. Nothing is fatal.
- Debris: a `FloatingDebris` piece is a Rigidbody with `Buoyancy`, carried by the current. Hitting one faster than 3.5 m/s is a hull strike. `DebrisBarrier` is a solid block the boat cannot pass and the swimmer's colliders ignore; `LineOfSight` passes it, so nobody behind it is hidden and a line can be thrown over it.
- Hall: each resident who steps ashore joins the line at the `RegistrationDesk` (2 s each, one at a time, in the order they landed). Only when registered are they safe (`RescueManager.Evacuated`) and scored. When time runs out, those already ashore are still registered before the report; those still aboard are not, and the boat is not unloaded any more.
- Score (`ScoreSheet`): 100 per registered resident x tag when taken (Red 1.5, Yellow 1.25, Green 1; x0.5 if critical); 50 per member of each household of two or more that all reached the hall; at the end, up to 100 for how often the most urgent person the crew knew about was taken first, and 100 for a clean run (nothing in the safety record).
- Report (`AfterActionReport`): result, each resident with how they came and what they scored (or why they were left), households, score breakdown, priority, safety record, response times, and the pack. Laid out on a 1280 x 720 page scaled to the window.

The shipped mission (`Assets/Resources/Missions/Mission.asset`, 480 s, storm 0.7, water rising 0.1 m/min up to 0.6 m):

| Household | Who | Where | Needs |
|---|---|---|---|
| Dela Cruz | Joy (kid) | adrift in the street, 15 m from the start | Salbabida |
| Dela Cruz | Ben | clinging to the south post at the east junction | Salbabida |
| Dela Cruz | Nena (elder) | on the shop roof at x -27 | Lubid |
| Bautista | Celia | clinging, south post, second junction (live wire) | Salbabida |
| Bautista | Jun (teen) | clinging, north post, second junction (live wire) | Salbabida |
| Villanueva | Ador (elder) | clinging, north post, third junction | Salbabida |
| Villanueva | Tessa (teen) | on the shop roof by the hall, x -87 | Lubid |
| Reyes | Lito | adrift, hurt, near x -80 | Salbabida, Botika |
| Santos | Rosa | behind the jam in the side street at x -71, hurt | Lubid, Botika |
| Santos | Migo (kid) | behind the jam | Lubid |

The suggested pack (Salbabida, Botika, Lubid, Radyo) can reach all ten, and the Radyo is what unlocks the Bautistas. The boat holds 4, so it takes three trips.

Balance, from a bot that played it with real keys (nearest-first, not triage-optimal, a few minutes lost pinned on scenery): all 10 registered in 358 s of the 480 s, 1,540 points, three residents scored half because they went critical before it reached them, 27 hull strikes (it drove at 5 m/s through the wreckage). The same route with the stuck minutes removed is about 230 s. A first-time human will be slower, hence 480 s rather than 420 s. Joy and Lito go critical about 90 s in, the trapped Santos about 150 s in, and the Bautistas in about 100 s unless the power is cut, so the order matters. Numbers to change if it plays too hard or too soft: `RescueRules` (drain, thresholds, score), the mission asset (`timeLimit`, `waterRisePerMinute`, each resident's `startCondition`), `BoatRescue` (`throwRange`, `pullSpeed`), `LiveWireZone` (`cutDelay`, `boatStallSeconds`), `RegistrationDesk.secondsPerResident`.

Decisions made without asking (all are Inspector or asset fields, change freely):

- Nobody dies. Critical means half marks and the Botika hold.
- Registration is one at a time and takes a while, so a boatload is not safe the moment the boat touches the porch. Score is awarded at registration.
- Households are only a bonus for two or more; a single person with a family name is not one.
- The Radyo is the only way to cut the power, so the live wire is the reason to pack it. Without it the wire stays live all mission and the Bautistas are lost.
- A swimmer set to be stopped by a jam was tried and dropped (the solver does not reliably push a velocity-driven swimmer out of a kinematic block); the swimmer's colliders ignore the jam, the boat's do not.
- Scenes store only settings (`DebrisField`, `DebrisBarrier`, `LiveWireZone`), not pieces or materials, so the 22 MB scene stays small.

Tests: EditMode 71 (new: `RescueRulesTests` 11, `ScoreSheetTests` 14, `AfterActionReportTests` 22, `MissionDefinitionTests` 12; earlier: `HudMarkerTests` 5, `LoadoutTests` 7); PlayMode 101 (new: `ResidentTests` 11, `ThrowRescueTests` 10, `SwimRescueTests` 11, `HazardTests` 19, `RegistrationTests` 9, `MissionLevelTests` 11, `InputLockTests` 3; changed: `MissionFlowTests` now plays all ten residents in three trips). Tests that stage their own world call `GameplayTestHelpers.ClearTheStage()` first, which removes the mission's residents and hazards from the loaded scene.

## M4 original scope (all done)

1. Survivor model: situation, triage colour, condition timer. 2. Assessment markers at about 20 m with line of sight. 3. Throw rescue. 4. Swim rescue with tether and tow. 5. Hazards: live wire with a radio request to cut power, floating debris reusing `Buoyancy`, rising water. 6. Evacuation centre procedure: named roster, timed registration, reunited-household bonus. 7. `MissionDefinition` asset feeding the briefing and the level; 10 residents on roofs, posts, open water and behind debris. 8. Priority-weighted score and a fuller after-action report.

## M5 — backlog

- Canvas HUD with pause and onboarding prompts (the HUD is fixed-pixel IMGUI). A pause should also stop boat and player input: reuse `InputHelper.GameplayLocked`, which already does that once the mission is over (extend it with a pause flag).
- Input Actions with gamepad.
- Audio: none exists; needs clips from the owner.
- Performance, after profiling: about 24,000 draw calls and 20,360 mesh colliders; batching and one collision mesh per map tile.
- Rain streaks near the camera are too thick (a streak a few centimetres from the lens shows up as a thick white bar).
- A rushed pick-up of an injured survivor is not counted in the report.
- Hygiene: duplicate `filipino_characters_30 1.glb`; stray `Assets/Gameplay.unity`; unused `New Actions.inputactions`; font in the Scripts folder; `AddCollidersToChildren` left on five scene objects; `.git` and `*.slnx` not in `ignore.conf`; "Botika" means drugstore.

## Running tests from the CLI

- EditMode runs synchronously: `unity command run_tests --mode EditMode`.
- PlayMode must be async (entering Play mode reloads the domain): `unity command run_tests --mode PlayMode --async_tests true`, then poll `unity command test_status` until `status` is not `running`. Add `--filter <class or test name>` to run a subset. The whole PlayMode suite takes about 330 s now (101 tests; the full-mission and registration tests are the slow ones), so run a class at a time while working and the whole suite once at the end. `test_status` returns JSON inside a JSON string, and a finished run's file can be stale; check `duration` and that you started the run yourself.
- Tests that need the real scene use `GameplayTestHelpers`; ones that stage their own world call `ClearTheStage()` first, because the scene now has ten residents, a live wire, a jam and wreckage in it. `TakeAboard`, `ArriveAtTheHall` and `SetTimeLeft` do what the player or the clock would. Private game methods that only input reaches (`SimpleBoat.PickUp`, `PlayerRescuer.Mount` and `Dismount`) are called by reflection, so a rename fails with a message naming it.

## Driving the game from the CLI in the background

- After `unity command editor_play`, set `Application.runInBackground = true` through `unity command eval`, or the game stays at frame 2.
- The mission timer is 480 s of game time (the mission asset's `timeLimit`) and ends the game (time scale 0). To explore for longer, set the private `timeLeft` on `RescueManager.Instance` by reflection to `100000f`. Not `1e6f`: at that size a float cannot represent a 0.02 s step, so it never counts down.
- `eval` takes a method body: no `using` directives, `UnityEngine.Object` must be written out, and a static class cannot be aliased with `var` (write `UnityEngine.InputSystem.InputSystem...` in full). `eval_file` takes the same from a file.
- For input: set `InputSystem.settings.backgroundBehavior = IgnoreFocus` and `editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`, add virtual `Keyboard` and `Mouse` devices, disable the real ones, and queue `KeyboardState` events on the virtual keyboard. The devices outlive Play mode: remove any named `Virtual*` before adding new ones (a second keyboard becomes `VirtualKeyboard1` and the first, now disabled, swallows your keys), and re-enable the real devices before leaving Play mode.
- Do not read key state inside a sampling eval while a key is held; it releases the key.
- A press of E with nobody in reach steps the player off the boat, so do not combine E with a drive key unless a survivor is in range.
- To teleport the boat use `LookRotation(want) * Inverse(LookRotation(boat.LocalBow))`, and zero its velocities.
- Useful coordinates: boat start (-0.5, 0.8); the ten residents' anchors are the `Anchor_<id>` objects (Joy (-16, -8), Ben post (6.1, -11.8), Nena roof (-27.1, 3.8, 0.3), Celia and Jun posts at x -38, Ador post (-74.3, -1.6), Tessa roof (-86.9, 3.9, 0.3), Lito (-80, -7.5), Santos behind the jam at (-71, 19) in the side street whose mouth is at x -71); the live wire is centred at (-38, -6.7); hall drop-off zone centre (-107.5, -4). The main street runs east to west with its centre line at about z = -6.5; a boat teleported to (-30, -6.5) facing west has a clear run of 70 m. Anything driven straight at the hall from the start gets stuck on umbrella stands and the floating minibus.
- Screenshots: `unity command screenshot` saves under `Temp/` (untracked, safe). `capture_game_view --save_path` writes under `Assets/`, where the version-control plugin auto-adds the file and its `.meta`: do not use it. To photograph a pose from a chosen angle, make a temporary `Camera` in an eval, render it to a `RenderTexture` and write the PNG to `Temp/`.
- Do not use `cm diff` here: it hangs trying to open a GUI. Compare with `cm cat <path> > file` and `diff`.
- A bot can play the whole mission with the virtual keyboard: an `EditorApplication.update` handler (registered from `eval_file`, stored in `AppDomain.SetData` so it can be removed) that steers with W/A/S/D towards a target, taps E, T and C for one update each (E held for the 2 s first-aid hold), and walks a list of tasks; poll its log from another eval. Give it waypoints rather than a straight line to the target, and skip a waypoint once the boat is past it (an early version stood still at the waypoint for minutes). Watch the boat's strikes and what is near it to find what pins it. `capture_game_view --source screen --json` captures IMGUI too (decode the base64; it comes back 1280 x 720 whatever the window size).
- Level scans: raycast down from 80 m with `~0`, ignoring `Water`, the boat, the player and residents, to get the top of whatever is there; list colliders by name and bounds to find roof decks (`*_roof_deck`, top about 3.8 to 3.9 m), posts (`post_*_shaft`, at the kerbs, z -1.0 and -12.4) and free corridors (nothing above 0.5 m). Temporary orthographic and perspective cameras rendering to a `RenderTexture` give top-down maps and close-ups without touching `Assets/`.
- Editing the scene from the CLI: stop Play mode, run an `eval_file` that builds objects (`new GameObject`, `AddComponent`, then `EditorSceneManager.MarkSceneDirty`), and `unity command save_scene`. `unity command open_scene --path Assets/Scenes/Loadout.unity` switches scenes (play the Loadout scene to see the pack screen; open Gameplay again afterwards). `MissionAssetBuilder.Build()` can be called from `eval` to rewrite the asset.
- macOS has no `timeout`; use the tool's own timeout. `sed -i` needs `-i ''`.
- A recompile while Play mode is on is not safe: stop Play, `recompile`, poll `recompile_status`, then play. A compile error leaves the old assemblies running, so check `failed` and `errors` before trusting a run.
