using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace KinoVR
{
    // All copies are made once; shared project materials and baked lighting stay intact.
    public sealed class KinoBoostPresentation : MonoBehaviour
    {
        public KinoNumberBoard board;
        public Graphic[] panels;
        public CanvasGroup announcement;
        public GameObject activeBadge;
        public GameObject normalBrand;
        public GameObject goldAccents;
        public AudioSource announcementAudio;
        [Min(.5f)] public float announcementSeconds = 2.2f;
        public bool IsBoostActive { get; private set; }

        readonly List<Material> panelCopies = new List<Material>();
        readonly Dictionary<Material, Material> ledCopies = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> marbleCopies = new Dictionary<Material, Material>();
        readonly Dictionary<Renderer, Material[]> originalSurfaces = new Dictionary<Renderer, Material[]>();
        readonly List<Material> originalPanels = new List<Material>();
        Volume volume;
        VolumeProfile profile;
        float transition, announcedAt;
        bool initialized;

        void Awake() => Initialize();

        void Initialize()
        {
            if (initialized) return;
            initialized = true;
            foreach (var panel in panels)
            {
                originalPanels.Add(panel ? panel.material : null);
                if (!panel) { panelCopies.Add(null); continue; }
                var copy = new Material(panel.material) { name = panel.material.name + " (runtime boost)" };
                panel.material = copy;
                panelCopies.Add(copy);
            }
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled) continue;
                var originals = renderer.sharedMaterials;
                var replacement = (Material[])originals.Clone();
                bool changed = false;
                for (int i = 0; i < originals.Length; i++)
                {
                    var source = originals[i];
                    if (!source) continue;
                    bool led = source.name == "WarmLED" || source.name == "CeilingLED";
                    bool marble = source.name == "NeroMarble" || source.name == "IvoryMarble";
                    if (!led && !marble) continue;
                    var copies = led ? ledCopies : marbleCopies;
                    if (!copies.TryGetValue(source, out var copy))
                    {
                        copy = new Material(source) { name = source.name + " (runtime boost)" };
                        copies.Add(source, copy);
                    }
                    replacement[i] = copy;
                    changed = true;
                }
                if (!changed) continue;
                originalSurfaces.Add(renderer, originals);
                renderer.sharedMaterials = replacement;
            }
            var lighting = new GameObject("BOOST warm grading");
            lighting.transform.SetParent(transform, false);
            volume = lighting.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20;
            volume.weight = 0;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            var grade = profile.Add<ColorAdjustments>();
            grade.colorFilter.Override(new Color(1, .86f, .65f));
            grade.saturation.Override(4);
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(.65f);
            bloom.tint.Override(new Color(1, .72f, .28f));
        }
        public void SetBoost(bool active)
        {
            if (active && !initialized) Initialize();
            bool entering = active && !IsBoostActive;
            IsBoostActive = active;
            if (activeBadge) activeBadge.SetActive(active);
            if (normalBrand) normalBrand.SetActive(!active);
            if (goldAccents) goldAccents.SetActive(active);
            if (board) board.SetBoostColors(active);
            if (entering)
            {
                announcedAt = Time.unscaledTime;
                if (announcementAudio && announcementAudio.clip) announcementAudio.Play();
            }
            if (announcement)
            {
                announcement.gameObject.SetActive(active);
                announcement.alpha = active ? 1 : 0;
            }
            if (!active)
            {
                transition = 0;
                ApplyLighting(0);
                if (announcementAudio) announcementAudio.Stop();
            }
        }
        void Update()
        {
            if (!IsBoostActive) return;
            if (transition < 1)
            {
                transition = Mathf.MoveTowards(transition, 1, Time.deltaTime / .3f);
                ApplyLighting(transition);
            }
            if (announcement && announcement.gameObject.activeSelf)
            {
                float elapsed = Time.unscaledTime - announcedAt;
                announcement.alpha = Mathf.Clamp01((announcementSeconds - elapsed) / .35f);
                if (elapsed >= announcementSeconds) announcement.gameObject.SetActive(false);
            }
        }
        void ApplyLighting(float amount)
        {
            foreach (var panel in panelCopies) if (panel) panel.SetFloat("_BoostStrength", amount);
            foreach (var pair in ledCopies)
            {
                var original = pair.Key.GetColor("_EmissionColor");
                // Preserve a gold hue even on the Quest camera's LDR path.
                var golden = new Color(3, .5f, .03f);
                pair.Value.SetColor("_EmissionColor", Color.Lerp(original, golden, amount));
            }
            foreach (var pair in marbleCopies)
            {
                var original = pair.Key.GetColor("_BaseColor");
                var golden = pair.Key.name == "NeroMarble" ? new Color(1.05f, .64f, .18f, original.a) :
                    original * new Color(.58f, .4f, .17f, 1);
                pair.Value.SetColor("_BaseColor", Color.Lerp(original, golden, amount));
            }
            if (volume) volume.weight = amount;
        }
        // Used by the explicit editor preview command, without entering the game clock.
        public void PreviewBoost()
        {
            SetBoost(true);
            transition = 1;
            ApplyLighting(1);
        }
        void OnDisable() => SetBoost(false);
        void OnDestroy()
        {
            for (int i = 0; i < originalPanels.Count; i++) if (panels[i]) panels[i].material = originalPanels[i];
            foreach (var pair in originalSurfaces) if (pair.Key) pair.Key.sharedMaterials = pair.Value;
            foreach (var material in panelCopies) Release(material);
            foreach (var material in ledCopies.Values) Release(material);
            foreach (var material in marbleCopies.Values) Release(material);
            if (profile)
            {
                foreach (var component in profile.components) Release(component);
                Release(profile);
            }
            if (volume) Release(volume.gameObject);
        }
        static void Release(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
