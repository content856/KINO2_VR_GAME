using System;
using System.Collections.Generic;
using UnityEngine;

namespace KinoVR
{
    // The two catching hands stay legible over every instruction background.
    // Gameplay keeps its original materials; the SDK still owns tracking visibility.
    internal sealed class KinoModeHands : IDisposable
    {
        sealed class Hand
        {
            public SkinnedMeshRenderer renderer;
            public OVRMeshRenderer driver;
            public Material original, menu;
            public int originalOrder;
            public bool applied;
        }

        readonly List<Hand> hands = new List<Hand>(2);
        bool disposed;

        /// <summary>True once the SDK is rendering at least one tracked hand (mesh loaded, data valid).</summary>
        public bool AnyHandVisible
        {
            get
            {
                foreach (var hand in hands)
                    if (hand.renderer && hand.renderer.enabled && hand.renderer.sharedMesh && hand.renderer.gameObject.activeInHierarchy) return true;
                return false;
            }
        }

        public KinoModeHands(Transform vrRig)
        {
            if (!vrRig) return;
            var seen = new HashSet<SkinnedMeshRenderer>();
            foreach (var catcher in vrRig.GetComponentsInChildren<HandCatcher>(true))
            {
                var trackedHand = catcher.GetComponentInParent<OVRHand>(true);
                if (!trackedHand || !trackedHand.transform.IsChildOf(vrRig)) continue;
                var renderer = trackedHand.GetComponent<SkinnedMeshRenderer>();
                var driver = trackedHand.GetComponent<OVRMeshRenderer>();
                if (!renderer || !driver || !seen.Add(renderer)) continue;
                hands.Add(new Hand { renderer = renderer, driver = driver,
                    original = renderer.sharedMaterial, originalOrder = renderer.sortingOrder });
            }
        }

        public void SetVisible(bool visible)
        {
            if (disposed) return;
            foreach (var hand in hands)
            {
                if (!hand.renderer) continue;
                if (!visible) { Restore(hand); continue; }
                if (hand.applied && hand.renderer.sharedMaterial == hand.menu) continue;
                // Permit the SDK to supply a material after this helper is constructed.
                if (!hand.original) hand.original = hand.renderer.sharedMaterial;
                if (!hand.original) continue;
                if (!hand.menu)
                    // Preserve the original shader, textures, colour and lighting.
                    // Only the draw queue changes so the black enclosure cannot cover it.
                    hand.menu = new Material(hand.original)
                    {
                        name = hand.original.name + " (above instruction background)",
                        hideFlags = HideFlags.DontSave,
                        renderQueue = 3000
                    };
                // Updating the SDK's cache prevents its Update from undoing the swap.
                if (hand.driver) hand.driver.SetMaterial(hand.menu);
                hand.renderer.sharedMaterial = hand.menu;
                // Retained ZWrite lets the later world UI correctly occlude a hand
                // behind a button, while a hand in front occludes the button.
                hand.renderer.sortingOrder = 75;
                hand.applied = true;
            }
        }

        static void Restore(Hand hand)
        {
            if (!hand.applied) return;
            if (hand.driver) hand.driver.SetMaterial(hand.original);
            if (hand.renderer)
            {
                hand.renderer.sharedMaterial = hand.original;
                hand.renderer.sortingOrder = hand.originalOrder;
            }
            hand.applied = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            foreach (var hand in hands)
            {
                Restore(hand);
                if (!hand.menu) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(hand.menu);
                else UnityEngine.Object.DestroyImmediate(hand.menu);
            }
            hands.Clear();
            disposed = true;
        }
    }
}
