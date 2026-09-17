#ifndef KINO_GOLD_SURFACE_INCLUDED
#define KINO_GOLD_SURFACE_INCLUDED

// The board tokens and the physical balls share the same yellow lacquer palette.
// Input normal points toward the viewer (+Z), with the softbox at the upper left.
float3 KinoLacquerSurface(float3 normal, float bonusRed, float secondChanceGreen)
{
    float height = saturate(normal.y * .5 + .5);
    float3 low = lerp(float3(.86, .57, .015), float3(.48, .008, .025), bonusRed);
    float3 high = lerp(float3(1, .98, .20), float3(1, .075, .12), bonusRed);
    low = lerp(low, float3(.008, .28, .045), secondChanceGreen);
    high = lerp(high, float3(.18, .95, .32), secondChanceGreen);
    float3 gold = lerp(low, high, pow(height, .52));
    float front = saturate(normal.z);
    gold = lerp(gold * .82, gold, smoothstep(0, .3, front));
    float softbox = pow(saturate(dot(normal, normalize(float3(-.38, .58, .72)))), 24);
    float reflection = pow(saturate(dot(normal, normalize(float3(.5, -.32, .8)))), 38);
    gold = lerp(gold, float3(1, 1, .94), softbox * .94);
    float strip = exp(-pow((normal.y - .70) * 18, 2)) * exp(-pow((normal.x + .2) * 2, 2));
    gold = lerp(gold, float3(1, 1, .92), strip * smoothstep(.25, .55, front) * .5);
    gold += lerp(float3(.14, .12, .035), float3(.18, .035, .05), bonusRed) * reflection;
    float rim = 1 - smoothstep(.12, .28, front);
    float3 rimColor = lerp(float3(1, .93, .45), float3(1, .22, .28), bonusRed);
    return lerp(gold, lerp(rimColor, float3(.45, 1, .62), secondChanceGreen), rim * .85);
}
float3 KinoLacquerSurface(float3 normal, float bonusRed) { return KinoLacquerSurface(normal, bonusRed, 0); }
float3 KinoGoldSurface(float3 normal) { return KinoLacquerSurface(normal, 0); }
#endif
