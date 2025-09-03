// =============================================
// Unity Rust-like Map Generator - Minimal MVP
// =============================================
// Drop MapGenerator on an empty GameObject in a fresh scene.
// Create a Terrain (GameObject > 3D Object > Terrain) OR let this script create one.
// Assign a water plane prefab (or use Unity's Plane) and a few placeholder prefabs
// for trees/ores/monuments in the inspector.
// Press Play and hit the "Generate" button exposed by the MapGenerator via a ContextMenu.
// Tested for URP/HDRP/BRP; splat painting requires TerrainLayers set up in the inspector.
// ---------------------------------------------
// Files in this canvas:
// 1) MapGenerator.cs
// 2) BiomeDefinition.cs
// 3) PoissonDiskSampler.cs
// 4) RiverGenerator.cs
// 5) RoadGenerator.cs
// 6) PropSpawner.cs
// ---------------------------------------------

// =====================
// 1) MapGenerator.cs
// =====================
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[ExecuteAlways]
public class MapGenerator : MonoBehaviour
{
    [Header("Terrain Target")]
    [Tooltip("If null, a Terrain + TerrainData will be created at runtime.")]
    public Terrain targetTerrain;

    [Header("Seed & Size")]
    public int seed = 12345;
    [Tooltip("Terrain world size (x,z). Height is scaled separately.")]
    public Vector2 terrainSize = new Vector2(2048, 2048);
    [Range(100f, 1200f)] public float heightScale = 350f;
    [Range(0f, 1f)] public float seaLevel = 0.28f;
    [Range(256, 4097)] public int heightmapResolution = 1025;
    [Range(16, 512)] public int alphamapResolution = 256;

    [Header("Noise (FBM + Domain Warp)")]
    public float noiseScale = 900f;
    [Range(1, 8)] public int octaves = 5;
    [Range(1.5f, 3.5f)] public float lacunarity = 2.0f;
    [Range(0.3f, 0.9f)] public float persistence = 0.5f;
    public float warpStrength = 120f;
    public float continentBias = 0.6f; // > flattens oceans, raises land
    public float edgeGradient = 0.07f;

    [Header("Climate Maps")]
    public float tempScale = 1500f;
    public float moistureScale = 1100f;

    [Header("Biomes & Layers")]
    public List<BiomeDefinition> biomes = new();
    public TerrainLayer[] terrainLayers; // order should match biomes' terrainLayerIndex where possible

    [Header("Rivers & Roads")]
    [Range(0, 32)] public int riverCount = 10;
    public int roadCount = 3;
    [Range(1f, 10f)] public float riverCarveDepth = 3.5f;

    [Header("Props")]
    public PropSpawner propSpawner;

    [Header("Monuments")]
    public MonumentSpawner monumentSpawner;


    [Header("Water")]
    public Transform waterPrefab; // flat plane with water shader

    [Header("Debug Toggles")]
    public bool showBiomeGizmos;

    // Runtime caches
    private float[,] _height01; // normalized heights 0..1
    private float[,] _temperature;
    private float[,] _moisture;
    private int[,] _biomeIndex; // per splat cell (alphamap resolution)
    private List<Vector3> _monuments = new();

