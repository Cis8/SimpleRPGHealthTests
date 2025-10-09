using System;
using System.Reflection;
using UnityEngine;
using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgFramework.Events;
using ElectricDrill.SoapRpgFramework.Stats;
using ElectricDrill.SoapRpgFramework.Utils;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRPGHealth;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Events;
using ElectricDrill.SoapRpgHealth.Heal;

namespace Tests.PlayMode.Utils
{
    /// <summary>
    /// Utility factory methods to spawn fully configured EntityHealth objects for play mode tests.
    /// Avoid duplicating reflection setup logic across test classes.
    /// </summary>
    public static class TestHealthFactory
    {
        public struct HealthEventsBundle
        {
            public PreDmgGameEvent PreDmg;
            public DamageResolutionGameEvent DamageResolution;
            public EntityMaxHealthChangedGameEvent MaxHpChanged;
            public EntityGainedHealthGameEvent Gained;
            public EntityLostHealthGameEvent Lost;
            public EntityDiedGameEvent Died;
            public PreHealGameEvent PreHeal;
            public EntityHealedGameEvent Healed;
        }

        public static HealthEventsBundle CreateSharedEvents()
        {
            return new HealthEventsBundle
            {
                PreDmg = ScriptableObject.CreateInstance<PreDmgGameEvent>(),
                DamageResolution = ScriptableObject.CreateInstance<DamageResolutionGameEvent>(),
                MaxHpChanged = ScriptableObject.CreateInstance<EntityMaxHealthChangedGameEvent>(),
                Gained = ScriptableObject.CreateInstance<EntityGainedHealthGameEvent>(),
                Lost = ScriptableObject.CreateInstance<EntityLostHealthGameEvent>(),
                Died = ScriptableObject.CreateInstance<EntityDiedGameEvent>(),
                PreHeal = ScriptableObject.CreateInstance<PreHealGameEvent>(),
                Healed = ScriptableObject.CreateInstance<EntityHealedGameEvent>()
            };
        }

        public struct HealthEntityBundle
        {
            public GameObject Go;
            public EntityCore Core;
            public EntityStats Stats;
            public EntityHealth Health;
            public SoapRpgHealthConfig Config;
            public DmgType DefaultDmgType;
            public DmgSource DefaultDmgSource;
            public HealthEventsBundle Events; // events actually used (shared or per-entity)
        }

        public static HealthEntityBundle CreateEntity(string name = "Entity",
            SoapRpgHealthConfig sharedConfig = null,
            long maxHp = 100,
            bool allowNegative = false,
            long barrierAmount = 0,
            Action<SoapRpgHealthConfig> configMutator = null,
            Action<EntityHealth> healthMutator = null,
            bool initializeStats = false,
            HealthEventsBundle? sharedEvents = null)
        {
            // Always create a brand new config if one is not explicitly shared.
            // This avoids reusing a previously left-over provider instance with stale damage / lifesteal mappings.
            var config = sharedConfig;
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<SoapRpgHealthConfig>();
                configMutator?.Invoke(config);
                SetConfigProviderInstance(config);
            }
            else
            {
                // Ensure provider points to the shared config (in case another test created a different one before)
                SetConfigProviderInstance(config);
            }

            // GameObject inactive so Awake sees injected fields
            var go = new GameObject(name);
            go.SetActive(false);

            // Core (no reflection needed for Level property: internal setter)
            var core = go.AddComponent<EntityCore>();
            core.Level = new EntityLevel();
            // Still need reflection for private _onLevelUp inside EntityLevel (no public/internal API exposed)
            typeof(EntityLevel)
                .GetField("_onLevelUp", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(core.Level, ScriptableObject.CreateInstance<EntityLeveledUpGameEvent>());
            core.SpawnedEntityEvent = ScriptableObject.CreateInstance<EntityCoreGameEvent>();

            // Stats
            var stats = go.AddComponent<EntityStats>();
            stats.UseClassBaseStats = false;
            if (!initializeStats)
                stats.enabled = false;
            
            var statChangedEvt = ScriptableObject.CreateInstance<StatChangedGameEvent>();
            stats.OnStatChanged = statChangedEvt;

            // Ensure a fixed StatSet exists (internal field accessible)
            if (stats._fixedBaseStatsStatSet == null)
            {
                stats._fixedBaseStatsStatSet = ScriptableObject.CreateInstance<StatSet>();
                // Initialize internal fixed base stats structures
                stats.InitializeFixedBaseStats();
            }

            var health = go.AddComponent<EntityHealth>();

            var events = sharedEvents ?? CreateSharedEvents();
            var evtBundle = events;

            // Refs
            var baseMax = new LongRef { Value = maxHp };
            var totalMax = new LongRef { Value = maxHp };
            var hp = new LongRef { Value = maxHp };
            var barrier = new LongRef { Value = barrierAmount };
            var deathThreshold = ScriptableObject.CreateInstance<LongVar>();
            deathThreshold.Value = allowNegative ? -9999 : 0;

            // Private serialized fields of EntityHealth still require reflection (cannot avoid)
            void SetPrivate(string fieldName, object value) =>
                typeof(EntityHealth).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(health, value);

            SetPrivate("_preDmgInfoEvent", evtBundle.PreDmg);
            SetPrivate("_damageResolutionEvent", evtBundle.DamageResolution);
            SetPrivate("_maxHealthChangedEvent", evtBundle.MaxHpChanged);
            SetPrivate("_gainedHealthEvent", evtBundle.Gained);
            SetPrivate("_lostHealthEvent", evtBundle.Lost);
            SetPrivate("_entityDiedEvent", evtBundle.Died);
            SetPrivate("_preHealEvent", evtBundle.PreHeal);
            SetPrivate("_entityHealedEvent", evtBundle.Healed);

            // OnDeathStrategy (private)
            var onDeathStrategy = ScriptableObject.CreateInstance<TestOnDeathStrategy>();
            SetPrivate("_onDeathStrategy", onDeathStrategy);

            // Internal (accessible directly)
            health.baseMaxHp = baseMax;
            health.totalMaxHp = totalMax;
            health.hp = hp;
            health.barrier = barrier;
            health.deathThreshold = deathThreshold;
            health.HealthCanBeNegative = allowNegative;

            healthMutator?.Invoke(health);

            go.SetActive(true);

            // Dmg type & source
            var dmgType = ScriptableObject.CreateInstance<DmgType>();
            dmgType.name = $"{name}_DmgType";
            var dmgSource = ScriptableObject.CreateInstance<DmgSource>();
            dmgSource.name = $"{name}_DmgSource";

            return new HealthEntityBundle
            {
                Go = go,
                Core = core,
                Stats = stats,
                Health = health,
                Config = config,
                DefaultDmgType = dmgType,
                DefaultDmgSource = dmgSource,
                Events = evtBundle
            };
        }

