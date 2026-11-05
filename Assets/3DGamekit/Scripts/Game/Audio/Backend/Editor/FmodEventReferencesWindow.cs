using System.Collections.Generic;
using System.IO;
using System.Linq;
using FMODUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamekit3D
{
    /// <summary>
    /// FMOD > Event References: browse the FMOD project's events (folders like FMOD's Event Browser) and find where
    /// each one is hooked up in Unity (prefabs and scenes), with a click to open the prefab/scene and select it.
    ///
    /// Select an event, then Find References. Results are remembered for the editor session only (nothing is saved;
    /// Rescan clears them). Right-click any FMOD event field in the Inspector > Find FMOD Event References.
    ///
    /// Prefabs and open scenes are located exactly. A scene that isn't open is found by a quick text search and listed
    /// as one entry; Open loads the scene and then lists the exact objects.
    /// </summary>
    public partial class FmodEventReferencesWindow : EditorWindow
    {
        const string k_Title = "FMOD Event References";

        class Usage
        {
            public string assetPath;
            public bool isScene;
            public bool located;      // false = scene not open yet, exact objects unknown
            public string objectPath; // from the prefab root / scene root, for display
            public int[] siblingPath; // sibling indices from the root, to find the exact object (names can repeat)
            public string component;
            public string field;
            public string original;  // the original Unity sound, for the CSV export
        }

        class Node
        {
            public string name;
            public string key; // folder path, used for expanded state
            public EditorEventRef ev; // null for folders
            public List<Node> children = new List<Node>();
        }

        // Remembered for the session (cleared on Rescan or script reload). Keyed by event GUID.
        static readonly Dictionary<FMOD.GUID, List<Usage>> s_Results = new Dictionary<FMOD.GUID, List<Usage>>();
        // Exact references per prefab, so each prefab is loaded at most once per session.
        static readonly Dictionary<string, List<KeyValuePair<FMOD.GUID, Usage>>> s_PrefabRefs =
            new Dictionary<string, List<KeyValuePair<FMOD.GUID, Usage>>>();

        static Texture2D s_FolderOpen, s_FolderClosed, s_EventIcon, s_SnapshotIcon;
        static GUIStyle s_RowStyle, s_UnusedRowStyle;

        string m_Search = "";
        FMOD.GUID m_Selected;
        bool m_HasSelection;
        Vector2 m_ListScroll, m_ResultScroll;
        readonly HashSet<string> m_Expanded = new HashSet<string> { "Events", "Snapshots" };

        [MenuItem("FMOD/Event References", priority = 100)]
        static void Open()
        {
            GetWindow<FmodEventReferencesWindow>(k_Title);
        }

        [InitializeOnLoadMethod]
        static void RegisterContextMenu()
        {
            EditorApplication.contextualPropertyMenu += (menu, property) =>
            {
                if (property.type != "EventReference")
                    return;

                var guid = ReadGuid(property);
                if (guid.IsNull)
                    return;

                menu.AddItem(new GUIContent("Find FMOD Event References"), false, () =>
                {
                    var window = GetWindow<FmodEventReferencesWindow>(k_Title);
                    window.Select(guid);
                    window.Reveal(guid);
                    window.FindReferences(guid);
                });
            };
        }

        void Select(FMOD.GUID guid)
        {
            m_Selected = guid;
            m_HasSelection = true;
            Repaint();
        }

        // Expands the folders down to an event so it's visible in the tree.
        void Reveal(FMOD.GUID guid)
        {
            var ev = EventManager.EventFromGUID(guid);
            if (ev == null)
                return;
            string key = "";
            var parts = SplitPath(ev.Path);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                key = i == 0 ? parts[0] : key + "/" + parts[i];
                m_Expanded.Add(key);
            }
        }

        void FindReferences(FMOD.GUID guid)
        {
            try
            {
                s_Results[guid] = Scan(guid);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                s_Results.Remove(guid);
            }
            Repaint();
        }

        void OnGUI()
        {
            LoadIcons();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawEventList();
                DrawResults();
            }
        }

        // --- Event tree (left) ---

        void DrawEventList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.45f)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    m_Search = EditorGUILayout.TextField(m_Search, EditorStyles.toolbarSearchField);
                    if (GUILayout.Button(new GUIContent("Scan All", "Find references for every event, to see counts and unused events (grey)"), GUILayout.Width(70)))
                        ScanAll();
                    if (GUILayout.Button(new GUIContent("Export CSV", "Write the audio spec (every hook, event, parameters, when it plays) as a CSV"), GUILayout.Width(80)))
                    {
                        ExportCsv();
                        GUIUtility.ExitGUI();
                    }
                }

                m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll);
                if (string.IsNullOrEmpty(m_Search))
                {
                    foreach (var root in BuildTree().children)
                        DrawNode(root, 0);
                }
                else
                {
                    // Like FMOD's browser: a flat list of matches while searching.
                    foreach (var ev in EventManager.Events.OrderBy(e => e.Path))
                    {
                        if (ev.Path.IndexOf(m_Search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                            DrawEventRow(ev, ev.Path, 0);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        static string[] SplitPath(string path)
        {
            // "event:/SFX/Characters/Ellen/X" -> Events, SFX, Characters, Ellen, X
            bool snapshot = path.StartsWith("snapshot:");
            var rest = path.Substring(path.IndexOf(":/") + 2).Split('/');
            var parts = new string[rest.Length + 1];
            parts[0] = snapshot ? "Snapshots" : "Events";
            rest.CopyTo(parts, 1);
            return parts;
        }

        static Node BuildTree()
        {
            var root = new Node();
            foreach (var ev in EventManager.Events)
            {
                if (!ev.Path.StartsWith("event:/") && !ev.Path.StartsWith("snapshot:/"))
                    continue;

                var parts = SplitPath(ev.Path);
                var node = root;
                string key = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    key = i == 0 ? parts[0] : key + "/" + parts[i];
                    var child = node.children.FirstOrDefault(c => c.ev == null && c.name == parts[i]);
                    if (child == null)
                    {
                        child = new Node { name = parts[i], key = key };
                        node.children.Add(child);
                    }
                    node = child;
                }
                node.children.Add(new Node { name = parts[parts.Length - 1], ev = ev });
            }
            Sort(root);
            return root;
        }

        // Folders first, then events, alphabetically (as in FMOD's browser). Events before Snapshots at the top.
        static void Sort(Node node)
        {
            node.children = node.children
                .OrderBy(c => c.ev != null)
                .ThenBy(c => c.name == "Snapshots")
                .ThenBy(c => c.name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var child in node.children)
                Sort(child);
        }

        void DrawNode(Node node, int depth)
        {
            if (node.ev != null)
            {
                DrawEventRow(node.ev, node.name, depth);
                return;
            }

            bool expanded = m_Expanded.Contains(node.key);
            var rect = GUILayoutUtility.GetRect(new GUIContent(node.name), s_RowStyle);
            rect.xMin += depth * 14;

            var arrowRect = new Rect(rect.x, rect.y, 14, rect.height);
            bool toggled = EditorGUI.Foldout(arrowRect, expanded, GUIContent.none, true);
            var labelRect = new Rect(rect.x + 14, rect.y, rect.width - 14, rect.height);
            if (GUI.Button(labelRect, new GUIContent(node.name, expanded ? s_FolderOpen : s_FolderClosed), s_RowStyle))
                toggled = !expanded;

            if (toggled != expanded)
            {
                if (toggled) m_Expanded.Add(node.key);
                else m_Expanded.Remove(node.key);
            }

            if (toggled)
            {
                foreach (var child in node.children)
                    DrawNode(child, depth + 1);
            }
        }

        void DrawEventRow(EditorEventRef ev, string label, int depth)
        {
            List<Usage> found;
            bool scanned = s_Results.TryGetValue(ev.Guid, out found);
            string text = scanned ? label + "  (" + found.Count + ")" : label;
            var icon = ev.Path.StartsWith("snapshot:") ? s_SnapshotIcon : s_EventIcon;
            var style = scanned && found.Count == 0 ? s_UnusedRowStyle : s_RowStyle;

            var content = new GUIContent(text, icon);
            var rect = GUILayoutUtility.GetRect(content, style);
            rect.xMin += depth * 14 + 14;

            if (m_HasSelection && ev.Guid.Equals(m_Selected))
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 1f));

            if (GUI.Button(rect, content, style))
                Select(ev.Guid);
        }

        static void LoadIcons()
        {
            if (s_RowStyle != null)
                return;

            s_FolderOpen = EditorUtils.LoadImage("FolderIconOpen.png");
            s_FolderClosed = EditorUtils.LoadImage("FolderIconClosed.png");
            s_EventIcon = EditorUtils.LoadImage("EventIcon.png");
            s_SnapshotIcon = EditorUtils.LoadImage("SnapshotIcon.png");

            s_RowStyle = new GUIStyle(EditorStyles.label) { fixedHeight = 18, imagePosition = ImagePosition.ImageLeft };
            s_UnusedRowStyle = new GUIStyle(s_RowStyle) { normal = { textColor = Color.gray } };
        }

        // --- Results (right) ---

        void DrawResults()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (!m_HasSelection)
                {
                    EditorGUILayout.HelpBox("Select an event, then Find References.\n\nTip: right-click any FMOD event field in the Inspector > Find FMOD Event References.", MessageType.Info);
                    return;
                }

                var ev = EventManager.EventFromGUID(m_Selected);
                EditorGUILayout.LabelField(ev != null ? ev.Path : m_Selected.ToString(), EditorStyles.boldLabel);

                List<Usage> usages;
                bool scanned = s_Results.TryGetValue(m_Selected, out usages);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!scanned)
                    {
                        if (GUILayout.Button("Find References", GUILayout.Height(24)))
                        {
                            FindReferences(m_Selected);
                            GUIUtility.ExitGUI();
                        }
                    }
                    else if (GUILayout.Button(new GUIContent("Rescan", "Forget all remembered results and find references for this event again"), GUILayout.Width(70)))
                    {
                        s_Results.Clear();
                        s_PrefabRefs.Clear();
                        FindReferences(m_Selected);
                        GUIUtility.ExitGUI();
                    }
                }

                if (!scanned)
                    return;

                if (usages.Count == 0)
                {
                    EditorGUILayout.HelpBox("Not used in any prefab or scene.", MessageType.None);
                    return;
                }

                m_ResultScroll = EditorGUILayout.BeginScrollView(m_ResultScroll);
                foreach (var usage in usages.ToList())
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        string asset = Path.GetFileNameWithoutExtension(usage.assetPath) + (usage.isScene ? " (scene)" : " (prefab)");
                        string where = usage.located
                            ? usage.objectPath + "\n" + usage.component + " · " + ObjectNames.NicifyVariableName(usage.field)
                            : "used in this scene - open it to see the objects";
                        EditorGUILayout.LabelField(asset + "\n" + where, EditorStyles.wordWrappedLabel);

                        if (GUILayout.Button(usage.located ? "Show" : "Open", GUILayout.Width(50), GUILayout.Height(32)))
                        {
                            Show(usage);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        // --- Scanning ---

        static List<Usage> Scan(FMOD.GUID guid)
        {
            var result = new List<Usage>();
            string d1 = guid.Data1.ToString(), d4 = guid.Data4.ToString();

            foreach (var path in AssetPaths("t:Prefab"))
            {
                foreach (var pair in PrefabRefs(path))
                {
                    if (pair.Key.Equals(guid))
                        result.Add(pair.Value);
                }
            }

            foreach (var path in AssetPaths("t:Scene"))
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (scene.isLoaded)
                {
                    foreach (var pair in SceneRefs(scene))
                    {
                        if (pair.Key.Equals(guid))
                            result.Add(pair.Value);
                    }
                }
                else if (FileContains(path, d1, d4))
                {
                    result.Add(new Usage { assetPath = path, isScene = true, located = false });
                }
            }

            return result;
        }

        void ScanAll()
        {
            var events = EventManager.Events;
            try
            {
                for (int i = 0; i < events.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(k_Title, events[i].Path, (float)i / events.Count))
                        break;
                    if (s_Results.ContainsKey(events[i].Guid))
                        continue;
                    try
                    {
                        s_Results[events[i].Guid] = Scan(events[i].Guid);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static IEnumerable<string> AssetPaths(string filter)
        {
            foreach (var guid in AssetDatabase.FindAssets(filter, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // FMOD's own cache/plugin data contains every GUID, so skip it.
                if (path.StartsWith("Assets/Plugins/FMOD"))
                    continue;
                yield return path;
            }
        }

        // Quick text check: an EventReference stores its GUID as Data1..Data4 (in scene overrides as "value: ...").
        static bool FileContains(string path, string d1, string d4)
        {
            string text = File.ReadAllText(path);
            return text.Contains(d1) && text.Contains(d4);
        }

        static List<KeyValuePair<FMOD.GUID, Usage>> PrefabRefs(string path)
        {
            List<KeyValuePair<FMOD.GUID, Usage>> refs;
            if (s_PrefabRefs.TryGetValue(path, out refs))
                return refs;

            refs = new List<KeyValuePair<FMOD.GUID, Usage>>();
            // Most prefabs have no FMOD references; skip loading those.
            string text = File.ReadAllText(path);
            if (text.Contains("Guid:") || text.Contains(".Guid.Data1"))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root != null)
                    Collect(root, path, false, refs);
            }

            s_PrefabRefs[path] = refs;
            return refs;
        }

        static List<KeyValuePair<FMOD.GUID, Usage>> SceneRefs(Scene scene)
        {
            var refs = new List<KeyValuePair<FMOD.GUID, Usage>>();
            foreach (var root in scene.GetRootGameObjects())
                Collect(root, scene.path, true, refs);
            return refs;
        }

        static void Collect(GameObject root, string assetPath, bool isScene, List<KeyValuePair<FMOD.GUID, Usage>> into)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue; // missing script

                // A reference inherited unchanged from a prefab is listed under that prefab instead.
                bool fromPrefab = PrefabUtility.IsPartOfPrefabInstance(component);

                var serialized = new SerializedObject(component);
                var it = serialized.GetIterator();
                bool enterChildren = true;
                while (it.Next(enterChildren))
                {
                    enterChildren = true;

                    // Lists of events (e.g. Random Audio Player's Fmod Additional Events): read each entry directly.
                    if (it.isArray && it.arrayElementType == "EventReference")
                    {
                        for (int i = 0; i < it.arraySize; i++)
                            AddReference(it.GetArrayElementAtIndex(i), component, fromPrefab, assetPath, isScene, into);
                        enterChildren = false;
                        continue;
                    }

                    if (it.type != "EventReference")
                        continue;

                    AddReference(it, component, fromPrefab, assetPath, isScene, into);
                    enterChildren = false;
                }
            }
        }

        static void AddReference(SerializedProperty eventRef, Component component, bool fromPrefab, string assetPath, bool isScene,
            List<KeyValuePair<FMOD.GUID, Usage>> into)
        {
            if (fromPrefab && !HasOverride(eventRef))
                return;

            var guid = ReadGuid(eventRef);
            if (guid.IsNull)
                return;

            into.Add(new KeyValuePair<FMOD.GUID, Usage>(guid, new Usage
            {
                assetPath = assetPath,
                isScene = isScene,
                located = true,
                objectPath = GetPath(component.transform),
                siblingPath = GetSiblingPath(component.transform),
                component = ObjectNames.NicifyVariableName(component.GetType().Name),
                field = eventRef.propertyPath.Replace(".Array.data", ""),
                original = OriginalSound(component, eventRef.propertyPath),
            }));
        }

        static bool HasOverride(SerializedProperty eventRef)
        {
            var guid = eventRef.FindPropertyRelative("Guid");
            return eventRef.prefabOverride || (guid != null && guid.prefabOverride);
        }

        // Returns a null GUID for anything that isn't FMOD's EventReference (other types can share the name).
        static FMOD.GUID ReadGuid(SerializedProperty eventRef)
        {
            var guid = eventRef.FindPropertyRelative("Guid");
            var d1 = guid != null ? guid.FindPropertyRelative("Data1") : null;
            var d2 = guid != null ? guid.FindPropertyRelative("Data2") : null;
            var d3 = guid != null ? guid.FindPropertyRelative("Data3") : null;
            var d4 = guid != null ? guid.FindPropertyRelative("Data4") : null;
            if (d1 == null || d2 == null || d3 == null || d4 == null)
                return new FMOD.GUID();

            return new FMOD.GUID { Data1 = d1.intValue, Data2 = d2.intValue, Data3 = d3.intValue, Data4 = d4.intValue };
        }

        static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        // Root's index among the scene roots (or 0 for a prefab root), then each child's sibling index.
        static int[] GetSiblingPath(Transform t)
        {
            var path = new List<int>();
            while (t != null)
            {
                path.Insert(0, t.GetSiblingIndex());
                t = t.parent;
            }
            return path.ToArray();
        }

        // --- Navigation ---

        void Show(Usage usage)
        {
            if (usage.isScene)
            {
                var scene = SceneManager.GetSceneByPath(usage.assetPath);
                if (!scene.isLoaded)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        return;
                    scene = EditorSceneManager.OpenScene(usage.assetPath, OpenSceneMode.Single);

                    // Now the scene is open, replace the placeholder with the exact objects.
                    var exact = SceneRefs(scene).Where(p => p.Key.Equals(m_Selected)).Select(p => p.Value).ToList();
                    var list = s_Results[m_Selected];
                    list.Remove(usage);
                    list.AddRange(exact);
                    if (exact.Count == 0)
                        return;
                    usage = exact[0];
                }

                SelectObject(Find(scene.GetRootGameObjects(), usage));
            }
            else
            {
                var stage = PrefabStageUtility.OpenPrefab(usage.assetPath);
                if (stage != null)
                    SelectObject(Find(new[] { stage.prefabContentsRoot }, usage, prefab: true));
            }
        }

        // Follows the sibling indices; falls back to the name path if the hierarchy changed since the scan.
        static GameObject Find(GameObject[] roots, Usage usage, bool prefab = false)
        {
            var p = usage.siblingPath;
            int rootIndex = prefab ? 0 : p[0];
            if (rootIndex < roots.Length)
            {
                Transform t = roots[rootIndex].transform;
                for (int i = 1; i < p.Length && t != null; i++)
                    t = p[i] < t.childCount ? t.GetChild(p[i]) : null;
                if (t != null && GetPath(t) == usage.objectPath)
                    return t.gameObject;
            }

            int slash = usage.objectPath.IndexOf('/');
            string rootName = slash < 0 ? usage.objectPath : usage.objectPath.Substring(0, slash);
            foreach (var root in roots)
            {
                if (root.name != rootName)
                    continue;
                if (slash < 0)
                    return root;
                var child = root.transform.Find(usage.objectPath.Substring(slash + 1));
                if (child != null)
                    return child.gameObject;
            }
            return null;
        }

        static void SelectObject(GameObject go)
        {
            if (go == null)
                return;
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }
    }
}
