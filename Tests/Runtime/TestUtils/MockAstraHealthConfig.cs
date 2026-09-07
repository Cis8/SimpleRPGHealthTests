using ElectricDrill.AstraRpgFramework.Contexts;
using ElectricDrill.AstraRpgFramework.GameActions;
using ElectricDrill.AstraRpgFramework.GameActions.Actions.Component;
using ElectricDrill.AstraRpgFramework.GameActions.Actions.WithIHasEntity;
using ElectricDrill.AstraRpgFramework.Ownership;
using ElectricDrill.AstraRpgFramework.Scaling.ScalingComponents;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraHealth.Config;
using ElectricDrill.AstraHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraHealth.Events;
using ElectricDrill.AstraHealth.Experience;
using ElectricDrill.AstraHealth.Heal;
using UnityEngine;

namespace ElectricDrill.AstraRpgHealthTests.TestUtils
{
    /// <summary>
    /// Mock implementation of IAstraHealthConfig for testing purposes.
    /// Provides sensible defaults and allows overriding specific properties.
    /// </summary>
    internal class MockAstraHealthConfig : IAstraHealthConfig
    {
        public AttributesScalingComponentSO HealthAttributesScaling { get; set; }
        public StatSO GenericPercentageHealAmountModifierStat { get; set; }
        public StatSO GenericFlatHealAmountModifierStat { get; set; }
        public HealthRoundingSettings RoundingSettings { get; }
        public StatSO GenericPercentageDamageModificationStat { get; set; }
        public StatSO GenericFlatDamageModificationStat { get; set; }
        public DamageCalculationStrategySO DefaultDamageCalculationCalculationStrategy { get; set; }
        public bool RecordDamageStepTrace { get; set; } = true;
        public HealSourceSO HealthRegenerationSource { get; set; }
        public StatSO PassiveHealthRegenerationStat { get; set; }
        public float PassiveHealthRegenerationInterval { get; set; }
        public StatSO ManualHealthRegenerationStat { get; set; }
        public bool SuppressPassiveRegenerationEvents { get; set; }
        public bool SuppressManualRegenerationEvents { get; set; }
        public EntityAttribution LifestealAttribution { get; set; }
        public EntityAttribution KillCreditAttribution { get; set; }
        public EntityAttribution DamageStatsAttribution { get; set; }
        public LifestealStatConfig GenericLifesteal { get; set; }
        public LifestealStatSource LifestealStatSource { get; set; }
        public bool SuppressLifestealEvents { get; set; }
        public bool UnifyLifestealHeals { get; set; }
        public GameAction<IHasEntity> DefaultOnDeathGameAction { get; set; }
        public GameAction<IHasEntity> DefaultOnResurrectionGameAction { get; set; }
        public HealSourceSO DefaultResurrectionSource { get; set; }
        public ExpCollectionStrategySO DefaultExpCollectionStrategy { get; set; }
        public PreDamageGameEvent GlobalPreDamageInfoEvent { get; set; }
        public DamageResolutionGameEvent GlobalDamageResolutionEvent { get; set; }
        public EntityDiedGameEvent GlobalEntityDiedEvent { get; set; }
        public EntityMaxHealthChangedGameEvent GlobalMaxHealthChangedEvent { get; set; }
        public EntityHealthChangedGameEvent GlobalHealthChangedEvent { get; set; }
        public HealthRatioChangedGameEvent GlobalHealthRatioChangedEvent { get; set; }
        public PreHealGameEvent GlobalPreHealEvent { get; set; }
        public EntityHealedGameEvent GlobalEntityHealedEvent { get; set; }
        public EntityResurrectedGameEvent GlobalEntityResurrectedEvent { get; set; }

        public MockAstraHealthConfig() : this(default)
        {
        }

        public MockAstraHealthConfig(HealthRoundingSettings roundingSettings)
        {
            RoundingSettings = roundingSettings;
            // Initialize with sensible defaults
            PassiveHealthRegenerationInterval = 1f;
            SuppressPassiveRegenerationEvents = false;
            SuppressManualRegenerationEvents = false;
            SuppressLifestealEvents = false;
            
            // HealthAttributesScaling is null by default - only set if test needs it
            // This avoids validation errors when test entities don't have EntityAttributes
            HealthAttributesScaling = null;
            
            // Create a default damage calculation strategy
            DefaultDamageCalculationCalculationStrategy = ScriptableObject.CreateInstance<DamageCalculationStrategySO>();
            
            // Create a default death strategy
            DefaultOnDeathGameAction = ScriptableObject.CreateInstance<DoNothingEntityContextGameAction>();
            
            // Create a default HealSource for resurrection
            DefaultResurrectionSource = ScriptableObject.CreateInstance<HealSourceSO>();
            
            // Initialize the three required global events
            GlobalPreDamageInfoEvent = ScriptableObject.CreateInstance<PreDamageGameEvent>();
            GlobalDamageResolutionEvent = ScriptableObject.CreateInstance<DamageResolutionGameEvent>();
            GlobalEntityDiedEvent = ScriptableObject.CreateInstance<EntityDiedGameEvent>();
        }

        /// <summary>
        /// Creates a minimal mock config with only the essentials needed for basic tests.
        /// </summary>
        public static MockAstraHealthConfig CreateMinimal()
        {
            return new MockAstraHealthConfig();
        }

        /// <summary>
        /// Creates a mock config with a custom damage calculation strategy.
        /// </summary>
        public static MockAstraHealthConfig WithDamageStrategy(DamageCalculationStrategySO strategy)
        {
            var config = new MockAstraHealthConfig
            {
                DefaultDamageCalculationCalculationStrategy = strategy
            };
            return config;
        }

        /// <summary>
        /// Creates a mock config with a custom death strategy.
        /// </summary>
        public static MockAstraHealthConfig WithDeathGameAction(GameAction<IHasEntity> strategy)
        {
            var config = new MockAstraHealthConfig
            {
                DefaultOnDeathGameAction = strategy
            };
            return config;
        }
        
        /// <summary>
        /// Creates a mock config with a custom resurrection heal source.
        /// </summary>
        public static MockAstraHealthConfig WithResurrectionSource(HealSourceSO healSource)
        {
            var config = new MockAstraHealthConfig
            {
                DefaultResurrectionSource = healSource
            };
            return config;
        }

        /// <summary>
        /// Creates a mock config with a custom health attributes scaling component.
        /// Use this only if your test entities have EntityAttributes configured.
        /// </summary>
        public static MockAstraHealthConfig WithHealthAttributesScaling(AttributesScalingComponentSO scalingComponent)
        {
            var config = new MockAstraHealthConfig
            {
                HealthAttributesScaling = scalingComponent
            };
            return config;
        }
    }
}
