# KINO baked audio — first pass

All 38 new WAVs are original sounds rendered offline by `bake_audio.py`. They include stylized synthesized birds and an original instrumental Boost loop, not ElevenLabs output or licensed library recordings. The supplied Chillout source and existing Boost announcement remain separate. The Python tool is outside `Assets` and is not included in the game build. Unity never generates audio sample data, including during startup.

## Listen and replace

`Artifacts/KinoGameplay/Audio/KinoAudio_FirstPass.wav` is a listening reel; `ListeningGuide.md` gives timestamps. The reel presents clips individually; the game applies the quieter mix below.

Open **Tools > KINO VR > Audio > 3 - Select sound bank (replace clips)**. Replace any variant in `Assets/KINOVR/Audio/KinoAudioBank.asset` with a new imported clip. Keep short positional effects mono. Alternatively replace a WAV in `Assets/KINOVR/Audio/Baked` while preserving its `.meta` file to retain all references. No script edits are needed. The setup command preserves the existing bank and its authored choices.

Open **Audio > 4 - Select mix controls** to adjust Music, Effects, Ambience and Announcement on `KINO Gameplay / KinoAudioController`. Defaults: 0.25, 0.8, 0.3 and 0.8. Each bank cue also has its own level, pitch variation, spatial switch and priority. `Countdown Enabled` can disable the restrained final-five-seconds ticks.

| Cue / files | Connected event |
|---|---|
| RoundStart | Begin/restart; gentle pressure rise before the first ball clears its tube |
| TubeRise_01–03 | Actual tube ascent, duration adjusted to the current ascent time |
| TubeExit_01–03 | The ball physically clears the outlet |
| BallWhoosh_01–03 | 55% through the approach flight; source follows the ball |
| CatchNormal_01–03 | Accepted gold catch |
| CatchGreen_01–03 | Accepted Second Chance catch |
| CatchBonus | Accepted red KINO Bonus catch |
| CatchBoost_01–03 | Accepted Boost catch |
| BallMiss_01–03 | Physical floor contact; silent timeout/cleanup |
| BonusReveal | Entering the red Bonus phase |
| SecondChanceOut / SecondChanceReveal | Start of blackout / appearance of the Second Chance title |
| BoostStart / BoostEnd | Entering Boost / finishing the Boost sequence |
| RoundComplete | Final result; delayed slightly after BoostEnd |
| Restart | Accepted hand/button press, after the restart guard |
| Countdown | Once per second during the last five seconds of Main or Boost |
| Bird_01–04 | Irregular distant calls while idle/finished |
| OutdoorAmbienceLoop | Courtyard breeze; reduced during play, increased between rounds |
| MachineLoop | Quiet lottery motor/contacts; louder during gameplay |
| BoostMusicLoop | Dedicated 120 BPM instrumental; crossfade from/to Chillout |

The existing `KinoBoost.wav` announcement still belongs to the Boost presentation. Music ducks while it plays and briefly for major rewards/reveals. Chillout keeps its playback position through ordinary restarts and phase changes; Boost music starts on entry. The ambience/motor loops and all following/one-shot audio stop if the round/controller is disabled.

## Runtime budget and import

There are 16 pre-created, reusable effect voices, 2 music sources, 2 ambient/motor sources and the existing voice source: 21 sources total. Low-priority air/birds cannot steal catch or transition voices. Recycling a ball explicitly releases its following sound before its transform can be reused. Audio has a separate random generator, so variants never change the number draw or trajectories.

Short effects and the small mono motor loop use PCM / Decompress On Load with preload. Long stereo music/breeze loops use Vorbis streaming, including explicit Android settings. Actual mixer, streaming CPU and memory costs still need profiling on Quest; editor tests do not establish headset performance. There are no realtime synthesis callbacks, generated AudioClips, convolution reverbs or per-effect GameObject allocations.

## Rebuild and verify

Run `bake_audio.py` with Python and NumPy **only when deliberately regenerating the original first-pass files**; it overwrites those WAVs. It also creates the manifest and listening reel. Preserve replacement assets elsewhere if regenerating.

**Audio > 1 - Set up baked audio** imports assets and updates the prefab. **Audio > 2 - Validate baked audio** checks its bank, source counts and import modes. **Audio > 5 - Test playback and full sequence** checks a real tube flight, catch/miss guards, recycled sounds, gameplay randomness, disable/restart and all three existing round-sequence scenarios. Reports are in `Artifacts/KinoGameplay/Audio` and `Artifacts/KinoGameplay/SecondChance`.

Batch entry points: `KinoVR.Editor.KinoAudioSetup.BatchSetup` and `KinoVR.Editor.KinoAudioSetup.BatchTest`. The existing request-file bridge also accepts `audio` and `audio-test`.
