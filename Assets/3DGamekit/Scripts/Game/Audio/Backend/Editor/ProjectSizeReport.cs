using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Kit Tools > Audio Backend > Project Size Report (read-only).
    ///
    /// Uses Unity's own dependency data (AssetDatabase.GetDependencies, which also follows binary assets such as
    /// lighting data) to report:
    ///   - what only Level2 uses (the saving from removing Level2), and
    ///   - content no kept scene uses at all.
    /// Writes Temp/ProjectSizeReport.txt (Temp is not in git) and logs a summary. Nothing is deleted.
    /// </summary>
    static class ProjectSizeReport
    {
        static readonly string[] k_KeptScenes =
        {
            "Assets/3DGamekit/Scenes/Start.unity",
            "Assets/3DGamekit/Scenes/GamePlay/Level1.unity",
            "Assets/3DGamekit/Scenes/_TemplateScene.unity",
            "Assets/3DGamekit/Scenes/AudioTest/AudioTest.unity",
        };
        const string k_Level2 = "Assets/3DGamekit/Scenes/GamePlay/Level2.unity";
        const string k_ReportPath = "Temp/ProjectSizeReport.txt";

        // Never report these as unused: code, editor tools, plugins, things loaded by name, documentation.
        static readonly string[] k_AlwaysKeepFolders =
        {
            "/Editor/", "/Resources/", "/Plugins/", "/Scripts/", "/Packages/", "/Gizmos/", "/Documentation/",
            "/Tutorials/", "/TutorialInfo/", "/TextMesh Pro/", "/Settings/", "/StreamingAssets/",
        };
        static readonly string[] k_AlwaysKeepExtensions =
        {
            ".cs", ".asmdef", ".asmref", ".dll", ".shader", ".hlsl", ".cginc", ".compute", ".shadergraph",
            ".shadersubgraph", ".uss", ".uxml", ".json", ".txt", ".md", ".pdf", ".inputactions",
        };

        [MenuItem("Kit Tools/Audio Backend/Project Size Report", priority = 260)]
        static void Run()
        {
            EditorUtility.DisplayProgressBar("Project Size Report", "Collecting dependencies...", 0.2f);
            try
            {
                var kept = new HashSet<string>(AssetDatabase.GetDependencies(k_KeptScenes, true));
                var level2 = new HashSet<string>(AssetDatabase.GetDependencies(new[] { k_Level2 }, true));

                // Level2-only: used by Level2 and not by any kept scene. Its scene folder (probes) is included by
                // GetDependencies through its lighting data.
                var level2Only = level2.Where(p => !kept.Contains(p) && p.StartsWith("Assets/")).ToList();

                EditorUtility.DisplayProgressBar("Project Size Report", "Finding unused content...", 0.6f);
                var used = new HashSet<string>(kept.Concat(level2));
                var unused = AssetDatabase.GetAllAssetPaths()
                    .Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p) && !used.Contains(p))
                    .Where(p => !k_AlwaysKeepFolders.Any(f => p.Contains(f)))
                    .Where(p => !k_AlwaysKeepExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    .ToList();

                var report = new StringBuilder();
                long level2Bytes = Section(report, "ONLY USED BY LEVEL2 (saved by removing Level2)", level2Only);
                long unusedBytes = Section(report, "NOT USED BY ANY SCENE (Start, Level1, Level2, template, audio test)", unused);
                report.Insert(0, string.Format("Level2-only: {0}\nUnused by any scene: {1}\n" +
                    "Kept scenes: {2}\nAlways kept (never listed): code, shaders, Editor/Resources/Plugins/Scripts/Settings folders, docs\n\n",
                    Mb(level2Bytes), Mb(unusedBytes), string.Join(", ", k_KeptScenes.Select(Path.GetFileNameWithoutExtension))));

                Directory.CreateDirectory("Temp");
                File.WriteAllText(k_ReportPath, report.ToString());
                Debug.Log("[Size Report] Level2-only: " + Mb(level2Bytes) + " (" + level2Only.Count + " files). Unused by any scene: " +
                          Mb(unusedBytes) + " (" + unused.Count + " files). Details: " + Path.GetFullPath(k_ReportPath));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Writes the section (total, then by folder, then files by size) and returns the total bytes.
        static long Section(StringBuilder report, string title, List<string> paths)
        {
            var sizes = paths.Select(p => new { Path = p, Bytes = FileSize(p) }).OrderByDescending(f => f.Bytes).ToList();
            long total = sizes.Sum(f => f.Bytes);

            report.AppendLine("===== " + title + ": " + Mb(total) + " in " + sizes.Count + " files");
            report.AppendLine("-- by folder:");
            foreach (var folder in sizes.GroupBy(f => Path.GetDirectoryName(f.Path).Replace('\\', '/'))
                                        .Select(g => new { Folder = g.Key, Bytes = g.Sum(f => f.Bytes), Count = g.Count() })
                                        .OrderByDescending(g => g.Bytes))
                report.AppendLine(string.Format("  {0,10}  {1,5} files  {2}", Mb(folder.Bytes), folder.Count, folder.Folder));
            report.AppendLine("-- files:");
            foreach (var f in sizes)
                report.AppendLine(string.Format("  {0,10}  {1}", Mb(f.Bytes), f.Path));
            report.AppendLine();
            return total;
        }

        static long FileSize(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }

        static string Mb(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }
    }
}
