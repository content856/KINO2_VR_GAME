# Guided experience

Open `Assets/KinoRotunda/Scenes/KinoRotunda.unity` and press Play. Mounting the headset, or entering Desktop Editor Play, first shows **NORMAL / ΜΕ BOOST**. Touch either button with a tracked catching hand, or click it in the desktop Game view. The selection starts the complete sequence; no session starts or gameplay balls launch while the menu is waiting. `KinoExperienceController` owns the eight stages from the supplied presentation:

1. Headset startup (1.2 seconds).
2. Safety notice (12 seconds).
3. Allwyn, then KINO, on black: Allwyn for 3 seconds, black for 0.5 seconds, then KINO for 3 seconds. Each logo's 3-second duration includes a 0.65-second fade in and fade out.
4. Introduction on black with existing music (6 seconds); a fade conceals the switch to the room when gameplay begins.
5. Main game (60 seconds by default, clamped to 60–90).
6. Guaranteed Second Chance, including with zero ordinary catches. Its announcement uses a separate reading screen at 2.5 metres on a black 360° background before the green balls launch.
7. Final score (10 seconds).
8. Calm headset-removal message (5 seconds, with the last 0.5 seconds fading to black and silence), then automatic return to **NORMAL / ΜΕ BOOST** for the next visitor.

**NORMAL** goes from Second Chance directly to the final score. **ΜΕ BOOST** inserts the existing 25-second Boost wave after Second Chance and before the final score. Boost uses only numbers already caught in the normal draw or Second Chance, gives 3 points per catch, and preserves the accumulated score. If no numbers are eligible, the existing rule skips the Boost wave and proceeds to the finale.

Removing the headset cancels an unfinished session and clears live balls; mounting it again returns to mode selection. With the headset still mounted, the five-second closing message automatically returns to mode selection, resets the visible score and board, and waits for a new choice. A fresh session starts only after that choice. Completion remains observable for one frame so the close timestamp and `onSessionClosed` event are recorded once. Operator calls `BeginNormalSession()` and `BeginBoostSession()` start the corresponding full sequence from Waiting, Complete or ModeSelection; `ShowModeSelection()` opens the choice from Waiting or Complete. `BeginSession()` remains an alias for Normal. Player restart stays hidden during the guided experience. Hands, pool and room remain loaded.

## VR layout

Reading screens use a World Space canvas measuring **2.2 × 1.4 metres at 2.5 metres** by default, centered at the current eye height. `contentWidth` and `contentDistance` configure the width and distance; height follows the 1100:700 canvas ratio. Position is established on each stage change and stays fixed while the player looks around. At the defaults, the panel spans approximately 47.5° × 31.3° and the body text's capital height is approximately 1.07°.

Mode selection, safety, branding, introduction and the Second Chance announcement use an opaque black 360° enclosure. The final score uses 15% background transparency (85% black opacity) and the removal message uses 55% transparency (45% black opacity), while text stays at full opacity. The Inspector fields `finaleBackgroundTransparency` and `closingBackgroundTransparency` use percentages: 0 is solid black and 100 is fully transparent. `KinoBlackEnclosure` creates a closed upper ellipsoid with a **6-metre radius**, an apex at **world Y = 5 metres** and a flat floor at **Y = 0.003 metres**. Its center stays at **world X/Z = 0/0**, independently of the headset or gameplay parent. No enclosure geometry extends below the room floor. Looking sideways, behind, up or down therefore keeps the room hidden while the foreground screen stays at its reading position. The Second Chance announcement is a separate 2.2 × 1.4-metre World Space canvas at 2.5 metres, shown for the existing three-second reveal period.

The opaque background and every session/Second Chance fade share this geometry. The background draws after room objects and before the text and logo canvases; the fade draws over both. Session and round opacity channels combine by taking their maximum, so resetting one channel cannot cancel another active fade. The generated mesh has no colliders or shadows, and its shader supports stereo rendering. The old flat backdrops and camera-following blackout canvases are removed by setup.

The separate World Space selection canvas uses `modeViewOffset = (0, -0.18, 0.46)` metres relative to the head's horizontal heading and tilts to face the player. Its two buttons measure **0.22 × 0.11 metres**, with a **0.04-metre gap**, and remain fixed after placement. A 0.45-second activation guard prevents an incidental immediate selection.

These measurements come from `KinoExperienceController`, `KinoExperienceSetup` and the Greek font's cap-height metrics. [Meta's hand UI guidance](https://developers.meta.com/horizon/design/hands-ui-best-practices/) informed the touch placement; the reading distance is beyond [Meta's minimum distance for prolonged fixation](https://developers.meta.com/horizon/design/display/). The room board remains about 5.84 × 3.75 metres at 11.86 metres forward; its small status/multiplier labels need particular attention in headset readability testing. Geometry and source checks do not establish comfort or sharpness on a real Quest.

## Placeholder and external handoff

Safety copy is explicitly a prototype placeholder. Replace `safetyText`, `safetyVersion` and `placeholderSafety` in the Inspector. Timed display records exposure, not legal consent. `requireExternalSafetyConfirmation` optionally waits for the operator/tablet to call `ConfirmSafetyFromOperator()`, which cannot skip the minimum display time. No tablet service is connected.

`onSafetyDisplayed` and `onResultReady` emit session JSON. Receipts are written to `Application.persistentDataPath/KinoSessions/<sessionId>.json`: random session ID, timestamps, copy version, score and catch counts, without player identity. `includeBoost` records the selected mode, and `boostCatches` records catches in its optional wave. External confirmation has a separate timestamp and is never inferred from a timer. The result event fires once at the finale. Storage failures appear in `LastStorageError` and the Unity console. Registration, gifts and the top-50 leaderboard need a separate integration. No reward eligibility claims are shown.

## Mechanics and assets

The selected session mode controls `showcaseBoostAfterSecondChance`; Normal leaves it disabled and Με Boost enables it for that session. The existing red bonus and three green Second Chance balls remain. Second Chance tints room LEDs/marble blue while keeping the green ball palette; its black announcement screen conceals the room until the green-ball phase begins. More Wins and Mystery spawn/scoring definitions await the mechanics document or approved replacement rules. The experience uses the existing environment and music; no cinematic or voiceover was supplied.

Original PPTX/PDF/SVG files are in `SourceArt/Experience`. `Textures/Experience` contains transparent, proportion-preserving logo renders. `AllwynOnBlack.png` is derived from the supplied SVG: it keeps the geometry and cyan symbol, with a white wordmark for contrast on black. The original SVG and original-color `AllwynLogo.png` remain preserved. KINO follows Allwyn sequentially, never simultaneously during branding. `brandingSeconds`, `logoBlackSeconds` and `logoFadeSeconds` control the logo timing. A prebuilt Greek font atlas prevents missing glyphs and runtime font generation. Safety replacements must use characters covered by the atlas.

## Checks

Use **Tools > KINO VR > Experience > 1 - Apply eight-stage flow**, **2 - Validate flow**, and **3 - Test and capture sessions**. Reports/images go to `Artifacts/ExperienceFlow`. The experience checks cover both modes, enclosure geometry and black coverage in multiple viewing directions, and automatic return to mode selection. Round regression tests disable the session controller to exercise standalone showcase/restart behavior. Actual hand tracking, headset presence, stereo readability, audio and comfort still need Quest testing.
