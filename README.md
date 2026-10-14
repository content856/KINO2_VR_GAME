# KINO2_VR_GAME

The Quest leaderboard integration is configured in **Tools → KINO VR → Experience Sequence**.
The gameplay prefab has **Connect tablet / leaderboard** enabled and uses
`https://kinovr.digitaltribes.gr/api/v1`.

On first pairing, enter the eight-character code from **Admin → Devices → TestYannis → Pairing code**
in the headset. The Quest keeps its pairing in Android Keystore encrypted app storage. The Unity
Editor deliberately keeps credentials only in memory, so a new Editor/domain session needs pairing
again; use a separate admin device for Editor testing instead of replacing a headset's pairing.
Admin and documentation passwords are never included in the application.

At each app launch, choose campaign (automatic when only one is available), region and store.
The visitor loop begins at **Player selection**, followed by Ready and the existing game sequence.
Only registrations eligible in the selected store's open event appear. The visitor chooses their
nickname; Ready shows that nickname and offers **Αλλαγή παίκτη**. Removing the headset releases
an unfinished visitor but retains the selected store. Store settings are accessible from the player
picker. Turning off the leaderboard connection in Experience Sequence enables local rehearsal.

Final results are journaled before upload, with one stable submission ID. A new game cannot start
until the previous result is accepted or a definite rejection is acknowledged. Network/429/5xx
errors retry with backoff and Retry-After, including while the headset is removed (while the app is
running). The server still requires an active session and open event; it does not accept late scores.
Receipts and rejected results remain under `Application.persistentDataPath/KinoBackend`.
Refresh tokens rotate once; an interrupted rotation requires a new pairing code rather than unsafe
reuse of the old refresh token.

Validation menus:

- **Tools → KINO VR → Backend → Validate API authentication rules** checks token, URL and retry rules.
- **Tools → KINO VR → Backend → Test visitor and score lifecycle** exercises the coordinator against
  an in-memory API in an isolated scene. It never contacts the live service. Results are written to
  `Artifacts/KinoBackend/lifecycle-tests.txt`.
- Existing Experience tests use offline rehearsal. Real device acceptance still needs a Quest,
  tablet registration and the Board page to verify hands, pairing persistence and score arrival.
