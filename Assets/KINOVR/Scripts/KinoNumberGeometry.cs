using System;
using UnityEngine;

namespace KinoVR
{
    // Generated from the gameplay TMP label. Positions are in its face's local
    // space; UV.w stores TMP's signed SDF scale for a unit-scale ball.
    public sealed class KinoNumberGeometry : ScriptableObject
    {
        [Serializable]
        public sealed class NumberShape
        {
            public Vector3[] vertices;
            public Vector4[] uv;
            public Color32[] colors;
            public int[] triangles;
        }
        public Material material;
        public NumberShape[] numbers = Array.Empty<NumberShape>();
        public NumberShape Get(int number) => number >= 1 && number <= numbers.Length ? numbers[number - 1] : null;
    }
}
