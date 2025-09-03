using UnityEngine;
using System.Collections.Generic;

public class MonumentSpawner : MonoBehaviour
{
    [Header("References")]
    public Terrain terrain;
    public GameObject[] monumentPrefabs;

    [Header("Spawn Settings")]
    public int monumentCount = 10;
    public float minDistance = 40f;   // Poisson disk spacing
    public float spawnRadius = 500f;  // Around the center point
    [Range(0f, 1f)] public float seaLevel = 0.3f;
    public float slopeLimit = 35f;

    public enum RoadMode { PaintSplat, MeshStrip }
    public enum RoutingMode { DirectSegments, AStar }

    [Header("Road Mode")]
    public RoadMode roadMode = RoadMode.MeshStrip;
    public RoutingMode routingMode = RoutingMode.AStar;

    [Header("PaintSplat Settings")]
    [Tooltip("TerrainLayer for road surface when using PaintSplat.")]
    public TerrainLayer roadTerrainLayer;
    [Tooltip("Painted road width in meters.")]
    public float roadWidthMeters = 8f;
    [Tooltip("Blend shoulder in meters where it fades into ground.")]
    public float shoulderWidthMeters = 6f;
    [Tooltip("How much to lower terrain at road center (meters). 0 to disable cutting.")]
    public float roadCutDepthMeters = 0.35f;

    [Header("MeshStrip Settings")]
    [Tooltip("Material for generated road mesh.")]
    public Material roadMaterial;
    [Tooltip("Road mesh width (edge to edge).")]
    public float meshRoadWidthMeters = 8f;
    [Tooltip("Offset along terrain normal to float road (prevents z-fighting).")]
    public float meshYOffset = 0.04f;
    [Tooltip("Subtle center ‘crown’ height added at the midline (meters).")]
    public float crownHeight = 0.06f;
    [Tooltip("UV tiling along length (U repeats per meter).")]
    public float uvTilingPerMeter = 0.15f;
    [Tooltip("Also carve a shallow bed under the mesh road.")]
    public bool carveUnderMesh = true;

    [Header("Path Sampling")]
    [Tooltip("Uniform sampling step along each road (meters).")]
    public float pathStepMeters = 1.5f;
    [Tooltip("Extra smoothing radius (meters) when carving/painting.")]
    public float brushFeatherMeters = 2.0f;

    [Header("A* Routing Grid (for RoutingMode = AStar)")]
    [Tooltip("Number of grid cells along X across the terrain.")]
    public int astarGridX = 256;
    [Tooltip("Number of grid cells along Z across the terrain.")]
    public int astarGridZ = 256;
    [Tooltip("Weight added per degree of slope; higher = avoids hills more.")]
    public float slopeCostWeight = 0.15f;
    [Tooltip("Extra penalty if a cell is below sea level.")]
    public float seaPenalty = 50f;
    [Tooltip("Cells above this slope (degrees) are impassable.")]
    public float impassableSlopeDeg = 55f;
    [Tooltip("Catmull-Rom smoothing subdivisions per path segment (higher=curvier).")]
    public int catmullSubdivisions = 6;

    [Header("Debug")]
    public bool logSkippedReasons = false;
    public bool drawGizmos = false;
    public bool drawDebugPaths = false;

    private List<Vector3> spawnPoints = new List<Vector3>();
    private List<Vector3> placedMonumentPositions = new List<Vector3>();
    private List<GameObject> monuments = new List<GameObject>();

    // Cached terrain values
    private TerrainData td;
    private Vector3 tPos;
    private Vector3 tSize;
    private int hmResX, hmResZ, alphaResX, alphaResZ, alphaLayers;
    private int roadLayerIndex = -1;

    // A* cost field
    private float[,] cellCost;   // per grid cell
    private float cellSizeX, cellSizeZ;


    public GameObject meshParent;

    void clear() {
        foreach (GameObject child in monuments)
        {
        #if UNITY_EDITOR
            if (Application.isEditor && !Application.isPlaying)
                DestroyImmediate(child);
            else
                Destroy(child);
        #else
            Destroy(child);
        #endif
        }

    }

