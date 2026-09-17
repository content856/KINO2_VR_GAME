using UnityEngine;
using UnityEngine.Events;

namespace KinoVR
{
    public sealed class KinoRoundController : MonoBehaviour
    {
        [Min(1)] public float roundDuration = 60;
        public bool startAutomatically = true;
        [Header("Final KINO Bonus")]
        [Tooltip("Seconds each previously caught number stays visible on the final bonus ball.")]
        [Min(.25f)] public float bonusNumberInterval = 1;
        // Retain the room/brand presentation reference, without its old timed phase.
        [HideInInspector]
        public KinoBoostPresentation boostPresentation;
        public BallLauncher launcher;
        public KinoNumberBoard board;
        public ScoreManager score;
        public KinoPlayerView playerView;
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
            State.Begin(duration, Time.timeAsDouble);
            finishPresented = false;
            if (boostPresentation) boostPresentation.SetBoost(false);
            if (score) score.ResetScore();
            if (board) board.ResetBoard();
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
                // Keep the last ordinary flight catchable after 00:00. Freeze the
                // bonus candidate set only after every ordinary ball is resolved.
                if (State.Phase == KinoRoundPhase.Settling && State.ResolvedNormalCount == KinoRoundState.NormalBallLimit &&
                    (!launcher || launcher.ActiveBallCount == 0)) State.TryBeginBonus();
                if (!State.IsRunning || (State.Phase == KinoRoundPhase.Bonus && State.BonusLaunched &&
                    (!launcher || launcher.ActiveBallCount == 0))) FinishRound();
                RefreshBoard();
            }
            finally { refreshing = false; }
        }
        public bool TryCatch(int number, Catchable.BallType ballType = Catchable.BallType.Normal)
        {
            RefreshClock();
            int previousScore = State.Score;
            if (!State.TryCatch(number, Time.timeAsDouble, ballType == Catchable.BallType.KinoBonus))
            {
                if (!State.IsRunning && !finishPresented) FinishRound();
                return false;
            }
            if (score) score.AddScore(State.Score - previousScore, ballType);
            if (board) board.MarkCaught(number, ballType == Catchable.BallType.KinoBonus);
            RefreshBoard();
            return true;
        }
        public void MissBall(bool isKinoBonus)
        {
            State.Tick(Time.timeAsDouble);
            State.TryMiss(isKinoBonus);
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
            onRoundFinished.Invoke();
        }
        void RefreshBoard()
        {
            if (board) board.SetRoundProgress(State, finishPresented);
        }
        void OnDisable()
        {
            State.Stop();
            if (launcher) launcher.StopLaunching(true);
            if (launcher) launcher.SetPace(1, 1, 1);
            if (boostPresentation) boostPresentation.SetBoost(false);
        }
    }
}
