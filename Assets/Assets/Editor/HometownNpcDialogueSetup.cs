using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires DialogueBox + NPC components and the Interactble Symbol prefab for HomeTown.
/// </summary>
public static class HometownNpcDialogueSetup
{
    private const string HomeTownSceneName = "HomeTown";
    private const string InteractSymbolPrefabPath = "Assets/Prefabs/Visual effects/Interactble Symbol.prefab";

    [MenuItem("Gameoverse/HomeTown/Wire NPC Dialogue Components")]
    public static void WireFromMenu()
    {
        EnsureInteractSymbolPrefab();
        if (!WireActiveHomeTownScene())
            Debug.LogWarning("[HometownNPCDialogueSetup] Open the HomeTown scene first, then run this again.");
        else
            Debug.Log("[HometownNPCDialogueSetup] Wired DialogueBox, NPCs, and Interactble Symbol prefab.");
    }

    [InitializeOnLoadMethod]
    private static void AutoWireWhenHomeTownOpens()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.name != HomeTownSceneName)
            return;

        EditorApplication.delayCall += () =>
        {
            EnsureInteractSymbolPrefab();
            if (WireActiveHomeTownScene())
                Debug.Log("[HometownNPCDialogueSetup] Wired DialogueBox + NPC + Interactble Symbol in HomeTown.");
        };
    }

    private static GameVisualEffect EnsureInteractSymbolPrefab()
    {
        GameVisualEffect prefab = AssetDatabase.LoadAssetAtPath<GameVisualEffect>(InteractSymbolPrefabPath);
        if (prefab != null)
            return prefab;

        GameObject sceneSymbol = GameObject.Find("Interactble Symbol");
        if (sceneSymbol == null)
            return null;

        GameVisualEffect fx = sceneSymbol.GetComponent<GameVisualEffect>();
        if (fx == null)
            fx = Undo.AddComponent<GameVisualEffect>(sceneSymbol);

        SerializedObject so = new SerializedObject(fx);
        so.FindProperty("effectType").enumValueIndex = (int)VisualEffectType.InteractableSymbol;
        so.FindProperty("interactableBobAmplitude").floatValue = 0.12f;
        so.FindProperty("interactableBobSpeed").floatValue = 2.4f;
        so.FindProperty("sortOrderInFrontOfHost").intValue = 3;
        so.ApplyModifiedPropertiesWithoutUndo();

        string folder = "Assets/Prefabs/Visual effects";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Visual effects");

        GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(sceneSymbol, InteractSymbolPrefabPath);
        return prefabRoot != null ? prefabRoot.GetComponent<GameVisualEffect>() : null;
    }

    private static bool WireActiveHomeTownScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != HomeTownSceneName)
            return false;

        bool changed = false;
        GameVisualEffect symbolPrefab = EnsureInteractSymbolPrefab();

        GameObject dialogueRoot = GameObject.Find("Dialouge box");
        if (dialogueRoot != null && dialogueRoot.GetComponent<DialogueBox>() == null)
        {
            Undo.AddComponent<DialogueBox>(dialogueRoot);
            changed = true;
        }

        GameObject sceneSymbol = GameObject.Find("Interactble Symbol");
        if (sceneSymbol != null)
        {
            GameVisualEffect fx = sceneSymbol.GetComponent<GameVisualEffect>();
            if (fx == null)
            {
                fx = Undo.AddComponent<GameVisualEffect>(sceneSymbol);
                changed = true;
            }

            SerializedObject so = new SerializedObject(fx);
            SerializedProperty typeProp = so.FindProperty("effectType");
            if (typeProp != null && typeProp.enumValueIndex != (int)VisualEffectType.InteractableSymbol)
            {
                typeProp.enumValueIndex = (int)VisualEffectType.InteractableSymbol;
                so.FindProperty("interactableBobAmplitude").floatValue = 0.12f;
                so.FindProperty("interactableBobSpeed").floatValue = 2.4f;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            // Keep the scene object as a hidden template; NPCs spawn from the prefab.
            if (sceneSymbol.activeSelf)
            {
                sceneSymbol.SetActive(false);
                changed = true;
            }
        }

        string[] npcNames =
        {
            "NPC Bass",
            "Kit's mom",
            "Kit's dad",
            "Scratch",
            "Cat Kid Boy",
            "Cat Kid Girl",
            "Blonde Cat Woman",
            "Blonde Cat man",
            "Old Man Cat",
            "Black Smith Cat",
            "Purple hair cat",
            "Granny Cat"
        };

        for (int i = 0; i < npcNames.Length; i++)
        {
            GameObject npcGo = GameObject.Find(npcNames[i]);
            if (npcGo == null)
                continue;

            NPC npc = npcGo.GetComponent<NPC>();
            if (npc == null)
            {
                npc = Undo.AddComponent<NPC>(npcGo);
                changed = true;
            }

            if (symbolPrefab != null)
            {
                SerializedObject npcSo = new SerializedObject(npc);
                SerializedProperty prefabProp = npcSo.FindProperty("interactSymbolPrefab");
                if (prefabProp != null && prefabProp.objectReferenceValue != symbolPrefab)
                {
                    prefabProp.objectReferenceValue = symbolPrefab;
                    npcSo.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }
        }

        if (changed)
            EditorSceneManager.MarkSceneDirty(scene);

        return true;
    }
}
