using System.Collections.Generic;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Contexts;
using ElectricDrill.AstraRpgFramework.GameActions;
using ElectricDrill.AstraRpgFramework.Ownership;
using ElectricDrill.AstraRpgFramework.Scaling.ScalingComponents;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraHealth.Config;
using ElectricDrill.AstraHealth.Damage;
using ElectricDrill.AstraHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraHealth.Events;
using ElectricDrill.AstraHealth.Experience;
using ElectricDrill.AstraHealth.Heal;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.AstraRpgHealthTests.DamagePipeline
{
    public class ApplyPercentageDmgModifiersStepTests
    {
        private class MockDamageType : DamageTypeSO
        {
            public static MockDamageType Create(string name = "MockType", StatSO percentageStat = null)
            {
                var t = CreateInstance<MockDamageType>();
                t.name = name;
                t.PercentageDamageModificationStat = percentageStat;
                return t;
            }
        }

        private class MockDamageSource : DamageSourceSO
        {
            public static MockDamageSource Create(string name = "MockSource", StatSO percentageStat = null)
            {
                var s = CreateInstance<MockDamageSource>();
                s.name = name;
                s.PercentageDamageModificationStat = percentageStat;
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
            public AttributesScalingComponentSO HealthAttributesScaling { get; set; }
            public StatSO GenericFlatHealAmountModifierStat { get; set; }
            public StatSO GenericPercentageHealAmountModifierStat { get; set; }
            public DamageCalculationStrategySO DefaultDamageCalculationCalculationStrategy { get; set; }
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

        private (StubEntityCore target, StubEntityCore dealer) MakeEntities(
            long genericModValue = 0,
            long sourceModValue = 0,
            long typeModValue = 0,
            StatSO genericStat = null,
            StatSO sourceStat = null,
            StatSO typeStat = null)
        {
            var target = new GameObject("Target").AddComponent<StubEntityCore>();
            target.RegisterStat(genericStat, genericModValue);
            target.RegisterStat(sourceStat, sourceModValue);
            target.RegisterStat(typeStat, typeModValue);

            var dealer = new GameObject("Dealer").AddComponent<StubEntityCore>();
            return (target, dealer);
        }

        private DamageInfo MakeDamageInfo(long raw, DamageTypeSO type, DamageSourceSO source, EntityCore target,
            EntityCore dealer)
        {
            var pre = PreDamageContext.Builder
                .WithAmount(raw)
                .WithType(type)
                .WithSource(source)
                .WithTarget(target)
                .WithPerformer(dealer)
                .Build();
            return new DamageInfo(pre, AstraHealthConfigProvider.Instance);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                Object.DestroyImmediate(go);
        }

        private static StatSO CreateStat(string name)
        {
            var stat = ScriptableObject.CreateInstance<StatSO>();
            stat.name = name;
            return stat;
        }

        [Test]
        public void ApplyDmgModifiersStep_SetsAllDamageImmuneReason_WhenGenericModifierIsNegative100()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericDmgMod");

            var (target, dealer) = MakeEntities(genericModValue: -100, genericStat: genericStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.IsTrue((info.Reasons & DamagePreventionReason.AllDamageImmune) != 0);
            Assert.AreEqual(typeof(ApplyPercentageDmgModifiersStep), info.TerminationStepType);
        }

        [Test]
        public void ApplyDmgModifiersStep_SetsAllDamageImmuneReason_WhenGenericModifierIsLessThanNegative100()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericDmgMod");

            var (target, dealer) = MakeEntities(genericModValue: -150, genericStat: genericStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.IsTrue((info.Reasons & DamagePreventionReason.AllDamageImmune) != 0);
            Assert.AreEqual(typeof(ApplyPercentageDmgModifiersStep), info.TerminationStepType);
        }

        [Test]
        public void ApplyDmgModifiersStep_DoesNotSetAllDamageImmuneReason_WhenGenericModifierIsGreaterThanNegative100()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericDmgMod");

            var (target, dealer) = MakeEntities(genericModValue: -50, genericStat: genericStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(50, info.Amounts.Current); // 100 - 50% = 50
            Assert.IsFalse((info.Reasons & DamagePreventionReason.AllDamageImmune) != 0);
            Assert.IsNull(info.TerminationStepType);
        }

        [Test]
        public void ApplyDmgModifiersStep_SetsDamageSourceImmuneReason_WhenSourceModifierIsNegative100()
        {
            const long raw = 100;
            var sourceModStat = CreateStat("SourceMod");
            var source = MockDamageSource.Create("TestSource", sourceModStat);

            var (target, dealer) = MakeEntities(sourceModValue: -100, sourceStat: sourceModStat);

            AstraHealthConfigProvider.Instance = new MockConfig();

            var info = MakeDamageInfo(raw, MockDamageType.Create(), source, target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.IsTrue((info.Reasons & DamagePreventionReason.DamageSourceImmune) != 0);
            Assert.AreEqual(typeof(ApplyPercentageDmgModifiersStep), info.TerminationStepType);
        }

        [Test]
        public void ApplyDmgModifiersStep_SetsDamageTypeImmuneReason_WhenTypeModifierIsNegative100()
        {
            const long raw = 100;
            var typeModStat = CreateStat("TypeMod");
            var type = MockDamageType.Create("TestType", typeModStat);

            var (target, dealer) = MakeEntities(typeModValue: -100, typeStat: typeModStat);

            AstraHealthConfigProvider.Instance = new MockConfig();

            var info = MakeDamageInfo(raw, type, MockDamageSource.Create(), target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.IsTrue((info.Reasons & DamagePreventionReason.DamageTypeImmune) != 0);
            Assert.AreEqual(typeof(ApplyPercentageDmgModifiersStep), info.TerminationStepType);
        }

        [Test]
        public void ApplyDmgModifiersStep_PrioritizesGenericImmunity_OverSourceAndTypeModifiers()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericDmgMod");
            var sourceStat = CreateStat("SourceMod");
            var typeStat = CreateStat("TypeMod");

            var type = MockDamageType.Create("TestType", typeStat);
            var source = MockDamageSource.Create("TestSource", sourceStat);

            var (target, dealer) = MakeEntities(
                genericModValue: -100,
                sourceModValue: 50,
                typeModValue: 50,
                genericStat: genericStat,
                sourceStat: sourceStat,
                typeStat: typeStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, type, source, target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.IsTrue((info.Reasons & DamagePreventionReason.AllDamageImmune) != 0);
            Assert.IsFalse((info.Reasons & DamagePreventionReason.DamageSourceImmune) != 0);
            Assert.IsFalse((info.Reasons & DamagePreventionReason.DamageTypeImmune) != 0);
        }

        [Test]
        public void ApplyDmgModifiersStep_AppliesCumulativeModifiers_WhenNoneReachImmunity()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericDmgMod");
            var sourceStat = CreateStat("SourceDmgMod");
            var typeStat = CreateStat("TypeDmgMod");

            var type = MockDamageType.Create("TestType", typeStat);
            var source = MockDamageSource.Create("TestSource", sourceStat);

            var (target, dealer) = MakeEntities(
                genericModValue: -20,
                sourceModValue: -30,
                typeModValue: 10,
                genericStat: genericStat,
                sourceStat: sourceStat,
                typeStat: typeStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericPercentageDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, type, source, target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            // Net: -20 -30 +10 = -40% → 100 + (100 * -0.40) = 60
            Assert.AreEqual(60, info.Amounts.Current);
            Assert.AreEqual(DamagePreventionReason.None, info.Reasons);
            Assert.IsNull(info.TerminationStepType);
        }

        [Test]
        public void ApplyDmgModifiersStep_DoesNotModify_WhenNoConfigProvided()
        {
            const long raw = 100;
            var (target, dealer) = MakeEntities();

            AstraHealthConfigProvider.Instance = null;

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(raw, info.Amounts.Current);
            Assert.AreEqual(DamagePreventionReason.None, info.Reasons);
        }
    }
}
