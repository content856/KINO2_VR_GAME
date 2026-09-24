using UnityEngine;
using UnityEngine.Events;

namespace KinoVR
{
    public sealed class KinoRoundController : MonoBehaviour
    {
        [Min(1)] public float roundDuration = 60;
        public bool startAutomatically = true;
        [Header("Main draw: glow and extra Mystery balls")]
        public bool enableMainSpecialBalls = true;
        public KinoCatchFeedback catchFeedback;
        [Header("KINO Bonus (after the 20-ball draw)")]
        [Tooltip("Seconds each previously caught number stays visible on the red bonus ball.")]
        [Min(.25f)] public float bonusNumberInterval = 1;
        [Header("Client showcase (normal cycle ends after Second Chance)")]
        public bool showcaseBoostAfterSecondChance = false;
        [HideInInspector] public KinoExperienceController experience;
        [Min(1)] public float showcaseBoostDuration = 25;
        [Min(.25f)] public float showcaseBoostInterval = 1;
        // The gold presentation is used only by the optional client showcase.
        [HideInInspector]
        public KinoBoostPresentation boostPresentation;
        public KinoSecondChancePresentation secondChancePresentation;
        public BallLauncher launcher;
        public KinoNumberBoard board;
        public ScoreManager score;
        public KinoPlayerView playerView;
        public KinoRestartButton restartButton;
        public KinoAudioController audioController;
        public UnityEvent onRoundFinished = new UnityEvent();
        public KinoRoundState State { get; } = new KinoRoundState();
        public bool IsRunning => State.IsRunning;
        bool finishPresented;
        bool refreshing;
        void Start()
        {
            if (startAutomatically) BeginRound();
        }
        public void BeginRound() => BeginRound(roundDuration);
        public void BeginRound(float duration)
        {
            if (launcher) launcher.StopLaunching(true);
            State.Begin(duration, Time.timeAsDouble, enableMainSpecialBalls ? (int?)Random.Range(0, int.MaxValue) : null);
            if (catchFeedback) catchFeedback.ResetFeedback(playerView ? playerView.View : null);
            finishPresented = false;
            if (restartButton) restartButton.Hide();
            if (boostPresentation) boostPresentation.SetBoost(false);
            if (secondChancePresentation) secondChancePresentation.ResetPresentation();
            if (score) score.ResetScore();
            if (board) board.ResetBoard();
            if (audioController) audioController.BeginRound();
            if (launcher)
            {
                launcher.round = this;
                if (playerView && playerView.View) launcher.player = playerView.View;
                launcher.SetPace(1, 1, 1);
                launcher.StartLaunching();
            }
            RefreshBoard();
        }
        void Update() => RefreshClock();
        public void RefreshClock()
        {
            if (refreshing || finishPresented || !State.IsRunning) return;
            refreshing = true;
            try
            {
                State.Tick(Time.timeAsDouble);
                // Keep the last flight catchable before each transition.
                bool noFlights = !launcher || launcher.ActiveBallCount == 0;
                if (State.Phase == KinoRoundPhase.Settling && State.MainDrawResolved && noFlights)
                {
                    if (!State.TryBeginBonus()) State.TryBeginSecondChanceTransition(Time.timeAsDouble);
                }
                if (State.Phase == KinoRoundPhase.Bonus && State.BonusLaunched && noFlights)
                    State.TryBeginSecondChanceTransition(Time.timeAsDouble);
                if (State.Phase == KinoRoundPhase.SecondChance && State.ResolvedSecondChanceCount == KinoRoundState.SecondChanceBallLimit &&
                    noFlights)
                {
                    if (!showcaseBoostAfterSecondChance || !State.TryBeginShowcaseBoost(Time.timeAsDouble, showcaseBoostDuration, showcaseBoostInterval)) FinishRound();
                    else
                    {
                        if (boostPresentation) boostPresentation.SetBoost(true, true);
                        if (launcher) launcher.SetPace(1, .72f, .75f);
                    }
                }
                if (!State.IsRunning || (State.Phase == KinoRoundPhase.BoostSettling && State.ResolvedBoostCount == State.BoostLaunchCount && noFlights)) FinishRound();
                RefreshBoard();
            }
            finally { refreshing = false; }
        }
        public bool TryCatch(int number, Catchable.BallType ballType = Catchable.BallType.Normal)
        {
            RefreshClock();
            int previousScore = State.Score;
            bool accepted = ballType == Catchable.BallType.Mystery ? State.TryCatchMystery(Random.Range(2, 5), Time.timeAsDouble) :
                State.TryCatch(number, Time.timeAsDouble, ballType == Catchable.BallType.KinoBonus,
                    ballType == Catchable.BallType.SecondChance, ballType == Catchable.BallType.KinoBoost, ballType == Catchable.BallType.MoreWins);
            if (!accepted)
            {
                if (!State.IsRunning && !finishPresented) FinishRound();
                return false;
            }
            if (score) score.AddScore(State.Score - previousScore, ballType);
            if (board && ballType != Catchable.BallType.Mystery)
            {
                board.MarkCaught(number, ballType == Catchable.BallType.KinoBonus, ballType == Catchable.BallType.SecondChance);
                if (ballType == Catchable.BallType.KinoBoost) board.ShowMultiplier(number);
            }
            RefreshBoard();
            return true;
        }
        public void MissBall(bool isKinoBonus, bool isSecondChance = false, bool isBoost = false, bool isGlow = false, bool isMystery = false)
        {
            State.Tick(Time.timeAsDouble);
            if (isMystery) State.TryMissMystery();
            else State.TryMiss(isKinoBonus, isSecondChance, isBoost, isGlow);
        }
        public void FinishRound()
        {
            if (finishPresented) return;
            State.Stop();
            finishPresented = true;
            if (launcher) launcher.StopLaunching(true);
            if (launcher) launcher.SetPace(1, 1, 1);
            if (boostPresentation) boostPresentation.SetBoost(false);
            RefreshBoard();
            if (restartButton && (!experience || !experience.isActiveAndEnabled)) restartButton.Show();
            onRoundFinished.Invoke();
        }
        void RefreshBoard()
        {
            if (catchFeedback) catchFeedback.Present(State);
            if (audioController) audioController.Present(State);
            if (board) board.SetRoundProgress(State, finishPresented);
            if (secondChancePresentation) secondChancePresentation.Present(State, Time.timeAsDouble);
        }
        void OnDisable()
        {
            if (audioController) audioController.StopAll();
            State.Stop();
            if (restartButton) restartButton.Hide();
            if (secondChancePresentation) secondChancePresentation.ResetPresentation();
            if (launcher) launcher.StopLaunching(true);
            if (launcher) launcher.SetPace(1, 1, 1);
            if (boostPresentation) boostPresentation.SetBoost(false);
        }
    }
}
