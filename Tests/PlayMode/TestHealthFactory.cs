using System;
using System.Reflection;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Attributes;
using ElectricDrill.AstraRpgFramework.Events;
using ElectricDrill.AstraRpgFramework.Experience;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraRpgHealth;
using ElectricDrill.AstraRpgHealth.Config;
using ElectricDrill.AstraRpgHealth.Damage;
using ElectricDrill.AstraRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraRpgHealth.Death;
using ElectricDrill.AstraRpgHealth.Events;
using ElectricDrill.AstraRpgHealth.Heal;
using UnityEngine;
using Attribute = ElectricDrill.AstraRpgFramework.Attributes.Attribute;

namespace ElectricDrill.AstraRpgHealthTests.Tests.PlayMode
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
            public EntityAttributes Attributes;
            public EntityHealth Health;
            public AstraRpgHealthConfig Config;
            public DamageType DefaultDamageType;
            public DamageSource DefaultDamageSource;
            public HealthEventsBundle Events; // events actually used (shared or per-entity)
        }

        public static HealthEntityBundle CreateEntity(string name = "Entity",
            AstraRpgHealthConfig sharedConfig = null,
            long maxHp = 100,
            bool allowNegative = false,
            long barrierAmount = 0,
            Action<AstraRpgHealthConfig> configMutator = null,
            Action<EntityHealth> healthMutator = null,
            bool initializeStats = false,
            bool initializeAttributes = false,
            HealthEventsBundle? sharedEvents = null)
        {
            // Always create a brand new config if one is not explicitly shared.
            // This avoids reusing a previously left-over provider instance with stale damage / lifesteal mappings.
            var config = sharedConfig;
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<AstraRpgHealthConfig>();
                // Ensure a default OnDeathStrategy on the config (no reflection)
                var cfgDeath = ScriptableObject.CreateInstance<TestOnDeathStrategy>();
                config.DefaultOnDeathStrategy = cfgDeath;
                configMutator?.Invoke(config);
                SetConfigProviderInstance(config);
            }
            else
            {
                if (config.DefaultOnDeathStrategy == null)
                {
                    var cfgDeath = ScriptableObject.CreateInstance<TestOnDeathStrategy>();
                    config.DefaultOnDeathStrategy = cfgDeath;
                }
                SetConfigProviderInstance(config);
            }

            // Ensure default damage calculation strategy exists (prevents "No Damage Calculation Strategy" errors)
            void EnsureDefaultStrategy()
            {
                if (config.DefaultDamageCalculationCalculationStrategy != null) return;

                var strat = ScriptableObject.CreateInstance<DamageCalculationStrategy>();
                strat.name = "Auto_DefaultDamageCalculationStrategy";
                strat.steps.Add(new ApplyCriticalMultiplierStep());
                strat.steps.Add(new ApplyBarrierStep());
                strat.steps.Add(new ApplyDefenseStep());
                strat.steps.Add(new ApplyDmgModifiersStep());

                config.DefaultDamageCalculationCalculationStrategy = strat;
            }
            EnsureDefaultStrategy();

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
            
            // Attributes
            var attributes = go.AddComponent<EntityAttributes>();
            if (!initializeAttributes)
                attributes.enabled = false;
            
            var attrChangedEvt = ScriptableObject.CreateInstance<AttributeChangedGameEvent>();
            attributes.OnAttributeChanged = attrChangedEvt;
            
            // Ensure a fixed AttributeSet exists (internal field accessible)
            if (attributes._fixedBaseAttributeSet == null)
            {
                attributes._fixedBaseAttributeSet = ScriptableObject.CreateInstance<AttributeSet>();
                // Initialize internal fixed base attributes structures
                attributes.InitializeFixedBaseAttributes();
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

            // OnDeathStrategy override (use public property, not reflection)
            var onDeathStrategy = ScriptableObject.CreateInstance<TestOnDeathStrategy>();
            health.OverrideOnDeathStrategy = onDeathStrategy;

            // Internal (accessible directly)
            health._baseMaxHp = baseMax;
            health._totalMaxHp = totalMax;
            health._hp = hp;
            health._barrier = barrier;
            health._deathThreshold = deathThreshold;
            health.HealthCanBeNegative = allowNegative;

            healthMutator?.Invoke(health);

            go.SetActive(true);

            // Dmg type & source
            var dmgType = ScriptableObject.CreateInstance<DamageType>();
            dmgType.name = $"{name}_DmgType";
            var dmgSource = ScriptableObject.CreateInstance<DamageSource>();
            dmgSource.name = $"{name}_DmgSource";

            return new HealthEntityBundle
            {
                Go = go,
                Core = core,
                Stats = stats,
                Attributes = attributes, // added: expose created EntityAttributes
                Health = health,
                Config = config,
                DefaultDamageType = dmgType,
                DefaultDamageSource = dmgSource,
                Events = evtBundle
            };
        }

        private static void SetConfigProviderInstance(AstraRpgHealthConfig config)
        {
            AstraRpgHealthConfigProvider.Instance = config;
        }

        public static PreDamageInfo BuildPre(long amount, HealthEntityBundle dealer, HealthEntityBundle target,
            DamageType type = null, DamageSource source = null, bool crit = false, double critMult = 1d, bool ignore = false)
        {
            var dmgType = type ?? dealer.DefaultDamageType;
            var dmgSource = source ?? dealer.DefaultDamageSource;

            var pre = PreDamageInfo.Builder
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
        /// Inject a flat (raw long) stat value into an EntityStats (non-percentage defensive or similar).
        /// </summary>
        public static void InjectFlatStat(EntityStats stats, Stat stat, long value)
        {
            if (stats == null || stat == null) return;

            if (stats._fixedBaseStatsStatSet == null)
            {
                stats._fixedBaseStatsStatSet = ScriptableObject.CreateInstance<StatSet>();
                stats.InitializeFixedBaseStats();
            }

            if (!stats.StatSet.Contains(stat))
            {
                if (!stats._fixedBaseStatsStatSet._stats.Contains(stat))
                    stats._fixedBaseStatsStatSet._stats.Add(stat);
                stats.InitializeFixedBaseStats();
            }

            stats.SetFixed(stat, value);
            stats.StatsCache.Invalidate(stat);
        }
        
        /// <summary>
        /// Inject a flat (raw long) attribute value into an EntityAttributes.
        /// Ensures the attribute exists in the fixed base attribute set and sets its fixed base value.
        /// </summary>
        public static void InjectFlatAttribute(EntityAttributes attributes, Attribute attribute, long value)
        {
            if (attributes == null || attribute == null) return;
            
            if (attributes._fixedBaseAttributeSet == null)
            {
                attributes._fixedBaseAttributeSet = ScriptableObject.CreateInstance<AttributeSet>();
                attributes.InitializeFixedBaseAttributes();
            }

            if (!attributes.AttributeSet.Contains(attribute))
            {
                if (!attributes._fixedBaseAttributeSet._attributes.Contains(attribute))
                    attributes._fixedBaseAttributeSet._attributes.Add(attribute);
                attributes.InitializeFixedBaseAttributes();
            }

            // Set base value (EntityAttributes API analogous to EntityStats)
            attributes.SetFixed(attribute, value);

            // Invalidate cache if present
            attributes.AttributesCache.Invalidate(attribute);
        }

        /// <summary>
        /// Creates a LifestealConfig with a single mapping (_damageType -> lifestealStatConfig) and assigns it to the provided config.
        /// Returns the created LifestealConfig so tests can Destroy it.
        /// </summary>
        public static LifestealConfig AssignLifestealMapping(AstraRpgHealthConfig config, DamageType damageType, Stat lifestealStat, HealSource lifestealSource)
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
            lifestealConfig.SetMapping(damageType, lifestealStat, lifestealSource);

            return lifestealConfig;
        }

        public static void SetPrivateField(object obj, string fieldName, object value)
        {
            if (obj == null) return;
            obj.GetType()
               .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
               ?.SetValue(obj, value);
        }

        /// <summary>
        /// Creates a DamageCalculationStrategy with steps ordered:
        /// Critical -> Barrier -> Defense -> Weakness/Resistances (ApplyDmgModifiers).
        /// No reflection: we rely on the concrete step classes directly.
        /// </summary>
        public static DamageCalculationStrategy CreateCritBarrierDefenseWeaknessStrategy()
        {
            var strat = ScriptableObject.CreateInstance<DamageCalculationStrategy>();
            strat.steps.Add(new ApplyCriticalMultiplierStep());
            strat.steps.Add(new ApplyBarrierStep());
            strat.steps.Add(new ApplyDefenseStep());
            strat.steps.Add(new ApplyDmgModifiersStep());
            return strat;
        }

        /// <summary>
        /// Configures lifesteal so that its basis is the damage amount recorded AFTER the Critical step (Post).
        /// Uses Step mode (no reflection).
        /// Overwrites any existing mapping for the _damageType.
        /// </summary>
        public static LifestealStatConfig ConfigureLifestealBasisAfterCritical(
            LifestealConfig cfg,
            DamageType damageType,
            Stat lifestealStat,
            HealSource lifestealSource)
        {
            if (!cfg) throw new ArgumentNullException(nameof(cfg));
            if (!damageType) throw new ArgumentNullException(nameof(damageType));
            if (!lifestealStat) throw new ArgumentNullException(nameof(lifestealStat));
            if (!lifestealSource) throw new ArgumentNullException(nameof(lifestealSource));

            var selector = new LifestealAmountSelector(
                LifestealBasisMode.Step,
                typeof(ApplyCriticalMultiplierStep).AssemblyQualifiedName,
                StepValuePoint.Post);

            // Overwrite mapping with desired selector.
            return cfg.SetMapping(damageType, lifestealStat, lifestealSource, selector, overwrite: true);
        }
    }
}
