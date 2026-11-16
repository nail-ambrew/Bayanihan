# Running Tests

- **EditMode**: Test Runner window, or `run_tests --mode editmode` over the Unity MCP.
- **PlayMode** can't run synchronously over the MCP (entering Play mode reloads the domain). And it fails at once if the
  Editor is already in Play mode — stop Play mode first.
  1. Note the time (`touch` a marker file).
  2. In an eval: `TestRunnerApi.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, groupNames = new[] { "^MabuhayPropTests" } }))`.
  3. Wait until `~/Library/Application Support/DefaultCompany/Bayanihan/TestResults.xml` is newer than the marker, then
     read the `test-case` results.
- The full PlayMode suite takes ~15 minutes. `MabuhayBotTests` is explicit (plays the whole mission): run it by name.
- The Unity MCP drops on every recompile and Play-mode change; wait for port 7800 before the next call.
- Project settings changed from code (e.g. a new layer) may not be written to disk until forced — check `git diff ProjectSettings`.
