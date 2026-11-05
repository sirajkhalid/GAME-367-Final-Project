using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Drives the GLOBAL labeled FMOD parameter "MusicState". Highest priority first:
    ///   Boss                    - while the boss is aware of Ellen (reported by CombatTracker)
    ///   BossDefeated            - for a few seconds after the boss dies (time set on the boss, e.g. the Grenadier prefab)
    ///   Main, Zone01, Zone02... - the music zone Ellen is in (reported by SoundTrack)
    /// When the higher states end, it returns to the current zone.
    /// </summary>
    public static class MusicState
    {
        public const string Parameter = "MusicState";
        public const string Main = "Main";
        public const string Boss = "Boss";
        public const string BossDefeated = "BossDefeated";

        static string s_Zone;
        static bool s_Boss;
        static float s_DefeatedUntil;
        static string s_Sent;

        /// <summary>The music zone label, ignoring the boss.</summary>
        public static string Zone { get { return s_Zone; } }

        /// <summary>The label currently sent to FMOD.</summary>
        public static string Current
        {
            get
            {
                if (s_Boss)
                    return Boss;
                if (Time.unscaledTime < s_DefeatedUntil)
                    return BossDefeated;
                return s_Zone;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Zone = Main; // no music zones in the scene (e.g. the audio test scene)
            s_Boss = false;
            s_DefeatedUntil = 0f;
            s_Sent = null;
        }

        /// <summary>Plays "BossDefeated" for the given time, then returns to the current zone.</summary>
        public static void SetBossDefeated(float seconds)
        {
            s_DefeatedUntil = Time.unscaledTime + Mathf.Max(0f, seconds);
            Apply();
        }

        /// <summary>Called every frame (by AudioBackendRunner) so timed states like BossDefeated can end.</summary>
        public static void Update()
        {
            Apply();
        }

        public static void SetZone(string zone)
        {
            s_Zone = zone;
            Apply();
        }

        public static void SetBoss(bool bossAware)
        {
            s_Boss = bossAware;
            Apply();
        }

        /// <summary>Sends the current label again (e.g. once FMOD's banks have loaded).</summary>
        public static void Resend()
        {
            s_Sent = null;
            Apply();
        }

        static void Apply()
        {
            string label = Current;
            if (label == null || label == s_Sent)
                return;

            s_Sent = label;
            FmodAudio.SetGlobalParameterLabel(Parameter, label);
        }
    }
}
