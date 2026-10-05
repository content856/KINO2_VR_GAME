using UnityEngine;
using UnityEngine.UI;

namespace KinoVR
{
    // Mesh-only primitives. Geometry changes only when Unity dirties the Graphic;
    // no textures, materials, objects or arrays are allocated by these helpers.
    internal static class KinoPanelMesh
    {
        const int RoundSteps = 9;
        internal static Rect Inset(Rect r, float d) { return new Rect(r.x + d, r.y + d, r.width - d * 2, r.height - d * 2); }

        internal static void Fit(VertexHelper vh, Rect rect, Vector2 reference, Color tint)
        {
            var vertex = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.position = new Vector3(rect.center.x + vertex.position.x * rect.width / reference.x,
                    rect.center.y + vertex.position.y * rect.height / reference.y, 0);
                vertex.color = (Color)vertex.color * tint;
                vh.SetUIVertex(vertex, i);
            }
        }

        internal static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, ca, Vector2.zero); vh.AddVert(b, cb, Vector2.zero); vh.AddVert(c, cc, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }

        internal static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color ca, Color cb)
        {
            Vector2 dir = (b - a).normalized;
            Vector2 offset = new Vector2(-dir.y, dir.x) * (width * .5f);
            int start = vh.currentVertCount;
            vh.AddVert(a - offset, ca, Vector2.zero); vh.AddVert(a + offset, ca, Vector2.zero);
            vh.AddVert(b - offset, cb, Vector2.zero); vh.AddVert(b + offset, cb, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start + 2, start + 1, start + 3);
        }

        internal static void Diamond(VertexHelper vh, Vector2 centre, Vector2 radius, Color color)
        {
            int start = vh.currentVertCount;
            vh.AddVert(centre + new Vector2(0, radius.y), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(radius.x, 0), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(0, -radius.y), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(-radius.x, 0), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }

        internal static void Glow(VertexHelper vh, Vector2 centre, Vector2 radius, Color color)
        {
            const int steps = 32;
            int start = vh.currentVertCount;
            vh.AddVert(centre, color, Vector2.zero);
            color.a = 0;
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2 / steps;
                vh.AddVert(centre + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y), color, Vector2.zero);
            }
            for (int i = 0; i < steps; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % steps);
        }

        internal static void Spark(VertexHelper vh, Vector2 centre, float width, float height, Color color)
        {
            Glow(vh, centre, new Vector2(width * .7f, height * .95f), color * new Color(1, 1, 1, .25f));
            int start = vh.currentVertCount;
            vh.AddVert(centre, color, Vector2.zero);
            Color edge = color; edge.a = 0;
            vh.AddVert(centre + new Vector2(-width, 0), edge, Vector2.zero);
            vh.AddVert(centre + new Vector2(0, height), edge, Vector2.zero);
            vh.AddVert(centre + new Vector2(width, 0), edge, Vector2.zero);
            vh.AddVert(centre + new Vector2(0, -height), edge, Vector2.zero);
            for (int i = 0; i < 4; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 4);
            Diamond(vh, centre, new Vector2(2.1f, 2.1f), new Color(1, 1, .86f, color.a));
        }

        internal static void ChamferFill(VertexHelper vh, Rect rect, float cut, Color bottom, Color top) { Fill(vh, rect, cut, bottom, top, false); }
        internal static void RoundedFill(VertexHelper vh, Rect rect, float radius, Color bottom, Color top) { Fill(vh, rect, radius, bottom, top, true); }

        static void Fill(VertexHelper vh, Rect rect, float radius, Color bottom, Color top, bool round)
        {
            int count = round ? 4 * (RoundSteps + 1) : 8;
            int start = vh.currentVertCount;
            vh.AddVert(rect.center, Color.Lerp(bottom, top, .5f), Vector2.zero);
            for (int i = 0; i < count; i++)
            {
                Vector2 p = round ? RoundPoint(rect, radius, i) : ChamferPoint(rect, radius, i);
                vh.AddVert(p, Color.Lerp(bottom, top, Mathf.InverseLerp(rect.yMin, rect.yMax, p.y)), Vector2.zero);
            }
            for (int i = 0; i < count; i++) vh.AddTriangle(start, start + 1 + (i + 1) % count, start + 1 + i);
        }

        internal static void ChamferRing(VertexHelper vh, Rect rect, float cut, float width, Color top, Color bottom) { Ring(vh, rect, cut, width, top, bottom, false); }
        internal static void RoundedRing(VertexHelper vh, Rect rect, float radius, float width, Color top, Color bottom) { Ring(vh, rect, radius, width, top, bottom, true); }

        static void Ring(VertexHelper vh, Rect rect, float radius, float width, Color top, Color bottom, bool round)
        {
            int count = round ? 4 * (RoundSteps + 1) : 8;
            int start = vh.currentVertCount;
            Rect inner = Inset(rect, width);
            float innerRadius = Mathf.Max(0, radius - width * (round ? 1 : .6f));
            for (int i = 0; i < count; i++)
            {
                Vector2 p = round ? RoundPoint(rect, radius, i) : ChamferPoint(rect, radius, i);
                Color color = Color.Lerp(bottom, top, Mathf.InverseLerp(rect.yMin, rect.yMax, p.y));
                vh.AddVert(p, color, Vector2.zero);
                vh.AddVert(round ? RoundPoint(inner, innerRadius, i) : ChamferPoint(inner, innerRadius, i), color, Vector2.zero);
            }
            JoinStrip(vh, start, count, true);
        }

        static Vector2 ChamferPoint(Rect r, float cut, int index)
        {
            switch (index)
            {
                case 0: return new Vector2(r.xMin + cut, r.yMin);
                case 1: return new Vector2(r.xMax - cut, r.yMin);
                case 2: return new Vector2(r.xMax, r.yMin + cut);
                case 3: return new Vector2(r.xMax, r.yMax - cut);
                case 4: return new Vector2(r.xMax - cut, r.yMax);
                case 5: return new Vector2(r.xMin + cut, r.yMax);
                case 6: return new Vector2(r.xMin, r.yMax - cut);
                default: return new Vector2(r.xMin, r.yMin + cut);
            }
        }

        static Vector2 RoundPoint(Rect rect, float radius, int index)
        {
            int corner = index / (RoundSteps + 1);
            float angle = (-90 + corner * 90 + (index % (RoundSteps + 1)) * 90f / RoundSteps) * Mathf.Deg2Rad;
            Vector2 centre = new Vector2(corner < 2 ? rect.xMax - radius : rect.xMin + radius,
                corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius);
            return centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        internal static void Arc(VertexHelper vh, Vector2 centre, Vector2 radius, float from, float to,
            float width, Color startColor, Color endColor, int steps)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float angle = Mathf.Lerp(from, to, t) * Mathf.Deg2Rad;
                Vector2 normal = new Vector2(Mathf.Cos(angle) / radius.x, Mathf.Sin(angle) / radius.y).normalized;
                Vector2 point = centre + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);
                Color color = Color.Lerp(startColor, endColor, t);
                vh.AddVert(point - normal * width * .5f, color, Vector2.zero);
                vh.AddVert(point + normal * width * .5f, color, Vector2.zero);
            }
            JoinStrip(vh, start, steps + 1, false);
        }

        internal static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        internal static void Curve(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            float width, Color startColor, Color endColor, int steps)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1 - t;
                Vector2 point = Bezier(a, b, c, d, t);
                Vector2 tangent = (3 * u * u * (b - a) + 6 * u * t * (c - b) + 3 * t * t * (d - c)).normalized;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                Color color = Color.Lerp(startColor, endColor, t);
                vh.AddVert(point - normal * width * .5f, color, Vector2.zero);
                vh.AddVert(point + normal * width * .5f, color, Vector2.zero);
            }
            JoinStrip(vh, start, steps + 1, false);
        }

        internal static void Leaf(VertexHelper vh, Vector2 root, Vector2 tip, float width, Color shade, Color highlight)
        {
            const int steps = 9;
            int start = vh.currentVertCount;
            Vector2 direction = tip - root;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float belly = Mathf.Sin(t * Mathf.PI);
                Vector2 point = Vector2.Lerp(root, tip, t) + normal * belly * width * .25f;
                float halfWidth = Mathf.Pow(Mathf.Max(0, belly), 1.15f) * width;
                Color ridge = Color.Lerp(shade, highlight, Mathf.Sin(t * Mathf.PI * .75f));
                vh.AddVert(point - normal * halfWidth, shade, Vector2.zero);
                vh.AddVert(point, ridge, Vector2.zero);
                vh.AddVert(point + normal * halfWidth * .65f, highlight, Vector2.zero);
            }
            for (int i = 0; i < steps; i++)
            {
                int a = start + i * 3;
                for (int side = 0; side < 2; side++)
                {
                    vh.AddTriangle(a + side, a + side + 1, a + side + 3);
                    vh.AddTriangle(a + side + 3, a + side + 1, a + side + 4);
                }
            }
        }

        static void JoinStrip(VertexHelper vh, int start, int count, bool closed)
        {
            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                int b = start + ((i + 1) % count) * 2;
                vh.AddTriangle(a, a + 1, b); vh.AddTriangle(b, a + 1, b + 1);
            }
        }
    }
}
