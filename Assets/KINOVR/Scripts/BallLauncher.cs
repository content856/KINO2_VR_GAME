using System.Collections.Generic;
using KinoVR;
using UnityEngine;

public class BallLauncher : MonoBehaviour
{
    [Header("Target")]
    public Transform player;
    public Transform[] spawnPoints;
    public Transform[] exitPoints;
    public GameObject ballPrefab;
    [Header("Round")]
    public KinoRoundController round;
    public bool autoStart = true;
    [Min(1)] public float ballLifetime = 6;
    [Header("Pool")]
    [Min(1)] public int poolCapacity = 16;
    [Header("Standalone timing (rounds use 20 evenly spaced launches)")]
    [Min(.05f)] public float minSpawnInterval = .5f;
    [Min(.05f)] public float maxSpawnInterval = 1.2f;
    [Header("Tube ascent")]
    [Min(.2f)] public float tubeRiseTime = 1.8f;
    [Header("Difficulty")]
    [Tooltip("Seconds from the tube exit to the hand zone. Lower = faster. Applies to new balls.")]
    [Min(.5f)] public float flightTime = 2.6f;
    [Range(0, .5f)] public float speedVariance = .12f;
    [Tooltip("Maximum wind acceleration in m/s². Higher = quicker acceleration towards the player after exiting.")]
    [Range(4, 60)] public float exitAcceleration = 28;
    [Tooltip("Sideways/up-down flight deviation in metres. 0 = smooth flight; higher = harder. Can change during Play.")]
    [Range(0, 2)] public float turbulence = .35f;
    [Tooltip("How quickly the airflow changes direction. Can change during Play.")]
    [Range(.25f, 3)] public float turbulenceFrequency = 1;
    [Min(0)] public float missRadius = .4f;
    public float aimHeightOffset = -.3f;
    [Header("Orientation")]
    [Tooltip("Gentle tilt in degrees around a horizontal, player-facing pose. Independent of flight turbulence.")]
    [Range(0, 8)] public float wobbleAngle = 3;

    readonly Stack<KinoPooledBall> available = new Stack<KinoPooledBall>();
    readonly List<KinoPooledBall> pool = new List<KinoPooledBall>();
    double nextSpawn;
    int nextTube;
    bool initialized;
    public bool HasLaunchedKinoBonus => round && round.State.BonusLaunched;
    public bool IsLaunching { get; private set; }
    public int PoolCount => pool.Count;
    public int ActiveBallCount { get; private set; }
    public float IntervalMultiplier { get; private set; } = 1;
    public float FlightTimeMultiplier { get; private set; } = 1;
    public float TubeTimeMultiplier { get; private set; } = 1;
    public float EffectiveFlightTime => flightTime * FlightTimeMultiplier;
    public float EffectiveTubeRiseTime => tubeRiseTime * TubeTimeMultiplier;

    public void SetPace(float interval, float flight, float tube)
    {
        IntervalMultiplier = Mathf.Max(.1f, interval);
        FlightTimeMultiplier = Mathf.Max(.1f, flight);
        TubeTimeMultiplier = Mathf.Max(.1f, tube);
        if (IsLaunching) ScheduleNext();
    }

    void Awake() => Prewarm();
    void OnEnable() { if (!round && autoStart) StartLaunching(); }
    void OnDisable() => StopLaunching(true);
    void Update()
    {
        if (!IsLaunching) return;
        if (round)
        {
            SpawnBall(); // The round's absolute 20-slot schedule controls eligibility.
            return;
        }
        if (Time.timeAsDouble < nextSpawn) return;
        if (round && !round.IsRunning) { StopLaunching(true); return; }
        SpawnBall();
        ScheduleNext();
    }
    void ScheduleNext()
    {
        float low = Mathf.Max(.05f, minSpawnInterval);
        nextSpawn = Time.timeAsDouble + Mathf.Max(.05f, Random.Range(low, Mathf.Max(low, maxSpawnInterval)) * IntervalMultiplier);
    }
    public void Prewarm()
    {
        if (initialized || !ballPrefab) return;
        initialized = true;
        var root = new GameObject("Gameplay ball pool");
        root.transform.SetParent(transform, false);
        root.SetActive(false);
        for (int i = 0; i < Mathf.Max(1, poolCapacity); i++)
        {
            var go = Instantiate(ballPrefab, root.transform);
            go.name = "Pooled gameplay ball " + (i + 1);
            var ball = go.GetComponent<KinoPooledBall>();
            if (!ball) ball = go.AddComponent<KinoPooledBall>();
            ball.Initialize(this);
            pool.Add(ball);
            available.Push(ball);
        }
        root.SetActive(true);
    }
    public void StartLaunching()
    {
        if (IsLaunching || !isActiveAndEnabled) return;
        Prewarm();
        nextTube = 0;
        IsLaunching = true;
        ScheduleNext();
    }
    public void StopLaunching(bool clearBalls)
    {
        IsLaunching = false;
        if (clearBalls)
            foreach (var ball in pool) if (ball) ball.ReturnToPool(ball.Generation);
    }
    public GameObject SpawnBall(int spawnIndex = -1)
    {
        if (round) round.RefreshClock();
        if (round && !round.IsRunning) return null;
        bool isKinoBonus = round && round.State.Phase == KinoRoundPhase.Bonus;
        if (round && (isKinoBonus ? round.State.BonusLaunched || round.State.UniqueCount == 0 : !round.State.IsNormalLaunchDue(Time.timeAsDouble))) return null;
        if (!isActiveAndEnabled || spawnPoints == null || spawnPoints.Length == 0 || !ballPrefab || !player) return null;
        int index = spawnIndex < 0 ? nextTube++ % spawnPoints.Length : spawnIndex % spawnPoints.Length;
        var spawn = spawnPoints[index];
        if (!spawn) return null;
        Prewarm();
        // A full pool skips this spawn; never steal a live ball or instantiate mid-round.
        if (available.Count == 0) return null;
        if (round && !(isKinoBonus ? round.State.TryRegisterBonusLaunch() : round.State.TryRegisterNormalLaunch(Time.timeAsDouble))) return null;
        var ball = available.Pop();
        var exit = exitPoints != null && index < exitPoints.Length ? exitPoints[index] : null;
        ActiveBallCount++;
        int number = isKinoBonus ? round.State.GetCaughtNumber(Random.Range(0, round.State.UniqueCount)) : Random.Range(1, 81);
        ball.Activate(spawn, exit, number, isKinoBonus);
        return ball.gameObject;
    }
    internal void Recycle(KinoPooledBall ball)
    {
        ActiveBallCount--;
        available.Push(ball);
    }
}
