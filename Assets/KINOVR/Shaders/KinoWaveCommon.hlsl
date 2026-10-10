#ifndef KINO_WAVE_COMMON_INCLUDED
#define KINO_WAVE_COMMON_INCLUDED
// Shared constants and the ribbon curve for the pre-game wave environment.
// KW_W is one cycle per 120-second loop; every animated term uses a whole multiple.
#define KW_TAU 6.28318530718
#define KW_LOOP_SECONDS 120.0
#define KW_W (KW_TAU / KW_LOOP_SECONDS)

// a = centre offset, amplitude, azimuth cycles, speed cycles; b = phase, twist, spread, radius.
// origin.xyz = eye anchor, origin.w = front yaw. Returns the world point; mask = reveal mask.
float3 KinoWavePoint(float u, float s, float4 a, float4 b, float4 origin, float waveTime,
    float calmFront, float collapse, float reveal, out float mask)
{
    float theta = u * KW_TAU;
    float delta = theta - origin.w;
    float away = abs(atan2(sin(delta), cos(delta)));            // 0 in front, PI behind
    float env = lerp(calmFront, 1.0, smoothstep(0.0, 1.1, away)) * (1.0 - collapse);
    float t = waveTime * KW_W;
    float ph = b.x;
    float phase = ph + a.w * t + s * b.y * (0.35 + 0.65 * sin(theta * 2.0 + t * 11.0 + ph));
    float y = a.x * (1.0 - 0.6 * collapse)
            + a.y * env * sin(a.z * theta + phase)
            + s * b.z * (0.15 + 0.85 * env)
            + a.y * 0.18 * env * sin(theta * 5.0 + t * 17.0 + ph * 2.0);
    float r = b.w + 0.3 * sin(theta * 3.0 + t * 8.0 + s * 1.7);
    // Reveal grows from the front around both sides to the back.
    mask = saturate((reveal * 3.4 - away) / 0.35);
    return float3(origin.x + r * sin(theta), origin.y + y, origin.z + r * cos(theta));
}
#endif
