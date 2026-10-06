using System;
using UnityEngine;

namespace KinoVR
{
    // Playback of imported clips only. Sound synthesis/baking lives outside Assets.
    [DefaultExecutionOrder(-200), DisallowMultipleComponent]
    public sealed class KinoAudioController : MonoBehaviour
    {
        public KinoRoundController round;
        public KinoAudioBank bank;
        [Header("Mix")]
        [Range(0, 1)] public float musicVolume = .25f;
        [Range(0, 1)] public float effectsVolume = .8f;
        [Range(0, 1)] public float ambienceVolume = .3f;
        [Range(0, 1)] public float announcementVolume = .8f;
        [Min(.1f)] public float musicCrossfadeSeconds = 1.25f;
        public bool countdownEnabled = true;
        [HideInInspector, Range(0, 1)] public float sessionVolume = 1;
        [Header("Fixed sources (created in the prefab, reused during play)")]
        public AudioSource chillMusic;
        public AudioSource boostMusic;
        public AudioSource outdoorAmbience;
        public AudioSource machine;
        public AudioSource announcement;
        public AudioSource[] voices;

        KinoAudioBank.Cue[] cues;
        double[] lastPlayed;
        int[] lastVariant;
        Transform[] following;
        float[] voiceGains;
        bool[] birdVoices;
        double[] voiceStarted;
        readonly System.Random variations = new System.Random(7419);
        KinoRoundPhase phase;
        bool hasPhase, initialized, suspended;
        float boostBlend, duck = 1, idleBlend = 1;
        double nextBirdAt, duckUntil, finishAt = -1;
        int countdownSecond = -1;

#if UNITY_EDITOR
        // Editor validation can observe accepted playback without changing game randomness.
        public event Action<KinoSound> Played;
#endif

        void Awake()
        {
            if (!bank || voices == null || voices.Length == 0) { enabled = false; return; }
            int count = Enum.GetValues(typeof(KinoSound)).Length;
            cues = new KinoAudioBank.Cue[count];
            lastPlayed = new double[count];
            lastVariant = new int[count];
            for (int i = 0; i < count; i++) { lastPlayed[i] = double.NegativeInfinity; lastVariant[i] = -1; }
            if (bank.cues != null)
                foreach (var cue in bank.cues)
                    if (cue != null && (int)cue.sound >= 0 && (int)cue.sound < count) cues[(int)cue.sound] = cue;
            following = new Transform[voices.Length];
            voiceGains = new float[voices.Length];
            birdVoices = new bool[voices.Length];
            voiceStarted = new double[voices.Length];
            SetLoop(chillMusic, bank.chillMusic);
            SetLoop(boostMusic, bank.boostMusic);
            SetLoop(outdoorAmbience, bank.outdoorAmbience);
            SetLoop(machine, bank.machineLoop);
            initialized = true;
        }

        static void SetLoop(AudioSource source, AudioClip clip)
        {
            if (!source) return;
            source.playOnAwake = false;
            source.loop = true;
            source.clip = clip;
            source.volume = 0;
        }

        void OnEnable()
        {
            if (!initialized) return;
            ResumeLoops();
            hasPhase = false;
        }

        void Start()
        {
            // The lottery belongs to the environment scene, outside the gameplay prefab.
            foreach (var chamber in FindObjectsByType<KinoAirChamber>(FindObjectsSortMode.None))
                if (chamber.kind == KinoAirChamber.ChamberKind.Lottery)
                {
                    if (machine) machine.transform.position = chamber.transform.position + Vector3.up * .5f;
                    break;
                }
            if (round) Present(round.State);
        }

        void ResumeLoops()
        {
            suspended = false;
            StartIfStopped(chillMusic);
            StartIfStopped(outdoorAmbience);
            StartIfStopped(machine);
            nextBirdAt = Time.unscaledTimeAsDouble + 3 + variations.NextDouble() * 4;
        }

        static void StartIfStopped(AudioSource source)
        {
            if (source && source.clip && !source.isPlaying) source.Play();
        }

        public void BeginRound()
        {
            if (!initialized || !isActiveAndEnabled) return;
            ClearVoices();
            for (int i = 0; i < lastPlayed.Length; i++) lastPlayed[i] = double.NegativeInfinity;
            finishAt = -1;
            duckUntil = 0;
            hasPhase = false;
            countdownSecond = -1;
            ResumeLoops();
            if (round) Present(round.State);
        }

