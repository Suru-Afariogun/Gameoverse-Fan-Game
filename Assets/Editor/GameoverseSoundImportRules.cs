#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Assets/Sounds import settings WebGL-safe: 2D, mono SFX, Vorbis in memory.
/// Matches README rules (no streaming music on WebGL, no 3D spatialization for UI/SFX).
/// </summary>
public class GameoverseSoundImportPostprocessor : AssetPostprocessor
{
    private const string SoundsRoot = "Assets/Sounds/";

    [MenuItem("Gameoverse/Audio/Reimport All Sounds (WebGL rules)")]
    public static void ReimportAllSoundsMenu()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SoundsRoot });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        Debug.Log($"Reimported {guids.Length} audio clips under {SoundsRoot} with WebGL-safe settings.");
    }

    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(SoundsRoot))
            return;

        if (assetPath.EndsWith(".meta") || assetPath.EndsWith(".prefab") || assetPath.EndsWith(".zip"))
            return;

        var importer = (AudioImporter)assetImporter;
        bool isMusic = IsMusicAsset(assetPath);

        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.CompressedInMemory;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = isMusic ? 0.7f : 0.65f;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;

        var webGl = importer.GetOverrideSampleSettings("WebGL");
        webGl.loadType = AudioClipLoadType.CompressedInMemory;
        webGl.compressionFormat = AudioCompressionFormat.Vorbis;
        webGl.quality = isMusic ? 0.7f : 0.65f;
        webGl.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        webGl.preloadAudioData = true;
        importer.SetOverrideSampleSettings("WebGL", webGl);

        importer.forceToMono = !isMusic;
        importer.loadInBackground = isMusic;
        importer.ambisonic = false;
    }

    private static bool IsMusicAsset(string path)
    {
        string lower = path.ToLowerInvariant();
        if (lower.Contains("song") || lower.Contains("music") || lower.Contains("theme") || lower.Contains("beat"))
            return true;

        return lower.Contains("lofi") ||
               lower.Contains("waiting music") ||
               lower.Contains("boss fight song") ||
               lower.Contains("cyber song");
    }
}

#endif