    void OnValidate()
    {
        heightmapResolution = Mathf.ClosestPowerOfTwo(heightmapResolution - 1) + 1; // enforce pow2+1
        alphamapResolution = Mathf.ClosestPowerOfTwo(alphamapResolution);
        seaLevel = Mathf.Clamp01(seaLevel);
        continentBias = Mathf.Clamp01(continentBias);
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        var prng = new System.Random(seed);
        Random.InitState(seed);

        var terrain = EnsureTerrain();
        var data = terrain.terrainData;
        data.heightmapResolution = heightmapResolution;
        data.alphamapResolution = alphamapResolution;
        data.baseMapResolution = alphamapResolution;
        data.size = new Vector3(terrainSize.x, heightScale, terrainSize.y);

        _height01 = BuildHeight01(data.heightmapResolution, data.size);
        ApplyNaturalCoastalBeach(_height01, seaLevel, edgeGradient);
        data.SetHeights(0, 0, _height01);


        // Climate
        _temperature = BuildNoiseMap(data.heightmapResolution, tempScale, offsetSeed: seed * 17);
        _moisture    = BuildNoiseMap(data.heightmapResolution, moistureScale, offsetSeed: seed * 29);

        // Splat / Biomes
        data.terrainLayers = terrainLayers;
        _biomeIndex = BuildBiomeIndex(alphamapResolution);
        ApplySplatmap(data, _biomeIndex);

        // Water plane
        EnsureHdrpWater(terrain);

        monumentSpawner.GenerateMonuments();

        // 8) Props (trees/ores) per biome using Poisson
        if (propSpawner != null)
        {
            propSpawner.Spawn(this, terrain, _biomeIndex);
        }



        Debug.Log("Map generated.");
    }

    Terrain EnsureTerrain()
    {
        if (targetTerrain != null) return targetTerrain;

        var terrainGO = GameObject.Find("Generated Terrain");
        if (terrainGO == null)
        {
            terrainGO = new GameObject("Generated Terrain");
            var terrain = terrainGO.AddComponent<Terrain>();
            var collider = terrainGO.AddComponent<TerrainCollider>();
            var data = new TerrainData();
            terrain.terrainData = data;
            collider.terrainData = data;
            targetTerrain = terrain;
        }
        else
        {
            targetTerrain = terrainGO.GetComponent<Terrain>();
            if (targetTerrain.terrainData == null)
                targetTerrain.terrainData = new TerrainData();
        }
        return targetTerrain;
    }