        public void BeginSessionAudio()
        {
            if (!initialized || !isActiveAndEnabled) return;
            finishAt = -1;
            phase = KinoRoundPhase.Idle;
            hasPhase = false;
            ResumeLoops();
        }

        public void Present(KinoRoundState state)
        {
            if (!initialized || suspended || !isActiveAndEnabled) return;
            if (!hasPhase || state.Phase != phase)
            {
                var previous = phase;
                phase = state.Phase;
                hasPhase = true;
                countdownSecond = -1;
                switch (phase)
                {
                    case KinoRoundPhase.Main:
                        Play(KinoSound.RoundStart, machine ? machine.transform.position : transform.position);
                        break;
                    case KinoRoundPhase.Bonus: Play(KinoSound.BonusReveal, transform.position); break;
                    case KinoRoundPhase.FadeOut: Play(KinoSound.SecondChanceOut, transform.position); break;
                    case KinoRoundPhase.SecondChanceReveal: Play(KinoSound.SecondChanceReveal, transform.position); break;
                    case KinoRoundPhase.BoostIntro:
                    case KinoRoundPhase.Boost:
                        if (previous == KinoRoundPhase.BoostIntro) break;
                        if (boostMusic && boostMusic.clip) { boostMusic.time = 0; boostMusic.Play(); }
                        Play(KinoSound.BoostStart, transform.position);
                        break;
                    case KinoRoundPhase.Complete:
                        if (previous == KinoRoundPhase.Boost || previous == KinoRoundPhase.BoostSettling)
                        {
                            Play(KinoSound.BoostEnd, transform.position);
                            finishAt = Time.unscaledTimeAsDouble + .55;
                        }
                        else Play(KinoSound.RoundComplete, transform.position);
                        nextBirdAt = Time.unscaledTimeAsDouble + 2.5;
                        break;
                }
            }
            if (countdownEnabled && (phase == KinoRoundPhase.Main || phase == KinoRoundPhase.Boost))
            {
                int second = Mathf.CeilToInt(state.RemainingSeconds);
                if (second >= 1 && second <= 5 && second != countdownSecond)
                {
                    countdownSecond = second;
                    Play(KinoSound.Countdown, transform.position);
                }
            }
        }

        public void PlayCatch(Catchable.BallType type, Vector3 position)
        {
            var sound = type == Catchable.BallType.KinoBonus || type == Catchable.BallType.MoreWins ? KinoSound.CatchBonus :
                type == Catchable.BallType.SecondChance ? KinoSound.CatchGreen :
                type == Catchable.BallType.KinoBoost || type == Catchable.BallType.Mystery ? KinoSound.CatchBoost : KinoSound.CatchNormal;
            Play(sound, position);
        }

        public void Play(KinoSound sound, Vector3 position, Transform follow = null, float duration = 0)
        {
            if (!initialized || suspended || !isActiveAndEnabled) return;
            int key = (int)sound;
            if (key < 0 || key >= cues.Length) return;
            var cue = cues[key];
            if (cue == null || cue.variants == null || cue.variants.Length == 0) return;
            double now = Time.unscaledTimeAsDouble;
            if (now - lastPlayed[key] < cue.minimumInterval) return;
            int slot = -1;
            for (int i = 0; i < voices.Length; i++)
                if (voices[i] && !voices[i].isPlaying) { slot = i; break; }
            if (slot < 0)
            {
                // Air and birds can never steal an important catch/phase cue.
                for (int i = 0; i < voices.Length; i++)
                    if (voices[i] && voices[i].priority > cue.priority &&
                        (slot < 0 || voices[i].priority > voices[slot].priority ||
                         (voices[i].priority == voices[slot].priority && voiceStarted[i] < voiceStarted[slot]))) slot = i;
                if (slot < 0) return;
            }
            int variant = variations.Next(cue.variants.Length);
            if (cue.variants.Length > 1 && variant == lastVariant[key]) variant = (variant + 1) % cue.variants.Length;
            var clip = cue.variants[variant];
            if (!clip) return;
            var source = voices[slot];
            source.Stop();
            source.transform.position = position;
            source.clip = clip;
            source.loop = false;
            source.spatialBlend = cue.spatial ? 1 : 0;
            source.priority = cue.priority;
            source.pitch = duration > 0 ? Mathf.Clamp(clip.length / duration, .3f, 3) :
                1 + ((float)variations.NextDouble() * 2 - 1) * cue.pitchVariation;
            following[slot] = follow;
            voiceGains[slot] = cue.volume;
            birdVoices[slot] = sound == KinoSound.Bird;
            voiceStarted[slot] = now;
            source.volume = sessionVolume * cue.volume * (birdVoices[slot] ? ambienceVolume : effectsVolume);
            source.Play();
            lastVariant[key] = variant;
            lastPlayed[key] = now;
            if (sound == KinoSound.CatchBonus || sound == KinoSound.BonusReveal ||
                sound == KinoSound.SecondChanceReveal || sound == KinoSound.RoundComplete)
                duckUntil = Math.Max(duckUntil, now + .65);
#if UNITY_EDITOR
            Played?.Invoke(sound);
#endif
        }

