using System;
using UnityEngine;

namespace KinoRotunda.Editor
{
    /// <summary>Analysis for readable, linear, monoscopic equirectangular 360-degree images.</summary>
    public static class KinoPanoramaAnalysis
    {
        public struct SunEstimate
        {
            /// <summary>Texture UV: v=0 is the nadir, v=0.5 is the horizon.</summary>
            public Vector2 uv;
            public float confidence;
            public bool reliable;
        }

        struct BrightRegion
        {
            public int pixels;
            public double mass, x, y, z, area;
        }

        /// <summary>
        /// Estimates a visible solar highlight, not a physically recovered HDR light source.
        /// Supply an already downsampled linear texture, preferably 1024x512. Never mutates it.
        /// Below-horizon pixels are excluded, and bright connected regions wrap across u=0/1.
        /// Confidence combines peak contrast against the sky's 90th luminance percentile,
        /// angular compactness, dominance over other highlights and a usable azimuth.
        /// Uniform/overcast skies, broad clouds, competing highlights and one-pixel peaks
        /// remain unreliable; callers should preserve their current rotation or ask for a UV.
        /// This is a conservative image heuristic; it cannot prove that a highlight is the sun.
        /// </summary>
        public static SunEstimate EstimateSun(Texture2D linearPanorama)
        {
            if (!linearPanorama) throw new ArgumentNullException(nameof(linearPanorama));
            if (!linearPanorama.isReadable)
                throw new ArgumentException("Sun analysis requires a readable linear texture.", nameof(linearPanorama));
            int width = linearPanorama.width, height = linearPanorama.height;
            if (width < 8 || height < 4 || width > 1024 || height > 512)
                throw new ArgumentException("Downsample the panorama to between 8x4 and 1024x512 before analysis.", nameof(linearPanorama));

            var result = new SunEstimate { uv = new Vector2(0.5f, 0.75f) };
            Color[] colors = linearPanorama.GetPixels();
            // Pixel centres on or above the horizon only. GetPixels starts at bottom-left.
            int firstSkyRow = height / 2;
            int skyHeight = height - firstSkyRow;
            int count = width * skyHeight;
            var luminance = new float[count];
            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                Color color = colors[firstSkyRow * width + i];
                float value = 0.2126f * NonnegativeFinite(color.r)
                            + 0.7152f * NonnegativeFinite(color.g)
                            + 0.0722f * NonnegativeFinite(color.b);
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                luminance[i] = value;
                peak = Mathf.Max(peak, value);
            }
            if (peak <= 1e-8f) return result;

            var sorted = (float[])luminance.Clone();
            Array.Sort(sorted);
            float background = sorted[Mathf.FloorToInt((count - 1) * 0.9f)];
            float relativeContrast = (peak - background) / peak;
            if (relativeContrast <= 0.12f) return result;
            float threshold = background + 0.65f * (peak - background);
            float inverseRange = 1f / (peak - threshold);

            // Cache the spherical coordinates. cos(elevation) is the solid-angle correction
            // that prevents stretched pixels near the zenith from outweighing the horizon.
            var cosAzimuth = new float[width];
            var sinAzimuth = new float[width];
            var cosElevation = new float[skyHeight];
            var sinElevation = new float[skyHeight];
            for (int x = 0; x < width; x++)
            {
                float azimuth = (0.5f - (x + 0.5f) / width) * 2f * Mathf.PI;
                cosAzimuth[x] = Mathf.Cos(azimuth);
                sinAzimuth[x] = Mathf.Sin(azimuth);
            }
            for (int y = 0; y < skyHeight; y++)
            {
                float elevation = ((y + firstSkyRow + 0.5f) / height - 0.5f) * Mathf.PI;
                cosElevation[y] = Mathf.Max(0f, Mathf.Cos(elevation));
                sinElevation[y] = Mathf.Sin(elevation);
            }

