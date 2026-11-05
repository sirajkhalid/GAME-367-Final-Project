using System.Collections.Generic;
using FMODUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamekit3D
{
    /// <summary>
    /// Kit Tools > Audio Backend > Add FMOD Mirrors.
    ///
    /// Adds an FmodAudioSourceMirror to every plain AudioSource listed in the spec below, in the interactable
    /// prefabs and in the open scene(s), and fills in the event path. Level1's switches, pads, crates and doors
    /// are unpacked copies (not linked to their prefabs), so they need their own mirrors.
    ///
    /// Sources are recognised by their clip (the clip names are the same in the prefabs and in Level1), plus the
    /// object name where several sources share a clip. Safe to run again: existing mirrors are kept, and an
    /// event is only filled in where the mirror's event is still empty.
    /// </summary>
    static class FmodMirrorTool
    {
        public struct Rule
        {
            public string clipPrefix;
            public string objectName; // null = any
            public string eventPath;

            public Rule(string clipPrefix, string objectName, string eventPath)
            {
                this.clipPrefix = clipPrefix;
                this.objectName = objectName;
                this.eventPath = eventPath;
            }
        }

        // Not mirrored on purpose: the switch's "Pickup" child (layered into Switch Activate), StaffTakeTrigger
        // (never played), the switch prefab's AudioAfterSwitch (the hum just stops after activation), fireflies
        // (inaudible) and the Grenadier's DestroyPlayer (its sound is an additional event on DeathPlayer).
        public static readonly Rule[] Rules =
        {
            new Rule("Switch_Activated_Before_Activation_Loop", null, "event:/SFX/Interactables/Switch/SFX_Switch_Idle_Loop"),
            new Rule("Switch_Activated_0", "Switch", "event:/SFX/Interactables/Switch/SFX_Switch_Activate"),
            new Rule("Pressure_Pad_Loop", null, "event:/SFX/Interactables/PressurePad/SFX_PressurePad_Idle_Loop"),
            new Rule("Pressure_Pad_Initial", null, "event:/SFX/Interactables/PressurePad/SFX_Pressure_Activate"),
            new Rule("Key_Pickup_Health_Box_Loop", null, "event:/SFX/Interactables/HealthCrate/SFX_HealthCrate_Idle_Loop"),
            new Rule("Key_Pickup_Health_Box_Open", null, "event:/SFX/Interactables/HealthCrate/SFX_HealthCrate_Open"),
            new Rule("Door_Open_Big", null, "event:/SFX/Interactables/Door/SFX_Door_Open"),
            new Rule("Switch_Activated_01", "WeaponPedastal", "event:/SFX/Interactables/WeaponPedestal/SFX_WeaponPedestal_Take"),
            new Rule("Teleporter_Loop", null, "event:/SFX/Interactables/Teleporter/SFX_Teleporter_Loop"),
            new Rule("Acid_Pool_Jump", null, "event:/SFX/Hazard/Acid/SFX_Acid_Splash"),
        };

        static readonly string[] k_Prefabs =
        {
            "Assets/3DGamekit/Prefabs/Interactables/Switch.prefab",
            "Assets/3DGamekit/Prefabs/Interactables/PressurePad.prefab",
            "Assets/3DGamekit/Prefabs/Interactables/HealthCrate.prefab",
            "Assets/3DGamekit/Prefabs/Interactables/DoorHuge.prefab",
            "Assets/3DGamekit/Prefabs/Interactables/WeaponPedestal.prefab",
            "Assets/3DGamekit/Prefabs/Interactables/DeathVolume.prefab",
            "Assets/3DGamekit/Prefabs/Environment/Structures/GateWayHugeTeleporter.prefab",
        };

        [MenuItem("Kit Tools/Audio Backend/Add FMOD Mirrors (Prefabs + Open Scenes)", priority = 230)]
        static void AddMirrors()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[FMOD Mirrors] Exit play mode first.");
                return;
            }

            var log = new List<string>();
            int added = 0, filled = 0;

            foreach (var path in k_Prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                {
                    log.Add("Prefab not found: " + path);
                    continue;
                }

                bool changed = false;
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                {
                    // Sources inside a nested prefab belong to that prefab.
                    if (PrefabUtility.IsPartOfPrefabInstance(source))
                        continue;
                    changed |= Apply(source, path, log, ref added, ref filled, undo: false);
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                bool changed = false;
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var source in go.GetComponentsInChildren<AudioSource>(true))
                    {
                        // Prefab instances (e.g. WeaponPedestal, DeathVolume) get their mirror from the prefab.
                        if (PrefabUtility.IsPartOfPrefabInstance(source))
                            continue;
                        changed |= Apply(source, scene.name, log, ref added, ref filled, undo: true);
                    }
                }

                if (changed)
                    EditorSceneManager.MarkSceneDirty(scene);
            }

            log.Sort();
            Debug.Log("[FMOD Mirrors] Added " + added + " mirror(s), filled in " + filled + " event(s). Save the scene to keep scene changes.\n" +
                      string.Join("\n", log));
        }

        static bool Apply(AudioSource source, string where, List<string> log, ref int added, ref int filled, bool undo)
        {
            string eventPath = Match(source);
            if (eventPath == null)
                return false;

            bool changed = false;
            var mirror = source.GetComponent<FmodAudioSourceMirror>();
            if (mirror == null)
            {
                mirror = undo ? Undo.AddComponent<FmodAudioSourceMirror>(source.gameObject) : source.gameObject.AddComponent<FmodAudioSourceMirror>();
                added++;
                changed = true;
            }

            // Fill an empty event, or repair one that has a path but no GUID (the event wasn't in FMOD's
            // event cache when it was filled in, so it can't be found at runtime).
            bool empty = mirror.fmodEvent.IsNull;
            bool missingGuid = !empty && mirror.fmodEvent.Guid.IsNull && !string.IsNullOrEmpty(mirror.fmodEvent.Path);
            if (empty || missingGuid)
            {
                if (undo)
                    Undo.RecordObject(mirror, "Fill FMOD Mirror Event");
                mirror.fmodEvent = FindEvent(empty ? eventPath : mirror.fmodEvent.Path);
                EditorUtility.SetDirty(mirror);
                filled++;
                changed = true;
            }

            string note = mirror.fmodEvent.Guid.IsNull ? "   <-- NOT FOUND in FMOD: create/build it, then run this again" : "";
            log.Add(where + ": " + GetPath(source.transform) + " -> " + mirror.fmodEvent.Path + note);
            return changed;
        }

        public static string Match(AudioSource source)
        {
            var clip = source.clip;
            if (clip == null)
                return null;

            foreach (var rule in Rules)
            {
                if (!clip.name.StartsWith(rule.clipPrefix))
                    continue;
                if (rule.objectName != null && source.gameObject.name != rule.objectName)
                    continue;
                return rule.eventPath;
            }
            return null;
        }

        // Fills in the GUID too when the event already exists in the built banks; otherwise only the path is set,
        // and FMOD's Event Reference Updater (FMOD > Update Event References) can resolve it later.
        static EventReference FindEvent(string path)
        {
            try
            {
                return EventReference.Find(path);
            }
            catch (System.Exception)
            {
                return new EventReference { Path = path };
            }
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
    }
}
