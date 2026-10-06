using System;
using System.IO;
using UnityEngine;

namespace KinoVR.Editor
{
    public static class KinoMainSpecialTests
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        public static void ValidateRules()
        {
            var glowCounts = new bool[7]; var mysteryCounts = new bool[7];
            for (int seed = 0; seed < 256; seed++)
            {
                float duration = seed % 3 == 0 ? 45 : seed % 3 == 1 ? 60 : 90;
                var state = new KinoRoundState(); state.Begin(duration, 0, seed);
                Check(state.GlowTargetCount >= 2 && state.GlowTargetCount <= 6 && state.MysteryTargetCount >= 2 && state.MysteryTargetCount <= 6, "Special quota out of range.");
                glowCounts[state.GlowTargetCount] = mysteryCounts[state.MysteryTargetCount] = true;
                int expectedScore = 0, numbered = 0, mysteries = 0, glows = 0, multiplier = 1, previousGap = -1;
                double expiry = 0, previousTime = -1;
                while (state.NormalLaunchCount < 20 || state.MysteryLaunchCount < state.MysteryTargetCount)
                {
                    double next = Math.Min(state.NormalLaunchCount < 20 ? state.NextNormalLaunchAt : double.PositiveInfinity, state.NextMysteryLaunchAt);
                    Check(next > previousTime, "Main schedule not strictly ordered."); previousTime = next;
                    state.Tick(next);
                    if (next >= expiry) multiplier = 1;
                    if (state.ShouldLaunchMystery(next))
                    {
                        int gap = (int)(next / (duration / 20));
                        Check(gap > previousGap && gap < 19 && next > gap * duration / 20 && next < (gap + 1) * duration / 20, "Mystery not in a distinct internal gap.");
                        previousGap = gap;
                        Check(state.TryRegisterMysteryLaunch(next), "Mystery launch missing.");
                        int before = state.Score, unique = state.UniqueCount;
                        int roll = 2 + mysteries % 3;
                        Check(state.TryCatchMystery(roll, next), "Mystery catch rejected.");
                        Check(state.Score == before && state.UniqueCount == unique, "Mystery awarded points or a board number.");
                        Check(!state.TryCatchMystery(roll, next), "Mystery duplicate catch.");
                        multiplier = roll; expiry = next + duration / 20 * 2; mysteries++;
                    }
                    else
                    {
                        bool glow = state.NextNormalIsGlow;
                        Check(state.TryRegisterNormalLaunch(next), "Numbered launch missing.");
                        Check(!state.TryCatch(numbered + 1, next, isGlow: !glow), "Wrong appearance accepted for outstanding ball.");
                        Check(state.TryCatch(numbered + 1, next, isGlow: glow), "Numbered catch rejected.");
                        Check(!state.TryCatch(numbered + 1, next, isGlow: glow), "Numbered duplicate catch.");
                        numbered++; if (glow) glows++;
                        expectedScore += (glow ? 2 : 1) * multiplier;
                    }
                    Check(state.Score == expectedScore, "Independent score model mismatch.");
                }
                Check(numbered == 20 && mysteries == state.MysteryTargetCount && glows == state.GlowTargetCount && state.MainDrawResolved, "Draw count mismatch.");
                Check(!state.TryBeginBonus(), "Bonus began before main clock ended.");
                state.Tick(duration);
                Check(state.TryBeginBonus() && state.ActiveMultiplier == 1 && state.MultiplierRemainingSeconds == 0, "Multiplier leaked into red bonus.");
                Check(state.TryRegisterBonusLaunch() && state.TryCatch(1, duration, true) && state.Score == expectedScore + 3, "Red bonus value changed.");
                Check(state.TryBeginSecondChanceTransition(duration), "Second Chance transition blocked.");
                double revealAt = duration + KinoRoundState.BoardHoldSeconds + KinoRoundState.FadeSeconds;
                double greenAt = revealAt + KinoRoundState.RevealSeconds;
                state.Tick(duration + KinoRoundState.BoardHoldSeconds); state.Tick(revealAt); state.Tick(greenAt);
                for (int i = 0; i < 3; i++)
                {
                    Check(state.TryRegisterSecondChanceLaunch(greenAt + i * 3) && state.TryCatch(30 + i, greenAt + i * 3, isSecondChance: true), "Green catch rejected.");
                }
                Check(state.Score == expectedScore + 12 && state.ActiveMultiplier == 1, "Main multiplier affected Second Chance.");
                Check(state.TryBeginShowcaseBoost(duration + 14, 2, 1) && state.TryRegisterBoostLaunch(duration + 14) &&
                    state.TryCatch(1, duration + 14, isBoost: true) && state.Score == expectedScore + 15, "Main multiplier affected Boost.");
                state.Begin(duration, 200, seed);
                Check(state.ActiveMultiplier == 1 && state.MysteryCatchCount == 0 && state.GlowCatchCount == 0 && state.Score == 0, "Restart retained special state.");
            }
            for (int i = 2; i <= 6; i++) Check(glowCounts[i] && mysteryCounts[i], "Random plan never generated a permitted count.");
            foreach (float duration in new[] { 45f, 60f, 90f })
            {
                var state = new KinoRoundState(); state.Begin(duration, 0, 12);
                double first = state.NextMysteryLaunchAt;
                Check(state.TryRegisterMysteryLaunch(first), "First Mystery missing.");
                double second = state.NextMysteryLaunchAt;
                Check(state.TryRegisterMysteryLaunch(second), "Second Mystery missing.");
                Check(!state.TryCatchMystery(5, second) && !state.TryCatchMystery(1, second), "Invalid multiplier accepted.");
                Check(state.TryCatchMystery(2, second), "First multiplier rejected.");
                double replacement = second + 1;
                Check(state.TryCatchMystery(4, replacement) && state.ActiveMultiplier == 4, "Multiplier stacked instead of replacing.");
                float effect = duration / 20 * 2;
                Check(Math.Abs(state.MultiplierRemainingSeconds - effect) < .001, "Replacement did not restart full duration.");
                state.Tick(replacement + effect - .001);
                Check(state.ActiveMultiplier == 4, "Multiplier expired early.");
                state.Tick(replacement + effect);
                Check(state.ActiveMultiplier == 1 && state.MultiplierRemainingSeconds == 0, "Multiplier failed to expire.");
                state.Stop(); Check(state.ActiveMultiplier == 1, "Stop retained multiplier.");
            }
            // A stalled/full pool must be able to drain every slot after the clock,
            // and transition must wait for the extra Mystery flight too.
            var late = new KinoRoundState(); late.Begin(60, 0, 123);
            late.Tick(100);
            while (late.NormalLaunchCount < 20)
            {
                bool glow = late.NextNormalIsGlow;
                Check(late.TryRegisterNormalLaunch(100) && late.TryMiss(false, isGlow: glow), "Late numbered quota dropped.");
            }
            Check(!late.TryBeginSecondChanceTransition(100), "Unresolved Mystery skipped.");
            while (late.MysteryLaunchCount < late.MysteryTargetCount)
            {
                late.TryRegisterMysteryLaunch(100);
                if (late.MysteryLaunchCount == 1)
                    Check(late.TryCatchMystery(4, 100) && late.ActiveMultiplier == 4, "Last main flight lost its Mystery reward.");
                else Check(late.TryMissMystery(), "Late Mystery miss failed.");
            }
            Check(late.UniqueCount == 0 && late.Score == 0 && late.MainDrawResolved && late.TryBeginSecondChanceTransition(100) && late.ActiveMultiplier == 1, "All-miss round did not reach Second Chance or retained multiplier.");
            Check(!late.TryCatchMystery(4, 100), "Mystery accepted during transition.");
            KinoSecondChanceTests.ValidateRules();
            ValidateFlightTiming();
            Directory.CreateDirectory(KinoMainSpecialSetup.Output);
            File.WriteAllText(KinoMainSpecialSetup.Output + "/rules.txt",
                "PASS: 256 seeded 45/60/90-second draws; all 2-6 glow and extra Mystery quotas; 20 numbered slots; distinct internal Mystery gaps; independent score model; +1/+2 and x2/x3/x4; duplicate and invalid catches; 4.5/6/9-second expiry; replacement and timer reset; late flights and all-miss transition; reset; unchanged red/Second Chance/Boost scoring; per-type flight times and gradual Boost pace.\n");
        }
        static void ValidateFlightTiming()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(KinoExperienceSetup.PrefabPath);
            var settings = prefab.GetComponentInChildren<BallLauncher>(true);
            Check(prefab.GetComponent<KinoRoundController>().roundDuration == 45, "Saved main duration is not 45 seconds.");
            var root = new GameObject("Flight timing validation");
            root.SetActive(false);
            try
            {
                var round = root.AddComponent<KinoRoundController>();
                var launcher = root.AddComponent<BallLauncher>();
                launcher.round = round;
                launcher.flightTime = settings.flightTime;
                launcher.moreWinsFlightTime = settings.moreWinsFlightTime;
                launcher.mysteryFlightTime = settings.mysteryFlightTime;
                launcher.boostStartFlightTime = settings.boostStartFlightTime;
                launcher.boostEndFlightTime = settings.boostEndFlightTime;
                Check(Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.Normal, 0), 2.6f) &&
                    Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.SecondChance, 0), 2.6f) &&
                    Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.MoreWins, 0), 2.1f) &&
                    Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.KinoBonus, 0), 2.1f) &&
                    Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.Mystery, 0), 1.8f), "Incorrect per-type flight timing.");
                var state = round.State;
                state.Begin(45, 0);
                for (int i = 0; i < 20; i++)
                    Check(state.TryRegisterNormalLaunch(i * 2.25) && state.TryCatch(1, i * 2.25), "45-second draw slot failed.");
                state.Tick(45);
                Check(state.TryBeginBonus() && state.TryRegisterBonusLaunch() && state.TryCatch(1, 45, true) &&
                    state.TryBeginSecondChanceTransition(45), "45-second draw transition failed.");
                double revealAt = 45 + KinoRoundState.BoardHoldSeconds + KinoRoundState.FadeSeconds;
                double greenAt = revealAt + KinoRoundState.RevealSeconds;
                state.Tick(48); state.Tick(revealAt); state.Tick(greenAt);
                for (int i = 0; i < 3; i++)
                    Check(state.TryRegisterSecondChanceLaunch(greenAt + i * 3) && state.TryMiss(false, true), "Second Chance failed.");
                Check(state.TryBeginShowcaseBoost(60, 25, 1), "Boost failed to start.");
                float previous = float.PositiveInfinity;
                for (int i = 0; i < 25; i++)
                {
                    Check(state.TryRegisterBoostLaunch(60 + i), "Boost cadence changed.");
                    float flight = launcher.GetFlightTime(Catchable.BallType.KinoBoost, 60 + i);
                    Check(flight < previous && flight >= 1.5f && flight <= 2, "Boost acceleration is not gradual/bounded.");
                    previous = flight;
                }
                Check(Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.KinoBoost, 60), 2) &&
                    Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.KinoBoost, 72.5), 1.75f) &&
                    Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.KinoBoost, 85), 1.5f) &&
                    !state.TryRegisterBoostLaunch(85), "Boost endpoints or duration changed.");
                state.Begin(45, 100);
                Check(Mathf.Approximately(launcher.GetFlightTime(Catchable.BallType.Normal, 100), 2.6f), "Boost pace leaked into the next draw.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        public static void CatchAndCheck(KinoRoundController round, Catchable ball)
        {
            round.State.Tick(Time.timeAsDouble);
            bool mystery = ball.IsMystery, glow = ball.IsGlow;
            var visual = ball.GetComponent<KinoBallNumber>();
            Check(visual && visual.glowHalo && visual.glowHalo.activeSelf == glow, "Pooled halo leaked or missing.");
            Check(visual.numberLabel.text == (mystery ? "?" : ball.Number.ToString()), "Pooled Mystery/number label incorrect.");
            if (mystery) Check(ball.Number == 0 && ball.GetComponent<MeshRenderer>().sharedMaterial == visual.mysteryMaterial, "Mystery appearance/number missing.");
            if (glow) Check(ball.GetComponent<MeshRenderer>().sharedMaterial == visual.glowMaterial, "Glow appearance missing.");
            int before = round.State.Score;
            int expected = mystery ? 0 : glow ? 2 * round.State.ActiveMultiplier : ball.ballType == Catchable.BallType.Normal ? round.State.ActiveMultiplier : 3;
            int popups = round.catchFeedback.ShownCount;
            var point = ball.transform.position + Vector3.up * .03f;
            var type = ball.ballType;
            ball.CatchAt(point); ball.CatchAt(point);
            Check(round.State.Score == before + expected, "Live catch score/duplicate guard mismatch.");
            string popup = mystery ? "×" + round.State.LastMysteryMultiplier : glow ? "+2" : type == Catchable.BallType.Normal ? "+1" : "+3";
            Check(round.catchFeedback.ShownCount == popups + 1 && round.catchFeedback.LastText == popup && round.catchFeedback.LastContactPoint == point,
                "Wrong contact text, contact position, or repeated popup.");
            Check(!visual.glowHalo.activeSelf && visual.numberLabel.text == "1" && ball.ballType == Catchable.BallType.Normal,
                "Appearance/type not cleared when returned to pool.");
        }
    }
}
