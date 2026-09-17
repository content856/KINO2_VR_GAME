using System;
using System.Collections.Generic;

namespace KinoVR
{
    public enum KinoRoundPhase { Idle, Main, Settling, Bonus, Complete, BoardHold, FadeOut, SecondChanceReveal, SecondChance, Boost, BoostSettling }
    // Scene-independent draw quota, catch memory and final-bonus rules.
    public sealed class KinoRoundState
    {
        public const int BonusMultiplier = 3;
        public const int NormalBallLimit = 20;
        public const int SecondChanceBallLimit = 3;
        public const float BoardHoldSeconds = 3;
        public const float FadeSeconds = 1;
        public const float RevealSeconds = 3;
        public const float SecondChanceInterval = 3;
        readonly HashSet<int> numbers = new HashSet<int>();
        readonly List<int> caughtNumbers = new List<int>(NormalBallLimit);
        double startedAt, drawDeadline;
        double boostDeadline;
        float boostInterval;
        public int BoostLaunchCount { get; private set; }
        public int BoostCatchCount { get; private set; }
        public int BoostMissCount { get; private set; }
        public int ResolvedBoostCount => BoostCatchCount + BoostMissCount;
        public double NextBoostLaunchAt => PhaseStartedAt + BoostLaunchCount * (double)boostInterval;
        public double PhaseStartedAt { get; private set; }
        public int SecondChanceLaunchCount { get; private set; }
        public int SecondChanceCatchCount { get; private set; }
        public int SecondChanceMissCount { get; private set; }
        public int ResolvedSecondChanceCount => SecondChanceCatchCount + SecondChanceMissCount;
        public double NextSecondChanceLaunchAt => PhaseStartedAt + SecondChanceLaunchCount * SecondChanceInterval;
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
            SecondChanceLaunchCount = SecondChanceCatchCount = SecondChanceMissCount = 0;
            BoostLaunchCount = BoostCatchCount = BoostMissCount = 0;
            PhaseStartedAt = now;
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
            // Keep each transition observable for at least one rendered frame, even
            // when a frame stalls. No balls can launch behind the blackout/title.
            if (Phase == KinoRoundPhase.BoardHold && now - PhaseStartedAt >= BoardHoldSeconds)
                EnterPhase(KinoRoundPhase.FadeOut, now);
            else if (Phase == KinoRoundPhase.FadeOut && now - PhaseStartedAt >= FadeSeconds)
                EnterPhase(KinoRoundPhase.SecondChanceReveal, now);
            else if (Phase == KinoRoundPhase.SecondChanceReveal && now - PhaseStartedAt >= RevealSeconds)
                EnterPhase(KinoRoundPhase.SecondChance, now);
            if (Phase == KinoRoundPhase.Boost && now >= boostDeadline) Phase = KinoRoundPhase.BoostSettling;
            RemainingSeconds = Phase == KinoRoundPhase.Main ? (float)Math.Max(0, drawDeadline - now) :
                Phase == KinoRoundPhase.Boost ? (float)Math.Max(0, boostDeadline - now) : 0;
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
        void EnterPhase(KinoRoundPhase phase, double now) { Phase = phase; PhaseStartedAt = now; }
        public bool TryBeginSecondChanceTransition(double now)
        {
            if (!IsRunning || !((Phase == KinoRoundPhase.Settling && ResolvedNormalCount == NormalBallLimit && UniqueCount == 0) ||
                (Phase == KinoRoundPhase.Bonus && BonusLaunched && (BonusMissed || BonusCaughtNumber != 0)))) return false;
            EnterPhase(KinoRoundPhase.BoardHold, now);
            return true;
        }
        public bool IsSecondChanceLaunchDue(double now) => IsRunning && Phase == KinoRoundPhase.SecondChance &&
            SecondChanceLaunchCount < SecondChanceBallLimit && now >= NextSecondChanceLaunchAt;
        public bool TryRegisterSecondChanceLaunch(double now)
        {
            if (!IsSecondChanceLaunchDue(now)) return false;
            SecondChanceLaunchCount++;
            return true;
        }
        // The single red bonus follows the ordinary draw, before Second Chance.
        public bool TryBeginBonus()
        {
            if (!IsRunning || Phase != KinoRoundPhase.Settling || ResolvedNormalCount != NormalBallLimit || UniqueCount == 0) return false;
            Phase = KinoRoundPhase.Bonus;
            return true;
        }
        public bool TryBeginShowcaseBoost(double now, float duration, float interval)
        {
            if (!IsRunning || Phase != KinoRoundPhase.SecondChance || ResolvedSecondChanceCount != SecondChanceBallLimit || UniqueCount == 0) return false;
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0 ||
                float.IsNaN(interval) || float.IsInfinity(interval) || interval <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
            EnterPhase(KinoRoundPhase.Boost, now);
            PhaseDuration = RemainingSeconds = duration;
            boostDeadline = now + duration;
            boostInterval = interval;
            return true;
        }
        public bool IsBoostLaunchDue(double now) => IsRunning && Phase == KinoRoundPhase.Boost &&
            UniqueCount > 0 && now < boostDeadline && now >= NextBoostLaunchAt;
        public bool TryRegisterBoostLaunch(double now)
        {
            Tick(now);
            if (!IsBoostLaunchDue(now)) return false;
            BoostLaunchCount++;
            return true;
        }
        public bool TryRegisterBonusLaunch()
        {
            if (!IsRunning || Phase != KinoRoundPhase.Bonus || BonusLaunched || UniqueCount == 0) return false;
            BonusLaunched = true;
            return true;
        }
        public bool TryCatch(int number, double now, bool isKinoBonus = false, bool isSecondChance = false, bool isBoost = false)
        {
            Tick(now);
            if (!IsRunning || number < 1 || number > 80) return false;
            if (isBoost)
            {
                if ((Phase != KinoRoundPhase.Boost && Phase != KinoRoundPhase.BoostSettling) ||
                    ResolvedBoostCount >= BoostLaunchCount || !numbers.Contains(number)) return false;
                BoostCatchCount++;
                Score += BonusMultiplier;
            }
            else if (isKinoBonus)
            {
                if (Phase != KinoRoundPhase.Bonus || !BonusLaunched || BonusMissed || BonusCaughtNumber != 0 || !numbers.Contains(number)) return false;
                BonusCaughtNumber = number;
                Score += BonusMultiplier;
            }
            else if (isSecondChance)
            {
                if (Phase != KinoRoundPhase.SecondChance || ResolvedSecondChanceCount >= SecondChanceLaunchCount) return false;
                SecondChanceCatchCount++;
                Score += BonusMultiplier;
                if (numbers.Add(number)) caughtNumbers.Add(number);
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
        public bool TryMiss(bool isKinoBonus, bool isSecondChance = false, bool isBoost = false)
        {
            if (!IsRunning) return false;
            if (isBoost)
            {
                if ((Phase != KinoRoundPhase.Boost && Phase != KinoRoundPhase.BoostSettling) || ResolvedBoostCount >= BoostLaunchCount) return false;
                BoostMissCount++;
            }
            else if (isKinoBonus)
            {
                if (Phase != KinoRoundPhase.Bonus || !BonusLaunched || BonusMissed || BonusCaughtNumber != 0) return false;
                BonusMissed = true;
            }
            else if (isSecondChance)
            {
                if (Phase != KinoRoundPhase.SecondChance || ResolvedSecondChanceCount >= SecondChanceLaunchCount) return false;
                SecondChanceMissCount++;
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
