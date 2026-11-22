# Online multiplayer

The Sites lobby creates six-character rooms, assigns up to eight players to two teams of four, and locks the roster when the host starts. The lobby passes each participant's slot to the WebGL client and distributes the Unity session code after the host creates it. The Unity Multiplayer Services session uses Relay over WSS; the host runs the authoritative simulation and broadcasts battle state. If the host disconnects, the match ends.

The match currently uses the Desert Battle map with fixed vehicles. The host waits for the roster's clients to load before beginning. Gameplay data includes movement, aiming, firing, projectiles, smoke, destructible props, vehicle condition and the result. Unity's project is linked to its existing Cloud project; Multiplayer Services and anonymous authentication must remain enabled there.

## Verification

- Lobby API checks exercised eight concurrent slots, rejection of a ninth player, balanced teams, host-only start and session-code publishing, roster locking, and host re-election.
- Two Unity Editor processes created and joined an actual Relay/WSS session; both entered the battle, and movement, projectiles and smoke were observed on host and client. A separate local network test checked victory and destroyed-vehicle replication.
- The final WebGL build succeeded. In the local lobby, eight browser tabs joined the same room, all seven guests connected, and both teams appeared on the host's battlefield radar. The browser automation could not deliver keyboard input into the embedded game frame, so manual browser control was not verified by that test.

The production lobby is deployed independently from the Unity WebGL build under the repository's GitHub Pages `multiplayer/` directory. GitHub Pages does not set Unity's Brotli `Content-Encoding` headers; the published build uses decompressed assets and adjusts the generated `index.html` references accordingly.
