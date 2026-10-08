using System;

namespace KinoVR
{
    public enum KinoExperienceStage { Waiting, ModeSelection, Startup, Safety, Branding, Introduction, Gameplay, SecondChance, Boost, Finale, Closing, Complete, Standby, KinoSplash }

    // Session clock is independent of ball scoring and uses elapsed, unscaled time.
    public sealed class KinoExperienceState
    {
        public KinoExperienceStage Stage { get; private set; }
        public double EnteredAt { get; private set; }
        public bool ExternalConfirmation { get; private set; }
        public bool IncludeBoost { get; private set; }
        public bool HasSelectedMode { get; private set; }
        public void BeginBranding(double now) => Enter(KinoExperienceStage.Branding, now);
        public void SelectMode(double now) { ExternalConfirmation = IncludeBoost = HasSelectedMode = false; Enter(KinoExperienceStage.ModeSelection, now); }
        public void ChooseMode(double now, bool withBoost) { HasSelectedMode = true; IncludeBoost = withBoost; Standby(now); }
        public void Standby(double now) { ExternalConfirmation = false; Enter(KinoExperienceStage.Standby, now); }
        public void Begin(double now, bool withBoost = false) { ExternalConfirmation = false; HasSelectedMode = true; IncludeBoost = withBoost; Enter(KinoExperienceStage.Startup, now); }
        public void Suspend(double now) { ExternalConfirmation = false; Enter(KinoExperienceStage.Waiting, now); }
        public void Reset(double now) { ExternalConfirmation = IncludeBoost = HasSelectedMode = false; Enter(KinoExperienceStage.Waiting, now); }
        public bool ConfirmSafety()
        {
            if (Stage != KinoExperienceStage.Safety || ExternalConfirmation) return false;
            ExternalConfirmation = true;
            return true;
        }
        public bool Advance(double now, float startup, float safety, float branding, float introduction,
            float kinoSplash, float finale, float closing, bool requireConfirmation)
        {
            double elapsed = Math.Max(0, now - EnteredAt);
            switch (Stage)
            {
                case KinoExperienceStage.Startup: if (elapsed >= startup) return Enter(KinoExperienceStage.Safety, now); break;
                case KinoExperienceStage.Safety:
                    if (elapsed >= safety && (!requireConfirmation || ExternalConfirmation)) return Enter(KinoExperienceStage.Introduction, now);
                    break;
                case KinoExperienceStage.Branding: if (elapsed >= branding) return Enter(KinoExperienceStage.ModeSelection, now); break;
                case KinoExperienceStage.Introduction: if (elapsed >= introduction) return Enter(KinoExperienceStage.KinoSplash, now); break;
                case KinoExperienceStage.KinoSplash: if (elapsed >= kinoSplash) return Enter(KinoExperienceStage.Gameplay, now); break;
                case KinoExperienceStage.Finale: if (elapsed >= finale) return Enter(KinoExperienceStage.Closing, now); break;
                case KinoExperienceStage.Closing: if (elapsed >= closing) return Enter(KinoExperienceStage.Complete, now); break;
            }
            return false;
        }
        public bool BeginSecondChance(double now) => Stage == KinoExperienceStage.Gameplay && Enter(KinoExperienceStage.SecondChance, now);
        public bool BeginBoost(double now) => IncludeBoost && Stage == KinoExperienceStage.SecondChance && Enter(KinoExperienceStage.Boost, now);
        public bool Finish(double now) => (Stage == KinoExperienceStage.SecondChance || Stage == KinoExperienceStage.Boost) && Enter(KinoExperienceStage.Finale, now);
        bool Enter(KinoExperienceStage stage, double now) { Stage = stage; EnteredAt = now; return true; }
    }
}
