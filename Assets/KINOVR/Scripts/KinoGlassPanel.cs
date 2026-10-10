using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    // Rounded glass reading panel for the blue environment: translucent navy fill,
    // a glowing blue edge and a faint top sheen. Mesh only; no textures.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class KinoGlassPanel : MaskableGraphic
    {
        [Min(0)] public float cornerRadius = 38;
        [Min(0)] public float edgeWidth = 3;
        [Min(0)] public float outerGlow = 26;
        [Min(0)] public float innerGlow = 18;
        public Color fill = new Color(.016f, .07f, .23f, .74f);
        public Color edge = new Color(.33f, .63f, 1f, 1f);
        [Range(0, 1)] public float glowAlpha = .45f;
        [Range(0, .3f)] public float sheen = .05f;

        const int CornerSteps = 10;
        static readonly List<Vector2> outerA = new List<Vector2>(), outerB = new List<Vector2>();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            var tint = color;
            Color Mul(Color c, float a = 1) => new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a * tint.a * a);

            // Fill: centre fan with a slightly lighter top half.
            Outline(r, edgeWidth, outerA);
            int centre = vh.currentVertCount;
            vh.AddVert(r.center, Mul(fill), Vector2.zero);
            int first = vh.currentVertCount;
            foreach (var p in outerA)
            {
                float top = Mathf.InverseLerp(r.center.y, r.yMax, p.y);
                var c = Color.Lerp(fill, new Color(fill.r + sheen, fill.g + sheen * 1.4f, fill.b + sheen * 2, fill.a), top);
                vh.AddVert(p, Mul(c), Vector2.zero);
            }
            for (int i = 0; i < outerA.Count; i++)
                vh.AddTriangle(centre, first + i, first + (i + 1) % outerA.Count);

            var glow = new Color(edge.r, edge.g, edge.b, 0);
            // Inner glow fades into the glass.
            Outline(r, edgeWidth + innerGlow, outerB);
            Ring(vh, outerA, outerB, Mul(edge, glowAlpha * .5f), Mul(glow));
            // Crisp edge.
            Outline(r, 0, outerB);
            Ring(vh, outerB, outerA, Mul(edge), Mul(edge));
            // Outer glow fades into the environment.
            Outline(r, -outerGlow, outerA);
            Ring(vh, outerB, outerA, Mul(edge, glowAlpha), Mul(glow));
        }

        static void Ring(VertexHelper vh, List<Vector2> a, List<Vector2> b, Color ca, Color cb)
        {
            int start = vh.currentVertCount, n = a.Count;
            for (int i = 0; i < n; i++) { vh.AddVert(a[i], ca, Vector2.zero); vh.AddVert(b[i], cb, Vector2.zero); }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a0 = start + i * 2, b0 = a0 + 1, a1 = start + j * 2, b1 = a1 + 1;
                vh.AddTriangle(a0, b0, a1); vh.AddTriangle(a1, b0, b1);
            }
        }

        // Rounded-rectangle outline inset by `inset` (negative grows outward); same point count for every inset.
        void Outline(Rect rect, float inset, List<Vector2> points)
        {
            points.Clear();
            float radius = Mathf.Max(0, Mathf.Min(cornerRadius - inset, Mathf.Min(rect.width, rect.height) * .5f - inset));
            var inner = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2, rect.height - inset * 2);
            var centres = new[]
            {
                new Vector2(inner.xMax - radius, inner.yMax - radius),
                new Vector2(inner.xMin + radius, inner.yMax - radius),
                new Vector2(inner.xMin + radius, inner.yMin + radius),
                new Vector2(inner.xMax - radius, inner.yMin + radius),
            };
            for (int corner = 0; corner < 4; corner++)
                for (int step = 0; step <= CornerSteps; step++)
                {
                    float angle = (corner + step / (float)CornerSteps) * Mathf.PI * .5f;
                    points.Add(centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
        }

#if UNITY_EDITOR
        protected override void OnValidate() { base.OnValidate(); SetVerticesDirty(); }
#endif
    }
}
