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
        public const float FadeSeconds = 1.5f;
        // Includes the fade back in, followed by two seconds of fully visible artwork.
        public const float RevealSeconds = FadeSeconds + 2;
        public const float SecondChanceInterval = 3;
        readonly HashSet<int> numbers = new HashSet<int>();
        readonly List<int> caughtNumbers = new List<int>(NormalBallLimit);
        double startedAt, drawDeadline;
        double boostDeadline;
        float boostInterval;
        readonly bool[] glowSlots = new bool[NormalBallLimit];
        readonly List<double> mysteryOffsets = new List<double>(6);
        double multiplierDeadline;
        public float MainDuration { get; private set; }
        public int GlowTargetCount { get; private set; }
        public int GlowLaunchCount { get; private set; }
        public int GlowCatchCount { get; private set; }
        public int GlowMissCount { get; private set; }
        public int MysteryTargetCount => mysteryOffsets.Count;
        public int MysteryLaunchCount { get; private set; }
        public int MysteryCatchCount { get; private set; }
        public int MysteryMissCount { get; private set; }
        public int MainScore { get; private set; }
        public int ActiveMultiplier { get; private set; } = 1;
        public int LastMysteryMultiplier { get; private set; }
        public float MultiplierRemainingSeconds { get; private set; }
        public float MysteryEffectSeconds => MainDuration / NormalBallLimit * 2;
        public bool MainDrawResolved => ResolvedNormalCount == NormalBallLimit &&
            MysteryCatchCount + MysteryMissCount == MysteryTargetCount;
        public bool NextNormalIsGlow => NormalLaunchCount < NormalBallLimit && glowSlots[NormalLaunchCount];
        public bool IsGlowSlot(int index) => index >= 0 && index < NormalBallLimit && glowSlots[index];
        public double NextMysteryLaunchAt => MysteryLaunchCount < MysteryTargetCount ?
            startedAt + mysteryOffsets[MysteryLaunchCount] : double.PositiveInfinity;
        public bool IsMysteryLaunchDue(double now) => IsRunning &&
            (Phase == KinoRoundPhase.Main || Phase == KinoRoundPhase.Settling) && now >= NextMysteryLaunchAt;
        public bool ShouldLaunchMystery(double now) => IsMysteryLaunchDue(now) &&
            (!IsNormalLaunchDue(now) || NextMysteryLaunchAt < NextNormalLaunchAt);
        public bool TryRegisterMysteryLaunch(double now)
        {
            Tick(now);
            if (!IsMysteryLaunchDue(now)) return false;
            MysteryLaunchCount++;
            return true;
        }
        public bool TryCatchMystery(int multiplier, double now)
        {
            Tick(now);
            if (!IsRunning || (Phase != KinoRoundPhase.Main && Phase != KinoRoundPhase.Settling) ||
                multiplier < 2 || multiplier > 4 || MysteryCatchCount + MysteryMissCount >= MysteryLaunchCount) return false;
            MysteryCatchCount++; CatchCount++;
            LastMysteryMultiplier = multiplier;
            ActiveMultiplier = multiplier;
            multiplierDeadline = now + MysteryEffectSeconds;
            MultiplierRemainingSeconds = MysteryEffectSeconds;
            return true;
        }
        public bool TryMissMystery()
        {
            if (!IsRunning || (Phase != KinoRoundPhase.Main && Phase != KinoRoundPhase.Settling) ||
                MysteryCatchCount + MysteryMissCount >= MysteryLaunchCount) return false;
            MysteryMissCount++;
            return true;
        }
        void ClearMultiplier() { ActiveMultiplier = 1; MultiplierRemainingSeconds = 0; multiplierDeadline = 0; }
        void PlanSpecials(int? seed)
        {
            Array.Clear(glowSlots, 0, glowSlots.Length);
            mysteryOffsets.Clear();
            GlowTargetCount = 0;
            if (!seed.HasValue) return; // Explicit legacy mode for standalone/older rule tests.
            var random = new Random(seed.Value);
            GlowTargetCount = random.Next(2, 7);
            var slots = new List<int>();
            for (int i = 0; i < NormalBallLimit; i++) slots.Add(i);
            for (int i = 0; i < GlowTargetCount; i++)
            {
                int pick = random.Next(slots.Count);
                glowSlots[slots[pick]] = true;
                slots.RemoveAt(pick);
            }
            slots.Clear();
            for (int i = 0; i < NormalBallLimit - 1; i++) slots.Add(i);
            int mysteries = random.Next(2, 7);
            for (int i = 0; i < mysteries; i++)
            {
                int pick = random.Next(slots.Count);
                mysteryOffsets.Add((slots[pick] + .35 + random.NextDouble() * .3) * MainDuration / NormalBallLimit);
                slots.RemoveAt(pick);
            }
            mysteryOffsets.Sort();
        }
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
        public double NextNormalLaunchAt => startedAt + NormalLaunchCount * (double)MainDuration / NormalBallLimit;
        public bool HasCaught(int number) => numbers.Contains(number);
        public int GetCaughtNumber(int index) => caughtNumbers[index];
        public void Begin(float duration, double now, int? specialSeed = null)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            numbers.Clear(); caughtNumbers.Clear();
            CatchCount = NormalCatchCount = NormalMissCount = NormalLaunchCount = Score = BonusCaughtNumber = 0;
            BonusLaunched = BonusMissed = false;
            SecondChanceLaunchCount = SecondChanceCatchCount = SecondChanceMissCount = 0;
            BoostLaunchCount = BoostCatchCount = BoostMissCount = 0;
            GlowLaunchCount = GlowCatchCount = GlowMissCount = 0;
            MysteryLaunchCount = MysteryCatchCount = MysteryMissCount = LastMysteryMultiplier = MainScore = 0;
            ClearMultiplier();
            MainDuration = duration;
            PlanSpecials(specialSeed);
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
            if ((Phase != KinoRoundPhase.Main && Phase != KinoRoundPhase.Settling) || now >= multiplierDeadline) ClearMultiplier();
            else MultiplierRemainingSeconds = (float)Math.Max(0, multiplierDeadline - now);
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
            if (NextNormalIsGlow) GlowLaunchCount++;
            NormalLaunchCount++;
            return true;
        }
        void EnterPhase(KinoRoundPhase phase, double now)
        {
            Phase = phase; PhaseStartedAt = now;
            if (phase != KinoRoundPhase.Main && phase != KinoRoundPhase.Settling) ClearMultiplier();
        }
        public bool TryBeginSecondChanceTransition(double now)
        {
            if (!IsRunning || !((Phase == KinoRoundPhase.Settling && MainDrawResolved && UniqueCount == 0) ||
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
            if (!IsRunning || Phase != KinoRoundPhase.Settling || !MainDrawResolved || UniqueCount == 0) return false;
            Phase = KinoRoundPhase.Bonus;
            ClearMultiplier();
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
        public bool TryCatch(int number, double now, bool isKinoBonus = false, bool isSecondChance = false, bool isBoost = false, bool isGlow = false)
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
                if (!HasOutstandingNumbered(isGlow)) return false;
                NormalCatchCount++;
                if (isGlow) GlowCatchCount++;
                int points = (isGlow ? 2 : 1) * ActiveMultiplier;
                Score += points; MainScore += points;
                if (numbers.Add(number)) caughtNumbers.Add(number);
            }
            CatchCount++;
            return true;
        }
        public bool TryMiss(bool isKinoBonus, bool isSecondChance = false, bool isBoost = false, bool isGlow = false)
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
                if (!HasOutstandingNumbered(isGlow)) return false;
                NormalMissCount++;
                if (isGlow) GlowMissCount++;
            }
            return true;
        }
        bool HasOutstandingNumbered(bool glow) => glow ? GlowCatchCount + GlowMissCount < GlowLaunchCount :
            ResolvedNormalCount - GlowCatchCount - GlowMissCount < NormalLaunchCount - GlowLaunchCount;
        public void Stop()
        {
            IsRunning = false;
            Phase = KinoRoundPhase.Complete;
            RemainingSeconds = 0;
            ClearMultiplier();
        }
    }
}
