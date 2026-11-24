using System;
using System.IO;
using System.Linq;
using SKECS.GA.Unity.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class GaUnityConformanceBuild
{
    public static void BuildMono() => Build(ScriptingImplementation.Mono2x);
    public static void BuildIl2Cpp() => Build(ScriptingImplementation.IL2CPP);
    public static void GenerateMono() => Generate(ScriptingImplementation.Mono2x);
    public static void GenerateIl2Cpp() => Generate(ScriptingImplementation.IL2CPP);

    private static void Generate(ScriptingImplementation backend)
    {
        Configure(backend);
        GaUnityGenerator.GenerateAll();
    }

    private static BuildTarget Configure(ScriptingImplementation backend)
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        var group = BuildPipeline.GetBuildTargetGroup(target);
        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
        if (target != BuildTarget.WebGL)
            PlayerSettings.SetScriptingBackend(namedTarget, backend);
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.Medium);
        PlayerSettings.runInBackground = true;

        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-ga-graphics-api");
        if (index >= 0)
        {
            var apis = args[index + 1].Split(',').Select(value => Enum.Parse<GraphicsDeviceType>(value, ignoreCase: true)).ToArray();
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, apis);
        }
        return target;
    }

    private static void Build(ScriptingImplementation backend)
    {
        var target = Configure(backend);
        var args = Environment.GetCommandLineArgs();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const string scenePath = "Assets/GaUnityConformance.unity";
        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new BuildFailedException("Could not save the Unity GA conformance scene.");
        int outputIndex = Array.IndexOf(args, "-ga-output");
        if (outputIndex < 0 || outputIndex + 1 == args.Length)
            throw new ArgumentException("Supply the Player path with -ga-output.");
        string output = Path.GetFullPath(args[outputIndex + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new BuildFailedException($"Unity GA Player build failed ({report.summary.result}).");
        Debug.Log($"SKECS_GA_UNITY_BUILD_OK target={target} backend={backend}");
    }
}
