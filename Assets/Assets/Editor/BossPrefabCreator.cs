#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wonderink-style helper: duplicate a character prefab and swap the player script for a boss script,
/// keeping Animator, colliders, AttackBox, GroundCheck, etc.
/// </summary>
public static class BossPrefabCreator
{
    private const string OutputFolder = "Assets/Prefabs/Bosses";

    private const string BassSourcePrefabPath = "Assets/Prefabs/Bass/Bass.prefab";
    private const string BassSourceFolder = "Assets/Prefabs/Bass";
    private const string BassOutputFolder = "Assets/Prefabs/BossBass";
    private const string BassBossFileName = "BossBass.prefab";

    /// <summary>
    /// Dual / VFX prefabs mirrored into <see cref="BassOutputFolder"/> so Boss Bass has a self-contained kit
    /// (same scripts as playable Bass; edit boss copies without changing the player kit).
    /// </summary>
    private static readonly string[] BassDualAssetFileNames =
    {
        "Metal Blade.prefab",
        "Leaf Shield.prefab",
        "Crash Bomb.prefab",
        "Crash Bomb Plus.prefab",
        "Crash Bomb explosion.prefab",
        "Small Heat Flare.prefab",
        "Heat Flare Stack.prefab",
        "Heat Flare Ball.prefab",
        "Heat Dash flare.prefab",
        "Bubble Bullet.prefab",
        "Bubble bits.prefab",
        "Air Tornado.prefab",
        "Quick Boomerang.prefab",
        "Flash Time Star.prefab",
        "Bass Stunned effect.prefab"
    };

    /// <summary>
    /// BassPlayerController serialized fields copied onto BossBass (same property names on both).
    /// Object references to assets are copied as-is; references to the prefab's own children are re-pointed by name.
    /// Dual weapon refs prefer copies under Prefabs/BossBass when present.
    /// </summary>
    private static readonly string[] BassCopiedProperties =
    {
        "metalBladePrefab",
        "leafShieldPrefab",
        "crashBombPrefab",
        "crashBombPlusPrefab",
        "crashBombExplosionPrefab",
        "smallHeatFlarePrefab",
        "heatFlareStackPrefab",
        "heatFlareBallPrefab",
        "heatDashFlarePrefab",
        "bubbleBulletPrefab",
        "airTornadoPrefab",
        "quickBoomerangPrefab",
        "flashTimeStarPrefab",
        "purpleShotPrefab",
        "firePoint",
        "sparkSpawner",
        "groundLayers",
        "groundCheckRadius",
        "stunnedEffectPrefab",
        "deathEnergyBallPrefab",
        "healedEffectPrefab"
    };

    [MenuItem("Gameoverse/Boss/Create Boss Prefab From Selected Character Prefab")]
    private static void CreateBossPrefabFromSelection()
    {
        GameObject selected = Selection.activeGameObject;
        string assetPath = selected != null ? AssetDatabase.GetAssetPath(selected) : null;

        if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab"))
        {
            EditorUtility.DisplayDialog(
                "Boss Prefab",
                "Select a character prefab asset in the Project window first (e.g. Malice.prefab).",
                "OK");
            return;
        }

        string sourceName = Path.GetFileNameWithoutExtension(assetPath);
        System.Type bossType = ResolveBossType(sourceName);
        string destPath = CreateBossPrefab(assetPath, bossType, out string error);
        if (destPath == null)
        {
            EditorUtility.DisplayDialog("Boss Prefab", error ?? "Failed to create boss prefab.", "OK");
            return;
        }

        Object newPrefab = AssetDatabase.LoadAssetAtPath<Object>(destPath);
        Selection.activeObject = newPrefab;
        EditorGUIUtility.PingObject(newPrefab);

        EditorUtility.DisplayDialog(
            "Boss Prefab Ready",
            $"Created:\n{destPath}\n\n" +
            $"Script: {bossType.Name}\n\n" +
            "Check Ground Check, Ground Layers, Animator, and Attack Hitbox on the new prefab.\n" +
            "Drag it into a boss scene to test.",
            "OK");
    }