        public void StopFollowing(Transform target)
        {
            if (!initialized) return;
            for (int i = 0; i < voices.Length; i++)
                if (following[i] == target)
                {
                    if (voices[i]) voices[i].Stop();
                    following[i] = null;
                }
        }

        void Update()
        {
            if (!initialized || suspended) return;
            float dt = Time.unscaledDeltaTime;
            double now = Time.unscaledTimeAsDouble;
            bool boosting = phase == KinoRoundPhase.BoostIntro || phase == KinoRoundPhase.Boost || phase == KinoRoundPhase.BoostSettling;
            bool idle = !round || !round.IsRunning;
            boostBlend = Mathf.MoveTowards(boostBlend, boosting ? 1 : 0, dt / Mathf.Max(.1f, musicCrossfadeSeconds));
            bool speech = announcement && announcement.isPlaying;
            duck = Mathf.MoveTowards(duck, speech ? .28f : now < duckUntil ? .55f : 1, dt * (speech ? 6 : 2));
            idleBlend = Mathf.MoveTowards(idleBlend, idle ? 1 : 0, dt * .8f);
            if (chillMusic) chillMusic.volume = sessionVolume * musicVolume * (1 - boostBlend) * duck;
            if (boostMusic)
            {
                boostMusic.volume = sessionVolume * musicVolume * boostBlend * duck;
                if (!boosting && boostBlend == 0 && boostMusic.isPlaying) boostMusic.Stop();
            }
            if (outdoorAmbience) outdoorAmbience.volume = sessionVolume * ambienceVolume * Mathf.Lerp(.25f, 1, idleBlend);
            if (machine) machine.volume = sessionVolume * ambienceVolume * Mathf.Lerp(.8f, .15f, idleBlend);
            if (announcement) announcement.volume = sessionVolume * announcementVolume;
            if (finishAt >= 0 && now >= finishAt)
            {
                finishAt = -1;
                Play(KinoSound.RoundComplete, transform.position);
            }
            if (idle && now >= nextBirdAt)
            {
                var view = round && round.playerView ? round.playerView.View : transform;
                float angle = (float)variations.NextDouble() * Mathf.PI * 2;
                Vector3 origin = view ? view.position : transform.position;
                Play(KinoSound.Bird, origin + new Vector3(Mathf.Sin(angle) * 9, 4, Mathf.Cos(angle) * 9));
                nextBirdAt = now + 5 + variations.NextDouble() * 7;
            }
        }

        void LateUpdate()
        {
            if (!initialized || suspended) return;
            for (int i = 0; i < voices.Length; i++)
            {
                var source = voices[i];
                if (!source || !source.isPlaying) { following[i] = null; continue; }
                if (following[i])
                {
                    if (!following[i].gameObject.activeInHierarchy) { source.Stop(); following[i] = null; continue; }
                    source.transform.position = following[i].position;
                }
                source.volume = sessionVolume * voiceGains[i] * (birdVoices[i] ? ambienceVolume : effectsVolume);
            }
        }

        void ClearVoices()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i]) voices[i].Stop();
                following[i] = null;
            }
        }

        public void StopAll()
        {
            if (!initialized) return;
            suspended = true;
            finishAt = -1;
            ClearVoices();
            if (chillMusic) chillMusic.Stop();
            if (boostMusic) boostMusic.Stop();
            if (outdoorAmbience) outdoorAmbience.Stop();
            if (machine) machine.Stop();
            if (announcement) announcement.Stop();
            boostBlend = 0;
            phase = KinoRoundPhase.Idle;
            hasPhase = false;
        }

        void OnDisable() => StopAll();
    }
}
