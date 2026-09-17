using UnityEngine;
using UnityEngine.Events;

namespace KinoVR
{
    public sealed class KinoRoundController : MonoBehaviour
    {
        [Min(1)] public float roundDuration = 75;
        public bool startAutomatically = true;
        [Header("KINO BOOST bonus round")]
        public bool enableBoostRound = true;
        [Min(0)] public float calmDuration = 6;
        [Min(1)] public float boostDuration = 25;
        [Range(1, 4)] public float calmSpawnMultiplier = 2.4f;
        [Range(.2f, 1)] public float boostSpawnMultiplier = .48f;
        [Range(.4f, 1)] public float boostFlightMultiplier = .7f;
        public KinoBoostPresentation boostPresentation;
        public BallLauncher launcher;
        public KinoNumberBoard board;
        public ScoreManager score;
        public KinoPlayerView playerView;
        public UnityEvent onRoundFinished = new UnityEvent();
        public KinoRoundState State { get; } = new KinoRoundState();
        public bool IsRunning => State.IsRunning;
        bool finishPresented;
        KinoRoundPhase presentedPhase = KinoRoundPhase.Idle;
        void Start()
        {
            if (startAutomatically) BeginRound();
        }
        public void BeginRound() => BeginRound(roundDuration);
        public void BeginRound(float duration)
        {
            if (launcher) launcher.StopLaunching(true);
            State.Begin(duration, Time.timeAsDouble, enableBoostRound ? calmDuration : 0, enableBoostRound ? boostDuration : 0);
            presentedPhase = KinoRoundPhase.Idle;
            finishPresented = false;
            if (boostPresentation) boostPresentation.SetBoost(false);
            if (score) score.ResetScore();
            if (board) board.ResetBoard();
            if (launcher)
            {
                launcher.round = this;
                if (playerView && playerView.View) launcher.player = playerView.View;
                launcher.SetPace(1, 1, 1);
                launcher.PrepareRoundBonus(duration + (enableBoostRound ? calmDuration + boostDuration : 0));
                launcher.StartLaunching();
            }
            PresentPhase();
            RefreshBoard();
        }
        void Update() => RefreshClock();
        public void RefreshClock()
        {
            if (finishPresented || !State.IsRunning) return;
            State.Tick(Time.timeAsDouble);
            if (!State.IsRunning) FinishRound();
            else PresentPhase();
            RefreshBoard();
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
        void PresentPhase()
        {
            if (presentedPhase == State.Phase) return;
            presentedPhase = State.Phase;
            bool boosted = State.Phase == KinoRoundPhase.Boost;
            if (launcher)
            {
                if (boosted) launcher.SetPace(boostSpawnMultiplier, boostFlightMultiplier, boostFlightMultiplier);
                else if (State.Phase == KinoRoundPhase.Calm) launcher.SetPace(calmSpawnMultiplier, 1.15f, 1.15f);
                else launcher.SetPace(1, 1, 1);
            }
            if (boostPresentation) boostPresentation.SetBoost(boosted);
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
