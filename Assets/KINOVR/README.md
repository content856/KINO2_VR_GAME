# Timed numbered-ball prototype

Open `Assets/KinoRotunda/Scenes/KinoRotunda.unity` and press Play. This is now the enabled build scene. The original `KINO_VR_Game` test scene is retained.

The `KINO Gameplay` prefab instance contains the round, launcher, live board and a copy of the existing VR rig with both hand catchers. On headset it uses the VR head position. In the Editor without an active XR device it uses a stationary desktop camera for previewing the room and launches.

## Current rules

- The round starts automatically and lasts **75 seconds**. Change **Round Duration** on `KINO Gameplay` to adjust it.
- Each launched ball receives a random integer from **1 through 80**, displayed on a badge facing the player.
- Every successful catch adds one to **BALLS CAUGHT** and highlights its position in the board's 10-column, 8-row grid.
- Numbers may repeat. A repeat counts as another caught ball and pulses the same board position.
- There is no 20-ball limit, selected ticket, payout table or special-ball scoring in this round.
- At zero, spawning and catching stop, flying balls are removed, and the board retains the result with **ROUND COMPLETE**.
- Missed balls expire after six seconds. `BeginRound()` resets the timer, board, counter and flying balls for another round.

The live board keeps the existing KINO artwork and frame. Native UI replaces the static sample numbers and time fields with the live grid and countdown. The original screen material, texture, architecture and lighting are preserved.

## Assets and checks

- `Prefabs/KinoTimedGameplay.prefab`: connected gameplay setup for the Rotunda scene.
- `Prefabs/NumberedBall.prefab`: dedicated numbered ball; the original normal-ball prefab is retained.
- `KinoRoundState`: catch counting and deadline rules without scene dependencies.
- `KinoRoundController`: round lifecycle and connections to launcher, board and score.
- `KinoNumberBoard`: number highlighting, repeat pulse and header UI.

`Tools > KINO VR > 2 - Validate numbered round` checks scene references and round rules, including repeat numbers, catches after 20, exact deadline rejection and restart. `Tools > KINO VR > 3 - Capture board and ball previews` writes preview images under `Artifacts/KinoGameplay`.

The editor helper also accepts explicit `inspect`, `setup`, `pack`, `preview`, `validate` and `test` requests in `Temp/KinoGameplay.request`. Importing the helper does not set up or rebuild a scene automatically. The `test` request runs a short Play Mode integration check and exits Play Mode when finished. Actual hand tracking, comfort and performance still require headset testing.
