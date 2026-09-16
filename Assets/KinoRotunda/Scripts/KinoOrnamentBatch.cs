using UnityEngine;

namespace KinoRotunda
{
    // Disable a batch to restore its editable source ornaments for animation.
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    public sealed class KinoOrnamentBatch : MonoBehaviour
    {
        public MeshRenderer[] sources;
        void OnEnable() => SetCombined(true);
        void OnDisable() => SetCombined(false);
        void SetCombined(bool combined)
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer) renderer.enabled = combined;
            if (sources == null) return;
            foreach (var source in sources) if (source) source.enabled = !combined;
        }
    }
}
