#ifndef KINO_GOLD_SURFACE_INCLUDED
#define KINO_GOLD_SURFACE_INCLUDED

// The board tokens and the physical balls share the same yellow lacquer palette.
// Input normal points toward the viewer (+Z), with the softbox at the upper left.
float3 KinoGoldSurface(float3 normal)
{
    float height = saturate(normal.y * .5 + .5);
    float3 gold = lerp(float3(.86, .57, .015), float3(1, .98, .20), pow(height, .52));
    float front = saturate(normal.z);
    gold = lerp(gold * .82, gold, smoothstep(0, .3, front));
    float softbox = pow(saturate(dot(normal, normalize(float3(-.38, .58, .72)))), 24);
    float reflection = pow(saturate(dot(normal, normalize(float3(.5, -.32, .8)))), 38);
    gold = lerp(gold, float3(1, 1, .94), softbox * .94);
    float strip = exp(-pow((normal.y - .70) * 18, 2)) * exp(-pow((normal.x + .2) * 2, 2));
    gold = lerp(gold, float3(1, 1, .92), strip * smoothstep(.25, .55, front) * .5);
    gold += float3(.14, .12, .035) * reflection;
    float rim = 1 - smoothstep(.12, .28, front);
    return lerp(gold, float3(1, .93, .45), rim * .85);
}
#endif
