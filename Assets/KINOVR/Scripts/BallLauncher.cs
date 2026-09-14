using System.Collections;
using System.Collections.Generic;
using KinoVR;
using UnityEngine;

public class BallLauncher : MonoBehaviour
{
    [Header("Target")]
    public Transform player;
    public Transform[] spawnPoints;
    public GameObject ballPrefab;
    [Header("Round")]
    public KinoRoundController round;
    [Tooltip("Used only when this launcher is not controlled by a timed round.")]
    public bool autoStart = true;
    [Min(1)] public float ballLifetime = 6;
    [Header("Timing")]
    [Min(.05f)] public float minSpawnInterval = .5f;
    [Min(.05f)] public float maxSpawnInterval = 1.2f;
    [Header("Flight")]
    [Min(.1f)] public float flightTime = 1.1f;
    [Range(0, .5f)] public float speedVariance = .15f;
    [Min(0)] public float missRadius = .6f;
    public float aimHeightOffset = 1.2f;

    readonly List<GameObject> liveBalls = new List<GameObject>();
    Coroutine spawnRoutine;
    public bool IsLaunching => spawnRoutine != null;
    void OnEnable()
    {
        if (!round && autoStart) StartLaunching();
    }
    void OnDisable() => StopLaunching(true);
    public void StartLaunching()
    {
        if (spawnRoutine == null && isActiveAndEnabled) spawnRoutine = StartCoroutine(SpawnLoop());
    }
    public void StopLaunching(bool clearBalls)
    {
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        spawnRoutine = null;
        if (!clearBalls) return;
        foreach (var ball in liveBalls)
        {
            if (!ball) continue;
            ball.SetActive(false);
            Destroy(ball);
        }
        liveBalls.Clear();
    }
    IEnumerator SpawnLoop()
    {
        while (!round || round.IsRunning)
        {
            float low = Mathf.Max(.05f, minSpawnInterval);
            yield return new WaitForSeconds(Random.Range(low, Mathf.Max(low, maxSpawnInterval)));
            if (round && !round.IsRunning) break;
            SpawnBall();
        }
        spawnRoutine = null;
    }
    public GameObject SpawnBall()
    {
        if (round) round.RefreshClock();
        if (round && !round.IsRunning) return null;
        if (spawnPoints == null || spawnPoints.Length == 0 || !ballPrefab || !player) return null;
        var spawn = spawnPoints[Random.Range(0, spawnPoints.Length)];
        if (!spawn) return null;
        var ball = Instantiate(ballPrefab, spawn.position, Quaternion.identity);
        liveBalls.RemoveAll(item => !item);
        liveBalls.Add(ball);
        int number = Random.Range(1, 81);
        var catchable = ball.GetComponent<Catchable>();
        if (catchable) catchable.Configure(number, round);
        var visual = ball.GetComponent<KinoBallNumber>();
        if (visual) visual.SetNumber(number, player);
        var rb = ball.GetComponent<Rigidbody>();
        if (!rb) rb = ball.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        Vector2 offset = Random.insideUnitCircle * missRadius;
        Vector3 target = player.position + new Vector3(offset.x, aimHeightOffset, offset.y);
        float time = Mathf.Max(.1f, flightTime * Random.Range(1 - speedVariance, 1 + speedVariance));
        rb.linearVelocity = (target - spawn.position) / time - .5f * Physics.gravity * time;
        Destroy(ball, Mathf.Max(1, ballLifetime));
        return ball;
    }
}