    public void GenerateMonuments()
    {
        if (terrain == null)
        {
            Debug.LogError("Terrain is not assigned!");
            return;
        }

        clear();

        td = terrain.terrainData;
        tPos = terrain.GetPosition();
        tSize = td.size;
        hmResX = td.heightmapResolution;
        hmResZ = td.heightmapResolution;
        alphaResX = td.alphamapWidth;
        alphaResZ = td.alphamapHeight;
        alphaLayers = td.alphamapLayers;

        if (roadMode == RoadMode.PaintSplat)
        {
            roadLayerIndex = ResolveTerrainLayerIndex(roadTerrainLayer);
            if (roadLayerIndex < 0)
            {
                Debug.LogWarning("Road TerrainLayer not found on this Terrain. Road painting will be skipped.");
            }
        }

        if (routingMode == RoutingMode.AStar)
        {
            BuildCostField();
        }

        GenerateSpawnPoints();
        PlaceMonuments();
        BuildRoadNetworkAndApply();
    }

    void GenerateSpawnPoints()
    {
        Vector3 center = tPos + tSize / 2f;
        spawnPoints = PoissonDiskSample(center, spawnRadius, minDistance, monumentCount);
    }

    List<Vector3> PoissonDiskSample(Vector3 center, float radius, float minDist, int maxPoints)
    {
        List<Vector3> points = new List<Vector3>();
        int attempts = 0;

        while (points.Count < maxPoints && attempts < maxPoints * 50)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float randomRadius = Random.Range(0f, radius);
            Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * randomRadius;

            candidate.x += Random.Range(-minDist * 0.3f, minDist * 0.3f);
            candidate.z += Random.Range(-minDist * 0.3f, minDist * 0.3f);

            bool valid = true;
            foreach (Vector3 p in points)
            {
                if (Vector3.Distance(candidate, p) < minDist)
                {
                    valid = false;
                    break;
                }
            }

            if (valid) points.Add(candidate);
            attempts++;
        }