    [MenuItem("Gameoverse/Boss/Create Boss Prefab From Selected Character Prefab", true)]
    private static bool CreateBossPrefabValidate()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
            return false;
        string path = AssetDatabase.GetAssetPath(selected);
        return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab");
    }

    /// <summary>Loads <c>Assets/Prefabs/Bass/Bass.prefab</c> and creates <c>Assets/Prefabs/BossBass/BossBass.prefab</c> (no selection needed).</summary>
    [MenuItem("Gameoverse/Boss/Create Boss Bass Prefab")]
    private static void CreateBossBassPrefabFromMenu()
    {
        string destPath = CreateBossPrefab(BassSourcePrefabPath, typeof(BossBass), out string error);
        if (destPath == null)
        {
            EditorUtility.DisplayDialog("Boss Bass Prefab", error ?? "Failed to create Boss Bass prefab.", "OK");
            return;
        }

        Object newPrefab = AssetDatabase.LoadAssetAtPath<Object>(destPath);
        Selection.activeObject = newPrefab;
        EditorGUIUtility.PingObject(newPrefab);

        EditorUtility.DisplayDialog(
            "Boss Bass Prefab Ready",
            $"Created:\n{destPath}\n\n" +
            "Dual prefab refs were copied from the playable Bass prefab.\n" +
            "Boss Spawner (Global game tools) needs a \"Bass\" entry pointing at this prefab.",
            "OK");
    }

    /// <summary>
    /// Command line: <c>Unity.exe -batchmode -quit -projectPath "..." -executeMethod BossPrefabCreator.CreateBossBassPrefabBatch</c>
    /// </summary>
    public static void CreateBossBassPrefabBatch()
    {
        int exitCode = 0;
        try
        {
            string destPath = CreateBossPrefab(BassSourcePrefabPath, typeof(BossBass), out string error);
            if (destPath == null)
            {
                Debug.LogError($"[BossPrefabCreator] Boss Bass prefab failed: {error}");
                exitCode = 1;
            }
            else
            {
                Debug.Log($"[BossPrefabCreator] Boss Bass prefab ready: {destPath}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            exitCode = 1;
        }

        EditorApplication.Exit(exitCode);
    }

    /// <summary>
    /// Copies <paramref name="sourcePrefabPath"/> (or reuses the existing boss prefab so its GUID, and any references to it,
    /// survive), swaps the player controller for <paramref name="bossType"/>, and wires it. Returns the asset path.
    /// </summary>
    private static string CreateBossPrefab(string sourcePrefabPath, System.Type bossType, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(sourcePrefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath) == null)
        {
            error = $"Source prefab not found: {sourcePrefabPath}";
            return null;
        }

        bool isBass = bossType == typeof(BossBass);
        string folder = isBass ? BassOutputFolder : OutputFolder;
        EnsureFolder(folder);

        string sourceName = Path.GetFileNameWithoutExtension(sourcePrefabPath);
        string bossName = "Boss" + sourceName;
        string fileName = isBass ? BassBossFileName : bossName + ".prefab";
        string destPath = $"{folder}/{fileName}";

        bool reuseExisting = isBass && AssetDatabase.LoadAssetAtPath<GameObject>(destPath) != null;
        if (!reuseExisting)
        {
            destPath = AssetDatabase.GenerateUniqueAssetPath(destPath);
            if (!AssetDatabase.CopyAsset(sourcePrefabPath, destPath))
            {
                error = "Failed to copy prefab.";
                return null;
            }
        }

        GameObject root = PrefabUtility.LoadPrefabContents(destPath);
        try
        {
            root.name = Path.GetFileNameWithoutExtension(destPath);

            // Remove player controllers (and any existing boss scripts before re-adding).
            RemoveComponentsOfType(root, typeof(PlayerController));
            RemoveComponentsOfType(root, typeof(Boss));

            Boss boss = (Boss)root.AddComponent(bossType);
            AutoWireBoss(boss, root);

            if (isBass)
            {
                // Playable Bass extras that mean nothing on a boss.
                RemoveComponentsOfType(root, typeof(HexSparkEmitter));
                DestroyChildrenNamed(root, "Double Gear Visual Container");
                DestroyChildrenNamed(root, "Double Gear duration bar");

                EnsureBassDualKitCopied();
                CopyBassDualReferences(sourcePrefabPath, boss, root);
                ApplyBassBossDefaults(boss);
            }

            PrefabUtility.SaveAsPrefabAsset(root, destPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return destPath;
    }

    private static void EnsureFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static System.Type ResolveBossType(string sourceName)
    {
        if (sourceName.IndexOf("Malice", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return typeof(BossMalice);
        if (sourceName.IndexOf("Kit", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return typeof(BossKit);
        if (sourceName.IndexOf("Bass", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return typeof(BossBass);

        return typeof(Boss);
    }

    private static void RemoveComponentsOfType(GameObject root, System.Type baseType)
    {
        Component[] components = root.GetComponents<Component>();
        for (int i = components.Length - 1; i >= 0; i--)
        {
            Component c = components[i];
            if (c == null)
                continue;
            if (baseType.IsInstanceOfType(c))
                Object.DestroyImmediate(c, true);
        }
    }

    private static void DestroyChildrenNamed(GameObject root, string childName)
    {
        Transform child;
        while ((child = FindChildTrimmed(root.transform, childName)) != null && child != root.transform)
            Object.DestroyImmediate(child.gameObject, true);
    }

    private static void AutoWireBoss(Boss boss, GameObject root)
    {
        SerializedObject so = new SerializedObject(boss);

        AssignIfNull(so, "animator", root.GetComponent<Animator>());
        AssignIfNull(so, "spriteRenderer", root.GetComponent<SpriteRenderer>());
        AssignIfNull(so, "attackHitbox", root.GetComponentInChildren<AttackHitbox>(true));

        Transform ground = FindChildRecursive(root.transform, "GroundCheck");
        if (ground != null)
            AssignIfNull(so, "groundCheck", ground);

        Transform firePoint = FindChildRecursive(root.transform, "FirePoint");
        if (firePoint != null)
            AssignIfNull(so, "firePoint", firePoint);

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Mirrors Dual weapon / VFX prefabs from <see cref="BassSourceFolder"/> into <see cref="BassOutputFolder"/>
    /// (skips files that already exist so hand-tuned boss copies are kept).
    /// </summary>
    private static void EnsureBassDualKitCopied()
    {
        EnsureFolder(BassOutputFolder);
        int copied = 0;
        for (int i = 0; i < BassDualAssetFileNames.Length; i++)
        {
            string fileName = BassDualAssetFileNames[i];
            string from = $"{BassSourceFolder}/{fileName}";
            string to = $"{BassOutputFolder}/{fileName}";
            if (AssetDatabase.LoadAssetAtPath<Object>(from) == null)
            {
                Debug.LogWarning($"[BossPrefabCreator] Missing Dual asset: {from}");
                continue;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(to) != null)
                continue;

            if (AssetDatabase.CopyAsset(from, to))
                copied++;
            else
                Debug.LogWarning($"[BossPrefabCreator] Failed to copy Dual asset to {to}");
        }

        if (copied > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BossPrefabCreator] Mirrored {copied} Dual prefab(s) into {BassOutputFolder}.");
        }
    }

    /// <summary>
    /// When a Dual ref on playable Bass points at <c>Prefabs/Bass/X.prefab</c>, prefer the mirrored
    /// <c>Prefabs/BossBass/X.prefab</c> so the boss kit stays editable in its own folder.
    /// </summary>
    private static Object PreferBossBassKitCopy(Object value)
    {
        if (value == null)
            return null;

        string path = AssetDatabase.GetAssetPath(value);
        if (string.IsNullOrEmpty(path) || !path.StartsWith(BassSourceFolder + "/", System.StringComparison.OrdinalIgnoreCase))
            return value;

        string fileName = Path.GetFileName(path);
        string bossPath = $"{BassOutputFolder}/{fileName}";
        Object local = AssetDatabase.LoadAssetAtPath<Object>(bossPath);
        if (local == null)
            return value;

        // Prefer the same component type on the boss copy (e.g. BassMetalBlade on the root).
        if (value is Component wanted)
        {
            GameObject go = local as GameObject;
            if (go == null && local is Component c)
                go = c.gameObject;
            if (go != null)
            {
                Component match = go.GetComponent(wanted.GetType());
                if (match != null)
                    return match;
            }
        }

        return local;
    }

    /// <summary>
    /// Copies the Dual prefab refs (and a few shared fields) from the playable Bass prefab's BassPlayerController
    /// onto the new BossBass by serialized property name.
    /// </summary>
    private static void CopyBassDualReferences(string sourcePrefabPath, Boss boss, GameObject root)
    {
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
        BassPlayerController source = sourcePrefab != null ? sourcePrefab.GetComponent<BassPlayerController>() : null;
        if (source == null)
        {
            Debug.LogWarning($"[BossPrefabCreator] No BassPlayerController on {sourcePrefabPath}; Dual refs were not copied.");
            return;
        }

        SerializedObject src = new SerializedObject(source);
        SerializedObject dst = new SerializedObject(boss);
        int copied = 0;
        for (int i = 0; i < BassCopiedProperties.Length; i++)
        {
            string propName = BassCopiedProperties[i];
            SerializedProperty from = src.FindProperty(propName);
            SerializedProperty to = dst.FindProperty(propName);
            if (from == null || to == null || from.propertyType != to.propertyType)
                continue;

            switch (from.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    Object value = from.objectReferenceValue;
                    // A child of the source prefab (FirePoint, Spark Spawner…): point at the copy's child instead.
                    if (value is Component component && component.transform.IsChildOf(source.transform) &&
                        AssetDatabase.GetAssetPath(component) == sourcePrefabPath)
                    {
                        Transform mapped = FindChildTrimmed(root.transform, component.transform.name);
                        if (mapped == null)
                            continue;
                        value = mapped;
                    }
                    else
                    {
                        value = PreferBossBassKitCopy(value);
                    }

                    to.objectReferenceValue = value;
                    break;
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Integer:
                    to.intValue = from.intValue;
                    break;
                case SerializedPropertyType.Float:
                    to.floatValue = from.floatValue;
                    break;
                default:
                    continue;
            }

            copied++;
        }

        dst.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[BossPrefabCreator] Copied {copied} Dual property value(s) from Bass onto Boss Bass.");
    }

    /// <summary>Boss-style stats (the playable Bass has 10 HP, a 1.5 s i-frame and a longer stun).</summary>
    private static void ApplyBassBossDefaults(Boss boss)
    {
        SerializedObject so = new SerializedObject(boss);
        SetInt(so, "maxHealth", 60);
        SetInt(so, "currentHealth", 60);
        SetFloat(so, "moveSpeed", 7f);
        SetFloat(so, "jumpForce", 16f);
        SetFloat(so, "fallMultiplier", 2.5f);
        SetFloat(so, "hitStunDuration", 0.35f);
        SetFloat(so, "hitInvincibilityDuration", 0.75f);
        SerializedProperty facesLeft = so.FindProperty("spriteFacesLeft");
        if (facesLeft != null)
            facesLeft.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetInt(SerializedObject so, string propertyName, int value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null)
            prop.intValue = value;
    }

    private static void SetFloat(SerializedObject so, string propertyName, float value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null)
            prop.floatValue = value;
    }

    private static void AssignIfNull(SerializedObject so, string propertyName, Object value)
    {
        if (value == null)
            return;

        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null && prop.objectReferenceValue == null)
            prop.objectReferenceValue = value;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>Like <see cref="FindChildRecursive"/> but ignores stray spaces ("Spark Spawner " on the Bass prefab).</summary>
    private static Transform FindChildTrimmed(Transform parent, string name)
    {
        string wanted = name.Trim();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Trim() == wanted)
                return child;

            Transform found = FindChildTrimmed(child, name);
            if (found != null)
                return found;
        }

        return null;
    }
}
#endif
