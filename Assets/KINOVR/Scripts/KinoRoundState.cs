using System;
using System.Collections.Generic;

namespace KinoVR
{
    // Scene-independent catch and deadline rules.
    public sealed class KinoRoundState
    {
        readonly HashSet<int> numbers = new HashSet<int>();
        double deadline;
        public bool IsRunning { get; private set; }
        public int CatchCount { get; private set; }
        public int UniqueCount => numbers.Count;
        public float RemainingSeconds { get; private set; }
        public bool HasCaught(int number) => numbers.Contains(number);
        public void Begin(float duration, double now)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            numbers.Clear();
            CatchCount = 0;
            RemainingSeconds = duration;
            deadline = now + duration;
            IsRunning = true;
        }
        public void Tick(double now)
        {
            if (!IsRunning) return;
            RemainingSeconds = (float)Math.Max(0, deadline - now);
            if (now >= deadline) IsRunning = false;
        }
        public bool TryCatch(int number, double now)
        {
            Tick(now);
            if (!IsRunning || number < 1 || number > 80) return false;
            CatchCount++;
            numbers.Add(number);
            return true;
        }
        public void Stop()
        {
            IsRunning = false;
            RemainingSeconds = 0;
        }
    }
}
