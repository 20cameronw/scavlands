using System.Collections.Generic;
using UnityEngine;

public class PropSpawner : MonoBehaviour
{
    [System.Serializable]
    public class BiomeProps
    {
        public BiomeDefinition biome;

        [Header("Trees")]
        public List<Transform> treePrefabs;
        [Range(10f, 300f)] public float treePoissonRadius = 80f;
        [Range(0f, 1f)] public float forestDensity = 0.5f;
    }

    public List<BiomeProps> rules = new();
    public Transform treeRoot;

    // Track spawned objects per rule
    private readonly Dictionary<BiomeProps, List<GameObject>> activeTrees = new();

    /// <summary>Delete all spawned trees. If in editor and not playing, uses DestroyImmediate.</summary>
    public void ClearTrees(bool alsoClearUntrackedChildren = true)
    {
        // nuke everything we tracked
        foreach (var kv in activeTrees)
        {
            var list = kv.Value;
            if (list == null) continue;
            for (int i = list.Count - 1; i >= 0; i--)
                DestroySmart(list[i]);
            list.Clear();
        }
        activeTrees.Clear();

        // optional: nuke any leftover children under the root (safety)
        if (alsoClearUntrackedChildren && treeRoot != null)
        {
            for (int i = treeRoot.childCount - 1; i >= 0; i--)
                DestroySmart(treeRoot.GetChild(i).gameObject);
        }
    }

    [ContextMenu("Clear Spawned Props")]
    private void ContextClear() => ClearTrees(true);

    public void Spawn(MapGenerator gen, Terrain terrain, int[,] biomeIndex)
    {
        if (!gen || !terrain || biomeIndex == null) return;

        EnsureTreeRoot();

        // always start clean (works for editor buttons too)
        ClearTrees(true);

        var data = terrain.terrainData;
        int res = data.alphamapResolution;
        float stepX = data.size.x / (res - 1);
        float stepZ = data.size.z / (res - 1);

        foreach (var r in rules)
        {
            if (r == null || r.biome == null) continue;
            if (r.treePrefabs == null || r.treePrefabs.Count == 0) continue;

            // init tracking bucket
            if (!activeTrees.ContainsKey(r)) activeTrees[r] = new List<GameObject>();

            // quick presence scan for this biome
            bool anyCells = false;
            for (int z = 0; z < res && !anyCells; z++)
                for (int x = 0; x < res && !anyCells; x++)
                    if (biomeIndex[z, x] >= 0 && gen.biomes[biomeIndex[z, x]] == r.biome)
                        anyCells = true;
            if (!anyCells) continue;

            // Poisson over whole terrain, then filter by biome & density
            var pd = new PoissonDiskSampler((int)data.size.x, (int)data.size.z, r.treePoissonRadius, gen.seed + r.GetHashCode());
            foreach (var p in pd.Samples())
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(p.x / stepX), 0, res - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(p.y / stepZ), 0, res - 1);
                int bIdx = biomeIndex[cz, cx];
                if (bIdx < 0 || gen.biomes[bIdx] != r.biome) continue;

                // forest noise gate
                float noise = Mathf.PerlinNoise((p.x + gen.seed) * 0.002f, (p.y + gen.seed) * 0.002f);
                if (noise > r.forestDensity) continue;

                // small jitter
                float rx = Random.Range(-stepX, stepX);
                float rz = Random.Range(-stepZ, stepZ);

                float hWorld = terrain.terrainData.GetInterpolatedHeight((p.x + rx) / data.size.x, (p.y + rz) / data.size.z);
                float h01 = hWorld / data.size.y;
                if (h01 < gen.seaLevel + 0.01f) continue;

                Vector3 pos = new Vector3(p.x + rx, hWorld, p.y + rz);
                var prefab = r.treePrefabs[Random.Range(0, r.treePrefabs.Count)];
                if (!prefab) continue;

                var inst = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0, 360f), 0f), treeRoot);
                inst.localScale = Vector3.one * Random.Range(0.8f, 1.3f);
                activeTrees[r].Add(inst.gameObject);
            }
        }
    }

    private void EnsureTreeRoot()
    {
        if (treeRoot != null) return;
        var go = new GameObject("Trees");
        go.transform.SetParent(transform, false);
        treeRoot = go.transform;
    }

    private static void DestroySmart(GameObject go)
    {
        if (!go) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) Object.DestroyImmediate(go);
        else Object.Destroy(go);
#else
        Object.Destroy(go);
#endif
    }
}
