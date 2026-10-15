using UnityEngine;

namespace KinoVR
{
    /// <summary>The small UI-only contact at the tracked index tip, separate from ball-catching hands.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(SphereCollider))]
    public sealed class KinoFingerTip : MonoBehaviour
    {
        public const float Radius = .008f;
        SphereCollider contact;

        void Awake()
        {
            contact = GetComponent<SphereCollider>();
            contact.isTrigger = true;
            contact.radius = Radius;
        }

        internal static bool IsFinger(Collider collider) => collider && collider.enabled &&
            collider.gameObject.activeInHierarchy && collider.TryGetComponent<KinoFingerTip>(out var tip) && tip.isActiveAndEnabled;

        internal static bool IsTouching(BoxCollider area, Collider collider, float extraMetres = 0)
        {
            if (!area || !IsFinger(collider)) return false;
            var sphere = collider as SphereCollider;
            var center = sphere ? sphere.transform.TransformPoint(sphere.center) : collider.bounds.center;
            var local = area.transform.InverseTransformPoint(center) - area.center;
            var scale = area.transform.lossyScale;
            // Keep the tip's centre inside the visible row/key, so one finger cannot
            // select two neighbours. Only depth gets the fingertip's 8 mm reach.
            float x = extraMetres / Mathf.Max(.00001f, Mathf.Abs(scale.x));
            float y = extraMetres / Mathf.Max(.00001f, Mathf.Abs(scale.y));
            float z = (Radius + extraMetres) / Mathf.Max(.00001f, Mathf.Abs(scale.z));
            return Mathf.Abs(local.x) <= area.size.x * .5f + x &&
                Mathf.Abs(local.y) <= area.size.y * .5f + y &&
                Mathf.Abs(local.z) <= area.size.z * .5f + z;
        }
    }
}
