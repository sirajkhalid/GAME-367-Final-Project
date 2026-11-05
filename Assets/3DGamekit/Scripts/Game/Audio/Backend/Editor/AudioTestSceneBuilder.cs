using System.IO;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Gamekit3D
{
    /// <summary>
    /// Kit Tools > Audio Backend > Build Audio Test Scene.
    ///
    /// Builds a small test level with every sound source a few steps from the spawn, so audio can be
    /// checked without playing through Level1. It starts from the kit's _TemplateScene (Ellen, camera,
    /// UI, checkpoint, lighting), swaps the template's level art for a flat floor, and adds:
    /// surface pads (Earth/Grass/Puddle/Stone), an acid pool, destructible boxes, two Chompers and a
    /// Grenadier, plus a baked navmesh so the enemies can move. Ellen starts able to attack.
    ///
    /// Rebuilding replaces the scene, so add test content here rather than by hand.
    /// </summary>
    static class AudioTestSceneBuilder
    {
        const string k_Folder = "Assets/3DGamekit/Scenes/AudioTest";
        const string k_ScenePath = k_Folder + "/AudioTest.unity";
        const string k_NavMeshPath = k_Folder + "/AudioTest_NavMesh.asset";
        const string k_Prefabs = "Assets/3DGamekit/Prefabs/";
        const string k_Materials = "Assets/3DGamekit/Art/Materials/";
        const string k_LevelArtSection = "----- LevelAssets -----";
        const string k_EnemyAgentType = "Chomper"; // Chomper and Grenadier both use this agent type
        const string k_MusicEvent = "event:/Music/Music_Level1";

        [MenuItem("Kit Tools/Audio Backend/Build Audio Test Scene", priority = 240)]
        static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[AudioTest] Exit play mode before building the audio test scene.");
                return;
            }

            if (File.Exists(k_ScenePath) &&
                !EditorUtility.DisplayDialog("Rebuild Audio Test Scene?",
                    k_ScenePath + " already exists and will be replaced. Changes made to it by hand will be lost.",
                    "Rebuild", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string[] templates = AssetDatabase.FindAssets("_TemplateScene t:Scene");
            if (templates.Length == 0)
            {
                EditorUtility.DisplayDialog("Template not found", "_TemplateScene is required to build the audio test scene.", "OK");
                return;
            }

            // Don't overwrite the scene file while it's open.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Directory.CreateDirectory(k_Folder);
            AssetDatabase.DeleteAsset(k_NavMeshPath);
            string templatePath = AssetDatabase.GUIDToAssetPath(templates[0]);
            if (File.Exists(k_ScenePath))
            {
                // Overwrite the file but keep its .meta, so the scene keeps its GUID (build settings refer to it).
                File.Copy(templatePath, k_ScenePath, true);
                AssetDatabase.ImportAsset(k_ScenePath, ImportAssetOptions.ForceUpdate);
            }
            else if (!AssetDatabase.CopyAsset(templatePath, k_ScenePath))
            {
                Debug.LogError("[AudioTest] Couldn't copy _TemplateScene to " + k_ScenePath);
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            RemoveTemplateLevelArt(scene);

            var root = new GameObject("----- AudioTest -----");
            var ground = BuildGround(root.transform);
            BakeNavMesh(ground);
            PlaceContent(root.transform);
            LetEllenAttack();

            // Same as Level1: students put their FMOD Studio Event Emitters for ambience under this object.
            new GameObject("Ambience (FMOD)");

            EditorSceneManager.SaveScene(scene);
            AddToBuildSettings(k_ScenePath);
            Debug.Log("[AudioTest] Built " + k_ScenePath);
        }

        // The template's ground, rocks and vegetation sit in the LevelAssets section of the hierarchy.
        static void RemoveTemplateLevelArt(Scene scene)
        {
            bool inLevelArt = false;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name.StartsWith("-----"))
                {
                    inLevelArt = go.name == k_LevelArtSection;
                    continue;
                }

                if (inLevelArt)
                    Object.DestroyImmediate(go);
            }
        }

        static GameObject BuildGround(Transform parent)
        {
            var ground = new GameObject("Ground");
            ground.transform.SetParent(parent, false);

            // Layout: a plaza around the spawn (pads, acid, boxes), Chompers to the west, Grenadier to the east,
            // so either enemy can be reached without passing the other.
            // Floor uses a material in none of Ellen's surface groups, so it plays her default (Earth) footsteps.
            Box("Floor", ground.transform, new Vector3(-10, -0.5f, -20), new Vector3(250, 1, 130), "Environment/Ground/Default_Ground_Mat");

            // One pad per footstep surface, in a row in front of the spawn.
            Pad(ground.transform, "Earth", -18, "Environment/Rock/Ridge01_Mat");
            Pad(ground.transform, "Grass", -6, "Environment/Ground/Moss_Mat");
            Pad(ground.transform, "Puddle", 6, "Environment/Ground/Mud_Mat");
            Pad(ground.transform, "Stone", 18, "Environment/Stonework/FloorPanel03_Mat");

            // Acid pool: walking in kills Ellen and plays the DeathVolume splash.
            Box("AcidPool", ground.transform, new Vector3(-16, 0.05f, 28), new Vector3(8, 0.1f, 8), "Effects/Environment/Acid_Mat");
            Label(parent, "Acid\n(death + splash)", new Vector3(-16, 3, 28));

            return ground;
        }

        static void Pad(Transform parent, string surface, float x, string material)
        {
            Box(surface + "Pad", parent, new Vector3(x, 0.05f, 15), new Vector3(10, 0.1f, 10), material);
            Label(parent.parent, surface, new Vector3(x, 3, 15));
        }

        static void BakeNavMesh(GameObject ground)
        {
            var surface = ground.AddComponent<NavMeshSurface>();
            surface.agentTypeID = FindAgentTypeID(k_EnemyAgentType);
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            AssetDatabase.CreateAsset(surface.navMeshData, k_NavMeshPath);
            EditorUtility.SetDirty(surface);
        }

        static void PlaceContent(Transform parent)
        {
            var facingEast = Quaternion.Euler(0, 90, 0);
            var facingWest = Quaternion.Euler(0, -90, 0);

            Label(parent, "<- Chompers          Grenadier ->", new Vector3(0, 3, 5), width: 24);

            // Destructible boxes: staff hits play Ellen's hit with Surface = Rubble, then the box break.
            for (int i = 0; i < 4; i++)
                Prefab("Interactables/DestructibleBox", parent, new Vector3(14 + (i % 2) * 4, 0, 25 + (i / 2) * 6), Quaternion.identity);
            Label(parent, "Boxes", new Vector3(16, 3, 28));

            // West: Chompers spot Ellen at 15 m, so they wait until she heads this way.
            Prefab("Characters/Enemies/Chomper/Chomper", parent, new Vector3(-50, 0, 10), facingEast);
            Prefab("Characters/Enemies/Chomper/Chomper", parent, new Vector3(-50, 0, 20), facingEast);
            Label(parent, "Chompers\n(spot you at 15 m)", new Vector3(-30, 3, 15), facingWest);

            // East: the Grenadier spots Ellen at 40 m, so it sits far from the spawn.
            // In the game it can only be hit from behind (Damageable Hit Angle 170 around its back). The test copy
            // can be hit from any side; this is a scene override, the prefab is unchanged.
            var grenadier = Prefab("Characters/Enemies/Grenadier/Grenadier", parent, new Vector3(95, 0, 15), facingWest);
            var damageable = grenadier != null ? grenadier.GetComponentInChildren<Damageable>(true) : null;
            if (damageable != null)
                damageable.hitAngle = 360f;
            Label(parent, "Grenadier\n(spots you at 40 m,\nhit from any side)", new Vector3(35, 3, 15), facingEast);

            AddAcidDeathVolume(parent, new Vector3(-16, 0, 28));

            AddInteractables(parent);
            AddMusic(parent);
        }

        // Level1's music (Level01Music prefab + its FMOD event) with the zones moved into the test level:
        // three zones side by side behind the door (Zone01, Zone02, Zone03, west to east), marked with circles
        // on the floor. Main is everywhere else.
        static void AddMusic(Transform parent)
        {
            var music = Prefab("Audio/Level01Music", parent, Vector3.zero, Quaternion.identity);
            var soundTrack = music != null ? music.GetComponent<SoundTrack>() : null;
            if (soundTrack == null)
                return;

            try
            {
                soundTrack.fmodMusicEvent = FMODUnity.EventReference.Find(k_MusicEvent);
            }
            catch (System.Exception)
            {
                Debug.LogWarning("[AudioTest] Couldn't find " + k_MusicEvent + "; assign the music event on Level01Music by hand.");
            }

            // audioSources order gives the labels: [0] Main, [1] Zone01, [2] Zone02, [3] Zone03.
            var sources = soundTrack.audioSources;
            PlaceZone(parent, sources, 1, "Zone01", new Vector3(-30, 0, -65), 12f, new Color(0.2f, 0.5f, 1f));
            PlaceZone(parent, sources, 2, "Zone02", new Vector3(0, 0, -65), 12f, new Color(0.6f, 0.3f, 0.9f));
            PlaceZone(parent, sources, 3, "Zone03", new Vector3(30, 0, -65), 12f, new Color(1f, 0.4f, 0.7f));
        }

        static void PlaceZone(Transform parent, AudioSource[] sources, int index, string label, Vector3 position, float radius, Color color)
        {
            if (sources == null || index >= sources.Length || sources[index] == null)
                return;

            var zone = sources[index].transform;
            zone.position = position;
            var sphere = zone.GetComponent<SphereCollider>();
            if (sphere != null)
            {
                sphere.center = Vector3.zero;
                sphere.radius = radius;
            }

            // A flat disc just above the floor, no collider (so it doesn't change footsteps or the navmesh).
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Zone marker - " + label;
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(parent, false);
            disc.transform.position = position + Vector3.up * (0.02f + index * 0.01f);
            disc.transform.localScale = new Vector3(radius * 2f, 0.005f, radius * 2f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = ZoneMaterial(label, color);

            // Readable when walking south past the door to them.
            var facing = Quaternion.Euler(0, 180, 0);
            Label(parent, "Music " + label, position + Vector3.up * 4f, facing);
        }

        // Materials must be assets to be saved with the scene.
        static Material ZoneMaterial(string label, Color color)
        {
            string path = k_Folder + "/ZoneMarker_" + label + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // South of the spawn (turn around): switch, health crate, pressure pad + door, weapon pedestal, teleporter.
        static void AddInteractables(Transform parent)
        {
            var facingSouth = Quaternion.Euler(0, 180, 0);

            // Turn around from the spawn: the pressure pad is a few steps away and the door about 28 m beyond it
            // (Level1's doors are 46-80 m from their pads, so you hear the door start some way off).
            // Unrotated, the door slab faces along Z, so you watch it slide down into the floor.
            var door = Prefab("Interactables/DoorHuge", parent, new Vector3(0, 0, -35), Quaternion.identity);
            var receiver = door != null ? door.GetComponentInChildren<Gamekit3D.GameCommands.GameCommandReceiver>() : null;

            // The DoorHuge prefab's LOD Group only shows its meshes when the door fills 95% of the screen (its
            // low-detail LOD has no meshes), so it's invisible from a normal distance. Level1's doors have no LOD
            // Group. Switch it off on this copy so the door always shows (scene override; the prefab is unchanged).
            var lodGroup = door != null ? door.GetComponentInChildren<LODGroup>() : null;
            if (lodGroup != null)
                lodGroup.enabled = false;

            var pad = Prefab("Interactables/PressurePad", parent, new Vector3(0, 0, -7), facingSouth);
            LinkSenders(pad, receiver);
            Label(parent, "Pressure pad (opens door)", new Vector3(0, 2.5f, -7), facingSouth, width: 14);

            // The switch and pad send a command when used, and error without a receiver. The door ignores the
            // switch's Activate, so the switch just plays its sounds.
            var sw = Prefab("Interactables/Switch", parent, new Vector3(-20, 0, -12), facingSouth);
            LinkSenders(sw, receiver);
            Label(parent, "Switch", new Vector3(-20, 4, -12), facingSouth);

            Prefab("Interactables/HealthCrate", parent, new Vector3(-12, 0, -12), facingSouth);
            Label(parent, "Health crate", new Vector3(-12, 3, -12), facingSouth);

            Prefab("Interactables/WeaponPedestal", parent, new Vector3(14, 0, -12), facingSouth);
            Label(parent, "Weapon pedestal", new Vector3(14, 3, -12), facingSouth);

            // Its loop is heard from far away (as in Level1), so it sits in a corner. Rotated so the gateway's
            // opening faces east, toward the main area.
            Prefab("Environment/Structures/GateWayHugeTeleporter", parent, new Vector3(-120, 0, -35), Quaternion.Euler(0, 90, 0));
            Label(parent, "Teleporter (loop)", new Vector3(-108, 6, -35), Quaternion.Euler(0, -90, 0));
        }

        static void LinkSenders(GameObject go, Gamekit3D.GameCommands.GameCommandReceiver receiver)
        {
            if (go == null || receiver == null)
                return;

            foreach (var sender in go.GetComponentsInChildren<Gamekit3D.GameCommands.SendGameCommand>(true))
            {
                var serialized = new SerializedObject(sender);
                serialized.FindProperty("interactiveObject").objectReferenceValue = receiver;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // The DeathVolume prefab is the kit's 500 x 500 m fall-out-of-the-world kill zone. Shrink its trigger
        // to the pool and switch off its large solid collider, or it would cover the whole test floor.
        // Its splash AudioSource plays on awake in the prefab, so turn that off (the kit's scenes do the same).
        static void AddAcidDeathVolume(Transform parent, Vector3 position)
        {
            var death = Prefab("Interactables/DeathVolume", parent, position, Quaternion.identity);
            if (death == null)
                return;

            foreach (var source in death.GetComponentsInChildren<AudioSource>())
                source.playOnAwake = false;

            foreach (var box in death.GetComponentsInChildren<BoxCollider>())
            {
                if (box.isTrigger)
                {
                    box.center = new Vector3(0, 0.5f, 0);
                    box.size = new Vector3(8, 1, 8);
                }
                else
                {
                    box.enabled = false;
                }
            }
        }

        // Ellen normally has to pick up the staff from the pedestal first.
        static void LetEllenAttack()
        {
            var ellen = Object.FindFirstObjectByType<PlayerController>();
            if (ellen == null)
            {
                Debug.LogWarning("[AudioTest] Ellen not found in the template scene.");
                return;
            }

            var serialized = new SerializedObject(ellen);
            serialized.FindProperty("canAttack").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, string material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.isStatic = true;

            int environment = LayerMask.NameToLayer("Environment");
            if (environment >= 0)
                go.layer = environment;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_Materials + material + ".mat");
            if (mat != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            else
                Debug.LogWarning("[AudioTest] Material not found: " + material);

            return go;
        }

        static GameObject Prefab(string path, Transform parent, Vector3 position, Quaternion rotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefabs + path + ".prefab");
            if (prefab == null)
            {
                Debug.LogWarning("[AudioTest] Prefab not found: " + path);
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        // Text is readable when looking along the label's forward direction (default: away from the spawn).
        static void Label(Transform parent, string text, Vector3 position, Quaternion? rotation = null, float width = 10)
        {
            var go = new GameObject("Label - " + text.Split('\n')[0]);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 8;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(width, 3);
        }

        static int FindAgentTypeID(string agentName)
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                if (NavMesh.GetSettingsNameFromID(id) == agentName)
                    return id;
            }

            Debug.LogWarning("[AudioTest] Agent type '" + agentName + "' not found; enemies may not move.");
            return 0;
        }

        static void AddToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            var guid = new GUID(AssetDatabase.AssetPathToGUID(scenePath));
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == scenePath)
                {
                    // Repair an entry left with a stale GUID by an earlier build of this scene.
                    if (scenes[i].guid != guid)
                    {
                        scenes[i] = new EditorBuildSettingsScene(scenePath, scenes[i].enabled);
                        EditorBuildSettings.scenes = scenes;
                    }
                    return;
                }
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
