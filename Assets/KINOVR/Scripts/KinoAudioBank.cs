using System;
using UnityEngine;

namespace KinoVR
{
    public enum KinoSound
    {
        RoundStart, TubeRise, TubeExit, BallWhoosh, CatchNormal, CatchGreen, CatchBonus,
        CatchBoost, BallMiss, BonusReveal, SecondChanceOut, SecondChanceReveal,
        BoostStart, BoostEnd, RoundComplete, Restart, Countdown, Bird
    }

    [CreateAssetMenu(menuName = "KINO VR/Audio bank")]
    public sealed class KinoAudioBank : ScriptableObject
    {
        [Serializable]
        public sealed class Cue
        {
            public KinoSound sound;
            public AudioClip[] variants;
            [Range(0, 1)] public float volume = .7f;
            [Range(0, .15f)] public float pitchVariation = .025f;
            public bool spatial = true;
            [Range(0, 256)] public int priority = 128;
            [Min(0)] public float minimumInterval = .025f;
        }

        [Tooltip("Replace a clip here to change the sound without editing gameplay code.")]
        public Cue[] cues;
        public AudioClip chillMusic;
        public AudioClip boostMusic;
        public AudioClip outdoorAmbience;
        public AudioClip machineLoop;
    }
}
