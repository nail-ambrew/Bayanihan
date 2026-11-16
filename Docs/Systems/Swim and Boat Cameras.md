# Swim and Boat Cameras

## The boat
A Cinemachine orbit behind the boat, swung by the mouse or a drag and recentred after `boatLookRecenterDelay`. Its
**Deoccluder** collides with the Default layer only and ignores the `Boat` tag, so walls pull it in but floating debris
(layer 8, Debris) does not — see [[Flood Debris]].

## The swimmer (`PlayerRescuer`)
Two views, chosen with **V** or **SWIM VIEW** in settings (`GameSettings.SwimView`, first person by default).

- **The head is always on** — in both views and at the helm (its shadow, the view from the boat).
- **First person**: the head is measured once from the mesh (`MeasureHead`); the eye sits just outside that sphere in the
  direction of the look (`firstPersonClearance`), so no part of the head can be in view. The body turns to face the look;
  the stick strafes and backs up. Looking down stops at `firstPersonMaxDown` (25°) — past that the vest and arms fill
  the view.
- **Third person**: behind and above at `cameraDistance` (scroll to zoom), pulled in front of walls by a sphere cast that
  ignores anything with a rigidbody and the Debris layer, and kept 0.6 m above the water.

Tests: `SwimViewTests`, `RescuerViewTests`/`MabuhayRescuerViewTests` (shared `RescuerViewChecks`), `CameraDebrisTests`.
