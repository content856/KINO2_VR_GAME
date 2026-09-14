# Timed numbered-ball prototype

Open `Assets/KinoRotunda/Scenes/KinoRotunda.unity` and press Play. This is now the enabled build scene. The original `KINO_VR_Game` test scene is retained.

The `KINO Gameplay` prefab instance contains the round, launcher, live board and a copy of the existing VR rig with both hand catchers. On headset it uses the VR head position. In the Editor without an active XR device it uses a stationary desktop camera for previewing the room and launches.

## Current rules

- The round starts automatically and lasts **75 seconds**. Change **Round Duration** on `KINO Gameplay` to adjust it.
- The time bar fills from empty to full as the round elapses; the numeric timer still counts down. Restarting empties the bar.
- Each launched ball receives a random integer from **1 through 80**, displayed in black directly on its gold surface, facing the player.
- Every successful catch adds one to **BALLS CAUGHT** and highlights its position in the board's 10-column, 8-row grid.
- Numbers may repeat. A repeat counts as another caught ball and pulses the same board position.
- There is no 20-ball limit, selected ticket, payout table or special-ball scoring in this round.
- At zero, spawning and catching stop, flying balls are removed, and the board retains the result with **ROUND COMPLETE**.
- Missed balls expire after six seconds. `BeginRound()` resets the timer, board, counter and flying balls for another round.

The live board keeps the existing KINO artwork and frame. Native UI replaces the static sample numbers and time fields with the live grid and countdown. The original screen material, texture, architecture and lighting are preserved.

The catchable balls use a smooth horizontal oval mesh (1.68:1, matching the board markers), with uniform transforms so the text stays unstretched. A horizontal capsule collider follows their width and height. The black number remains centred toward the viewer at oblique angles, without a white badge. The physical balls and board tokens share a yellow lacquer palette and softbox/rim highlights through `KinoGoldSurface.hlsl`.

The live display adds slowly travelling cyan light around the number frame, soft background glows and a drifting blue ribbon, inspired by the reference video. Animation runs in the shared UI shader without additional scene lights or per-frame material allocation. The four `Board*` background materials expose **Neon motion speed** and **Neon brightness**; set motion speed to zero for a still background.

## Assets and checks

- `Prefabs/KinoTimedGameplay.prefab`: connected gameplay setup for the Rotunda scene.
- `Prefabs/NumberedBall.prefab`: dedicated numbered ball; the original normal-ball prefab is retained.
- `KinoRoundState`: catch counting and deadline rules without scene dependencies.
- `KinoRoundController`: round lifecycle and connections to launcher, board and score.
- `KinoNumberBoard`: number highlighting, repeat pulse and header UI.

`Tools > KINO VR > 2 - Validate numbered round` checks scene references and round rules, including repeat numbers, catches after 20, exact deadline rejection and restart. `Tools > KINO VR > 3 - Capture board and ball previews` writes preview images under `Artifacts/KinoGameplay`.

`Tools > KINO VR > 4 - Oval balls and animated display` applies the visual upgrade to an existing setup while preserving gameplay settings. Menu 5 captures board motion phases, a player view, single/double-digit balls and an oblique view under `Artifacts/KinoGameplay/VisualRefresh`.

The editor helper also accepts explicit `inspect`, `setup`, `pack`, `preview`, `validate`, `test`, `visuals`, `visual-preview` and `motion-preview` requests in `Temp/KinoGameplay.request`. Importing the helper does not set up or rebuild a scene automatically. `motion-preview` also captures 150 frames at 15 fps for a ten-second animation preview. The `test` request runs a short Play Mode integration check, including a physical hand-trigger catch at the oval's elongated end, and exits Play Mode when finished. Actual hand tracking, comfort and performance still require headset testing.
