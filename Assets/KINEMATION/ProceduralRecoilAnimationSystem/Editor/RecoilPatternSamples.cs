// RecoilPatternSamples.cs
// Place in: Assets/Editor/RecoilPatternSamples.cs

using UnityEditor;
using UnityEngine;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;

public static class RecoilPatternSamples
{
    [MenuItem("Assets/Create/Recoil/Import Legacy AK Sample")]
    public static void CreateLegacyAkPattern()
    {
        var asset = ScriptableObject.CreateInstance<RecoilPatternAsset>();
        asset.name = "AK_LegacyPattern";

        // HIP: Vector2(pitchUpDeg, yawRightDeg) per shot — 30-shot classic up/right → left → right → left wave.
        asset.hip = new Vector2[]
        {
            new(2.6f,  0.0f),
            new(2.7f,  0.3f),
            new(2.8f,  0.7f),
            new(2.9f,  1.2f),
            new(3.0f,  1.8f),
            new(3.0f,  2.5f),
            new(2.8f,  2.2f),
            new(2.6f,  1.5f),
            new(2.5f,  0.5f),
            new(2.4f, -0.8f),

            new(2.3f, -2.0f),
            new(2.2f, -3.2f),
            new(2.1f, -4.0f),
            new(2.0f, -3.0f),
            new(1.9f, -2.0f),
            new(1.8f, -1.0f),
            new(1.7f,  0.5f),
            new(1.6f,  2.0f),
            new(1.5f,  3.2f),
            new(1.4f,  4.0f),

            new(1.3f,  3.0f),
            new(1.2f,  2.0f),
            new(1.1f,  1.0f),
            new(1.0f, -0.5f),
            new(0.9f, -2.0f),
            new(0.8f, -3.0f),
            new(0.7f, -3.8f),
            new(0.6f, -3.0f),
            new(0.5f, -1.5f),
            new(0.4f,  0.0f),
        };

        // ADS pattern = scaled hip (tighter control). Tweak multiplier to taste.
        const float adsScale = 0.7f;
        asset.ads = new Vector2[asset.hip.Length];
        for (int i = 0; i < asset.hip.Length; i++)
            asset.ads[i] = asset.hip[i] * adsScale;

        // Optional per-shot local kick (x=right,y=up,z=back). Keep deterministic & simple.
        asset.loc = new Vector3[asset.hip.Length];
        for (int i = 0; i < asset.loc.Length; i++)
            asset.loc[i] = new Vector3(0f, 0f, 0.008f); // small uniform pushback

        asset.defaultKickPerShot = new Vector3(0f, 0f, 0.008f);
        asset.loop = true;
        asset.pingPong = false;

        var path = EditorUtility.SaveFilePanelInProject(
            "Save Legacy AK Recoil Pattern",
            "AK_LegacyPattern",
            "asset",
            "Choose where to save the recoil pattern asset"
        );

        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            Debug.Log($"Created legacy AK pattern at: {path}");
        }
    }
}