        return points;
    }

    void PlaceMonuments()
    {
        placedMonumentPositions.Clear();
        int placed = 0;

        foreach (Vector3 point in spawnPoints)
        {
            Vector3 pos = point;
            pos.y = terrain.SampleHeight(pos) + tPos.y;

            float nx = (pos.x - tPos.x) / tSize.x;
            float nz = (pos.z - tPos.z) / tSize.z;

            int hx = Mathf.Clamp((int)(nx * (hmResX - 1)), 0, hmResX - 1);
            int hz = Mathf.Clamp((int)(nz * (hmResZ - 1)), 0, hmResZ - 1);

            float heightNorm = td.GetHeight(hx, hz) / tSize.y;
            float slope = td.GetSteepness(nx, nz);

            bool skip = false;

            if (heightNorm < seaLevel + 0.01f)
            {
                if (logSkippedReasons) Debug.Log($"Skipped monument (too low): norm={heightNorm:F3}");
                skip = true;
            }

            if (slope > slopeLimit)
            {
                if (logSkippedReasons) Debug.Log($"Skipped monument (too steep): slope={slope:F1}");
                skip = true;
            }

            if (skip) continue;

            var prefab = monumentPrefabs[Random.Range(0, monumentPrefabs.Length)];
            GameObject go = Instantiate(prefab, pos, Quaternion.identity, transform);
            placedMonumentPositions.Add(pos);
            monuments.Add(go);
            placed++;
        }

        Debug.Log($"Placed {placed} monuments out of {monumentCount}");
    }

    // =========================
    // Road Network + Application
    // =========================
    void BuildRoadNetworkAndApply()
    {
        if (placedMonumentPositions.Count <= 1)
        {
            Debug.Log("Not enough monuments to build roads.");
            return;
        }

        // Build MST edges (indices into placedMonumentPositions) to avoid spaghetti
        var edges = BuildMinimumSpanningTree(placedMonumentPositions);



        float[,,] alphaMap = null;
        if (roadMode == RoadMode.PaintSplat && roadLayerIndex >= 0)
        {
            alphaMap = td.GetAlphamaps(0, 0, alphaResX, alphaResZ);
        }

        float[,] heights = null;
        bool wantsCarve = (roadMode == RoadMode.PaintSplat && roadCutDepthMeters > 0f) ||
                          (roadMode == RoadMode.MeshStrip && carveUnderMesh && roadCutDepthMeters > 0f);
        if (wantsCarve)
        {
            heights = td.GetHeights(0, 0, hmResX, hmResZ);
        }

        foreach (var edge in edges)
        {
            Vector3 a = placedMonumentPositions[edge.Item1];
            Vector3 b = placedMonumentPositions[edge.Item2];

            // Route
            List<Vector3> route;
            if (routingMode == RoutingMode.AStar)
            {
                route = ComputeAStarRouteWorld(a, b);
                route = SmoothCatmullRom(route, catmullSubdivisions);
            }
            else // DirectSegments
            {
                route = new List<Vector3> { a, b };
            }

            // Resample uniformly then conform to terrain
            var samples = ResampleToStep(route, pathStepMeters, conformToTerrain: true);

            // Build mesh if needed
            if (roadMode == RoadMode.MeshStrip)
            {
                var go = BuildRoadMeshBanked(samples, meshRoadWidthMeters, meshYOffset, crownHeight, uvTilingPerMeter);
                go.name = $"Road_{edge.Item1}_{edge.Item2}";
                go.transform.SetParent(meshParent.transform, true);
                if (roadMaterial != null)
                {
                    var mr = go.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = roadMaterial;
                }
            }

            // Carve & Paint (shared)
            if (wantsCarve || (roadMode == RoadMode.PaintSplat && roadLayerIndex >= 0))
            {
                ApplyCarveAndPaint(samples, ref heights, ref alphaMap,
                    (roadMode == RoadMode.MeshStrip ? meshRoadWidthMeters : roadWidthMeters),
                    shoulderWidthMeters);
            }
        }

        if (wantsCarve && heights != null)
        {
            td.SetHeights(0, 0, heights);
        }

        if (roadMode == RoadMode.PaintSplat && alphaMap != null && roadLayerIndex >= 0)
        {
            td.SetAlphamaps(0, 0, alphaMap);
        }
    }

    List<(int, int)> BuildMinimumSpanningTree(List<Vector3> pts)
    {
        int n = pts.Count;
        var inTree = new bool[n];
        var edges = new List<(int, int)>();

        inTree[0] = true;
        int added = 1;

        while (added < n)
        {
            float best = float.MaxValue;
            int bi = -1, bj = -1;
            for (int i = 0; i < n; i++)
            {
                if (!inTree[i]) continue;
                for (int j = 0; j < n; j++)
                {
                    if (inTree[j]) continue;
                    float d = Vector3.SqrMagnitude(pts[i] - pts[j]);
                    if (d < best)
                    {
                        best = d; bi = i; bj = j;
                    }
                }
            }
            if (bi >= 0 && bj >= 0)
            {
                edges.Add((bi, bj));
                inTree[bj] = true;
                added++;
            }
            else break;
        }
        return edges;
    }

    // =========================
    // Mesh Builder (banked + crown)
    // =========================
    GameObject BuildRoadMeshBanked(List<Vector3> samples, float width, float yOffset, float crown, float tilingPerMeter)
    {
        var go = new GameObject("RoadMesh", typeof(MeshFilter), typeof(MeshRenderer));
        var mf = go.GetComponent<MeshFilter>();
        var mr = go.GetComponent<MeshRenderer>();

        int count = samples.Count;
        var verts = new List<Vector3>(count * 2);
        var uvs   = new List<Vector2>(count * 2);
        var norms = new List<Vector3>(count * 2);
        var tris  = new List<int>((count - 1) * 6);

        float half = width * 0.5f;
        float accLen = 0f;

        // Pre-fetch normals for banking/orientation
        var normals = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 p = samples[i];
            float nx = Mathf.Clamp01((p.x - tPos.x) / tSize.x);
            float nz = Mathf.Clamp01((p.z - tPos.z) / tSize.z);
            normals[i] = td.GetInterpolatedNormal(nx, nz).normalized;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 p = samples[i];

            // Forward vector along the path
            Vector3 fwd;
            if (i == count - 1) fwd = (samples[i] - samples[i - 1]).normalized;
            else if (i == 0)     fwd = (samples[i + 1] - samples[i]).normalized;
            else                 fwd = (samples[i + 1] - samples[i - 1]).normalized;

            // Up from terrain normal at this point (bank/tilt match)
            Vector3 up = normals[i];
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;

            // Ensure orthonormal frame
            // Project forward onto plane orthogonal to up to avoid twist
            fwd = Vector3.ProjectOnPlane(fwd, up).normalized;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.Cross(up, Vector3.right).normalized;

            Vector3 right = Vector3.Cross(up, fwd).normalized;

            if (i > 0) accLen += Vector3.Distance(samples[i], samples[i - 1]);

            // Crown profile: center raised by 'crown', edges taper to 0.
            // param t across width [-1..1], crownFactor = 1 - t^2 (parabolic)
            float centerRaise = crown;

            Vector3 center = p + up * yOffset;

            Vector3 leftPos  = center - right * half + up * (centerRaise * (1f - Mathf.Pow(-1f, 2)));  // at -1 -> 0
            Vector3 rightPos = center + right * half + up * (centerRaise * (1f - Mathf.Pow( 1f, 2)));  // at +1 -> 0

            // For continuous across, sample multiple across the width? We keep two verts; crown applied only to center difference.
            // An improved approximation: shift both edges down slightly, keep center 'raised':
            float edgeDown = crown * 0.0f; // edges not lowered; crown is visual bump at center as road is offset overall.

            leftPos  += up * (-edgeDown);
            rightPos += up * (-edgeDown);

            verts.Add(leftPos);
            verts.Add(rightPos);

            float u = accLen * tilingPerMeter;
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));

            // Use terrain up so lighting follows bank
            norms.Add(up);
            norms.Add(up);
        }

        for (int i = 0; i < count - 1; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = i * 2 + 2;
            int i3 = i * 2 + 3;

            tris.Add(i0); tris.Add(i2); tris.Add(i1);
            tris.Add(i1); tris.Add(i2); tris.Add(i3);
        }

        var mesh = new Mesh();
        mesh.indexFormat = (verts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0, true);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        mf.sharedMesh = mesh;
        if (roadMaterial != null) mr.sharedMaterial = roadMaterial;

        monuments.Add(go);

        return go;
    }

    // =========================
    // Carve + Paint
    // =========================
    void ApplyCarveAndPaint(List<Vector3> samples, ref float[,] heights, ref float[,,] alphaMap, float widthMeters, float shoulderMeters)
    {
        float metersPerHeightPxX = tSize.x / (hmResX - 1);
        float metersPerHeightPxZ = tSize.z / (hmResZ - 1);

        float metersPerAlphaPxX = tSize.x / alphaResX;
        float metersPerAlphaPxZ = tSize.z / alphaResZ;

        float roadRadH_X = (widthMeters * 0.5f) / metersPerHeightPxX;
        float roadRadH_Z = (widthMeters * 0.5f) / metersPerHeightPxZ;
        float shoulderRadH_X = (shoulderMeters) / metersPerHeightPxX;
        float shoulderRadH_Z = (shoulderMeters) / metersPerHeightPxZ;

        float roadRadA_X = (widthMeters * 0.5f) / metersPerAlphaPxX;
        float roadRadA_Z = (widthMeters * 0.5f) / metersPerAlphaPxZ;
        float shoulderRadA_X = (shoulderMeters) / metersPerAlphaPxX;
        float shoulderRadA_Z = (shoulderMeters) / metersPerAlphaPxZ;

        float featherH_X = brushFeatherMeters / metersPerHeightPxX;
        float featherH_Z = brushFeatherMeters / metersPerHeightPxZ;
        float featherA_X = brushFeatherMeters / metersPerAlphaPxX;
        float featherA_Z = brushFeatherMeters / metersPerAlphaPxZ;

        for (int s = 0; s < samples.Count; s++)
        {
            Vector3 wp = samples[s];

            // HEIGHTMAP EDIT
            if (heights != null && roadCutDepthMeters > 0f)
            {
                int hx = Mathf.RoundToInt((wp.x - tPos.x) / tSize.x * (hmResX - 1));
                int hz = Mathf.RoundToInt((wp.z - tPos.z) / tSize.z * (hmResZ - 1));

                int minHX = Mathf.Max(0, Mathf.FloorToInt(hx - (roadRadH_X + shoulderRadH_X + featherH_X)));
                int maxHX = Mathf.Min(hmResX - 1, Mathf.CeilToInt(hx + (roadRadH_X + shoulderRadH_X + featherH_X)));
                int minHZ = Mathf.Max(0, Mathf.FloorToInt(hz - (roadRadH_Z + shoulderRadH_Z + featherH_Z)));
                int maxHZ = Mathf.Min(hmResZ - 1, Mathf.CeilToInt(hz + (roadRadH_Z + shoulderRadH_Z + featherH_Z)));

                for (int z = minHZ; z <= maxHZ; z++)
                {
                    for (int x = minHX; x <= maxHX; x++)
                    {
                        float dx = (x - hx) / (roadRadH_X + shoulderRadH_X + Mathf.Max(1f, featherH_X));
                        float dz = (z - hz) / (roadRadH_Z + shoulderRadH_Z + Mathf.Max(1f, featherH_Z));
                        float d = Mathf.Sqrt(dx * dx + dz * dz);

                        float t = Mathf.InverseLerp(0f, 1f, d);
                        float depth01 = Mathf.SmoothStep(1f, 0f, t);
                        float depthMeters = depth01 * roadCutDepthMeters;
                        float depthNorm = depthMeters / tSize.y;

                        heights[z, x] = Mathf.Max(0f, heights[z, x] - depthNorm);
                    }
                }
            }

            // ALPHAMAP PAINT
            if (alphaMap != null && roadLayerIndex >= 0)
            {
                int ax = Mathf.RoundToInt((wp.x - tPos.x) / tSize.x * (alphaResX - 1));
                int az = Mathf.RoundToInt((wp.z - tPos.z) / tSize.z * (alphaResZ - 1));

                int minAX = Mathf.Max(0, Mathf.FloorToInt(ax - (roadRadA_X + shoulderRadA_X + featherA_X)));
                int maxAX = Mathf.Min(alphaResX - 1, Mathf.CeilToInt(ax + (roadRadA_X + shoulderRadA_X + featherA_X)));
                int minAZ = Mathf.Max(0, Mathf.FloorToInt(az - (roadRadA_Z + shoulderRadA_Z + featherA_Z)));
                int maxAZ = Mathf.Min(alphaResZ - 1, Mathf.CeilToInt(az + (roadRadA_Z + shoulderRadA_Z + featherA_Z)));

                for (int z = minAZ; z <= maxAZ; z++)
                {
                    for (int x = minAX; x <= maxAX; x++)
                    {
                        float dx = (x - ax) / (roadRadA_X + shoulderRadA_X + Mathf.Max(1f, featherA_X));
                        float dz = (z - az) / (roadRadA_Z + shoulderRadA_Z + Mathf.Max(1f, featherA_Z));
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        float t = Mathf.InverseLerp(0f, 1f, d);

                        float strength = Mathf.SmoothStep(1f, 0f, t);

                        float sum = 0f;
                        for (int l = 0; l < alphaLayers; l++) sum += alphaMap[z, x, l];

                        float otherSum = sum - alphaMap[z, x, roadLayerIndex];
                        float targetRoad = Mathf.Max(alphaMap[z, x, roadLayerIndex], strength);
                        float remaining = Mathf.Max(0.0001f, 1f - targetRoad);

                        if (otherSum > 0f)
                        {
                            float scale = remaining / otherSum;
                            for (int l = 0; l < alphaLayers; l++)
                            {
                                if (l == roadLayerIndex) continue;
                                alphaMap[z, x, l] *= scale;
                            }
                        }
                        else
                        {
                            float even = remaining / Mathf.Max(1, alphaLayers - 1);
                            for (int l = 0; l < alphaLayers; l++)
                            {
                                if (l == roadLayerIndex) continue;
                                alphaMap[z, x, l] = even;
                            }
                        }

                        alphaMap[z, x, roadLayerIndex] = targetRoad;
                    }
                }
            }
        }
    }

    int ResolveTerrainLayerIndex(TerrainLayer layer)
    {
        if (layer == null || td == null) return -1;
        var layers = td.terrainLayers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == layer) return i;
        }
        return -1;
    }

    // =========================
    // A* Routing
    // =========================
    void BuildCostField()
    {
        astarGridX = Mathf.Max(8, astarGridX);
        astarGridZ = Mathf.Max(8, astarGridZ);

        cellCost = new float[astarGridX, astarGridZ];
        cellSizeX = tSize.x / astarGridX;
        cellSizeZ = tSize.z / astarGridZ;

        for (int gx = 0; gx < astarGridX; gx++)
        {
            for (int gz = 0; gz < astarGridZ; gz++)
            {
                float wx = tPos.x + (gx + 0.5f) * cellSizeX;
                float wz = tPos.z + (gz + 0.5f) * cellSizeZ;

                float nx = (wx - tPos.x) / tSize.x;
                float nz = (wz - tPos.z) / tSize.z;

                float height = terrain.SampleHeight(new Vector3(wx, 0, wz)) + tPos.y;
                float heightNorm = (height - tPos.y) / tSize.y;

                float slopeDeg = td.GetSteepness(nx, nz);
                float cost = 1f + slopeDeg * slopeCostWeight;

                if (slopeDeg >= impassableSlopeDeg) cost = float.PositiveInfinity;
                if (heightNorm < seaLevel) cost += seaPenalty;

                cellCost[gx, gz] = cost;
            }
        }
    }

    List<Vector3> ComputeAStarRouteWorld(Vector3 a, Vector3 b)
    {
        // Convert world to grid coords
        (int sx, int sz) = WorldToCell(a);
        (int ex, int ez) = WorldToCell(b);

        var pathCells = AStarCells(sx, sz, ex, ez);
        if (pathCells == null || pathCells.Count == 0)
        {
            // fallback to straight if blocked
            return new List<Vector3> { a, b };
        }

        // Cells to world centers
        var pts = new List<Vector3>(pathCells.Count);
        foreach (var c in pathCells)
        {
            float wx = tPos.x + (c.x + 0.5f) * cellSizeX;
            float wz = tPos.z + (c.z + 0.5f) * cellSizeZ;
            float wy = terrain.SampleHeight(new Vector3(wx, 0, wz)) + tPos.y;
            pts.Add(new Vector3(wx, wy, wz));
        }

        // Ensure endpoints are exact
        if (pts.Count > 0)
        {
            pts[0] = new Vector3(pts[0].x, a.y, pts[0].z);
            pts[pts.Count - 1] = new Vector3(pts[^1].x, b.y, pts[^1].z);
            pts.Insert(0, a);
            pts.Add(b);
        }
        else
        {
            pts = new List<Vector3> { a, b };
        }

        return pts;
    }

    struct Cell { public int x, z; public Cell(int x, int z) { this.x = x; this.z = z; } }

    List<Cell> AStarCells(int sx, int sz, int ex, int ez)
    {
        if (sx < 0 || sz < 0 || ex < 0 || ez < 0 ||
            sx >= astarGridX || sz >= astarGridZ || ex >= astarGridX || ez >= astarGridZ)
            return null;

        var open = new List<Cell>();
        var came = new Cell[astarGridX, astarGridZ];
        var g    = new float[astarGridX, astarGridZ];
        var f    = new float[astarGridX, astarGridZ];
        var closed = new bool[astarGridX, astarGridZ];

        for (int x = 0; x < astarGridX; x++)
            for (int z = 0; z < astarGridZ; z++)
            {
                g[x, z] = float.PositiveInfinity;
                f[x, z] = float.PositiveInfinity;
                came[x, z] = new Cell(-1, -1);
            }

        var start = new Cell(sx, sz);
        g[sx, sz] = 0f;
        f[sx, sz] = Heuristic(sx, sz, ex, ez);
        open.Add(start);

        int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dz = { -1, -1, -1, 0, 0, 1, 1, 1 };
        float[] stepCost = { 1.4142f, 1f, 1.4142f, 1f, 1f, 1.4142f, 1f, 1.4142f };

        while (open.Count > 0)
        {
            int bestI = 0; float bestF = f[open[0].x, open[0].z];
            for (int i = 1; i < open.Count; i++)
            {
                float fi = f[open[i].x, open[i].z];
                if (fi < bestF) { bestF = fi; bestI = i; }
            }

            var current = open[bestI];
            open.RemoveAt(bestI);

            if (current.x == ex && current.z == ez)
            {
                return ReconstructPath(came, current);
            }

            closed[current.x, current.z] = true;

            for (int n = 0; n < 8; n++)
            {
                int nx = current.x + dx[n];
                int nz = current.z + dz[n];

                if (nx < 0 || nz < 0 || nx >= astarGridX || nz >= astarGridZ) continue;
                if (closed[nx, nz]) continue;

                float cc = cellCost[nx, nz];
                if (float.IsInfinity(cc)) continue; // impassable

                // move base cost with slope weighting
                float tentative = g[current.x, current.z] + stepCost[n] * (1f + cc);

                if (tentative < g[nx, nz])
                {
                    came[nx, nz] = current;
                    g[nx, nz] = tentative;
                    f[nx, nz] = tentative + Heuristic(nx, nz, ex, ez);

                    // push if not in open
                    bool inOpen = false;
                    for (int j = 0; j < open.Count; j++)
                        if (open[j].x == nx && open[j].z == nz) { inOpen = true; break; }

                    if (!inOpen) open.Add(new Cell(nx, nz));
                }
            }
        }

        return null;
    }

    float Heuristic(int x, int z, int ex, int ez)
    {
        float dx = (x - ex);
        float dz = (z - ez);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    List<Cell> ReconstructPath(Cell[,] came, Cell current)
    {
        var path = new List<Cell>();
        while (current.x >= 0)
        {
            path.Add(current);
            var p = came[current.x, current.z];
            if (p.x < 0) break;
            current = p;
        }
        path.Reverse();
        return path;
    }

    (int, int) WorldToCell(Vector3 w)
    {
        int gx = Mathf.Clamp((int)((w.x - tPos.x) / tSize.x * astarGridX), 0, astarGridX - 1);
        int gz = Mathf.Clamp((int)((w.z - tPos.z) / tSize.z * astarGridZ), 0, astarGridZ - 1);
        return (gx, gz);
    }

    // =========================
    // Path smoothing & resampling
    // =========================
    List<Vector3> SmoothCatmullRom(List<Vector3> pts, int subdivisions)
    {
        if (pts == null || pts.Count < 2) return pts;
        subdivisions = Mathf.Clamp(subdivisions, 1, 16);

        var smoothed = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = i == 0 ? pts[i] : pts[i - 1];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[i + 1];
            Vector3 p3 = (i + 2 < pts.Count) ? pts[i + 2] : pts[i + 1];

            for (int j = 0; j < subdivisions; j++)
            {
                float t = j / (float)subdivisions;
                smoothed.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        smoothed.Add(pts[^1]);
        return smoothed;
    }

    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        // Standard Catmull-Rom
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    List<Vector3> ResampleToStep(List<Vector3> pts, float step, bool conformToTerrain)
    {
        if (pts == null || pts.Count == 0) return pts;
        var outPts = new List<Vector3>();
        outPts.Add(pts[0]);

        float dAcc = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector3 a = pts[i - 1];
            Vector3 b = pts[i];
            float d = Vector3.Distance(a, b);
            if (d < 1e-4f) continue;

            float t = 0f;
            while (t + step < d)
            {
                t += step;
                Vector3 p = Vector3.Lerp(a, b, t / d);
                if (conformToTerrain)
                {
                    p.y = terrain.SampleHeight(p) + tPos.y;
                }
                outPts.Add(p);
            }
        }
        var last = pts[^1];
        if (conformToTerrain) last.y = terrain.SampleHeight(last) + tPos.y;
        outPts.Add(last);

        return outPts;
    }

    // =========================
    // Gizmos
    // =========================
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || placedMonumentPositions == null) return;
        Gizmos.color = Color.yellow;
        foreach (var p in placedMonumentPositions)
            Gizmos.DrawSphere(p, 2f);
    }
}
