// Seamless spot pattern for Shader Graph (Custom Function node, Type: File, Name: Spots3D).
// Works from a 3D position instead of UVs, so the spots continue across UV seams, edges and corners.
// Distance is 0 at a spot's centre and grows outwards, like the 2D Voronoi node's Out:
// threshold it with Smoothstep + One Minus to get spots.
#ifndef BLOCKSESCAPE_SPOTS3D_INCLUDED
#define BLOCKSESCAPE_SPOTS3D_INCLUDED

float3 Spots3D_Hash(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453);
}

// Smooth 3D value noise in 0..1, used to make the spot outlines irregular.
float Spots3D_Noise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float x00 = lerp(Spots3D_Hash(i).x,                     Spots3D_Hash(i + float3(1, 0, 0)).x, u.x);
    float x10 = lerp(Spots3D_Hash(i + float3(0, 1, 0)).x,   Spots3D_Hash(i + float3(1, 1, 0)).x, u.x);
    float x01 = lerp(Spots3D_Hash(i + float3(0, 0, 1)).x,   Spots3D_Hash(i + float3(1, 0, 1)).x, u.x);
    float x11 = lerp(Spots3D_Hash(i + float3(0, 1, 1)).x,   Spots3D_Hash(i + float3(1, 1, 1)).x, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

// Distance to the nearest of one random point per grid cell.
float Spots3D_Voronoi(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    float nearest = 8.0;
    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float3 offset = float3(x, y, z);
        float3 d = offset + Spots3D_Hash(cell + offset) - f;
        nearest = min(nearest, dot(d, d));
    }
    return sqrt(nearest);
}

// Position: use the Position node in Object space, so the spots stick to the model as it moves.
// Scale: spots per unit (metre). Wobble: how far, in units, the outlines get pushed around.
void Spots3D_float(float3 Position, float Scale, float Wobble, out float Distance)
{
    float3 p = Position * Scale;
    float3 warp = float3(Spots3D_Noise(p),
                         Spots3D_Noise(p + float3(17.3, 5.1, 41.7)),
                         Spots3D_Noise(p + float3(63.9, 29.4, 8.2))) - 0.5;
    Distance = Spots3D_Voronoi((Position + warp * Wobble) * Scale);
}

void Spots3D_half(half3 Position, half Scale, half Wobble, out half Distance)
{
    float distance;
    Spots3D_float(Position, Scale, Wobble, distance);
    Distance = distance;
}

#endif
