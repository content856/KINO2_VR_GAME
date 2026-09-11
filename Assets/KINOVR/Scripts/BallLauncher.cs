using UnityEngine;
using System.Collections;

/// <summary>
/// Spawns balls and launches them on a physics-based arc toward the player.
/// The target point is randomized so balls don't land in exactly the same
/// spot every time — they still read as "coming at you" without being a
/// perfectly predictable straight shot.
///
/// Attach to an empty GameObject. Assign a player Transform, one or more
/// spawn point Transforms, and a ball prefab that has a Rigidbody
/// (or one will be added automatically) and a Collider.
/// </summary>
public class BallLauncher : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Usually the VR camera rig / head transform.")]
    public Transform player;

    [Header("Spawn Points")]
    public Transform[] spawnPoints;

    [Header("Ball")]
    public GameObject ballPrefab;

    [Header("Timing")]
    public float minSpawnInterval = 0.5f;
    public float maxSpawnInterval = 1.2f;

    [Header("Flight")]
    [Tooltip("Seconds for the ball to reach the target point. Lower = flatter/faster arc, higher = floatier arc.")]
    public float flightTime = 1.1f;
    [Tooltip("+/- percentage randomization applied to flightTime per ball, so not every ball moves at the same speed.")]
    [Range(0f, 0.5f)]
    public float speedVariance = 0.15f;

    [Header("Imperfect Aim")]
    [Tooltip("Max horizontal distance the target point can be offset from the player, in meters.")]
    public float missRadius = 0.6f;
    [Tooltip("Roughly where on the player the ball aims by default (1.2 = chest height if player.position is at floor level).")]
    public float aimHeightOffset = 1.2f;

    private Coroutine spawnRoutine;

    void OnEnable()
    {
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    void OnDisable()
    {
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minSpawnInterval, maxSpawnInterval));
            SpawnBall();
        }
    }

    void SpawnBall()
    {
        if (spawnPoints.Length == 0 || ballPrefab == null || player == null) return;

        Transform spawn = spawnPoints[Random.Range(0, spawnPoints.Length)];
        GameObject ball = Instantiate(ballPrefab, spawn.position, Quaternion.identity);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb == null) rb = ball.AddComponent<Rigidbody>();
        rb.useGravity = true;

        // Aim near the player, not exactly at them — a random horizontal
        // offset within missRadius, biased slightly upward so balls
        // don't skew toward the floor.
        Vector2 flatOffset = Random.insideUnitCircle * missRadius;
        Vector3 targetPos = player.position
                             + new Vector3(flatOffset.x, 0f, flatOffset.y)
                             + Vector3.up * aimHeightOffset;

        float thisFlightTime = flightTime * Random.Range(1f - speedVariance, 1f + speedVariance);

        rb.linearVelocity = LaunchVelocityForTime(spawn.position, targetPos, thisFlightTime);
        // Unity < 6: use rb.velocity instead of rb.linearVelocity.
    }

    /// <summary>
    /// Standard ballistic-trajectory solve: given a start point, an end point,
    /// and a desired flight duration, returns the initial velocity a
    /// gravity-affected Rigidbody needs to land exactly at that point at
    /// exactly that time. After launch, physics (gravity, collisions,
    /// air drag if you add it) takes over completely — nothing here
    /// scripts the actual path frame to frame.
    /// </summary>
    Vector3 LaunchVelocityForTime(Vector3 origin, Vector3 target, float time)
    {
        float gravity = Mathf.Abs(Physics.gravity.y);
        Vector3 delta = target - origin;
        Vector3 deltaFlat = new Vector3(delta.x, 0f, delta.z);

        Vector3 velocityFlat = deltaFlat / time;
        float velocityY = (delta.y / time) + 0.5f * gravity * time;

        return velocityFlat + Vector3.up * velocityY;
    }
}