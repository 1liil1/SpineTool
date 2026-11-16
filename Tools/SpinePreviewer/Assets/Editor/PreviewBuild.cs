using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class PreviewBuild
{
    public static void BuildWindows()
    {
        var output = GetArgument("-previewOutput", "Build/SpinePreviewer.exe");
        var fullOutput = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/Previewer.unity" },
            fullOutput,
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException($"Previewer build failed: {report.summary.result}; errors={report.summary.totalErrors}");
        Debug.Log($"Previewer build succeeded: {fullOutput}; size={report.summary.totalSize}");
    }

    private static string GetArgument(string name, string fallback)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return fallback;
    }
}
