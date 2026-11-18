# Running Tests

The open Editor is driven with the `unity` CLI (`unity cmd <command>`; `unity cmd --help` lists them).

## EditMode
`unity cmd run_tests --mode editor --filter <Class> --format json` runs synchronously and returns the results.
Leave out `--filter` for all of them (about 270, a few minutes).

## PlayMode
Entering Play mode reloads the domain, so PlayMode tests only run asynchronously:
1. Make sure the Editor is not in Play mode (`unity cmd editor_status`), and that no run is going
   (`unity cmd test_status` not *running*).
2. `unity cmd run_tests --mode playmode --filter <Class> --async_tests true` (the filter is a partial name match; leave
   it out for everything: about 230 tests, 40–60 minutes).
3. Wait until `test_status` reports *running* (otherwise you read the last run's results), then poll it until it
   doesn't. The finished status holds a summary and every result.
- If `test_status` sticks on *running* while the Editor is stopped (a run broke, e.g. Play mode was stopped mid-run),
  `unity cmd cancel_tests` clears it.
- `MabuhayBotTests` plays the whole mission with a bot (several minutes).

## Writing PlayMode tests
- Load a map with `GameplayTestHelpers.LoadGameplay()` (Manila) or `MabuhayTestHelpers.LoadMabuhay()`. Both start the
  rescuer **in the water** (`PlayerRescuer.StartAboardOverride = false`) for swimming tests; `Board(player)` puts them
  at the helm. `StartsAboardTests` checks the game's own start at the helm.
- Reset what you change in a `[UnityTearDown]`: `Time.timeScale`, `GamePause.Clear()`, `CutsceneHold.Release()`,
  `FloodLevel.Set(0)`, `ScreenFade.Clear()`, `GameSettings.Reset()`. A mission left loaded keeps its clock running and
  can pause time in the next test.
- To start from an empty scene, create one and unload the others, but never the test runner's own `InitTestScene…`.
- Time out instead of hanging: `GameplayTestHelpers.WaitUntil(condition, seconds, what)`.
- The Editor in the background runs slowly: test the behaviour (it happened, in order, before X) rather than tight
  wall-clock limits, which pick up frame lag.

## Other
- The connection drops on every recompile and Play-mode change; wait for port 7800 before the next call.
- `eval` has a 5 s limit, and `EditorApplication.delayCall` may not fire while the Editor is in the background: run
  long jobs as `Bayanihan` menu items (`unity cmd menu --path "Bayanihan/..."`).
- Project settings changed from code (e.g. a new layer) may not be written to disk until forced; check
  `git diff ProjectSettings`.
