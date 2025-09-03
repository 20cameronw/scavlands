// =======================
// 5) RoadGenerator.cs
// =======================
using System.Collections.Generic;
using UnityEngine;

public class RoadGenerator
{
    // Very naive: flatten/paint along straight-ish paths between random paired monuments
    public void GenerateRoads(Terrain t, float[,] h01, float seaLevel, List<Vector3> points, int roadCount)
    {
        if (points.Count < 2) return;
        int pairs = Mathf.Min(roadCount, points.Count / 2);
        for (int i = 0; i < pairs; i++)
        {
            var a = points[(i * 2) % points.Count];
            var b = points[(i * 2 + 1) % points.Count];
            CarveRoad(t, h01, a, b, width: 6f);
        }
    }

    void CarveRoad(Terrain t, float[,] h01, Vector3 a, Vector3 b, float width)
    {
        var data = t.terrainData;
        int res = data.heightmapResolution;
        Vector2 A = new Vector2(a.x / data.size.x, a.z / data.size.z);
        Vector2 B = new Vector2(b.x / data.size.x, b.z / data.size.z);
        int steps = Mathf.CeilToInt(Vector2.Distance(A, B) * res);
        for (int s = 0; s <= steps; s++)
        {
            float t01 = s / (float)steps;
            Vector2 p = Vector2.Lerp(A, B, t01);
            int cx = Mathf.RoundToInt(p.x * (res - 1));
            int cz = Mathf.RoundToInt(p.y * (res - 1));
            int rad = Mathf.RoundToInt(width * res / data.size.x);
            float target = SampleBilinear(h01, p.x, p.y); // keep road near terrain
            for (int dz = -rad; dz <= rad; dz++)
            for (int dx = -rad; dx <= rad; dx++)
            {
                int nx = Mathf.Clamp(cx + dx, 0, res - 1);
                int nz = Mathf.Clamp(cz + dz, 0, res - 1);
                float d = Mathf.Sqrt(dx*dx + dz*dz) / rad;
                if (d <= 1f)
                {
                    // smooth blend towards target height
                    h01[nz, nx] = Mathf.Lerp(h01[nz, nx], target, Mathf.SmoothStep(0.6f, 1f, 1f - d));
                }
            }
        }
        data.SetHeights(0, 0, h01);
    }

    float SampleBilinear(float[,] h, float u, float v)
    {
        int res = h.GetLength(0);
        float x = Mathf.Clamp(u * (res - 1), 0, res - 1);
        float y = Mathf.Clamp(v * (res - 1), 0, res - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, res - 1);
        int y1 = Mathf.Min(y0 + 1, res - 1);
        float tx = x - x0; float ty = y - y0;
        float a = Mathf.Lerp(h[y0, x0], h[y0, x1], tx);
        float b = Mathf.Lerp(h[y1, x0], h[y1, x1], tx);
        return Mathf.Lerp(a, b, ty);
    }
}