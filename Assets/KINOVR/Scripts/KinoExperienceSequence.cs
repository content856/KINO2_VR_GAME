using System;
using System.Collections.Generic;

namespace KinoVR
{
    // The screens around the game, in an editable order. Gameplay, Second Chance and the
    // optional Boost wave always sit between the two lists.
    public enum KinoSequenceStep { AllwynLogo, KinoLogo, Ready, Safety, Welcome, KinoSplash, Score, RemoveHeadset }

    [Serializable]
    public sealed class KinoSequenceEntry
    {
        public KinoSequenceStep step;
        public bool enabled = true;
        public KinoSequenceEntry() { }
        public KinoSequenceEntry(KinoSequenceStep step, bool enabled = true) { this.step = step; this.enabled = enabled; }
    }

    public static class KinoSequence
    {
        public static readonly KinoSequenceStep[] PreGameSteps =
            { KinoSequenceStep.AllwynLogo, KinoSequenceStep.KinoLogo, KinoSequenceStep.Ready, KinoSequenceStep.Safety, KinoSequenceStep.Welcome, KinoSequenceStep.KinoSplash };
        public static readonly KinoSequenceStep[] PostGameSteps = { KinoSequenceStep.Score, KinoSequenceStep.RemoveHeadset };

        public static List<KinoSequenceEntry> DefaultPreGame() => new List<KinoSequenceEntry>
        {
            new KinoSequenceEntry(KinoSequenceStep.AllwynLogo, false),
            new KinoSequenceEntry(KinoSequenceStep.KinoLogo),
            new KinoSequenceEntry(KinoSequenceStep.Ready),
            new KinoSequenceEntry(KinoSequenceStep.Safety),
            new KinoSequenceEntry(KinoSequenceStep.Welcome),
            new KinoSequenceEntry(KinoSequenceStep.KinoSplash),
        };

        public static List<KinoSequenceEntry> DefaultPostGame() => new List<KinoSequenceEntry>
        {
            new KinoSequenceEntry(KinoSequenceStep.Score),
            new KinoSequenceEntry(KinoSequenceStep.RemoveHeadset),
        };

        public static bool IsPreGame(KinoSequenceStep step) => Array.IndexOf(PreGameSteps, step) >= 0;

        public static KinoExperienceStage StageFor(KinoSequenceStep step)
        {
            switch (step)
            {
                case KinoSequenceStep.AllwynLogo: return KinoExperienceStage.AllwynLogo;
                case KinoSequenceStep.KinoLogo: return KinoExperienceStage.KinoLogo;
                case KinoSequenceStep.Ready: return KinoExperienceStage.Standby;
                case KinoSequenceStep.Safety: return KinoExperienceStage.Safety;
                case KinoSequenceStep.Welcome: return KinoExperienceStage.Introduction;
                case KinoSequenceStep.KinoSplash: return KinoExperienceStage.KinoSplash;
                case KinoSequenceStep.Score: return KinoExperienceStage.Finale;
                default: return KinoExperienceStage.Closing;
            }
        }

        public static string Label(KinoSequenceStep step)
        {
            switch (step)
            {
                case KinoSequenceStep.AllwynLogo: return "Allwyn logo";
                case KinoSequenceStep.KinoLogo: return "KINO + VR EXPERIENCE logos";
                case KinoSequenceStep.Ready: return "Ready screen (waits for ΞΕΚΙΝΑ)";
                case KinoSequenceStep.Safety: return "Safety message";
                case KinoSequenceStep.Welcome: return "Welcome to the world of KINO";
                case KinoSequenceStep.KinoSplash: return "KINO splash into the game";
                case KinoSequenceStep.Score: return "Final score";
                default: return "Remove headset (outro)";
            }
        }

        /// <summary>Each step exactly once, in the right list; missing steps are appended disabled.</summary>
        public static bool Sanitize(List<KinoSequenceEntry> list, KinoSequenceStep[] allowed)
        {
            bool changed = false;
            var seen = new HashSet<KinoSequenceStep>();
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null || Array.IndexOf(allowed, list[i].step) < 0) { list.RemoveAt(i); changed = true; }
            for (int i = 0; i < list.Count; i++)
                if (!seen.Add(list[i].step)) { list.RemoveAt(i--); changed = true; }
            foreach (var step in allowed)
                if (!seen.Contains(step)) { list.Add(new KinoSequenceEntry(step, false)); changed = true; }
            return changed;
        }
    }
}
