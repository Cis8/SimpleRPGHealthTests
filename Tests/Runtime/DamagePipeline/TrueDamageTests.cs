using System.Collections.Generic;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Contexts;
using ElectricDrill.AstraRpgFramework.GameActions;
using ElectricDrill.AstraRpgFramework.Ownership;
using ElectricDrill.AstraRpgFramework.Scaling.ScalingComponents;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraHealth;
using ElectricDrill.AstraHealth.Config;
using ElectricDrill.AstraHealth.Core;
using ElectricDrill.AstraHealth.Damage;
using ElectricDrill.AstraHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraHealth.Events;
using ElectricDrill.AstraHealth.Experience;
using ElectricDrill.AstraHealth.Heal;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricDrill.AstraRpgHealthTests.DamagePipeline
{
    /// <summary>
    /// Tests for True Damage mechanics: barriers and modifiers can be ignored based on DamageType configuration.
    /// </summary>
    public class TrueDamageTests
    {
        private class MockDamageType : DamageTypeSO
        {
            public static MockDamageType Create(
                string name = "MockType",
                bool ignoreBarrier = false,
                bool ignorePercentage = false,
                bool ignoreFlat = false)
            {
                var t = CreateInstance<MockDamageType>();
                t.name = name;
                t.IgnoresBarrier = ignoreBarrier;
                t.IgnoreGenericPercentageDamageModifiers = ignorePercentage;
                t.IgnoreGenericFlatDamageModifiers = ignoreFlat;
                return t;
            }
        }

        private class MockDamageSource : DamageSourceSO
        {
            public static MockDamageSource Create(string name = "MockSource")
            {
                var s = CreateInstance<MockDamageSource>();
                s.name = name;
                return s;
            }
        }

        private class MockConfig : IAstraHealthConfig
        {
            public MockConfig() : this(default) {
            }

            public MockConfig(HealthRoundingSettings roundingSettings) {
                RoundingSettings = roundingSettings;
            }

            public HealthRoundingSettings RoundingSettings { get; }
            public StatSO GenericPercentageDamageModificationStat { get; set; }
            public StatSO GenericFlatDamageModificationStat { get; set; }
            
            // Other required properties
            public AttributesScalingComponentSO HealthAttributesScaling { get; set; }
            public StatSO GenericFlatHealAmountModifierStat { get; set; }
            public StatSO GenericPercentageHealAmountModifierStat { get; set; }
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
        }

        /// <summary>
        /// EntityCore subclass that re-implements <see cref="IStatReader"/> with a simple dictionary,
        /// bypassing the <see cref="EntityStats"/> StatSet requirement entirely.
        /// C# interface re-implementation ensures this version is used when code calls
        /// TryGet through the IStatReader reference stored in <see cref="DamageInfo.TargetStats"/>.
        /// </summary>
        private class StubEntityCore : EntityCore, IStatReader
        {
            private readonly Dictionary<StatSO, long> _statMap = new();

            public void RegisterStat(StatSO stat, long value)
            {
                if (stat != null) _statMap[stat] = value;
            }

            bool IValueContainer<StatSO>.Contains(StatSO stat) => stat != null && _statMap.ContainsKey(stat);
            bool IStatReader.TryGet(StatSO stat, out long value) => _statMap.TryGetValue(stat, out value);
            bool IStatReader.TryGetBase(StatSO stat, out long value) => _statMap.TryGetValue(stat, out value);
        }

        private GameObject _targetGo;
        private GameObject _dealerGo;
        private StubEntityCore _targetCore;
        private EntityCore _dealerCore;
        private EntityHealth _targetHealth;

        [SetUp]
        public void Setup()
        {
            _targetGo = new GameObject("Target");
            _dealerGo = new GameObject("Dealer");

            _targetCore = _targetGo.AddComponent<StubEntityCore>();
            _dealerCore = _dealerGo.AddComponent<EntityCore>();

            // These tests build DamageInfo directly; inject a minimal config up front so
            // config-agnostic cases don't trigger the provider's Resources fallback path.
            AstraHealthConfigProvider.Instance = new MockConfig();

            _targetHealth = _targetGo.AddComponent<EntityHealth>();
            
            // Setup health component
            _targetHealth._entityCore = _targetCore;
            _targetHealth._baseMaxHp = new LongRef { UseConstant = true, ConstantValue = 100 };
            _targetHealth._totalMaxHp = new LongRef { UseConstant = true, ConstantValue = 100 };
            _targetHealth._hp = new LongRef { UseConstant = true, ConstantValue = 100 };
            _targetHealth._barrier = new LongRef { UseConstant = true, ConstantValue = 50 };
        }

        [TearDown]
        public void Cleanup()
        {
            AstraHealthConfigProvider.Reset();
            Object.DestroyImmediate(_targetGo);
            Object.DestroyImmediate(_dealerGo);
        }

        private static StatSO CreateStat(string name)
        {
            var stat = ScriptableObject.CreateInstance<StatSO>();
            stat.name = name;
            return stat;
        }

        private DamageInfo MakeDamageInfo(long raw, DamageTypeSO type, DamageSourceSO source)
        {
            var pre = PreDamageContext.Builder
                .WithAmount(raw)
                .WithType(type)
                .WithSource(source)
                .WithTarget(_targetCore)
                .WithPerformer(_dealerCore)
                .Build();
            return new DamageInfo(pre, AstraHealthConfigProvider.Instance);
        }

        #region Barrier Tests

        [Test]
        public void ApplyBarrierStep_IgnoresBarrier_WhenDamageTypeIgnoresBarrier()
        {
            const long raw = 60;
            var type = MockDamageType.Create(ignoreBarrier: true);
            var source = MockDamageSource.Create();
            
            var info = MakeDamageInfo(raw, type, source);
            
            var step = new ApplyBarrierStep();
            step.Process(info);

            // Damage should pass through barrier unchanged
            Assert.AreEqual(60, info.Amounts.Current, "Damage should ignore barrier.");
            Assert.AreEqual(50, _targetHealth.Barrier, "Barrier should remain intact.");
        }

        [Test]
        public void ApplyBarrierStep_AppliesBarrier_WhenDamageTypeDoesNotIgnoreBarrier()
        {
            const long raw = 60;
            var type = MockDamageType.Create(ignoreBarrier: false);
            var source = MockDamageSource.Create();
            
            var info = MakeDamageInfo(raw, type, source);
            
            var step = new ApplyBarrierStep();
            step.Process(info);

            // Damage should be reduced by barrier
            Assert.AreEqual(10, info.Amounts.Current, "Damage should be reduced by barrier (60 - 50 = 10).");
            Assert.AreEqual(0, _targetHealth.Barrier, "Barrier should be consumed.");
        }

        #endregion

        #region Percentage Modifier Tests

        [Test]
        public void ApplyPercentageDmgModifiersStep_IgnoresGenericModifier_WhenDamageTypeIgnoresFlagIsSet()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericPercentageMod");
            _targetCore.RegisterStat(genericStat, -50);

            var config = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };
            AstraHealthConfigProvider.Instance = config;

            var type = MockDamageType.Create(ignorePercentage: true);
            var source = MockDamageSource.Create();
            var info = MakeDamageInfo(raw, type, source);

            var step = new ApplyPercentageDmgModifiersStep();
            step.Process(info);

            // Damage should be unchanged despite -50% modifier
            Assert.AreEqual(100, info.Amounts.Current, "Damage should ignore generic percentage modifier.");
        }

        [Test]
        public void ApplyPercentageDmgModifiersStep_AppliesGenericModifier_WhenDamageTypeDoesNotIgnore()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericPercentageMod");
            _targetCore.RegisterStat(genericStat, -50);

            var config = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };
            AstraHealthConfigProvider.Instance = config;

            var type = MockDamageType.Create(ignorePercentage: false);
            var source = MockDamageSource.Create();
            var info = MakeDamageInfo(raw, type, source);

            var step = new ApplyPercentageDmgModifiersStep();
            step.Process(info);

            // Damage should be reduced by 50%
            Assert.AreEqual(50, info.Amounts.Current, "Damage should be modified by -50%.");
        }

        [Test]
        public void ApplyPercentageDmgModifiersStep_DoesNotSetImmunity_WhenIgnoringGenericWith100Resistance()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericPercentageMod");
            _targetCore.RegisterStat(genericStat, -100);

            var config = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };
            AstraHealthConfigProvider.Instance = config;

            var type = MockDamageType.Create(ignorePercentage: true);
            var source = MockDamageSource.Create();
            var info = MakeDamageInfo(raw, type, source);

            var step = new ApplyPercentageDmgModifiersStep();
            step.Process(info);

            // Damage should pass through and NOT trigger immunity
            Assert.AreEqual(100, info.Amounts.Current, "Damage should ignore immunity.");
            Assert.IsFalse((info.Reasons & DamagePreventionReason.AllDamageImmune) != 0, "Should not set AllDamageImmune.");
        }

        #endregion

        #region Flat Modifier Tests

        [Test]
        public void ApplyFlatDmgModifiersStep_IgnoresGenericModifier_WhenDamageTypeIgnoresFlagIsSet()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericFlatMod");
            _targetCore.RegisterStat(genericStat, -30);

            var config = new MockConfig
            {
                GenericFlatDamageModificationStat = genericStat
            };
            AstraHealthConfigProvider.Instance = config;

            var type = MockDamageType.Create(ignoreFlat: true);
            var source = MockDamageSource.Create();
            var info = MakeDamageInfo(raw, type, source);

            var step = new ApplyFlatDmgModifiersStep();
            step.Process(info);

            // Damage should be unchanged despite -30 flat modifier
            Assert.AreEqual(100, info.Amounts.Current, "Damage should ignore generic flat modifier.");
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_AppliesGenericModifier_WhenDamageTypeDoesNotIgnore()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericFlatMod");
            _targetCore.RegisterStat(genericStat, -30);

            var config = new MockConfig
            {
                GenericFlatDamageModificationStat = genericStat
            };
            AstraHealthConfigProvider.Instance = config;

            var type = MockDamageType.Create(ignoreFlat: false);
            var source = MockDamageSource.Create();
            var info = MakeDamageInfo(raw, type, source);

            var step = new ApplyFlatDmgModifiersStep();
            step.Process(info);

            // Damage should be reduced by 30
            Assert.AreEqual(70, info.Amounts.Current, "Damage should be reduced by 30 flat.");
        }

        #endregion

        #region Combined True Damage Tests

        [Test]
        public void TrueDamage_IgnoresAllModifications_WhenAllFlagsAreSet()
        {
            const long raw = 100;
            
            // Setup percentage modifier
            var percentageStat = CreateStat("PercentageMod");
            _targetCore.RegisterStat(percentageStat, -50);
            
            // Setup flat modifier
            var flatStat = CreateStat("FlatMod");
            _targetCore.RegisterStat(flatStat, -20);

            var config = new MockConfig
            {
                GenericPercentageDamageModificationStat = percentageStat,
                GenericFlatDamageModificationStat = flatStat
            };
            AstraHealthConfigProvider.Instance = config;

            var type = MockDamageType.Create(
                ignoreBarrier: true,
                ignorePercentage: true,
                ignoreFlat: true);
            var source = MockDamageSource.Create();
            var info = MakeDamageInfo(raw, type, source);

            // Apply all steps
            var barrierStep = new ApplyBarrierStep();
            var percentageStep = new ApplyPercentageDmgModifiersStep();
            var flatStep = new ApplyFlatDmgModifiersStep();

            barrierStep.Process(info);
            percentageStep.Process(info);
            flatStep.Process(info);

            // Damage should be 100 (unchanged)
            Assert.AreEqual(100, info.Amounts.Current, "True damage should ignore all modifications.");
            Assert.AreEqual(50, _targetHealth.Barrier, "Barrier should remain intact.");
        }

        [Test]
        public void TrueDamage_AppliesOnlyNonIgnoredModifications()
        {
            const long raw = 100;
            
            // Setup percentage modifier
            var percentageStat = CreateStat("PercentageMod");
            _targetCore.RegisterStat(percentageStat, -50);
            
            // Setup flat modifier
            var flatStat = CreateStat("FlatMod");
            _targetCore.RegisterStat(flatStat, -20);

            var config = new MockConfig
            {
                GenericPercentageDamageModificationStat = percentageStat,
                GenericFlatDamageModificationStat = flatStat
            };
            AstraHealthConfigProvider.Instance = config;

            // Ignore only barrier and percentage, but not flat
            var type = MockDamageType.Create(
                ignoreBarrier: true,
                ignorePercentage: true,
                ignoreFlat: false);
            var source = MockDamageSource.Create();
            var info = MakeDamageInfo(raw, type, source);

            // Apply all steps
            var barrierStep = new ApplyBarrierStep();
            var percentageStep = new ApplyPercentageDmgModifiersStep();
            var flatStep = new ApplyFlatDmgModifiersStep();

            barrierStep.Process(info);
            percentageStep.Process(info);
            flatStep.Process(info);

            // Damage should be 80 (100 - 20 flat, percentage ignored)
            Assert.AreEqual(80, info.Amounts.Current, "Damage should only apply flat modification.");
            Assert.AreEqual(50, _targetHealth.Barrier, "Barrier should remain intact.");
        }

        #endregion
    }
}
