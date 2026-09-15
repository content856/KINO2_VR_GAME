using System;
using UnityEngine;

namespace KinoVR
{
    /// <summary>Contained visual balls. The launcher owns gameplay balls separately.</summary>
    [DisallowMultipleComponent]
    public sealed class KinoAirChamber : MonoBehaviour
    {
        public enum ChamberKind { Tube, Lottery }
        public ChamberKind kind;
        public KinoRoundController round;
        public KinoPlayerView playerView;
        public KinoBallNumber[] balls = Array.Empty<KinoBallNumber>();
        public int[] numbers = Array.Empty<int>();
        [Header("Interior dimensions in metres")]
        public float radius = .265f;
        public float floor = .36f;
        public float ceiling = 5.63f;
        public float ballRadius = .189f;
        public float tubeSpacing = .29f;
        [Header("Air")]
        public int seed = 2;
        [Min(0)] public float tubeAirStrength = 3.8f;
        [Min(0)] public float idleMixSpeed = .32f;
        [Min(0)] public float gameplayMixSpeed = 1.65f;
        [Min(.01f)] public float speedBlendSeconds = .8f;

        public bool IdleMotionActive => kind == ChamberKind.Tube && !GameplayActive;
        public bool GameplayActive => round && round.isActiveAndEnabled && round.IsRunning;
        public float CurrentMixSpeed { get; private set; }
        Vector3[] positions, velocities;
        Quaternion[] initialRotations;
        float airTime, accumulator;
        Transform viewer;
        const float Step = 1f / 60f;

        void OnEnable() => ResetMotion();
        void Start()
        {
            if (!round) round = FindFirstObjectByType<KinoRoundController>();
            if (!playerView && round) playerView = round.playerView;
            var view = playerView && playerView.View ? playerView.View : Camera.main ? Camera.main.transform : null;
            FaceNumbers(view);
        }
        void Update() => Advance(Time.deltaTime, GameplayActive);