            var visited = new bool[count];
            var queue = new int[count];
            BrightRegion best = default;
            double secondMass = 0;
            double pixelSolidAngle = (2.0 * Math.PI / width) * (Math.PI / height);
            for (int seed = 0; seed < count; seed++)
            {
                if (visited[seed] || luminance[seed] <= threshold) continue;
                BrightRegion region = default;
                int head = 0, tail = 1;
                queue[0] = seed;
                visited[seed] = true;
                while (head < tail)
                {
                    int index = queue[head++], x = index % width, y = index / width;
                    double excess = (luminance[index] - threshold) * inverseRange;
                    double weight = excess * excess * cosElevation[y];
                    region.pixels++;
                    region.mass += weight;
                    region.x += weight * cosElevation[y] * cosAzimuth[x];
                    region.y += weight * sinElevation[y];
                    region.z += weight * cosElevation[y] * sinAzimuth[x];
                    region.area += cosElevation[y] * pixelSolidAngle;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= skyHeight) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = (x + dx + width) % width;
                            int neighbor = ny * width + nx;
                            if (visited[neighbor] || luminance[neighbor] <= threshold) continue;
                            visited[neighbor] = true;
                            queue[tail++] = neighbor;
                        }
                    }
                }
                if (region.mass > best.mass)
                {
                    secondMass = best.mass;
                    best = region;
                }
                else if (region.mass > secondMass) secondMass = region.mass;
            }
            if (best.mass <= 1e-12) return result;

            double length = Math.Sqrt(best.x * best.x + best.y * best.y + best.z * best.z);
            if (length <= 1e-12) return result;
            var direction = new Vector3((float)(best.x / length), (float)(best.y / length), (float)(best.z / length));
            result.uv = DirectionToUv(direction);
            // Use both occupied solid angle and angular spread, so long bright cloud strips
            // are rejected even when their area alone is small.
            float areaRadius = (float)Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - best.area / (2.0 * Math.PI)))) * Mathf.Rad2Deg;
            float spreadRadius = (float)Math.Acos(Math.Max(-1.0, Math.Min(1.0, length / best.mass))) * Mathf.Rad2Deg * 1.414214f;
            float contrast = SmoothRange(0.12f, 0.5f, relativeContrast);
            float compactness = 1f - SmoothRange(3f, 10f, Mathf.Max(areaRadius, spreadRadius));
            float dominance = SmoothRange(0.55f, 0.9f, (float)(best.mass / (best.mass + secondMass)));
            float azimuthVisibility = SmoothRange(0.01f, 0.12f, new Vector2(direction.x, direction.z).magnitude);
            float support = best.pixels >= 2 ? 1f : 0.35f;
            result.confidence = Mathf.Clamp01(contrast * compactness * dominance * azimuthVisibility * support);
            result.reliable = result.confidence >= 0.65f;
            return result;
        }

        /// <summary>
        /// Inverse of Unity's built-in Skybox/Panoramic ToRadialCoords:
        /// u=.5-atan2(z,x)/(2*pi), v=1-acos(y)/pi. This is not a camera yaw convention.
        /// </summary>
        public static Vector3 UvToDirection(Vector2 uv)
        {
            if (!Finite(uv.x) || !Finite(uv.y)) throw new ArgumentException("Panorama UV must be finite.", nameof(uv));
            float azimuth = (0.5f - Mathf.Repeat(uv.x, 1f)) * 2f * Mathf.PI;
            float elevation = (Mathf.Clamp01(uv.y) - 0.5f) * Mathf.PI;
            float horizontal = Mathf.Cos(elevation);
            return new Vector3(horizontal * Mathf.Cos(azimuth), Mathf.Sin(elevation), horizontal * Mathf.Sin(azimuth));
        }

        /// <summary>
        /// Returns _Rotation in [0,360), aligning only the panorama's sun azimuth.
        /// worldDirection points TOWARD the sun: use -directionalLight.transform.forward.
        /// Neither the light nor the horizon is tilted, so photograph/sun elevations may differ.
        /// Unity's shader rotates geometry x'=cos*x-sin*z, z'=sin*x+cos*z, while sampling
        /// its original coordinates. Therefore rotation is world atan2(z,x) minus source.
        /// Verified against Unity-Technologies/SkyboxPanoramicShader/Skybox-PanoramicBeta.shader.
        /// </summary>
        public static float RotationToMatchSun(Vector2 uv, Vector3 worldDirection)
        {
            if (!Finite(worldDirection.x) || !Finite(worldDirection.y) || !Finite(worldDirection.z))
                throw new ArgumentException("World sun direction must be finite.", nameof(worldDirection));
            Vector3 source = UvToDirection(uv);
            if (source.x * source.x + source.z * source.z < 1e-8f)
                throw new ArgumentException("A sun at the panorama pole has no azimuth.", nameof(uv));
            if (worldDirection.x * worldDirection.x + worldDirection.z * worldDirection.z < 1e-8f)
                throw new ArgumentException("World sun direction needs a horizontal component.", nameof(worldDirection));
            float sourceAzimuth = Mathf.Atan2(source.z, source.x) * Mathf.Rad2Deg;
            float worldAzimuth = Mathf.Atan2(worldDirection.z, worldDirection.x) * Mathf.Rad2Deg;
            return Mathf.Repeat(worldAzimuth - sourceAzimuth, 360f);
        }

        static Vector2 DirectionToUv(Vector3 direction)
        {
            return new Vector2(Mathf.Repeat(0.5f - Mathf.Atan2(direction.z, direction.x) / (2f * Mathf.PI), 1f),
                1f - Mathf.Acos(Mathf.Clamp(direction.y, -1f, 1f)) / Mathf.PI);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float NonnegativeFinite(float value) => Finite(value) ? Mathf.Max(0f, value) : 0f;
        static float SmoothRange(float low, float high, float value)
        {
            float t = Mathf.Clamp01((value - low) / (high - low));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Small deterministic fixtures; allocates/destroys textures, never touches scenes.</summary>
        public static void RunMathChecks()
        {
            Check(Vector3.Angle(UvToDirection(new Vector2(0.5f, 0.5f)), Vector3.right) < 0.01f, "UV centre must face +X.");
            Check(Vector3.Angle(UvToDirection(new Vector2(0.25f, 0.5f)), Vector3.forward) < 0.01f, "UV quarter must face +Z.");
            Check(Vector3.Angle(UvToDirection(new Vector2(0f, 0.5f)), Vector3.left) < 0.01f, "UV seam must face -X.");
            Check(Vector3.Angle(UvToDirection(new Vector2(0.5f, 1f)), Vector3.up) < 0.01f, "UV top must face +Y.");

            var uv = new Vector2(0.59985f, 0.55f);
            Vector3 currentSun = -(Quaternion.Euler(6.06f, 152f, 0f) * Vector3.forward);
            Check(Mathf.Abs(Mathf.DeltaAngle(RotationToMatchSun(uv, currentSun), 153.946f)) < 0.02f,
                "Existing Rotunda sun/skybox reference must yield approximately 154 degrees.");
            foreach (float rotation in new[] { 0f, 42f, 154f, 275f, 359f })
            {
                Vector3 source = UvToDirection(new Vector2(0.991f, 0.61f));
                float radians = rotation * Mathf.Deg2Rad;
                var target = new Vector3(Mathf.Cos(radians) * source.x - Mathf.Sin(radians) * source.z,
                    source.y, Mathf.Sin(radians) * source.x + Mathf.Cos(radians) * source.z);
                Check(Mathf.Abs(Mathf.DeltaAngle(RotationToMatchSun(new Vector2(0.991f, 0.61f), target), rotation)) < 0.01f,
                    "Panoramic shader rotation sign must round-trip.");
            }

            var texture = new Texture2D(256, 128, TextureFormat.RGBAFloat, false, true);
            try
            {
                Color[] pixels = new Color[texture.width * texture.height];
                FillFixture(texture, pixels, 0.2f);
                Check(!EstimateSun(texture).reliable, "Uniform sky must not auto-align.");
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                    {
                        float value = 0.4f + 0.2f * y / texture.height;
                        pixels[y * texture.width + x] = new Color(value, value, value, 1f);
                    }
                texture.SetPixels(pixels);
                texture.Apply();
                Check(!EstimateSun(texture).reliable, "Broad overcast gradient must not auto-align.");

                foreach (Vector2 center in new[] { new Vector2(0.73f, 0.57f), new Vector2(0.999f, 0.58f) })
                {
                    FillFixture(texture, pixels, 0.2f);
                    PaintFixtureDisk(texture, pixels, center, 2f, 8f);
                    PaintFixtureDisk(texture, pixels, new Vector2(0.35f, 0.3f), 8f, 1000f);
                    texture.SetPixels(pixels);
                    texture.Apply();
                    SunEstimate estimate = EstimateSun(texture);
                    Check(estimate.reliable, "A compact sky highlight must be reliable, including across the seam.");
                    Check(Vector3.Angle(UvToDirection(estimate.uv), UvToDirection(center)) < 1.5f,
                        "Sun direction must ignore brighter below-horizon city lights.");
                }

                FillFixture(texture, pixels, 0.2f);
                PaintFixtureDisk(texture, pixels, new Vector2(0.25f, 0.6f), 2f, 8f);
                PaintFixtureDisk(texture, pixels, new Vector2(0.75f, 0.6f), 2f, 8f);
                texture.SetPixels(pixels);
                texture.Apply();
                Check(!EstimateSun(texture).reliable, "Two equally bright sky highlights must remain ambiguous.");
                FillFixture(texture, pixels, 0.2f);
                pixels[texture.width * 80 + 130] = Color.white * 20f;
                texture.SetPixels(pixels);
                texture.Apply();
                Check(!EstimateSun(texture).reliable, "An isolated hot pixel must not auto-align.");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static void FillFixture(Texture2D texture, Color[] pixels, float value)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(value, value, value, 1f);
            texture.SetPixels(pixels);
            texture.Apply();
        }

        static void PaintFixtureDisk(Texture2D texture, Color[] pixels, Vector2 center, float radiusDegrees, float value)
        {
            Vector3 direction = UvToDirection(center);
            float minimumDot = Mathf.Cos(radiusDegrees * Mathf.Deg2Rad);
            for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                    if (Vector3.Dot(direction, UvToDirection(new Vector2((x + 0.5f) / texture.width, (y + 0.5f) / texture.height))) >= minimumDot)
                        pixels[y * texture.width + x] = new Color(value, value, value, 1f);
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Panorama analysis check failed: " + message);
        }
    }
}
