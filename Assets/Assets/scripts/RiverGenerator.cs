// =======================
// 4) RiverGenerator.cs
// =======================
using System.Collections.Generic;
using UnityEngine;

public class RiverGenerator
{
    public int Seed = 0;
    public float CarveDepth = 3f; // in height01 space scaled by terrain height later

    public void GenerateRivers(float[,] height01, float seaLevel, int riverCount)
    {
        int res = height01.GetLength(0);
        var rng = new System.Random(Seed);
        int tries = riverCount * 50;
        int made = 0;
        while (tries-- > 0 && made < riverCount)
        {
            int x = rng.Next(res);
            int z = rng.Next(res);
            if (height01[z, x] < seaLevel + 0.1f) continue; // start high-ish
            if (CarveRiver(height01, seaLevel, x, z)) made++;
        }
    }

    bool CarveRiver(float[,] h, float sea, int sx, int sz)
    {
        int res = h.GetLength(0);
        int x = sx, z = sz;
        int maxSteps = res * 4;
        int steps = 0;
        var path = new List<Vector2Int>();
        while (steps++ < maxSteps)
        {
            path.Add(new Vector2Int(x, z));
            float cur = h[z, x];
            if (cur <= sea) break;
            // move to lowest neighbor (8-way)
            int bestX = x, bestZ = z;
            float bestH = cur;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = Mathf.Clamp(x + dx, 0, res - 1);
                int nz = Mathf.Clamp(z + dz, 0, res - 1);
                float nh = h[nz, nx];
                if (nh < bestH)
                { bestH = nh; bestX = nx; bestZ = nz; }
            }
            if (bestX == x && bestZ == z)
                break; // local minima; stop
            x = bestX; z = bestZ;
        }
        if (path.Count < 32) return false; // tiny dribble
        // Carve
        float r = 3f; // pixels
        foreach (var p in path)
        {
            for (int dz = -4; dz <= 4; dz++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int nx = Mathf.Clamp(p.x + dx, 0, res - 1);
                int nz = Mathf.Clamp(p.y + dz, 0, res - 1);
                float d = Mathf.Sqrt(dx*dx + dz*dz);
                float falloff = Mathf.Clamp01(1f - d / r);
                h[nz, nx] -= CarveDepth * 0.0015f * falloff; // small carve in height01 space
            }
        }
        return true;
    }
}