        private static void SetConfigProviderInstance(SoapRpgHealthConfig config)
        {
            // Force override provider instance to guarantee test isolation
            var providerType = typeof(SoapRpgHealthConfigProvider);
            var field = providerType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(null, null); // clear previous
                field.SetValue(null, config);
            }
            else
            {
                var prop = providerType.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(null, null);
                    prop.SetValue(null, config);
                }
            }
        }

        public static PreDmgInfo BuildPre(long amount, HealthEntityBundle dealer, HealthEntityBundle target,
            DmgType type = null, DmgSource source = null, bool crit = false, double critMult = 1d, bool ignore = false)
        {
            var dmgType = type ?? dealer.DefaultDmgType;
            var dmgSource = source ?? dealer.DefaultDmgSource;

            var pre = PreDmgInfo.Builder
                .WithAmount(amount)
                .WithType(dmgType)
                .WithSource(dmgSource)
                .WithTarget(target.Core)
                .WithDealer(dealer.Core)
                .WithIsCritical(crit)
                .WithCriticalMultiplier(critMult)
                .Build();
            pre.Ignore = ignore;
            return pre;
        }

        // Simple OnDeathStrategy used in tests
        private class TestOnDeathStrategy : OnDeathStrategy
        {
            public override void Die(EntityHealth health) { /* no-op for tests */ }
        }

        /// <summary>
        /// Inject a Percentage stat value into an EntityStats without using reflection.
        /// Adds the stat to the fixed StatSet if missing and sets its fixed base value.
        /// </summary>
        public static void InjectPercentageStat(EntityStats stats, Stat stat, Percentage value)
        {
            if (stats == null || stat == null) return;

            // Ensure fixed stat set exists
            if (stats._fixedBaseStatsStatSet == null)
            {
                stats._fixedBaseStatsStatSet = ScriptableObject.CreateInstance<StatSet>();
                stats.InitializeFixedBaseStats();
            }

            // Add stat to StatSet if not already present
            if (!stats.StatSet.Contains(stat))
            {
                if (!stats._fixedBaseStatsStatSet._stats.Contains(stat))
                    stats._fixedBaseStatsStatSet._stats.Add(stat);

                // Re-initialize fixed base stats so internal dictionary reflects new stat
                stats.InitializeFixedBaseStats();
            }

            // Set base value (Percentage stored as long internally; (long)value extracts underlying)
            stats.SetFixed(stat, (long)value);

            // Invalidate cache so Get() recomputes with new stat
            stats.StatsCache.Invalidate(stat);
        }

        /// <summary>
        /// Creates a LifestealConfig with a single mapping (dmgType -> lifestealStatConfig) and assigns it to the provided config.
        /// Returns the created LifestealConfig so tests can Destroy it.
        /// </summary>
        public static LifestealConfig AssignLifestealMapping(SoapRpgHealthConfig config, DmgType dmgType, Stat lifestealStat, HealSource lifestealSource)
        {
            // Prefer existing lifesteal config if already set, else create a fresh one.
            var lifestealConfig = config.LifestealConfig;
            if (!lifestealConfig)
            {
                lifestealConfig = ScriptableObject.CreateInstance<LifestealConfig>();
                // Assign to config (if property has setter) else try reflection only for this assignment.
                var cfgType = config.GetType();
                var prop = cfgType.GetProperty("LifestealConfig", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite)
                    prop.SetValue(config, lifestealConfig);
                else
                {
                    var field = cfgType.GetField("LifestealConfig", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    field?.SetValue(config, lifestealConfig);
                }
            }

            // Use internal API (no reflection) to declare mapping.
            lifestealConfig.SetMapping(dmgType, lifestealStat, lifestealSource);

            return lifestealConfig;
        }
    }
}
