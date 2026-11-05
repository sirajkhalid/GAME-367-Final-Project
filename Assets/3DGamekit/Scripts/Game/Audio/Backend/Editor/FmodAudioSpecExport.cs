using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FMODUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamekit3D
{
    /// <summary>
    /// Export Audio Spec (CSV): one row per FMOD hook in Unity (prefabs + build scenes, excluding the audio test
    /// scene), with the event, 2D/3D, bank, parameters, where it is, when it plays and the original Unity sound.
    /// Then rows for the global parameters and for FMOD events not hooked up anywhere. For the student handout.
    /// Kit Tools > Audio Backend > Export Audio Spec (CSV), or the Export CSV button in FMOD > Event References.
    /// </summary>
    public partial class FmodEventReferencesWindow
    {
        static readonly string[] k_Header =
        {
            "Category", "Event", "Type", "Bank", "Parameters", "Location", "Object", "Component · Field",
            "Plays when", "Original Unity sound",
        };

        [MenuItem("Kit Tools/Audio Backend/Export Audio Spec (CSV)", priority = 250)]
        static void ExportCsvMenu()
        {
            ExportCsv();
        }

        static void ExportCsv()
        {
            // StudentDocs/ is included in student zips (Docs/ is instructor-only, export-ignore in .gitattributes).
            Directory.CreateDirectory("StudentDocs");
            string path = EditorUtility.SaveFilePanel("Export Audio Spec", "StudentDocs", "FMOD_Audio_Spec", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            var refs = CollectAll();
            if (refs == null)
                return;

            var csv = new StringBuilder();
            AppendRow(csv, k_Header);

            var rows = refs
                .Select(r => new { r.Key, Usage = r.Value, Category = Category(r.Value) })
                .OrderBy(r => CategoryOrder(r.Category)).ThenBy(r => r.Category).ThenBy(r => r.Usage.assetPath).ThenBy(r => r.Usage.objectPath);
            foreach (var row in rows)
            {
                var ev = EventManager.EventFromGUID(row.Key);
                var u = row.Usage;
                AppendRow(csv,
                    row.Category,
                    ev != null ? ev.Path : "(missing in FMOD) " + row.Key,
                    EventType(ev),
                    ev != null ? string.Join(", ", ev.Banks.Select(b => b.Name)) : "",
                    Parameters(ev),
                    Path.GetFileNameWithoutExtension(u.assetPath) + (u.isScene ? " (scene)" : " (prefab)"),
                    u.objectPath,
                    u.component + " · " + ObjectNames.NicifyVariableName(u.field),
                    Describe(u),
                    u.original);
            }

            // Global parameters (set from code, not from a slot).
            AppendRow(csv, "Global parameter", "MusicState", "labeled, global", "", "Main, Zone01, Zone02, Zone03, Boss, BossDefeated",
                "", "", "Music zones + boss",
                "Priority Boss > BossDefeated > zone. Zone = music zone Ellen is in (Main outside zones). Boss while the Grenadier is aware of Ellen (40 m). BossDefeated for N s after he dies (Grenadier Behaviour > Boss Defeated Music Time, default 10).",
                "");
            AppendRow(csv, "Global parameter", "CombatIntensity", "continuous, global", "", "0-10",
                "", "", "Combat tracker",
                "Number of enemies aware of Ellen (Chompers from spotting her until they lose her; Grenadier within 40 m). Whole steps, no smoothing: use seek speed.",
                "");

            // VCAs driven by the volume sliders (Pause > Options > Audio).
            foreach (var vca in new[] { "Master", "Music", "SFX" })
            {
                AppendRow(csv, "Volume", "vca:/" + vca, "VCA", "Master", "",
                    "MenuCanvases (prefab)", "AudioCanvas/" + vca + "VolumeSlider", "Mixer Slider Link · Fmod VCA",
                    "Options > Audio > " + vca + " slider (linear 0-1, set when the slider moves)." +
                    (vca == "Master" ? " The master bus can't go on a VCA: assign every top-level group to Master too." : ""),
                    "Unity mixer parameter " + vca + "Volume");
            }

            // FMOD events not referenced anywhere in Unity.
            var used = new HashSet<FMOD.GUID>(refs.Select(r => r.Key));
            foreach (var ev in EventManager.Events.Where(e => !used.Contains(e.Guid)).OrderBy(e => e.Path))
            {
                AppendRow(csv, "Not hooked up", ev.Path, EventType(ev), string.Join(", ", ev.Banks.Select(b => b.Name)),
                    Parameters(ev), "", "", "", "In the FMOD project but not used by any prefab or scene", "");
            }

            // UTF-8 with BOM so Excel shows the special characters correctly.
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            Debug.Log("[Audio Spec] Exported " + refs.Count + " hook(s) to " + path);
            EditorUtility.RevealInFinder(path);
        }

        // Every FMOD reference in prefabs and build scenes (scenes not open are opened additively and closed again).
        static List<KeyValuePair<FMOD.GUID, Usage>> CollectAll()
        {
            var all = new List<KeyValuePair<FMOD.GUID, Usage>>();
            s_PrefabRefs.Clear(); // fresh data for the export

            try
            {
                var prefabs = AssetPaths("t:Prefab").ToList();
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Export Audio Spec", prefabs[i], 0.8f * i / prefabs.Count))
                        return null;
                    all.AddRange(PrefabRefs(prefabs[i]));
                }

                foreach (var buildScene in EditorBuildSettings.scenes)
                {
                    // The audio test scene only repeats the prefabs.
                    if (!buildScene.enabled || buildScene.path.Contains("AudioTest"))
                        continue;

                    EditorUtility.DisplayProgressBar("Export Audio Spec", buildScene.path, 0.9f);
                    var scene = SceneManager.GetSceneByPath(buildScene.path);
                    bool opened = false;
                    if (!scene.isLoaded)
                    {
                        scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
                        opened = true;
                    }

                    all.AddRange(SceneRefs(scene));

                    if (opened)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return all;
        }

        // --- Columns ---

        static string EventType(EditorEventRef ev)
        {
            if (ev == null)
                return "";
            if (ev.Path.StartsWith("snapshot:"))
                return "Snapshot";
            return ev.Is3D ? "3D" : "2D";
        }

        static string Parameters(EditorEventRef ev)
        {
            if (ev == null || ev.Parameters == null)
                return "";

            return string.Join("; ", ev.Parameters.Select(p =>
            {
                string range = p.Type == ParameterType.Labeled
                    ? string.Join(", ", p.Labels)
                    : p.Min + "-" + p.Max;
                string type = p.Type == ParameterType.Labeled ? "labeled" : p.Type.ToString().ToLower();
                return p.Name + " (" + (p.IsGlobal ? "global, " : "") + type + ": " + range + ")";
            }));
        }

        static readonly string[] k_Categories =
        {
            "Ellen", "Chomper", "Grenadier", "Grenade", "Box", "Switch", "Pressure pad", "Health crate", "Door",
            "Weapon pedestal", "Teleporter", "Acid / water", "Ambience", "Music", "Pause", "UI", "Volume",
        };

        static int CategoryOrder(string category)
        {
            int i = System.Array.IndexOf(k_Categories, category);
            return i < 0 ? k_Categories.Length : i;
        }

        static readonly Dictionary<string, string> k_CategoryNames = new Dictionary<string, string>
        {
            { "Ellen", "Ellen" }, { "Chomper", "Chomper" }, { "Grenadier", "Grenadier" }, { "EnergyGrenade", "Grenade" },
            { "DestructibleBox", "Box" }, { "Switch", "Switch" }, { "PressurePad", "Pressure pad" },
            { "HealthCrate", "Health crate" }, { "DoorHuge", "Door" }, { "WeaponPedestal", "Weapon pedestal" },
            { "GateWayHugeTeleporter", "Teleporter" }, { "DeathVolume", "Acid / water" }, { "Level01Music", "Music" },
            { "Ambience (FMOD)", "Ambience" },
        };

        static string Category(Usage u)
        {
            if (u.field == "fmodPauseSnapshot" || u.field == "fmodPauseMusic")
                return "Pause";
            if (u.field == "hoverEvent" || u.field == "selectEvent" || u.field == "backEvent")
                return "UI";

            // The asset name for prefabs; for scene objects, the first recognisable object in the path.
            var names = new List<string> { Path.GetFileNameWithoutExtension(u.assetPath) };
            if (u.isScene)
                names.InsertRange(0, u.objectPath.Split('/'));
            foreach (var name in names)
            {
                string category;
                if (k_CategoryNames.TryGetValue(BaseName(name), out category))
                    return category;
            }
            return Path.GetFileNameWithoutExtension(u.assetPath);
        }

        // "DoorHuge1" / "ActivateAudio (1)" -> "DoorHuge" / "ActivateAudio"
        static string BaseName(string name)
        {
            return Regex.Replace(name, @"(\s*\(\d+\)|\d+)$", "");
        }

        // What happens in the game when this hook plays. Keyed by "Asset/Object#field", "Object#field",
        // "Asset/Object" or "Object" (object names without trailing numbers).
        static readonly Dictionary<string, string> k_PlaysWhen = new Dictionary<string, string>
        {
            // Ellen
            { "FootstepSource", "Each footfall. Surface = ground material (Earth / Grass / Puddle = mud / Stone); Speed = forward speed 0-8 (walk < ~4 < run)." },
            { "LandingSource", "Landing from a jump or fall. Surface + Speed as footsteps." },
            { "EmoteJump", "Jump effort (vocal)." },
            { "EmoteLanding", "Landing grunt (vocal)." },
            { "EmoteAttack", "Each staff combo swing (vocal). Tip: one emote at a time via Max Instances on a VO group." },
            { "HurtSource", "Ellen takes damage (vocal)." },
            { "EmoteDeath", "Ellen dies (vocal)." },
            { "SwingSound", "Staff swing whoosh." },
            { "HitSound", "Staff connects. Surface = Enemy (any enemy) or Rubble (destructible box)." },
            { "Ellen_Body#fmodRespawnEvent", "Respawn after a death: Ellen rematerialises at the checkpoint over ~1 s as the screen fades in (0.5 s). FMOD-only." },
            // Chomper
            { "FrontFootstepSource", "Chomper front-foot steps." },
            { "BackFootstepSource", "Chomper back-foot steps." },
            { "AttackSource", "Bite lunge; stopped (with fade) when the attack ends, so use an AHDSR release." },
            { "HitSource", "Damaged but alive. Never happens in Level1 (Chompers have 1 HP)." },
            { "BiteSource", "Bite connects with Ellen (once per bite; played by the Chomper's Melee Weapon > Hit Audio on AttackRoot). FMOD-only." },
            { "DeathSource", "Chomper killed (every staff hit kills)." },
            { "SpottedSource", "Chomper notices Ellen." },
            { "GruntSource", "Idle vocal every 2-5 s while idle (26 Chompers: consider max distance / Max Instances)." },
            // Grenadier
            { "FootstepPlayer", "Grenadier steps." },
            { "PunchPlayer", "Melee punch." },
            { "ThrowPlayer", "Grenade thrown (~2.4 s into the throw)." },
            { "GrenadeChargePlayer", "Throw wind-up: the grenade forms over 2 s; stopped at the end of the throw animation. FMOD-only." },
            { "ShieldUpPlayer", "Zone attack begins: build to the burst at ~3.3 s, when the shield appears (animation ~5.7 s); stopped at the end." },
            { "ShieldLoopPlayer", "While the damaging shield is up (3 s, can be extended); stopped when it drops. Loop + release. FMOD-only." },
            { "ShieldDownPlayer", "Shield drops: safe to attack. FMOD-only." },
            { "DamagePlayer", "Grenadier hit 1 and 2 (3 HP; only from behind, not while shielded)." },
            { "DeathPlayer", "Grenadier killed (hit 3): impact + vocal + explosion/collapse together." },
            // Grenade
            { "BouncePlayer", "Each grenade bounce. ImpactSpeed = speed into the surface (first bounce high, later low)." },
            { "ExplosionPlayer", "Grenade explodes (~4 s after launch)." },
            // Props / interactables
            { "DestructibleBox/Audio", "Box breaks." },
            { "AmbientAudio", "Switch hum from level start; stops when the switch is activated." },
            { "AudioBeforeSwitch", "Switch hum from level start; stops when the switch is activated." },
            { "Switch", "Switch activated." },
            { "PressurePad", "Pressure pad hum from level start; never stops." },
            { "ActivateAudio", "Pressure pad stepped on (once); opens a door." },
            { "PressurePad/Audio", "Pressure pad stepped on (once); opens a door." },
            { "HealthCrate", "Health crate hum until it's opened." },
            { "OpenAudio", "Health crate opens and refills Ellen's health." },
            { "DoorHuge", "Door starts moving (doors take 3 / 5 / 8 s; the original sound is 10.3 s)." },
            { "DoorHuge/Audio", "Door starts moving (doors take 3 / 5 / 8 s; the original sound is 10.3 s)." },
            { "WeaponPedastal", "Ellen takes the staff from the pedestal." },
            { "Staff", "Pedestal idle hum until the staff is taken. FMOD-only." },
            { "GateWayHugeTeleporter", "Teleporter loop, always on; heard from far away (3D, 7-500 m)." },
            { "DeathVolume/Audio", "Something falls into the water/acid (plays where it entered). Also fires once, far away, at level start." },
            // Music, pause, UI, volume
            { "Level01Music#fmodMusicEvent", "Level music for the whole level, driven by the global MusicState and CombatIntensity." },
            { "MenuCanvases#fmodPauseSnapshot", "Active while the game is paused (Esc)." },
            { "MenuCanvases#fmodPauseMusic", "Plays while paused; fades out on resume." },
            { "MenuCanvases#hoverEvent", "Mouse over any menu button." },
            { "MenuCanvases#selectEvent", "Options / Audio / Controls / Restart buttons." },
            { "MenuCanvases#backEvent", "Close (X) and Back buttons (the pause menu's Back quits)." },
        };

        static string Describe(Usage u)
        {
            string asset = Path.GetFileNameWithoutExtension(u.assetPath);
            string obj = BaseName(u.objectPath.Split('/').Last());
            string field = u.field.Split('[')[0];
            string parent = u.objectPath.Contains("/") ? BaseName(u.objectPath.Split('/').Reverse().Skip(1).First()) : "";

            foreach (var key in new[] { asset + "/" + obj + "#" + field, obj + "#" + field, asset + "/" + obj, parent + "/" + obj, obj })
            {
                string text;
                if (k_PlaysWhen.TryGetValue(key, out text))
                    return field == "fmodAdditionalEvents" ? text + " (additional event)" : text;
            }
            return u.component.Contains("Studio Event Emitter") ? "Student-placed emitter." : "";
        }

        // --- Original Unity sound (recorded while scanning) ---

        static string OriginalSound(Component component, string field)
        {
            var player = component as RandomAudioPlayer;
            if (player != null)
            {
                var clips = new List<AudioClip>();
                if (player.defaultBank != null && player.defaultBank.clips != null)
                    clips.AddRange(player.defaultBank.clips);
                if (player.overrides != null)
                {
                    foreach (var o in player.overrides)
                        foreach (var bank in o.banks ?? new RandomAudioPlayer.SoundBank[0])
                            if (bank.clips != null)
                                clips.AddRange(bank.clips);
                }
                return SummariseClips(clips);
            }

            if (component is FmodAudioSourceMirror)
            {
                var source = component.GetComponent<AudioSource>();
                return source != null && source.clip != null ? source.clip.name : "";
            }

            if (component is FmodEventWhileEnabled || component is EllenSpawn)
                return "none (FMOD-only)";
            if (component is SoundTrack)
                return "Gameplay music + 3 Atmos_Overground beds (one at a time)";
            if (component is StartUI)
                return "none (the original pause doesn't change audio)";
            if (field == "hoverEvent") return "Menu_Buttons_Mouse_Over_01";
            if (field == "selectEvent") return "Menu_Buttons_Confirm_01";
            if (field == "backEvent") return "Menu_Buttons_Back_01";
            return "";
        }

        // "Ellen_Footstep_Earth_Walk_01..." -> "Ellen_Footstep_Earth_Walk, Ellen_Footstep_Grass_Run, ... (x40 clips)"
        static string SummariseClips(List<AudioClip> clips)
        {
            var distinct = clips.Where(c => c != null).Distinct().ToList();
            if (distinct.Count == 0)
                return "none (FMOD-only)";

            var groups = distinct.Select(c => Regex.Replace(c.name, @"_?\d+$", "")).Distinct().ToList();
            string names = string.Join(", ", groups.Take(4)) + (groups.Count > 4 ? ", ..." : "");
            return distinct.Count == 1 ? distinct[0].name : names + " (x" + distinct.Count + " clips)";
        }

        // --- CSV ---

        static void AppendRow(StringBuilder csv, params string[] cells)
        {
            csv.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        static string Escape(string cell)
        {
            cell = cell ?? "";
            if (cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return cell;
            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }
    }
}
