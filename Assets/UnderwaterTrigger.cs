using UnityEngine;
using UnityEngine.Rendering;

public class UnderwaterTrigger : MonoBehaviour
{
    public Volume underwaterVolume;
    public Transform waterSurface;
    public float waterLevelOffset = 0.1f;
    public Camera cam;

    void Update()
    {
        if (cam.transform.position.y < waterSurface.position.y - waterLevelOffset)
            underwaterVolume.weight = 1f;  // Enable underwater effects
        else
            underwaterVolume.weight = 0f;  // Disable underwater effects
    }
}