    float[,] BuildHeight01(int res, Vector3 size)
    {
        float[,] h = new float[res, res];
        float inv = 1f / (res - 1);
        float center = (res - 1) / 2f;
        float maxRadius = center; // for island fade

        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            float nx = x * inv;
            float nz = z * inv;

            // Distance from center for island shaping
            float dx = x - center;
            float dz = z - center;
            float distToCenter = Mathf.Sqrt(dx * dx + dz * dz);
            float radialFalloff = Mathf.Clamp01(1f - (distToCenter / maxRadius));

            // Domain warp
            Vector2 warp = Warp(nx, nz, warpStrength);

            // FBM for continent details
            float e = FBM((nx * terrainSize.x + warp.x) / noiseScale, (nz * terrainSize.y + warp.y) / noiseScale);

            // Apply noise-based variation to coastline
            float coastNoise = Mathf.PerlinNoise((nx + seed) * 1.5f, (nz + seed) * 1.5f);
            radialFalloff *= Mathf.Lerp(0.8f, 1.2f, coastNoise); // irregular coast

            // Combine: noise + radial falloff for island shape
            e *= radialFalloff;

            // Continent shaping: bias oceans down, land up
            e = Mathf.Pow(Mathf.Clamp01(e), 1.2f);
            e = Mathf.Lerp(e * 0.85f, Mathf.SmoothStep(0f, 1f, e), continentBias);

            // Fade to ocean beyond edge
            if (distToCenter > maxRadius)
                e = 0f;

            h[z, x] = e;
        }
        return h;
    }


    float[,] BuildNoiseMap(int res, float scale, int offsetSeed)
    {
        float[,] m = new float[res, res];
        float inv = 1f / (res - 1);
        Vector2 off = Hash2(offsetSeed) * 10000f;
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            float nx = x * inv * terrainSize.x / scale + off.x;
            float nz = z * inv * terrainSize.y / scale + off.y;
            float v = FBM(nx, nz);
            m[z, x] = v;
        }
        return m;
    }

    Vector2 Warp(float nx, float nz, float strength)
    {
        float wx = Mathf.PerlinNoise(nx * 2.13f + seed, nz * 2.13f - seed);
        float wz = Mathf.PerlinNoise(nx * 1.77f - seed * 0.5f, nz * 1.77f + seed * 0.5f);
        return new Vector2((wx - 0.5f) * strength, (wz - 0.5f) * strength);
    }

    float FBM(float x, float y)
    {
        float amp = 1f;
        float freq = 1f;
        float sum = 0f;
        float maxSum = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x * freq + seed*0.001f*(i+1), y * freq - seed*0.001f*(i+11)) * amp;
            maxSum += amp;
            amp *= persistence;
            freq *= lacunarity;
        }
        return sum / Mathf.Max(0.0001f, maxSum);
    }

    int[,] BuildBiomeIndex(int res)
    {
        int[,] idx = new int[res, res];
        var tRes = targetTerrain.terrainData.heightmapResolution;
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            // sample climate and height at alphamap grid
            int hx = Mathf.RoundToInt((float)x / (res - 1) * (tRes - 1));
            int hz = Mathf.RoundToInt((float)z / (res - 1) * (tRes - 1));
            float h = _height01[hz, hx];
            float t = _temperature[hz, hx];
            float m = _moisture[hz, hx];
            int best = 0;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < biomes.Count; i++)
            {
                var b = biomes[i];
                if (!b) continue;
                float score = b.MatchScore(h, t, m);
                if (score > bestScore)
                {
                    best = i; bestScore = score;
                }
            }
            idx[z, x] = best;
        }
        return idx;
    }

    void ApplySplatmap(TerrainData data, int[,] idx)
    {
        int res = data.alphamapResolution;
        int layers = data.terrainLayers != null ? data.terrainLayers.Length : 0;
        if (layers == 0) return;
        float[,,] splat = new float[res, res, layers];

        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            int b = idx[z, x];
            int layer = 0;
            if (b >= 0 && b < biomes.Count && biomes[b] != null)
                layer = Mathf.Clamp(biomes[b].terrainLayerIndex, 0, layers - 1);

            for (int l = 0; l < layers; l++)
                splat[z, x, l] = (l == layer) ? 1f : 0f;
        }
        data.SetAlphamaps(0, 0, splat);
    }




    // Utility
    static Vector2 Hash2(int s)
    {
        uint x = (uint)s;
        x ^= 2747636419u; x *= 2654435769u; x ^= x >> 16; x *= 2654435769u; x ^= x >> 16; x *= 2654435769u;
        return new Vector2((x & 0xFFFF) / 65535f, ((x >> 16) & 0xFFFF) / 65535f);
    }

    void OnDrawGizmosSelected()
    {
        if (!showBiomeGizmos || _biomeIndex == null || targetTerrain == null) return;
        var data = targetTerrain.terrainData;
        int res = data.alphamapResolution;
        float stepX = data.size.x / (res - 1);
        float stepZ = data.size.z / (res - 1);
        Gizmos.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
        for (int z = 0; z < res; z+=8)
        for (int x = 0; x < res; x+=8)
        {
            int b = _biomeIndex[z, x];
            Color c = Color.HSVToRGB((b * 0.123f) % 1f, 0.7f, 0.9f);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.5f);
            Gizmos.DrawCube(new Vector3(x * stepX, 0.5f, z * stepZ), new Vector3(stepX*0.9f, 1f, stepZ*0.9f));
        }
    }

    void EnsureHdrpWater(Terrain t)
    {
        var size = t.terrainData.size;
        var seaY = seaLevel * size.y;

        // Try HDRP WaterSurface first (via reflection to avoid hard dep)
        var waterGO = GameObject.Find("HDRP Water") ?? new GameObject("HDRP Water");
        var h = waterGO.transform;
        h.position = new Vector3(size.x * 0.5f, seaY, size.z * 0.5f);

        var waterSurfaceType = Type.GetType("UnityEngine.Rendering.HighDefinition.WaterSurface, Unity.RenderPipelines.HighDefinition.Runtime");
        if (waterSurfaceType != null)
        {
            // Add component if missing
            var comp = waterGO.GetComponent(waterSurfaceType) ?? waterGO.AddComponent(waterSurfaceType);

            // Best effort defaults via reflection (safe if fields exist; otherwise ignored)
            try
            {
                // Set “infinite” ocean if available; else set finite size roughly to terrain
                var waterTypeProp = waterSurfaceType.GetProperty("waterType");
                var geomTypeProp  = waterSurfaceType.GetProperty("geometryType");
                var regionSizeProp = waterSurfaceType.GetProperty("regionSize");
                var centerProp     = waterSurfaceType.GetProperty("regionCenter");

                // Enums live on the WaterSurface type; grab them by name
                var wtEnum  = waterSurfaceType.GetNestedType("WaterSurfaceType");
                var gtEnum  = waterSurfaceType.GetNestedType("GeometryType");
                var WT_Ocean = Enum.Parse(wtEnum, "Ocean");
                var GT_Infinite = Enum.Parse(gtEnum, "Infinite");
                var GT_Finite   = Enum.Parse(gtEnum, "Finite");

                // Prefer infinite ocean for large maps
                waterTypeProp?.SetValue(comp, WT_Ocean);
                if (geomTypeProp != null && regionSizeProp != null && centerProp != null)
                {
                    // If you prefer finite, switch to GT_Finite and set region to terrain size
                    geomTypeProp.SetValue(comp, GT_Infinite);
                    // Finite example:
                    // geomTypeProp.SetValue(comp, GT_Finite);
                    // regionSizeProp.SetValue(comp, new Vector2(size.x, size.z));
                    // centerProp.SetValue(comp, new Vector2(size.x * 0.5f, size.z * 0.5f));
                }

                // Try to set a calm look (if properties exist)
                var largeAmp = waterSurfaceType.GetProperty("largeAmplitude");
                var windSpeed = waterSurfaceType.GetProperty("windSpeed");
                largeAmp?.SetValue(comp, 0.4f);
                windSpeed?.SetValue(comp, 3.0f);
            }
            catch { /* Properties differ by HDRP version; safe to ignore */ }

            waterGO.name = "HDRP Water";
            return;
        }

        // ---------- Fallback (no HDRP WaterSurface available): use your plane prefab ----------
        if (waterPrefab)
        {
            var existing = GameObject.Find("Water");
            Transform w;
            if (existing == null)
            {
                w = Instantiate(waterPrefab, new Vector3(size.x * 0.5f, seaY, size.z * 0.5f), Quaternion.identity);
                w.name = "Water";
            }
            else
            {
                w = existing.transform;
                w.position = new Vector3(size.x * 0.5f, seaY, size.z * 0.5f);
            }
            // Scale to cover terrain assuming a 10x10 Unity plane
            float scaleX = size.x / 10f;
            float scaleZ = size.z / 10f;
            w.localScale = new Vector3(scaleX, 1f, scaleZ);
        }
    }

    void ApplyNaturalCoastalBeach(float[,] heightMap, float seaLevel, float beachWidthNormalized = 0.03f, int seed = 0)
    {
        int res = heightMap.GetLength(0);
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float height = heightMap[z, x];

                // If height is near sea level, blend into a smooth beach
                if (height > seaLevel - beachWidthNormalized && height < seaLevel + beachWidthNormalized)
                {
                    float t = Mathf.InverseLerp(seaLevel - beachWidthNormalized, seaLevel + beachWidthNormalized, height);
                    float noise = (Mathf.PerlinNoise((x + seed) * 0.08f, (z + seed) * 0.08f) - 0.5f) * 0.01f;
                    heightMap[z, x] = Mathf.Lerp(seaLevel, height, t) + noise;
                }
            }
        }
    }
}

