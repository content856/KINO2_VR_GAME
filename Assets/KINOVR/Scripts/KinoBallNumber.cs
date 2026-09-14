using TMPro;
using UnityEngine;

namespace KinoVR
{
    public sealed class KinoBallNumber : MonoBehaviour
    {
        public TMP_Text numberLabel;
        public Transform face;
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
            if (away.sqrMagnitude > .0001f) face.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
