using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamekit3D
{
    /// <summary>
    /// Counts the enemies that are aware of Ellen and sends it to FMOD:
    ///   CombatIntensity (GLOBAL, continuous) - number of enemies currently aware of Ellen (0, 1, 2...)
    ///   MusicState = Boss                    - while a boss is aware of Ellen (see MusicState)
    ///
    /// Enemies register when enabled and unregister when they die or are disabled. "Aware" is each enemy's own
    /// test (e.g. a Chomper that has spotted Ellen and hasn't lost her). No smoothing is done here: use parameter
    /// seek speed / hold in FMOD so combat music doesn't flicker.
    /// Updated every frame by AudioBackendRunner.
    /// </summary>
    public static class CombatTracker
    {
        public const string IntensityParameter = "CombatIntensity";

        struct Combatant
        {
            public Func<bool> isAware;
            public bool isBoss;
        }

        static readonly Dictionary<object, Combatant> s_Combatants = new Dictionary<object, Combatant>();
        static int s_SentCount;

        /// <summary>Enemies currently aware of Ellen.</summary>
        public static int AwareCount { get; private set; }

        /// <summary>True while a boss is aware of Ellen.</summary>
        public static bool BossAware { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Combatants.Clear();
            s_SentCount = -1;
            AwareCount = 0;
            BossAware = false;
        }

        public static void Register(object enemy, Func<bool> isAware, bool isBoss = false)
        {
            s_Combatants[enemy] = new Combatant { isAware = isAware, isBoss = isBoss };
        }

        public static void Unregister(object enemy)
        {
            s_Combatants.Remove(enemy);
        }

        /// <summary>Sends the count again on the next Update (e.g. once FMOD's banks have loaded).</summary>
        public static void Resend()
        {
            s_SentCount = -1;
        }

        public static void Update()
        {
            int count = 0;
            bool boss = false;
            foreach (var combatant in s_Combatants.Values)
            {
                if (!combatant.isAware())
                    continue;
                count++;
                boss |= combatant.isBoss;
            }

            AwareCount = count;

            if (count != s_SentCount)
            {
                s_SentCount = count;
                FmodAudio.SetGlobalParameter(IntensityParameter, count);
            }

            if (boss != BossAware)
            {
                BossAware = boss;
                MusicState.SetBoss(boss);
            }
        }
    }
}
