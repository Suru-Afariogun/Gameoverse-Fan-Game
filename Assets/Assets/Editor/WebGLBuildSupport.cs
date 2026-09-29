using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Large IL2CPP WebGL builds can fail in wasm-ld with "LLVM ERROR: out of memory".
/// Raises the linker memory budget and forces release-oriented WebGL settings before each WebGL build.
/// </summary>
[InitializeOnLoad]
public sealed class WebGLBuildSupport : IPreprocessBuildWithReport
{
    // wasm-ld peak RAM during link (MB). Raise if you have 16+ GB system RAM and still OOM.
    private const string LinkerMemoryMegabytes = "12288";

    public int callbackOrder => 0;

    static WebGLBuildSupport()
    {
        Environment.SetEnvironmentVariable("EMCC_WASM_LD_MAX_MEMORY", LinkerMemoryMegabytes);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL)
            return;

        Environment.SetEnvironmentVariable("EMCC_WASM_LD_MAX_MEMORY", LinkerMemoryMegabytes);

        ForceReleaseWebGLBuildOptions();
        ApplyRecommendedWebGLSettings();

        Debug.Log(
            "WebGL build prep: release build, High stripping, Optimize Size IL2CPP, " +
            $"wasm-ld memory cap {LinkerMemoryMegabytes} MB.");
    }

    private static void ForceReleaseWebGLBuildOptions()
    {
        if (EditorUserBuildSettings.development)
        {
            Debug.LogWarning(
                "WebGL Development Build was enabled and is being turned off. " +
                "Development WebGL builds use the debug_WebGL_wasm linker path and often fail with " +
                "'LLVM ERROR: out of memory' on large projects.");
            EditorUserBuildSettings.development = false;
        }

        EditorUserBuildSettings.allowDebugging = false;
        EditorUserBuildSettings.connectProfiler = false;
        EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
    }

    private static void ApplyRecommendedWebGLSettings()
    {
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.High);

        NamedBuildTarget webGl = NamedBuildTarget.WebGL;
        PlayerSettings.SetIl2CppCodeGeneration(webGl, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetIl2CppCompilerConfiguration(webGl, Il2CppCompilerConfiguration.Master);

        PlayerSettings.WebGL.debugSymbols = false;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.stripEngineCode = true;
    }

    [MenuItem("Gameoverse/WebGL/Apply Recommended Build Settings")]
    private static void ApplyFromMenu()
    {
        ForceReleaseWebGLBuildOptions();
        ApplyRecommendedWebGLSettings();
        Environment.SetEnvironmentVariable("EMCC_WASM_LD_MAX_MEMORY", LinkerMemoryMegabytes);
        Debug.Log("Applied recommended WebGL build settings.");
    }
}
