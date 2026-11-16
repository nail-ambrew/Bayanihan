# UI and Settings

- Every screen is built in code with `UiKit` (boards, buttons, choices, sliders) and `UiTheme` (colours, sizes, radii),
  following the Figma file. Sprites exported from Figma are in `Assets/UI/Figma` (`UiAssets`).
- `SafeArea` fits the design to phones and tablets; landscape only.
- **Settings** (`MenuPages.Settings`, on the main menu and the pause menu), saved in PlayerPrefs by `GameSettings`:
  volume, graphics, colour grade, look speed, swim view, touch controls (auto/on/off), joystick side. Rows must fit
  the card without scrolling — `UiButtonTests` clicks every button at its centre.
- Touch: `TouchControls` (joystick measured from its centre, one finger owns it) and the action buttons; `InputHelper`
  merges touch and keys.

Tests: `UiButtonTests`, `GameUiTests`, `MapChoiceFlowTests`.
