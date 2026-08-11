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

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Bosses");

        string sourceName = Path.GetFileNameWithoutExtension(assetPath);
        System.Type bossType = ResolveBossType(sourceName);
        string bossName = "Boss" + sourceName;
        string destPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{bossName}.prefab");

        if (!AssetDatabase.CopyAsset(assetPath, destPath))
        {
            EditorUtility.DisplayDialog("Boss Prefab", "Failed to copy prefab.", "OK");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(destPath);
        try
        {
            root.name = bossName;

            // Remove player controllers (and any existing boss scripts before re-adding).
            RemoveComponentsOfType(root, typeof(PlayerController));
            RemoveComponentsOfType(root, typeof(Boss));

            Boss boss = (Boss)root.AddComponent(bossType);
            AutoWireBoss(boss, root);

            PrefabUtility.SaveAsPrefabAsset(root, destPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

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

    private static System.Type ResolveBossType(string sourceName)
    {
        if (sourceName.IndexOf("Malice", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return typeof(BossMalice);
        if (sourceName.IndexOf("Kit", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return typeof(BossKit);

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
}
#endif
