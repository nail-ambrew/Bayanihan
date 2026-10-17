# Multiplayer setup

## Target architecture

- Four players share one rescue session: Boat Driver, Rescuer, Medic, and Operator.
- Unity Multiplayer Services sessions use Relay for host/client connectivity. The host is the session authority for the capstone prototype.
- All four players use one Vivox audio-only mission channel. Push-to-talk starts muted and sends audio only while held.
- Medic and Operator remain distinct role seats and use separate evacuation-center stations/rooms. They do not need separate voice channels for the first playable version.
- Multiplayer Play Mode is the intended local development workflow; test two clients first, then all four.

## Added to the project

- `RescueSessionService` initializes Unity Services and anonymous Authentication, creates a four-player Relay session, joins by code, leaves cleanly, and reports status to a menu UI.
- It creates the Netcode `NetworkManager` and Unity Transport at runtime if the scene does not already contain one.
- `RescueVoiceService` logs players into Vivox and joins a session-scoped team channel. It exposes push-to-talk and microphone mute methods.
- `CrewRole` defines the four role seats and identifies the two evacuation-center roles.
- The project manifest lists Netcode for GameObjects 2.13.3, Multiplayer Services 2.4.0, Vivox 16.12.1, and Multiplayer Play Mode 2.0.2. NGO 2.13.3 is a pre-release version for this Unity editor line.

## Unity Dashboard and Editor setup

The project has the Capstone Cloud project ID and organization ID saved, but Unity Editor Services currently reports the Cloud link as disabled. In the Unity Editor, open **Edit > Project Settings > Services**, choose **Use an existing Unity project ID**, and link the project to the **IT8_Capstone / Capstone** project. Then enable Authentication's anonymous sign-in provider, Relay, and Vivox for the Development environment. Do not put Vivox signing keys or service credentials in client code.

The dashboard setup guide currently stops at the project-link step. Once the Editor link is active, finish its Vivox configuration step for the same Development environment. Relay session creation itself is performed by the game through `WithRelayNetwork()`.

## Gameplay integration still required

The current project gameplay is single-player: `PlayerRescuer` combines boat driving and rescue movement, and mission/resident systems are locally spawned. The files added here establish session, voice, and role naming, but do not yet assign unique roles or make gameplay objects authoritative or replicated. Before calling the game co-op complete, the team must:

1. Add a join/create-code screen that calls `RescueSessionService` and displays its status and join code.
2. Create and register a network player prefab, assign one role per connected player on the host, and reject already-claimed roles.
3. Split driver and rescuer controls, while keeping Medic and Operator on their evacuation-center stations.
4. Convert boat movement, boarding, victim pickup/drop-off, victim treatment, CCTV/alerts, and mission completion to server-authoritative network state.
5. Wire the UI's push-to-talk key/button to `RescueVoiceService.SetPushToTalkHeld` and add a visible mic mute indicator.
6. Test two local players, then four local players with Multiplayer Play Mode; finally test Relay and microphone permission on separate machines/builds.

Scenes were not edited as part of this setup.
