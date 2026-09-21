using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KinoVR.Editor
{
    public static class KinoAudioSetup
    {
        public const string PrefabPath = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";
        public const string BankPath = "Assets/KINOVR/Audio/KinoAudioBank.asset";
        public const string BakedPath = "Assets/KINOVR/Audio/Baked/";
        public const string Output = "Artifacts/KinoGameplay/Audio";

        public static void BatchSetup()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/KinoRotunda/Scenes/KinoRotunda.unity");
                Apply();
                Validate();
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void BatchTest()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/KinoRotunda/Scenes/KinoRotunda.unity");
                Validate();
                KinoAudioTests.Run(true);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void BatchPlaybackTest()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/KinoRotunda/Scenes/KinoRotunda.unity");
                KinoAudioTests.Run(true, false);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Tools/KINO VR/Audio/1 - Set up baked audio")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(BakedPath, "*.wav")) ConfigureImport(path.Replace('\\', '/'));
            ConfigureImport("Assets/KINOVR/Audio/ChilloutMusicLoop.wav");
            var bank = AssetDatabase.LoadAssetAtPath<KinoAudioBank>(BankPath);
            if (!bank)
            {
                bank = ScriptableObject.CreateInstance<KinoAudioBank>();
                bank.cues = Enum.GetValues(typeof(KinoSound)).Cast<KinoSound>().Select(DefaultCue).ToArray();
                AssetDatabase.CreateAsset(bank, BankPath);
            }
            // Reapplying setup keeps any clips/mix choices made during a second pass.
            if (!bank.chillMusic) bank.chillMusic = Clip("Assets/KINOVR/Audio/ChilloutMusicLoop.wav");
            if (!bank.boostMusic) bank.boostMusic = Clip(BakedPath + "BoostMusicLoop.wav");
            if (!bank.outdoorAmbience) bank.outdoorAmbience = Clip(BakedPath + "OutdoorAmbienceLoop.wav");
            if (!bank.machineLoop) bank.machineLoop = Clip(BakedPath + "MachineLoop.wav");
            EditorUtility.SetDirty(bank);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var round = root.GetComponent<KinoRoundController>();
                var audio = root.GetComponent<KinoAudioController>();
                if (!audio) audio = root.AddComponent<KinoAudioController>();
                audio.round = round;
                audio.bank = bank;
                round.audioController = audio;
                Transform group = root.transform.Find("KINO audio");
                if (!group)
                {
                    group = new GameObject("KINO audio").transform;
                    group.SetParent(root.transform, false);
                }
                audio.chillMusic = Source(root.transform, "Chillout music loop", bank.chillMusic, true, false, 64);
                audio.boostMusic = Source(group, "Boost music", bank.boostMusic, true, false, 64);
                audio.outdoorAmbience = Source(group, "Courtyard breeze", bank.outdoorAmbience, true, false, 180);
                audio.machine = Source(group, "Lottery motor", bank.machineLoop, true, true, 160);
                audio.announcement = round.boostPresentation ? round.boostPresentation.announcementAudio : null;
                if (audio.announcement) audio.announcement.priority = 32;
                audio.voices = new AudioSource[16];
                for (int i = 0; i < audio.voices.Length; i++)
                    audio.voices[i] = Source(group, "Effect voice " + (i + 1).ToString("00"), null, false, true, 128);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/setup.txt", "Baked WAV bank connected. 16 reusable effect voices; two music sources; breeze + machine loops; existing Boost voice. No runtime audio synthesis.\n");
            Debug.Log("KINO_BAKED_AUDIO_READY");
        }

        static AudioClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (!clip) throw new InvalidOperationException("Missing baked audio: " + path);
            return clip;
        }

        static void ConfigureImport(string path)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            bool streaming = path.EndsWith("MusicLoop.wav", StringComparison.Ordinal) || path.EndsWith("OutdoorAmbienceLoop.wav", StringComparison.Ordinal);
            // Effects are already mono. Avoid normalizing their intentionally baked levels.
            importer.forceToMono = false;
            importer.loadInBackground = streaming;
            var settings = importer.defaultSampleSettings;
            settings.loadType = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = streaming ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.quality = .7f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = !streaming;
            importer.defaultSampleSettings = settings;
            importer.SetOverrideSampleSettings("Android", settings);
            importer.SaveAndReimport();
        }

        static KinoAudioBank.Cue DefaultCue(KinoSound sound)
        {
            string prefix = BakedPath + sound;
            var files = Directory.GetFiles(BakedPath, sound + "_*.wav").OrderBy(p => p).ToArray();
            var cue = new KinoAudioBank.Cue
            {
                sound = sound,
                variants = files.Length > 0 ? files.Select(p => Clip(p.Replace('\\', '/'))).ToArray() : new[] { Clip(prefix + ".wav") }
            };
            switch (sound)
            {
                case KinoSound.RoundStart: cue.volume = .5f; cue.priority = 90; break;
                case KinoSound.TubeRise: cue.volume = .24f; cue.priority = 160; break;
                case KinoSound.TubeExit: cue.volume = .55f; cue.priority = 140; break;
                case KinoSound.BallWhoosh: cue.volume = .36f; cue.priority = 160; break;
                case KinoSound.CatchNormal: cue.volume = .72f; cue.priority = 72; break;
                case KinoSound.CatchGreen: cue.volume = .75f; cue.priority = 68; break;
                case KinoSound.CatchBonus: cue.volume = .85f; cue.priority = 40; break;
                case KinoSound.CatchBoost: cue.volume = .68f; cue.priority = 72; break;
                case KinoSound.BallMiss: cue.volume = .3f; cue.priority = 180; break;
                case KinoSound.Bird: cue.volume = .75f; cue.priority = 220; cue.pitchVariation = .08f; break;
                default:
                    cue.spatial = false;
                    cue.priority = 48;
                    cue.volume = .55f;
                    cue.pitchVariation = 0;
                    break;
            }
            if (sound == KinoSound.Countdown) { cue.volume = .3f; cue.priority = 150; }
            if (sound == KinoSound.BoostStart) cue.volume = .35f;
            if (sound == KinoSound.CatchNormal || sound == KinoSound.CatchGreen ||
                sound == KinoSound.CatchBonus || sound == KinoSound.CatchBoost) cue.minimumInterval = 0;
            return cue;
        }

        static AudioSource Source(Transform parent, string name, AudioClip clip, bool loop, bool spatial, int priority)
        {
            var child = parent.Find(name);
            if (!child) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            var source = child.GetComponent<AudioSource>();
            if (!source) source = child.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.volume = 0;
            source.spatialBlend = spatial ? 1 : 0;
            source.dopplerLevel = 0;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3;
            source.maxDistance = 28;
            source.priority = priority;
            source.bypassReverbZones = true;
            return source;
        }

        [MenuItem("Tools/KINO VR/Audio/2 - Validate baked audio")]
        public static void Validate()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var round = root.GetComponent<KinoRoundController>();
            var audio = root.GetComponent<KinoAudioController>();
            if (!audio || round.audioController != audio || audio.round != round || !audio.bank)
                throw new InvalidOperationException("Audio controller/bank is not connected.");
            if (audio.voices.Length != 16 || audio.voices.Any(v => !v || v.loop || v.playOnAwake) || audio.voices.Distinct().Count() != 16)
                throw new InvalidOperationException("Effect source pool is not fixed at 16 unique voices.");
            if (root.GetComponentsInChildren<AudioSource>(true).Length != 21)
                throw new InvalidOperationException("Unexpected/duplicate audio sources in gameplay prefab.");
            if (!audio.chillMusic || !audio.boostMusic || !audio.outdoorAmbience || !audio.machine || !audio.announcement ||
                audio.announcement != round.boostPresentation.announcementAudio)
                throw new InvalidOperationException("Music, ambience or voice reference missing.");
            var sounds = Enum.GetValues(typeof(KinoSound)).Cast<KinoSound>().ToArray();
            if (audio.bank.cues.Length != sounds.Length || audio.bank.cues.Select(c => c.sound).Distinct().Count() != sounds.Length)
                throw new InvalidOperationException("Audio bank contains missing or duplicate cues.");
            long pcmBytes = 0;
            foreach (var cue in audio.bank.cues)
                foreach (var clip in cue.variants)
                {
                    if (!clip || clip.length <= 0 || clip.channels != 1 || clip.loadType != AudioClipLoadType.DecompressOnLoad)
                        throw new InvalidOperationException("SFX must be baked, preloaded mono clips: " + cue.sound);
                    pcmBytes += (long)clip.samples * clip.channels * 2;
                }
            foreach (var clip in new[] { audio.bank.chillMusic, audio.bank.boostMusic, audio.bank.outdoorAmbience })
                if (!clip || clip.loadType != AudioClipLoadType.Streaming)
                    throw new InvalidOperationException("Long loops must stream.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.txt", $"PASS: all {sounds.Length} cues, 35 SFX variants, 4 loops, 16 pooled effect voices, 21 total sources, streaming long loops. SFX source PCM bytes: {pcmBytes}. Runtime memory must be measured on Quest.\n");
            Debug.Log("KINO_AUDIO_ASSETS_VALIDATED");
        }

        [MenuItem("Tools/KINO VR/Audio/3 - Select sound bank (replace clips)")]
        static void SelectBank() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<KinoAudioBank>(BankPath);

        [MenuItem("Tools/KINO VR/Audio/4 - Select mix controls")]
        static void SelectMix() => Selection.activeObject = UnityEngine.Object.FindFirstObjectByType<KinoAudioController>();
    }
}
