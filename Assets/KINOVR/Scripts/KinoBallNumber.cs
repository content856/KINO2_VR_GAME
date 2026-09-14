using TMPro;
using UnityEngine;

namespace KinoVR
{
    public sealed class KinoBallNumber : MonoBehaviour
    {
        public TMP_Text numberLabel;
        public Transform face;
        [Tooltip("Baked oval mesh radii; the root and label keep uniform scale.")]
        public Vector3 surfaceRadii = new Vector3(.63f, .375f, .375f);
        Transform viewer;
        public void SetNumber(int number, Transform view)
        {
            viewer = view;
            if (numberLabel) numberLabel.text = number.ToString();
            FaceViewer();
        }
        void LateUpdate() => FaceViewer();
        void FaceViewer()
        {
            if (!viewer || !face) return;
            Vector3 away = transform.position - viewer.position;
            if (away.sqrMagnitude <= .0001f) return;
            face.rotation = Quaternion.LookRotation(away, Vector3.up);
            // Keep the number centred in the silhouette, on the support plane toward
            // the viewer. The whole text plane stays outside the mesh at oblique angles.
            Vector3 direction = transform.InverseTransformDirection(-away.normalized);
            Vector3 scaled = Vector3.Scale(surfaceRadii, direction);
            float support = scaled.magnitude;
            if (support > .0001f)
                face.localPosition = direction * support;
        }
    }
}
