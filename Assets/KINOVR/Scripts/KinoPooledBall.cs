using UnityEngine;

namespace KinoVR
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody), typeof(Catchable))]
    public sealed class KinoPooledBall : MonoBehaviour
    {
        BallLauncher owner;
        Rigidbody body;
        Catchable catchable;
        KinoBallNumber visual;
        Vector3 start, exit, target, control1, control2, side;
        Quaternion initialRotation, levelRotation;
        float age, riseTime, flightDuration, phase;
        double expiresAt;
        bool leased, airflow, rising;
        public uint Generation { get; private set; }
        public bool IsRising => leased && rising;

        public void Initialize(BallLauncher launcher)
        {
            owner = launcher;
            body = GetComponent<Rigidbody>();
            body.constraints |= RigidbodyConstraints.FreezeRotation;
            catchable = GetComponent<Catchable>();
            visual = GetComponent<KinoBallNumber>();
            initialRotation = transform.localRotation;
            catchable.SetPool(this);
            if (visual && visual.numberLabel)
            {
                // Reserve the two-digit glyph buffer while the pool is warming.
                visual.SetNumber(80, null);
                visual.numberLabel.ForceMeshUpdate(true, true);
            }
            ResetState();
            gameObject.SetActive(false);
        }

        internal void Activate(Transform spawn, Transform outlet, int number)
        {
            Generation++;
            leased = airflow = true;
            rising = outlet;
            age = 0;
            phase = Random.Range(0f, Mathf.PI * 2);
            riseTime = Mathf.Max(.2f, owner.EffectiveTubeRiseTime);
            flightDuration = Mathf.Max(.2f, owner.EffectiveFlightTime * Random.Range(1 - owner.speedVariance, 1 + owner.speedVariance));
            expiresAt = Time.timeAsDouble + Mathf.Max(1, owner.ballLifetime);
            start = spawn.position;
            exit = outlet ? outlet.position : start;
            transform.SetPositionAndRotation(start, initialRotation);
            body.position = start;
            body.rotation = initialRotation;
            levelRotation = FacingPlayer(start, initialRotation);
            body.rotation = levelRotation;
            body.useGravity = false;
            body.isKinematic = rising;
            body.detectCollisions = !rising;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = rising ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
            catchable.Configure(number, owner.round);
            if (visual) visual.SetNumber(number, owner.player);
            gameObject.SetActive(true);
            if (!rising) BeginFlight();
        }

        void Update()
        {
            if (leased && Time.timeAsDouble >= expiresAt) ReturnToPool(Generation);
        }
        void FixedUpdate()
        {
            if (!leased) return;
            KeepLevel();
            if (!airflow) return;
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (rising)
            {
                float t = Mathf.Clamp01(age / riseTime);
                float lift = t * t * (2 - t);
                // Stay within 3 cm of the shaft axis and clear the rim before turning.
                float envelope = Mathf.Sin(Mathf.PI * t);
                var swirl = new Vector3(Mathf.Sin(phase + age * 5.1f), 0, Mathf.Cos(phase * 1.3f + age * 4.3f));
                body.MovePosition(Vector3.Lerp(start, exit, lift) + swirl * (.02f * envelope * envelope));
                if (t >= 1)
                {
                    body.position = exit;
                    BeginFlight();
                }
                return;
            }

            float u = Mathf.Clamp01(age / flightDuration);
            if (age > flightDuration + .35f) { StopAirflow(); return; }
            float v = 1 - u;
            Vector3 path = v * v * v * exit + 3 * v * v * u * control1 + 3 * v * u * u * control2 + u * u * u * target;
            Vector3 velocity = (3 * v * v * (control1 - exit) + 6 * v * u * (control2 - control1) + 3 * u * u * (target - control2)) / flightDuration;
            // Broad, smooth eddies; less movement near the catching zone.
            float envelopeFlight = Mathf.Sin(Mathf.PI * u);
            float windTime = age * owner.turbulenceFrequency;
            Vector3 eddy = side * Mathf.Sin(phase + windTime * 4.1f) + Vector3.up * (.55f * Mathf.Sin(phase * 1.7f + windTime * 5.3f));
            path += eddy * (owner.turbulence * envelopeFlight);
            Vector3 desired = velocity + (path - body.position) * 4;
            float fade = 1 - Mathf.Clamp01((age - flightDuration) / .35f);
            body.AddForce(Vector3.ClampMagnitude((desired - body.linearVelocity) * 6, owner.exitAcceleration) * fade, ForceMode.Acceleration);
        }

        void BeginFlight()
        {
            bool fromTube = rising;
            rising = false;
            age = 0;
            body.isKinematic = false;
            body.detectCollisions = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = fromTube ? (exit - start) / riseTime : Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Vector3 forward = Vector3.ProjectOnPlane(owner.player.forward, Vector3.up).normalized;
            Vector2 offset = Random.insideUnitCircle * owner.missRadius;
            side = Vector3.Cross(Vector3.up, (owner.player.position - exit).normalized).normalized;
            // Sample once on exiting the tube, so flight does not chase head turns.
            target = owner.player.position + forward * .65f + Vector3.up * owner.aimHeightOffset + side * offset.x + Vector3.up * (offset.y * .5f);
            Vector3 away = Vector3.ProjectOnPlane(exit - target, Vector3.up).normalized;
            // The first tangent already points inward, so wind accelerates towards
            // the player as soon as the ball clears the rim, with only a small lift.
            float departure = Mathf.Min(Vector3.Distance(exit, target) * .28f, 4);
            control1 = exit - away * departure + Vector3.up * .25f;
            control2 = target + away * 3 + Vector3.up * .8f;
        }

        Quaternion FacingPlayer(Vector3 position, Quaternion fallback)
        {
            if (!owner || !owner.player) return fallback;
            var away = Vector3.ProjectOnPlane(position - owner.player.position, Vector3.up);
            // Keep the last heading through the catch zone instead of flipping as
            // a missed ball passes beside/behind the head. The number still faces it.
            return away.sqrMagnitude > .36f ? Quaternion.LookRotation(away, Vector3.up) : fallback;
        }
        void KeepLevel()
        {
            levelRotation = Quaternion.RotateTowards(levelRotation, FacingPlayer(body.position, levelRotation), 90 * Time.fixedDeltaTime);
            float clock = Time.time * 2;
            float angle = owner.wobbleAngle;
            var wobble = Quaternion.Euler(angle * .65f * Mathf.Sin(phase + clock),
                angle * .35f * Mathf.Sin(phase + clock * .7f), angle * Mathf.Sin(phase * 1.3f + clock * .85f));
            body.MoveRotation(levelRotation * wobble);
        }

        public void StopAirflow()
        {
            airflow = rising = false;
            body.isKinematic = false;
            body.detectCollisions = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = true;
        }
        public void ReturnToPool(uint generation)
        {
            if (!leased || generation != Generation) return;
            leased = false;
            ResetState();
            gameObject.SetActive(false);
            if (owner) owner.Recycle(this);
        }
        void ResetState()
        {
            airflow = rising = false;
            expiresAt = 0;
            body.isKinematic = false;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.detectCollisions = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.rotation = initialRotation;
            catchable.ResetForPool();
            if (visual) visual.SetNumber(1, null);
        }
        void OnDisable() { if (leased) ReturnToPool(Generation); }
    }
}
