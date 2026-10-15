using System.Linq;
using UnityEditor;
using UnityEngine;

// Temporary: an unsigned Android App Bundle test build, results written next to the bundle. Delete after use.
static class TempTestBuild
{
    const string Dir = "C:/Users/karol/AppData/Local/Temp/claude/D--Dev-RootLevel-It-root-level-blocks-escape/31596934-73d1-42ac-a279-c59084fc1e3b/scratchpad/test-build/";

    [MenuItem("Blocks Escape/Temp Test Build")]
    static void Build()
    {
        System.IO.Directory.CreateDirectory(Dir);
        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = Dir + "OutOfTheBarn.aab",
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };
        string text;
        try
        {
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            text = $"result {s.result}\nsize {s.totalSize}\ntime {s.totalTime}\nerrors {s.totalErrors}\nwarnings {s.totalWarnings}\n";
            foreach (var step in report.steps)
                foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                    text += $"ERROR [{step.name}] {m.content}\n";
        }
        catch (System.Exception e) { text = "exception " + e; }
        System.IO.File.WriteAllText(Dir + "result.txt", text);
    }
}
