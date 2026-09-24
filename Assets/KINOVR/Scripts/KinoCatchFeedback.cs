using TMPro;
using UnityEngine;

namespace KinoVR
{
    // World-space contact feedback. Labels are prewarmed and reused, never parented
    // to a recycled ball or to the hand that caught it.
    public sealed class KinoCatchFeedback : MonoBehaviour
    {
        public TMP_FontAsset font;
        public Material textMaterial;
        const int Capacity = 16;
        const float Lifetime = .95f;
        readonly TextMeshPro[] labels = new TextMeshPro[Capacity];
        readonly Vector3[] origins = new Vector3[Capacity];
        readonly float[] born = new float[Capacity];
        readonly Color[] colors = new Color[Capacity];
        TextMeshPro timer;
        Transform viewer;
        int next;
        public string LastText { get; private set; }
        public Vector3 LastContactPoint { get; private set; }
        public int ShownCount { get; private set; }
        static readonly Color Gold = new Color(1, .87f, .24f);
        static readonly Color Mystery = new Color(.85f, .69f, 1);

        void Awake() => Warm();
        void Warm()
        {
            if (timer || !font) return;
            for (int i = 0; i < Capacity; i++) labels[i] = MakeLabel("Catch popup " + i, 6);
            timer = MakeLabel("Active Mystery multiplier", 4);
        }
        TextMeshPro MakeLabel(string title, float size)
        {
            var go = new GameObject(title);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * .16f;
            var label = go.AddComponent<TextMeshPro>();
            label.font = font;
            if (textMaterial) label.fontSharedMaterial = textMaterial;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(12, 2);
            label.text = "+1 +2 +3 ×2 ×3 ×4 9.9s";
            label.ForceMeshUpdate();
            go.SetActive(false);
            return label;
        }
        public void ResetFeedback(Transform view)
        {
            Warm();
            viewer = view;
            next = ShownCount = 0; LastText = null;
            foreach (var label in labels) if (label) label.gameObject.SetActive(false);
            if (!timer) return;
            timer.gameObject.SetActive(false);
            if (viewer)
            {
                var forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
                timer.transform.position = viewer.position + forward * 2.2f - Vector3.up * .35f;
            }
        }
        public void Show(Vector3 position, Catchable.BallType type, int multiplier)
        {
            Warm();
            if (!timer) return;
            string text = type == Catchable.BallType.Mystery ? "×" + Mathf.Clamp(multiplier, 2, 4) :
                type == Catchable.BallType.MoreWins ? "+2" : type == Catchable.BallType.Normal ? "+1" : "+3";
            int index = next++ % Capacity;
            var label = labels[index];
            origins[index] = position;
            born[index] = Time.time;
            colors[index] = type == Catchable.BallType.Mystery ? Mystery : type == Catchable.BallType.MoreWins ? Gold : Color.white;
            label.text = text;
            label.color = colors[index];
            label.transform.position = position;
            Face(label);
            label.gameObject.SetActive(true);
            LastText = text; LastContactPoint = position; ShownCount++;
        }
        public void Present(KinoRoundState state)
        {
            if (!timer) return;
            bool visible = state.IsRunning && (state.Phase == KinoRoundPhase.Main || state.Phase == KinoRoundPhase.Settling) && state.ActiveMultiplier > 1;
            timer.gameObject.SetActive(visible);
            if (!visible) return;
            timer.color = Mystery;
            timer.SetText("×{0}   {1:1}s", state.ActiveMultiplier, state.MultiplierRemainingSeconds);
        }
        void Face(TMP_Text label)
        {
            if (!viewer) return;
            Vector3 away = label.transform.position - viewer.position;
            if (away.sqrMagnitude > .0001f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
        void LateUpdate()
        {
            for (int i = 0; i < Capacity; i++)
            {
                var label = labels[i];
                if (!label || !label.gameObject.activeSelf) continue;
                float age = Time.time - born[i];
                if (age >= Lifetime) { label.gameObject.SetActive(false); continue; }
                label.transform.position = origins[i] + Vector3.up * (.16f * age / Lifetime);
                var color = colors[i];
                color.a = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, Lifetime, age));
                label.color = color;
                Face(label);
            }
            if (timer && timer.gameObject.activeSelf) Face(timer);
        }
        void OnDisable()
        {
            foreach (var label in labels) if (label) label.gameObject.SetActive(false);
            if (timer) timer.gameObject.SetActive(false);
        }
    }
}
