using TMPro;
using UnityEngine;

namespace KinoVR
{
    public sealed class KinoBallNumber : MonoBehaviour
    {
        public TMP_Text numberLabel;
        public Transform face;
        public Material kinoBonusMaterial;
        [Tooltip("Baked oval mesh radii; the root and label keep uniform scale.")]
        public Vector3 surfaceRadii = new Vector3(.63f, .375f, .375f);
        [SerializeField, HideInInspector] int number = 1;
        public int Number => number;
        Transform viewer;
        MeshRenderer ballRenderer;
        Material normalMaterial;
        Color normalNumberColor;
        bool appearanceCached;

        public void SetBonus(bool isBonus)
        {
            if (!appearanceCached)
            {
                ballRenderer = GetComponent<MeshRenderer>();
                normalMaterial = ballRenderer ? ballRenderer.sharedMaterial : null;
                normalNumberColor = numberLabel ? numberLabel.color : Color.black;
                appearanceCached = true;
            }
            if (ballRenderer) ballRenderer.sharedMaterial = isBonus && kinoBonusMaterial ? kinoBonusMaterial : normalMaterial;
            if (numberLabel) numberLabel.color = isBonus ? Color.white : normalNumberColor;
        }
        static readonly string[] NumberText = MakeNumberText();
        static string[] MakeNumberText()
        {
            var text = new string[81];
            for (int i = 0; i < text.Length; i++) text[i] = i.ToString();
            return text;
        }
        internal Transform Viewer => viewer;
        public void SetNumber(int value, Transform view)
        {
            number = value;
            viewer = view;
            if (numberLabel) numberLabel.text = NumberText[Mathf.Clamp(number, 0, 80)];
            RefreshFacing();
        }
        void LateUpdate() => RefreshFacing();
        public void RefreshFacing()
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
