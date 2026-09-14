using UnityEngine;
using UnityEngine.Events;

namespace KinoVR
{
    public sealed class KinoRoundController : MonoBehaviour
    {
        [Min(1)] public float roundDuration = 75;
        public bool startAutomatically = true;
        public BallLauncher launcher;
        public KinoNumberBoard board;
        public ScoreManager score;
        public KinoPlayerView playerView;
        public UnityEvent onRoundFinished = new UnityEvent();
        public KinoRoundState State { get; } = new KinoRoundState();
        public bool IsRunning => State.IsRunning;
        bool finishPresented;
        float currentDuration;
        void Start()
        {
            if (startAutomatically) BeginRound();
        }
        public void BeginRound() => BeginRound(roundDuration);
        public void BeginRound(float duration)
        {
            if (launcher) launcher.StopLaunching(true);
            State.Begin(duration, Time.timeAsDouble);
            currentDuration = duration;
            finishPresented = false;
            if (score) score.ResetScore();
            if (board) board.ResetBoard();
            if (launcher)
            {
                launcher.round = this;
                if (playerView && playerView.View) launcher.player = playerView.View;
                launcher.StartLaunching();
            }
            RefreshBoard();
        }
        void Update() => RefreshClock();
        public void RefreshClock()
        {
            if (finishPresented || !State.IsRunning) return;
            State.Tick(Time.timeAsDouble);
            if (!State.IsRunning) FinishRound();
            RefreshBoard();
        }
        public bool TryCatch(int number)
        {
            if (!State.TryCatch(number, Time.timeAsDouble))
            {
                if (!State.IsRunning && !finishPresented) FinishRound();
                return false;
            }
            if (score) score.AddScore(1, Catchable.BallType.Normal);
            if (board) board.MarkCaught(number);
            RefreshBoard();
            return true;
        }
        public void FinishRound()
        {
            if (finishPresented) return;
            State.Stop();
            finishPresented = true;
            if (launcher) launcher.StopLaunching(true);
            RefreshBoard();
            onRoundFinished.Invoke();
        }
        void RefreshBoard()
        {
            if (board) board.SetProgress(State.CatchCount, State.RemainingSeconds, currentDuration, finishPresented);
        }
        void OnDisable()
        {
            State.Stop();
            if (launcher) launcher.StopLaunching(true);
        }
    }
}
