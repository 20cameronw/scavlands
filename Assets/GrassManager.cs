using System.Collections.Generic;
using UnityEngine;

public class GrassAroundPlayer : MonoBehaviour
{
    [Header("References")]
    public Terrain terrain;
    public Mesh grassMesh;
    public Material grassMaterial;
    public Transform player;

    [Header("Grass Settings")]
    public float chunkSize = 20f;           // size of one grass chunk
    public int grassPerChunk = 200;         // grass per chunk
    public float viewDistance = 100f;       // radius around player to generate chunks
    public float minScale = 0.8f;
    public float maxScale = 1.3f;

    private Dictionary<Vector2Int, Matrix4x4[]> chunks = new();
    private List<Matrix4x4> visibleMatrices = new();

    void Update()
    {
        if (terrain == null || grassMesh == null || grassMaterial == null || player == null) return;

        GenerateVisibleChunks();
        DrawGrass();
    }

    void GenerateVisibleChunks()
    {
        visibleMatrices.Clear();

        Vector3 terrainPos = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;

        Vector2Int playerChunk = new Vector2Int(
            Mathf.FloorToInt((player.position.x - terrainPos.x) / chunkSize),
            Mathf.FloorToInt((player.position.z - terrainPos.z) / chunkSize)
        );

        int chunksRadius = Mathf.CeilToInt(viewDistance / chunkSize);

        for (int dz = -chunksRadius; dz <= chunksRadius; dz++)
        {
            for (int dx = -chunksRadius; dx <= chunksRadius; dx++)
            {
                Vector2Int c = new Vector2Int(playerChunk.x + dx, playerChunk.y + dz);
                if (chunks.ContainsKey(c)) 
                {
                    // Already spawned, just use
                    visibleMatrices.AddRange(chunks[c]);
                    continue;
                }

                // Spawn new chunk
                Vector3 chunkOrigin = new Vector3(c.x * chunkSize + terrainPos.x, 0f, c.y * chunkSize + terrainPos.z);
                Matrix4x4[] matrices = new Matrix4x4[grassPerChunk];
                for (int i = 0; i < grassPerChunk; i++)
                {
                    float x = chunkOrigin.x + Random.Range(0f, chunkSize);
                    float z = chunkOrigin.z + Random.Range(0f, chunkSize);

                    // Clamp to terrain
                    x = Mathf.Clamp(x, terrainPos.x, terrainPos.x + terrainSize.x);
                    z = Mathf.Clamp(z, terrainPos.z, terrainPos.z + terrainSize.z);

                    float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrainPos.y;
                    Vector3 pos = new Vector3(x, y, z);

                    Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    float s = Random.Range(minScale, maxScale);
                    matrices[i] = Matrix4x4.TRS(pos, rot, Vector3.one * s);
                }

                chunks[c] = matrices;
                visibleMatrices.AddRange(matrices);
            }
        }
    }

    void DrawGrass()
    {
        int total = visibleMatrices.Count;
        int batchSize = 1023;

        for (int i = 0; i < total; i += batchSize)
        {
            int count = Mathf.Min(batchSize, total - i);
            Matrix4x4[] batch = new Matrix4x4[count];
            visibleMatrices.CopyTo(i, batch, 0, count);
            Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, batch, count, null,
                UnityEngine.Rendering.ShadowCastingMode.On, true, 0, null,
                UnityEngine.Rendering.LightProbeUsage.Off, null);
        }
    }
}
