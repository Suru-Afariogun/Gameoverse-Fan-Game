using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// At runtime, marks factory platforms as Ground. Horizontal platforms are always included.
/// Vertical wall pieces are included except leftmost spawn walls and the two rightmost stage-end walls.
/// </summary>
public static class PlatformGroundLayerBootstrap
{
    private const float DefaultLeftVerticalExcludeMaxX = 23f;
    private const int DefaultRightVerticalExcludeCount = 2;

    private static PhysicsMaterial2D sharedFrictionlessMaterial;
    private static int appliedSceneHandle = int.MinValue;
    private static float appliedLeftExclude = float.NaN;
    private static int appliedRightExclude = int.MinValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnSceneLoad()
    {
        ApplyToActiveScene();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        appliedSceneHandle = int.MinValue;
        ApplyToActiveScene();
    }

    private static void ApplyToActiveScene()
    {
        PlatformGroundLayerSetup setup = Object.FindFirstObjectByType<PlatformGroundLayerSetup>();
        float leftExcludeMaxX = setup != null ? setup.LeftVerticalExcludeMaxX : DefaultLeftVerticalExcludeMaxX;
        int rightExcludeCount = setup != null ? setup.RightVerticalExcludeCount : DefaultRightVerticalExcludeCount;

        ApplyToActiveScene(leftExcludeMaxX, rightExcludeCount);
    }

    public static void ApplyToActiveScene(float leftVerticalExcludeMaxX, int rightVerticalExcludeCount)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
            return;

        leftVerticalExcludeMaxX = Mathf.Max(0f, leftVerticalExcludeMaxX);
        rightVerticalExcludeCount = Mathf.Max(0, rightVerticalExcludeCount);

        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (appliedSceneHandle == sceneHandle &&
            Mathf.Approximately(appliedLeftExclude, leftVerticalExcludeMaxX) &&
            appliedRightExclude == rightVerticalExcludeCount)
            return;

        appliedSceneHandle = sceneHandle;
        appliedLeftExclude = leftVerticalExcludeMaxX;
        appliedRightExclude = rightVerticalExcludeCount;

        List<Transform> verticalPlatforms = new List<Transform>(32);
        HashSet<float> verticalXPositions = new HashSet<float>();

        // Prefer colliders (platforms have them) over every Transform in the scene.
        // Share the per-frame collider cache with phase-refresh setup.
        Collider2D[] cols = SceneColliderCache.GetAllIncludeInactive();
        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];
            if (col == null)
                continue;

            Transform t = col.transform;
            if (t == null || !IsFactoryPlatform(t.name))
                continue;

            // Prefer the named root if children are named differently.
            Transform root = t;
            if (!IsFactoryPlatform(root.name) && root.parent != null && IsFactoryPlatform(root.parent.name))
                root = root.parent;

            if (IsVerticalPlatform(root.name))
            {
                if (!verticalPlatforms.Contains(root))
                {
                    verticalPlatforms.Add(root);
                    verticalXPositions.Add(Mathf.Round(root.position.x * 1000f) / 1000f);
                }
                continue;
            }

            root.gameObject.layer = groundLayer;
            ApplyFrictionlessColliders(root);
        }

        List<float> sortedUniqueX = new List<float>(verticalXPositions);
        sortedUniqueX.Sort((a, b) => b.CompareTo(a));

        HashSet<float> excludedRightX = new HashSet<float>();
        for (int i = 0; i < sortedUniqueX.Count && i < rightVerticalExcludeCount; i++)
            excludedRightX.Add(sortedUniqueX[i]);

        for (int i = 0; i < verticalPlatforms.Count; i++)
        {
            Transform t = verticalPlatforms[i];
            if (t == null)
                continue;

            float x = t.position.x;
            if (x <= leftVerticalExcludeMaxX)
                continue;

            float roundedX = Mathf.Round(x * 1000f) / 1000f;
            if (excludedRightX.Contains(roundedX))
                continue;

            t.gameObject.layer = groundLayer;
            ApplyFrictionlessColliders(t);
        }
    }

    private static void ApplyFrictionlessColliders(Transform platformRoot)
    {
        if (platformRoot == null)
            return;

        if (sharedFrictionlessMaterial == null)
        {
            sharedFrictionlessMaterial = new PhysicsMaterial2D("PlatformFrictionless")
            {
                friction = 0f,
                bounciness = 0f
            };
        }

        Collider2D[] cols = platformRoot.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];
            if (col != null && !col.isTrigger)
                col.sharedMaterial = sharedFrictionlessMaterial;
        }
    }

    private static bool IsFactoryPlatform(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        return objectName.Contains("Factory Platform")
            || objectName.Contains("Factroy")
            || objectName.IndexOf("Thick platform", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsVerticalPlatform(string objectName)
    {
        // Moving vertical platforms are floor platforms that travel on Y — not wall pieces.
        if (objectName.IndexOf("Moving Factory Platform", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return objectName.Contains("Vertical");
    }
}

/// <summary>
/// Optional scene override for vertical wall Ground-layer exclusions (Level one defaults work without this).
/// </summary>
public class PlatformGroundLayerSetup : MonoBehaviour
{
    [Tooltip("Vertical walls at or left of this world X stay off the Ground layer (spawn / camera-left walls).")]
    [SerializeField] private float leftVerticalExcludeMaxX = 23f;

    [Tooltip("How many distinct rightmost vertical wall X positions to leave off Ground (stage end).")]
    [SerializeField] private int rightVerticalExcludeCount = 2;

    public float LeftVerticalExcludeMaxX => leftVerticalExcludeMaxX;
    public int RightVerticalExcludeCount => rightVerticalExcludeCount;

    private void Awake()
    {
        PlatformGroundLayerBootstrap.ApplyToActiveScene(leftVerticalExcludeMaxX, rightVerticalExcludeCount);
    }
}
