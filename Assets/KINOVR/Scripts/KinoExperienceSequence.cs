using System;
using System.Collections.Generic;

namespace KinoVR
{
    // The screens around the game, in an editable order. Gameplay, Second Chance and the
    // optional Boost wave always sit between the two lists.
    // Append values: scenes and prefabs serialize these numeric IDs.
    public enum KinoSequenceStep { AllwynLogo, KinoLogo, Ready, Safety, Welcome, KinoSplash, Score, RemoveHeadset,
        CampaignSelection, RegionSelection, StoreSelection, PlayerSelection }

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
            { KinoSequenceStep.AllwynLogo, KinoSequenceStep.KinoLogo, KinoSequenceStep.CampaignSelection, KinoSequenceStep.RegionSelection,
              KinoSequenceStep.StoreSelection, KinoSequenceStep.PlayerSelection, KinoSequenceStep.Ready, KinoSequenceStep.Safety, KinoSequenceStep.Welcome, KinoSequenceStep.KinoSplash };
        public static readonly KinoSequenceStep[] PostGameSteps = { KinoSequenceStep.Score, KinoSequenceStep.RemoveHeadset };

        public static List<KinoSequenceEntry> DefaultPreGame() => new List<KinoSequenceEntry>
        {
            new KinoSequenceEntry(KinoSequenceStep.AllwynLogo, false),
            new KinoSequenceEntry(KinoSequenceStep.KinoLogo),
            new KinoSequenceEntry(KinoSequenceStep.CampaignSelection),
            new KinoSequenceEntry(KinoSequenceStep.RegionSelection),
            new KinoSequenceEntry(KinoSequenceStep.StoreSelection),
            new KinoSequenceEntry(KinoSequenceStep.PlayerSelection),
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
        public static bool IsBackend(KinoSequenceStep step) => step >= KinoSequenceStep.CampaignSelection && step <= KinoSequenceStep.PlayerSelection;
        public static void ArrangeLeaderboard(List<KinoSequenceEntry> list)
        {
            list.RemoveAll(e => e != null && IsBackend(e.step));
            int ready = list.FindIndex(e => e != null && e.step == KinoSequenceStep.Ready);
            if (ready < 0) ready = 0;
            list.InsertRange(ready, new[] { new KinoSequenceEntry(KinoSequenceStep.CampaignSelection),
                new KinoSequenceEntry(KinoSequenceStep.RegionSelection), new KinoSequenceEntry(KinoSequenceStep.StoreSelection),
                new KinoSequenceEntry(KinoSequenceStep.PlayerSelection) });
        }

        public static KinoExperienceStage StageFor(KinoSequenceStep step)
        {
            switch (step)
            {
                case KinoSequenceStep.CampaignSelection: return KinoExperienceStage.CampaignSelection;
                case KinoSequenceStep.RegionSelection: return KinoExperienceStage.RegionSelection;
                case KinoSequenceStep.StoreSelection: return KinoExperienceStage.StoreSelection;
                case KinoSequenceStep.PlayerSelection: return KinoExperienceStage.PlayerSelection;
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
                case KinoSequenceStep.CampaignSelection: return "Campaign (automatic when only one)";
                case KinoSequenceStep.RegionSelection: return "Region selection";
                case KinoSequenceStep.StoreSelection: return "Store selection";
                case KinoSequenceStep.PlayerSelection: return "Player selection (visitor loop starts here)";
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
