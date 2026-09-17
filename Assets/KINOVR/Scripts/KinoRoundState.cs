using System;
using System.Collections.Generic;

namespace KinoVR
{
    public enum KinoRoundPhase { Idle, Main, Calm, Boost, Complete }
    // Scene-independent catch and deadline rules.
    public sealed class KinoRoundState
    {
        public const int BonusMultiplier = 3;
        readonly HashSet<int> numbers = new HashSet<int>();
        double mainDeadline, calmDeadline, deadline;
        float mainDuration, calmDuration, boostDuration;
        public bool IsRunning { get; private set; }
        public KinoRoundPhase Phase { get; private set; }
        public int Score { get; private set; }
        public int Multiplier => Phase == KinoRoundPhase.Boost ? 3 : 1;
        public float PhaseDuration => Phase == KinoRoundPhase.Boost ? boostDuration : Phase == KinoRoundPhase.Calm ? calmDuration : mainDuration;
        public int CatchCount { get; private set; }
        public int UniqueCount => numbers.Count;
        public float RemainingSeconds { get; private set; }
        public bool HasCaught(int number) => numbers.Contains(number);
        public void Begin(float duration, double now, float calmSeconds = 0, float boostSeconds = 0)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (!float.IsFinite(calmSeconds) || calmSeconds < 0 || !float.IsFinite(boostSeconds) || boostSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(boostSeconds));
            numbers.Clear();
            CatchCount = Score = 0;
            mainDuration = duration;
            calmDuration = boostSeconds > 0 ? calmSeconds : 0;
            boostDuration = boostSeconds;
            RemainingSeconds = duration;
            mainDeadline = now + mainDuration;
            calmDeadline = mainDeadline + calmDuration;
            deadline = calmDeadline + boostDuration;
            Phase = KinoRoundPhase.Main;
            IsRunning = true;
        }
        public void Tick(double now)
        {
            if (!IsRunning) return;
            if (now >= deadline) { Stop(); return; }
            // Absolute deadlines prevent a late frame from extending the bonus round.
            Phase = now >= calmDeadline ? KinoRoundPhase.Boost : now >= mainDeadline ? KinoRoundPhase.Calm : KinoRoundPhase.Main;
            double phaseEnd = Phase == KinoRoundPhase.Main ? mainDeadline : Phase == KinoRoundPhase.Calm ? calmDeadline : deadline;
            RemainingSeconds = (float)Math.Max(0, phaseEnd - now);
        }
        public bool TryCatch(int number, double now, bool isKinoBonus = false)
        {
            Tick(now);
            if (!IsRunning || number < 1 || number > 80) return false;
            CatchCount++;
            Score += Multiplier * (isKinoBonus ? BonusMultiplier : 1);
            numbers.Add(number);
            return true;
        }
        public void Stop()
        {
            IsRunning = false;
            Phase = KinoRoundPhase.Complete;
            RemainingSeconds = 0;
        }
    }
}
