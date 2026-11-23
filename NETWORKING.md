# Online multiplayer

## Connection and authority

- Project version in ProjectSettings/ProjectVersion.txt is "m_EditorVersion: 6000.4.1f1".
- Packages/manifest.json pins "com.unity.netcode.gameobjects": "2.14.0" and "com.unity.services.multiplayer": "2.5.0". Packages/packages-lock.json resolves Unity Transport to "version": "2.7.4".
- The game uses Unity Multiplayer Services for private room codes and Relay over secure WebSockets. The player who creates the room is the NGO host and runs the authoritative simulation. ProjectSettings/ProjectSettings.asset enables "runInBackground: 1".
- MultiplayerSessionConnection creates the NetworkManager and registers the MultiplayerRoom prefab on both host and client before connecting. The host assigns all slots in MultiplayerRoom, writes the roster, and publishes the locked starting roster. The battle scene is loaded locally on each peer after it receives the starting roster and its own slot. The match transport waits for the battle scene to be ready before enabling controls.
- The existing host-side simulation and battle snapshots remain in use. The room supports up to eight players, four per team, and the host can start with at least one player per team. Disconnecting the host ends the match.

## Playing online

1. Open **ONLINE** from the title screen or the hangar.
2. The host selects **CREATE ROOM** and shares the in-game code. Friends enter it and select **JOIN** in their own game.
3. The room shows the assigned team and your tank. Select **SELECT TANK** to use the hangar or tech tree, then select **CONFIRM TANK** to return to the room. Each player can pick their own vehicle. The host can select **START MATCH** once both teams have a player and everyone has finished choosing.
4. The host can select **RETURN TO ROOM** from the match menu or result screen. All players return to the same room with its existing code, teams and selected vehicles. The lobby accepts new players again after the match ends. **LEAVE ROOM** disconnects a guest, and **CLOSE ROOM** ends the host's session.

Unity Cloud multiplayer and anonymous authentication must be enabled for the game's configured project. The WebGL player can be hosted as a static HTTPS page. It opens directly to the game's title screen.

## Verification

- Unity Editor host and a Multiplayer Play Mode clone created and joined through the game's UI using a real Relay connection. The host reported 29 ms RTT for the guest. Both peers reported a spawned room, two members, one member per team, and secure WebSockets. The host reported client ID 0 and slot 0. The guest reported client ID 1 and slot 4. Only the host had a Start Match button.
- Both peers loaded Desert Battle with two active vehicles and the same locked roster mask of 17. After guest movement, both read the guest tank at (117.85, 24.47) and 3 active smoke clouds. During a separate firing check, both observed up to 2 projectiles. When the host applied a tank loss through the existing combat code, both reported `RoundOver` with the guest tank destroyed.
- Leaving as host returned both peers to Hangar and closed both network connections. The guest saw the disconnect notice. The host then successfully entered a single-player battle.
- The Editor CLI's scenario shortcut did not resolve the multiplayer type in this Unity version. The test used the same Editor's Play Mode scenario manager and the clone's local Pipeline endpoint. The first clone opened the existing Desert Battle Editor scene and was moved to Hangar before testing. A simulated keyboard attempt did not drive the unfocused clone; gameplay verification used the game's existing client control sender.
- Full eight-player sessions, prop destruction and secondary weapons, and adverse network conditions were not exercised in this pass.

- The release WebGL build completed with zero errors. Its HTML, loader, data, framework and WebAssembly files all returned HTTP 200 when served from a local static server. The release ZIP contains the decoded player files for hosts that use standard static content headers. Browser multiplayer was not rerun against this ZIP.

## Guest rendering and input

The host sends battle snapshots at 20 Hz and maintains authority over combat and vehicle positions. Guests buffer other vehicles' snapshots for interpolation. Their own movement and turret aim respond locally each frame and reconcile with the host in the background. Continuous aim is sent at up to 30 Hz with a slower heartbeat while input is unchanged, and changes to held buttons are sent immediately. Smoke, repair and ammunition selection are queued separately and sent reliably.

Both roles use the same dust, muzzle flash, recoil, sound and projectile appearance code. Guests animate projectiles between snapshots and reuse their renderers. Online track marks recycle small sprites so that each tread does not upload the full ground texture. The lobby replicates vehicle choices in the roster and locks them when the match starts.

In a two-process Relay check, the guest drove forward by about 6 units in 1.6 seconds. Before the changes, 16 of 82 samples showed motion. Afterward, 72 of 78 samples showed motion. The host and guest both reported one deployed smoke screen and the selected ammunition slot. The online pause menu showed Leave Match, and leaving returned the guest to the hangar.

The WebGL vehicle-selection build was also tested in two Chrome tabs over Relay. The guest joined with an M1A2 SEP V2, switched to a Leopard 2A8 from the room, and confirmed it. The host waited for that confirmation before starting. Both battle HUDs showed the selected vehicles, and the guest drove and fired the Leopard with the projectile trail visible.

## Battle presentation and radar

The host simulates projectile contacts and damage. `BattleEffect` carries confirmed armor impacts, splash damage, wreck effects and environmental explosions to guests. The host and guests both call `PresentBattleEffect` and `RenderBattleFrame` locally, including for the X-ray replay, camera and HUD. Position prediction and remote interpolation remain separate from simulation and rendering.

`UpdateRadarContacts` checks each living player's viewport and line of sight to enemies. The snapshots carry one contact mask per observer. During play, tank rendering, tank movement effects, and the minimap use the same team visibility. The local view uses current line of sight, and living teammates share their confirmed observations. Props and smoke can block spotting. Allied vehicles remain visible.

`MultiplayerRoom.Round` identifies gameplay messages, including scene readiness, and increments on each deployment. A round ends by clearing its expected roster mask. The lobby can start the next round after all remaining members have returned to the hangar.

Verified over Relay with an Editor host and two Chrome WebGL guests. The enemy was absent from radar at spawn. With the host's target outside its viewport, an Alpha teammate's observation made the contact visible to the host, and a smoke screen hid it again. An authoritative cannon hit displayed the cutaway and knockout on the Bravo guest. All three players kept their room and selections into round two, and a guest left and rejoined with the same code after the host returned to the lobby. Solo deployment and firing were also checked in Chrome.
