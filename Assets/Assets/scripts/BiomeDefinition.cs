using UnityEngine;
// ========================
// 2) BiomeDefinition.cs
// ========================
[CreateAssetMenu(menuName = "Rustlike/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    [Header("Match Ranges (0..1)")]
    [Range(0,1)] public float minHeight = 0f;
    [Range(0,1)] public float maxHeight = 1f;
    [Range(0,1)] public float minTemp = 0f;
    [Range(0,1)] public float maxTemp = 1f;
    [Range(0,1)] public float minMoisture = 0f;
    [Range(0,1)] public float maxMoisture = 1f;

    [Header("Terrain Layer Mapping")]
    public int terrainLayerIndex = 0;

    [Header("Spawn Settings")] public float propDensity = 0.2f; // objects per 100x100 area

    public float MatchScore(float h, float t, float m)
    {
        // Soft score: distance from center of ranges
        float cH = Mathf.Lerp(minHeight, maxHeight, 0.5f);
        float cT = Mathf.Lerp(minTemp, maxTemp, 0.5f);
        float cM = Mathf.Lerp(minMoisture, maxMoisture, 0.5f);
        float dh = Mathf.InverseLerp(maxHeight, minHeight, Mathf.Abs(h - cH) + 1e-5f);
        float dt = Mathf.InverseLerp(maxTemp, minTemp, Mathf.Abs(t - cT) + 1e-5f);
        float dm = Mathf.InverseLerp(maxMoisture, minMoisture, Mathf.Abs(m - cM) + 1e-5f);
        // gate outside ranges
        if (h < minHeight || h > maxHeight || t < minTemp || t > maxTemp || m < minMoisture || m > maxMoisture)
            return -999f;
        return dh + dt + dm; // higher better
    }
}





