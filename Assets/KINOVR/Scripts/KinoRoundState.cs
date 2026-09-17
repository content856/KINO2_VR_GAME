using System;
using System.Collections.Generic;

namespace KinoVR
{
    public enum KinoRoundPhase { Idle, Main, Settling, Bonus, Complete }
    // Scene-independent draw quota, catch memory and final-bonus rules.
    public sealed class KinoRoundState
    {
        public const int BonusMultiplier = 3;
        public const int NormalBallLimit = 20;
        readonly HashSet<int> numbers = new HashSet<int>();
        readonly List<int> caughtNumbers = new List<int>(NormalBallLimit);
        double startedAt, drawDeadline;
        public bool IsRunning { get; private set; }
        public KinoRoundPhase Phase { get; private set; }
        public int Score { get; private set; }
        public float PhaseDuration { get; private set; }
        public int CatchCount { get; private set; }
        public int NormalCatchCount { get; private set; }
        public int NormalMissCount { get; private set; }
        public int ResolvedNormalCount => NormalCatchCount + NormalMissCount;
        public int NormalLaunchCount { get; private set; }
        public bool BonusLaunched { get; private set; }
        public int BonusCaughtNumber { get; private set; }
        public bool BonusMissed { get; private set; }
        public int UniqueCount => caughtNumbers.Count;
        public float RemainingSeconds { get; private set; }
        public double NextNormalLaunchAt => startedAt + NormalLaunchCount * (double)PhaseDuration / NormalBallLimit;
        public bool HasCaught(int number) => numbers.Contains(number);
        public int GetCaughtNumber(int index) => caughtNumbers[index];
        public void Begin(float duration, double now)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            numbers.Clear(); caughtNumbers.Clear();
            CatchCount = NormalCatchCount = NormalMissCount = NormalLaunchCount = Score = BonusCaughtNumber = 0;
            BonusLaunched = BonusMissed = false;
            PhaseDuration = RemainingSeconds = duration;
            startedAt = now;
            drawDeadline = now + duration;
            Phase = KinoRoundPhase.Main;
            IsRunning = true;
        }
        public void Tick(double now)
        {
            if (!IsRunning) return;
            if (Phase == KinoRoundPhase.Main && now >= drawDeadline) Phase = KinoRoundPhase.Settling;
            RemainingSeconds = Phase == KinoRoundPhase.Main ? (float)Math.Max(0, drawDeadline - now) : 0;
        }
        public bool IsNormalLaunchDue(double now) => IsRunning &&
            (Phase == KinoRoundPhase.Main || Phase == KinoRoundPhase.Settling) &&
            NormalLaunchCount < NormalBallLimit && now >= NextNormalLaunchAt;
        public bool TryRegisterNormalLaunch(double now)
        {
            Tick(now);
            if (!IsNormalLaunchDue(now)) return false;
            NormalLaunchCount++;
            return true;
        }
        // Called only once every ordinary ball has been caught or missed.
        public bool TryBeginBonus()
        {
            if (!IsRunning || Phase != KinoRoundPhase.Settling || ResolvedNormalCount != NormalBallLimit) return false;
            if (UniqueCount == 0) { Stop(); return false; }
            Phase = KinoRoundPhase.Bonus;
            return true;
        }
        public bool TryRegisterBonusLaunch()
        {
            if (!IsRunning || Phase != KinoRoundPhase.Bonus || BonusLaunched || UniqueCount == 0) return false;
            BonusLaunched = true;
            return true;
        }
        public bool TryCatch(int number, double now, bool isKinoBonus = false)
        {
            Tick(now);
            if (!IsRunning || number < 1 || number > 80) return false;
            if (isKinoBonus)
            {
                if (Phase != KinoRoundPhase.Bonus || !BonusLaunched || BonusMissed || BonusCaughtNumber != 0 || !numbers.Contains(number)) return false;
                BonusCaughtNumber = number;
                Score += BonusMultiplier;
            }
            else
            {
                if ((Phase != KinoRoundPhase.Main && Phase != KinoRoundPhase.Settling) || ResolvedNormalCount >= NormalLaunchCount) return false;
                NormalCatchCount++;
                Score++;
                if (numbers.Add(number)) caughtNumbers.Add(number);
            }
            CatchCount++;
            return true;
        }
        public bool TryMiss(bool isKinoBonus)
        {
            if (!IsRunning) return false;
            if (isKinoBonus)
            {
                if (Phase != KinoRoundPhase.Bonus || !BonusLaunched || BonusMissed || BonusCaughtNumber != 0) return false;
                BonusMissed = true;
            }
            else
            {
                if ((Phase != KinoRoundPhase.Main && Phase != KinoRoundPhase.Settling) || ResolvedNormalCount >= NormalLaunchCount) return false;
                NormalMissCount++;
            }
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
