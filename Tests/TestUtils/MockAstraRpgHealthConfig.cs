using ElectricDrill.AstraRpgFramework.Scaling.ScalingComponents;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraRpgHealth.Config;
using ElectricDrill.AstraRpgHealth.Damage;
using ElectricDrill.AstraRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraRpgHealth.Death;
using ElectricDrill.AstraRpgHealth.Heal;
using UnityEngine;

namespace ElectricDrill.AstraRpgHealthTests.TestUtils
{
    /// <summary>
    /// Mock implementation of IAstraRpgHealthConfig for testing purposes.
    /// Provides sensible defaults and allows overriding specific properties.
    /// </summary>
    public class MockAstraRpgHealthConfig : IAstraRpgHealthConfig
    {
        public AttributesScalingComponent HealthAttributesScaling { get; set; }
        public Stat HealAmountModifierStat { get; set; }
        public Stat GenericDamageModificationStat { get; set; }
        public DamageCalculationStrategy DefaultDamageCalculationCalculationStrategy { get; set; }
        public SerializableDictionary<DamageType, Stat> DamageTypeModifications { get; set; }
        public SerializableDictionary<DamageSource, Stat> DamageSourceModifications { get; set; }
        public HealSource PassiveHealthRegenerationSource { get; set; }
        public Stat PassiveHealthRegenerationStat { get; set; }
        public float PassiveHealthRegenerationInterval { get; set; }
        public Stat ManualHealthRegenerationStat { get; set; }
        public LifestealConfig LifestealConfig { get; set; }
        public OnDeathStrategy DefaultOnDeathStrategy { get; set; }

        public MockAstraRpgHealthConfig()
        {
            // Initialize with sensible defaults
            DamageTypeModifications = new SerializableDictionary<DamageType, Stat>();
            DamageSourceModifications = new SerializableDictionary<DamageSource, Stat>();
            PassiveHealthRegenerationInterval = 1f;
            
            // Create a default damage calculation strategy
            DefaultDamageCalculationCalculationStrategy = ScriptableObject.CreateInstance<DamageCalculationStrategy>();
            
            // Create a default death strategy
            DefaultOnDeathStrategy = ScriptableObject.CreateInstance<DestroyImmediateOnDeathStrategy>();
        }

        /// <summary>
        /// Creates a minimal mock config with only the essentials needed for basic tests.
        /// </summary>
        public static MockAstraRpgHealthConfig CreateMinimal()
        {
            return new MockAstraRpgHealthConfig();
        }

        /// <summary>
        /// Creates a mock config with a custom damage calculation strategy.
        /// </summary>
        public static MockAstraRpgHealthConfig WithDamageStrategy(DamageCalculationStrategy strategy)
        {
            var config = new MockAstraRpgHealthConfig
            {
                DefaultDamageCalculationCalculationStrategy = strategy
            };
            return config;
        }

        /// <summary>
        /// Creates a mock config with a custom death strategy.
        /// </summary>
        public static MockAstraRpgHealthConfig WithDeathStrategy(OnDeathStrategy strategy)
        {
            var config = new MockAstraRpgHealthConfig
            {
                DefaultOnDeathStrategy = strategy
            };
            return config;
        }
    }
}

