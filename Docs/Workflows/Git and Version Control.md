# Git and Version Control

- **Git**: `origin` = github.com/nail-ambrew/Bayanihan. Work happens on `account-login-and-figma-ui`; finished work goes
  to `main` by pull request (merge commit). Commit and push only when the owner asks.
- **Unity Version Control** (`Capstone/Bayanihan`, workspace in this folder's `.plastic`): **never check in** from here;
  teammates do. `cm status --short` is safe. If the project folder moves, run `cm wk move` to point the workspace at it.
- **Teammates' uncommitted work** sits in the working copy (UI, audio, settings, TextMesh Pro). Leave it out unless the
  owner says to include it; for a shared file, stage only your hunks (build the file from `HEAD` + your change and
  `git update-index`). Say in the commit message and to the owner what was left out.
- Large binaries (audio, GLB, textures) go through Git LFS automatically on push.
- Ignored: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, builds (`*.app`). Builds go to `../Builds`.
