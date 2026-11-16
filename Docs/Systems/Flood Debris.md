# Flood Debris

- `FloatingDebris.Spawn(kind, at)` makes a piece: a rigidbody on `Buoyancy`, dragged by `FloodCurrent`. Ramming one
  faster than `strikeSpeed` is a hull strike, unless it is soft (leaves, grass, creeper, a bucket).
- Kinds: wreckage (Plank, Crate, Drum, Log), plant scraps (Branch, BananaLeaf, GrassMat, WeedRaft — from the foliage
  pack), household scraps (Bucket, RoofSheet — from the prop packs). `DebrisField` scatters a mix; the level builder
  places the fields away from the hall.
- `DebrisBarrier` is the jam across A. Mapalad Street: solid to the boat, not to the swimmer, never hides anyone.
- **Layer 8, Debris.** Every piece and the jam are on it (`FloatingDebris.MoveToDebrisLayer`), and
  `CamerasLookPastDebris()` clears it from any camera deoccluder, so bumping debris never pulls the camera in.

Tests: `HazardTests`, `CameraDebrisTests`, `MabuhayPropTests` (bucket and sheet float).
