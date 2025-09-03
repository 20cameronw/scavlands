// ============================
// 3) PoissonDiskSampler.cs
// ============================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PoissonDiskSampler
{
    private readonly float radius;
    private readonly float cellSize;
    private readonly int k;
    private readonly int width, height;
    private readonly System.Random rng;

    private readonly Vector2[,] grid;
    private readonly List<Vector2> active = new();
    private readonly List<Vector2> samples = new();

    public PoissonDiskSampler(int width, int height, float radius, int seed, int k = 30)
    {
        this.width = width; this.height = height; this.radius = radius; this.k = k;
        this.cellSize = radius / Mathf.Sqrt(2);
        this.rng = new System.Random(seed);
        int gw = Mathf.CeilToInt(width / cellSize);
        int gh = Mathf.CeilToInt(height / cellSize);
        grid = new Vector2[gw, gh];
        for (int y = 0; y < gh; y++)
        for (int x = 0; x < gw; x++)
            grid[x, y] = new Vector2(-1, -1);

        // initial point
        AddSample(new Vector2(width * 0.5f, height * 0.5f));
    }

    void AddSample(Vector2 s)
    {
        samples.Add(s);
        active.Add(s);
        int gx = (int)(s.x / cellSize);
        int gy = (int)(s.y / cellSize);
        grid[gx, gy] = s;
    }

    public IEnumerable<Vector2> Samples()
    {
        while (active.Count > 0)
        {
            int i = rng.Next(active.Count);
            var s = active[i];
            bool found = false;
            for (int j = 0; j < k; j++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = radius * (1f + (float)rng.NextDouble());
                var cand = s + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (cand.x < 0 || cand.x >= width || cand.y < 0 || cand.y >= height) continue;
                if (IsFar(cand))
                {
                    AddSample(cand);
                    found = true; break;
                }
            }
            if (!found) active.RemoveAt(i);
        }
        return samples;
    }

    bool IsFar(Vector2 p)
    {
        int gx = (int)(p.x / cellSize);
        int gy = (int)(p.y / cellSize);
        int gw = grid.GetLength(0);
        int gh = grid.GetLength(1);
        int r = 2;
        for (int y = Mathf.Max(0, gy - r); y <= Mathf.Min(gh - 1, gy + r); y++)
        for (int x = Mathf.Max(0, gx - r); x <= Mathf.Min(gw - 1, gx + r); x++)
        {
            var s = grid[x, y];
            if (s.x < 0) continue;
            if ((s - p).sqrMagnitude < radius * radius) return false;
        }
        return true;
    }
}