        public void ResetMotion()
        {
            positions = new Vector3[balls.Length];
            velocities = new Vector3[balls.Length];
            initialRotations = new Quaternion[balls.Length];
            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i]) continue;
                positions[i] = transform.InverseTransformPoint(balls[i].transform.position);
                initialRotations[i] = balls[i].transform.localRotation;
            }
            CurrentMixSpeed = idleMixSpeed;
            airTime = accumulator = 0;
            viewer = null;
        }

        public void FaceNumbers(Transform view)
        {
            viewer = view;
            for (int i = 0; i < balls.Length; i++)
                if (balls[i]) balls[i].SetNumber(i < numbers.Length ? numbers[i] : i + 1, view);
        }

        /// <summary>Fixed simulation steps also make editor previews match runtime motion.</summary>
        public void Advance(float deltaTime, bool gameplay)
        {
            if (positions == null || positions.Length != balls.Length) ResetMotion();
            var view = playerView && playerView.View ? playerView.View : viewer;
            if (view && view != viewer) FaceNumbers(view);
            // Hand off immediately: no idle position/rotation writes during a round.
            // Retain phase and velocity so countdown/intermission can resume smoothly.
            if (kind == ChamberKind.Tube && gameplay) { accumulator = 0; return; }
            accumulator += Mathf.Clamp(deltaTime, 0, .1334f);
            while (accumulator >= Step)
            {
                accumulator -= Step;
                float speed = kind == ChamberKind.Tube ? 1 : gameplay ? gameplayMixSpeed : idleMixSpeed;
                CurrentMixSpeed = Mathf.MoveTowards(CurrentMixSpeed, speed,
                    Step * Mathf.Max(.01f, gameplayMixSpeed) / Mathf.Max(.01f, speedBlendSeconds));
                float dt = kind == ChamberKind.Tube ? Step : Step * CurrentMixSpeed;
                airTime += dt;
                Simulate(dt);
            }
            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i]) continue;
                balls[i].transform.localPosition = positions[i];
                float phase = airTime + seed * .73f + i * 2.39996f;
                balls[i].transform.localRotation = kind == ChamberKind.Tube
                    ? initialRotations[i] * Quaternion.Euler(6 * Mathf.Sin(phase * 1.7f), 12 * Mathf.Sin(phase * .63f), 8 * Mathf.Sin(phase * 1.2f))
                    : initialRotations[i] * Quaternion.Euler(airTime * (43 + i % 4 * 9), airTime * (61 + i % 3 * 13), airTime * 37);
            }
        }

        void Simulate(float dt)
        {
            if (dt <= 0) return;
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                float phase = seed * .73f + i * .17f;
                if (kind == ChamberKind.Tube)
                {
                    // A shared gust lifts the narrow column; smaller eddies disturb
                    // each ball. Gravity wins between gusts, letting the column fall.
                    float wind = tubeAirStrength * Mathf.Sin(airTime * 1.12f + phase)
                        + .85f * Mathf.Sin(airTime * 3.1f + i * 2.4f + seed) - .3f;
                    velocities[i].y += (wind - velocities[i].y * .8f) * dt;
                    p.y += velocities[i].y * dt;
                    float clearance = Mathf.Max(0, radius - ballRadius - .009f);
                    p.x = clearance * .68f * Mathf.Sin(airTime * 1.8f + i * 2.4f + seed);
                    p.z = clearance * .55f * Mathf.Sin(airTime * 2.3f + i * 1.7f + seed);
                }
                else
                {
                    // Tangential airflow plus irregular vertical lift. These are
                    // visual particles with bounded contacts, not 77 rigidbodies.
                    var tangent = new Vector3(-p.z, 0, p.x).normalized;
                    var desired = tangent * (.78f + .16f * Mathf.Sin(airTime * 2 + phase));
                    desired.y = .5f * Mathf.Sin(airTime * 2.5f + i * 2.4f) + .12f;
                    desired += new Vector3(Mathf.Sin(airTime * 3.3f + i), 0,
                        Mathf.Cos(airTime * 2.7f + i * 1.8f)) * .19f;
                    velocities[i] = Vector3.Lerp(velocities[i], desired, 1 - Mathf.Exp(-4 * dt));
                    p += velocities[i] * dt;
                }
                positions[i] = p;
                Contain(i);
            }
            // Iterative contacts keep neighbouring balls from visibly intersecting.
            for (int pass = 0; pass < 8; pass++)
            {
                for (int i = 0; i < positions.Length; i++)
                for (int j = i + 1; j < positions.Length; j++)
                {
                    var delta = positions[j] - positions[i];
                    if (kind == ChamberKind.Tube) delta = Vector3.up * delta.y;
                    float gap = kind == ChamberKind.Tube ? tubeSpacing : ballRadius * 2 + .003f;
                    float distance = delta.magnitude;
                    if (distance >= gap) continue;
                    var normal = distance > .00001f ? delta / distance : Vector3.up;
                    var correction = normal * ((gap - distance) * .5f);
                    positions[i] -= correction;
                    positions[j] += correction;
                    float closing = Vector3.Dot(velocities[j] - velocities[i], normal);
                    if (closing < 0)
                    {
                        velocities[i] += normal * closing * .6f;
                        velocities[j] -= normal * closing * .6f;
                    }
                }
                for (int i = 0; i < positions.Length; i++) Contain(i);
            }
        }

        void Contain(int i)
        {
            var p = positions[i];
            float bottom = floor + ballRadius + .012f;
            if (p.y < bottom) { p.y = bottom; velocities[i].y = Mathf.Abs(velocities[i].y) * .35f; }
            if (kind == ChamberKind.Tube)
            {
                float top = ceiling - ballRadius - .012f;
                if (p.y > top) { p.y = top; velocities[i].y = -Mathf.Abs(velocities[i].y) * .35f; }
            }
            else
            {
                // Reserve a central cylinder for the existing spindle/mixing fingers.
                float inner = .155f + ballRadius;
                float horizontal = new Vector2(p.x, p.z).magnitude;
                if (horizontal < inner)
                {
                    var outward = horizontal > .00001f ? new Vector3(p.x, 0, p.z) / horizontal : Vector3.right;
                    p.x = outward.x * inner; p.z = outward.z * inner;
                    float inwardSpeed = Vector3.Dot(velocities[i], outward);
                    if (inwardSpeed < 0) velocities[i] -= outward * inwardSpeed * 1.35f;
                }
                float outer = radius - ballRadius - .012f;
                // Keep enough radial clearance around the spindle at the dome's top.
                p.y = Mathf.Min(p.y, Mathf.Sqrt(Mathf.Max(0, outer * outer - inner * inner)));
                float maxHorizontal = Mathf.Sqrt(Mathf.Max(0, outer * outer - p.y * p.y));
                horizontal = new Vector2(p.x, p.z).magnitude;
                if (horizontal > maxHorizontal)
                {
                    var normal = p.normalized;
                    p.x *= maxHorizontal / horizontal; p.z *= maxHorizontal / horizontal;
                    float outwardSpeed = Vector3.Dot(velocities[i], normal);
                    if (outwardSpeed > 0) velocities[i] -= normal * outwardSpeed * 1.35f;
                }
            }
            positions[i] = p;
        }
    }
}
