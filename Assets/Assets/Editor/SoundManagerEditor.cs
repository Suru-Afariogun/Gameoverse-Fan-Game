#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(SoundManager))]
public class SoundManagerEditor : Editor
{
    private ReorderableList extraSceneMusicList;

    private SerializedProperty startScreenMusic;
    private SerializedProperty startScreenMusicVolume;
    private SerializedProperty homeTownMusic;
    private SerializedProperty homeTownMusicVolume;
    private SerializedProperty levelOneMusic;
    private SerializedProperty levelOneMusicVolume;
    private SerializedProperty levelOneCopyBotBossMusic;
    private SerializedProperty levelOneCopyBotBossMusicVolume;
    private SerializedProperty bossFightMusic;
    private SerializedProperty bossFightMusicVolume;
    private SerializedProperty extraSceneMusic;

    private void OnEnable()
    {
        startScreenMusic = serializedObject.FindProperty("startScreenMusic");
        startScreenMusicVolume = serializedObject.FindProperty("startScreenMusicVolume");
        homeTownMusic = serializedObject.FindProperty("homeTownMusic");
        homeTownMusicVolume = serializedObject.FindProperty("homeTownMusicVolume");
        levelOneMusic = serializedObject.FindProperty("levelOneMusic");
        levelOneMusicVolume = serializedObject.FindProperty("levelOneMusicVolume");
        levelOneCopyBotBossMusic = serializedObject.FindProperty("levelOneCopyBotBossMusic");
        levelOneCopyBotBossMusicVolume = serializedObject.FindProperty("levelOneCopyBotBossMusicVolume");
        bossFightMusic = serializedObject.FindProperty("bossFightMusic");
        bossFightMusicVolume = serializedObject.FindProperty("bossFightMusicVolume");
        extraSceneMusic = serializedObject.FindProperty("extraSceneMusic");

        extraSceneMusicList = new ReorderableList(serializedObject, extraSceneMusic, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Extra Scene Music (drag .unity scenes here or use +)"),
            elementHeight = EditorGUIUtility.singleLineHeight * 3f + 8f,
            drawElementCallback = DrawExtraSceneElement,
            onAddCallback = list =>
            {
                int index = list.serializedProperty.arraySize;
                list.serializedProperty.arraySize++;
                SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("sceneName").stringValue = string.Empty;
                element.FindPropertyRelative("music").objectReferenceValue = null;
                element.FindPropertyRelative("trackVolume").floatValue = 1f;
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "startScreenMusic",
            "startScreenMusicVolume",
            "homeTownMusic",
            "homeTownMusicVolume",
            "levelOneMusic",
            "levelOneMusicVolume",
            "levelOneCopyBotBossMusic",
            "levelOneCopyBotBossMusicVolume",
            "bossFightMusic",
            "bossFightMusicVolume",
            "extraSceneMusic");

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Scene Music", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each scene has its own song slot. Scenes with no clip assigned stay silent. " +
            "Drag .unity files into the box below to add more scene slots.",
            MessageType.Info);

        DrawDedicatedSceneMusic("Start Screen", startScreenMusic, startScreenMusicVolume);
        DrawDedicatedSceneMusic("Home Town", homeTownMusic, homeTownMusicVolume);
        DrawDedicatedSceneMusic("Level One", levelOneMusic, levelOneMusicVolume);
        DrawDedicatedSceneMusic("Level One — Copy Bot Boss", levelOneCopyBotBossMusic, levelOneCopyBotBossMusicVolume);
        DrawDedicatedSceneMusic("Boss Fight Mode", bossFightMusic, bossFightMusicVolume);

        EditorGUILayout.Space(4f);
        DrawSceneDragDropArea();
        extraSceneMusicList.DoLayoutList();

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawDedicatedSceneMusic(string label, SerializedProperty music, SerializedProperty volume)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(music, new GUIContent("Music"));
        EditorGUILayout.PropertyField(volume, new GUIContent("Track Volume"));
        EditorGUILayout.EndVertical();
    }

    private void DrawExtraSceneElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty element = extraSceneMusic.GetArrayElementAtIndex(index);
        SerializedProperty sceneNameProp = element.FindPropertyRelative("sceneName");
        SerializedProperty musicProp = element.FindPropertyRelative("music");
        SerializedProperty volumeProp = element.FindPropertyRelative("trackVolume");

        rect.y += 2f;
        float line = EditorGUIUtility.singleLineHeight;
        float pad = EditorGUIUtility.standardVerticalSpacing;

        Rect sceneRect = new Rect(rect.x, rect.y, rect.width, line);
        SceneAsset sceneAsset = FindSceneAssetByName(sceneNameProp.stringValue);
        EditorGUI.BeginChangeCheck();
        SceneAsset picked = (SceneAsset)EditorGUI.ObjectField(
            sceneRect,
            new GUIContent("Scene"),
            sceneAsset,
            typeof(SceneAsset),
            false);
        if (EditorGUI.EndChangeCheck())
            sceneNameProp.stringValue = picked != null ? picked.name : string.Empty;

        Rect musicRect = new Rect(rect.x, sceneRect.yMax + pad, rect.width, line);
        EditorGUI.PropertyField(musicRect, musicProp, new GUIContent("Music"));

        Rect volumeRect = new Rect(rect.x, musicRect.yMax + pad, rect.width, line);
        EditorGUI.PropertyField(volumeRect, volumeProp, new GUIContent("Track Volume"));
    }

    private void DrawSceneDragDropArea()
    {
        Rect dropArea = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag Scene (.unity) here to add a music slot", EditorStyles.helpBox);

        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition))
            return;

        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            return;

        bool hasScene = false;
        for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
        {
            if (DragAndDrop.objectReferences[i] is SceneAsset)
            {
                hasScene = true;
                break;
            }
        }

        if (!hasScene)
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                if (DragAndDrop.objectReferences[i] is SceneAsset sceneAsset)
                    AddOrFocusExtraSceneEntry(sceneAsset.name);
            }
        }

        evt.Use();
    }

    private void AddOrFocusExtraSceneEntry(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        for (int i = 0; i < extraSceneMusic.arraySize; i++)
        {
            SerializedProperty element = extraSceneMusic.GetArrayElementAtIndex(i);
            if (string.Equals(element.FindPropertyRelative("sceneName").stringValue, sceneName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        int index = extraSceneMusic.arraySize;
        extraSceneMusic.InsertArrayElementAtIndex(index);
        SerializedProperty added = extraSceneMusic.GetArrayElementAtIndex(index);
        added.FindPropertyRelative("sceneName").stringValue = sceneName;
        added.FindPropertyRelative("music").objectReferenceValue = null;
        added.FindPropertyRelative("trackVolume").floatValue = 1f;
    }

    private static SceneAsset FindSceneAssetByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return null;

        string[] guids = AssetDatabase.FindAssets($"t:SceneAsset {sceneName}");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset != null && string.Equals(asset.name, sceneName, System.StringComparison.OrdinalIgnoreCase))
                return asset;
        }

        return null;
    }
}
#endif
