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
using Object = UnityEngine.Object;

namespace ElectricDrill.AstraRpgHealthTests.DamagePipeline
{
    public class ApplyFlatDmgModifiersStepTests
    {
        private class MockDamageType : DamageTypeSO
        {
            public static MockDamageType Create(string name = "MockType", StatSO flatStat = null)
            {
                var t = CreateInstance<MockDamageType>();
                t.name = name;
                t.FlatDamageModificationStat = flatStat;
                return t;
            }
        }

        private class MockDamageSource : DamageSourceSO
        {
            public static MockDamageSource Create(string name = "MockSource", StatSO flatStat = null)
            {
                var s = CreateInstance<MockDamageSource>();
                s.name = name;
                s.FlatDamageModificationStat = flatStat;
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
        public void ApplyFlatDmgModifiersStep_AppliesGenericFlatReduction()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericFlatMod");

            var (target, dealer) = MakeEntities(genericModValue: -20, genericStat: genericStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericFlatDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(80, info.Amounts.Current); // 100 - 20 = 80
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_AppliesGenericFlatIncrement()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericFlatMod");

            var (target, dealer) = MakeEntities(genericModValue: 15, genericStat: genericStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericFlatDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(115, info.Amounts.Current); // 100 + 15 = 115
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_AppliesSourceFlatModification()
        {
            const long raw = 100;
            var sourceModStat = CreateStat("SourceFlatMod");
            var source = MockDamageSource.Create("TestSource", sourceModStat);

            var (target, dealer) = MakeEntities(sourceModValue: -30, sourceStat: sourceModStat);

            AstraHealthConfigProvider.Instance = new MockConfig();

            var info = MakeDamageInfo(raw, MockDamageType.Create(), source, target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(70, info.Amounts.Current); // 100 - 30 = 70
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_AppliesTypeFlatModification()
        {
            const long raw = 100;
            var typeModStat = CreateStat("TypeFlatMod");
            var type = MockDamageType.Create("TestType", typeModStat);

            var (target, dealer) = MakeEntities(typeModValue: 25, typeStat: typeModStat);

            AstraHealthConfigProvider.Instance = new MockConfig();

            var info = MakeDamageInfo(raw, type, MockDamageSource.Create(), target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(125, info.Amounts.Current); // 100 + 25 = 125
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_AppliesCumulativeFlatModifications()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericFlatMod");
            var sourceStat = CreateStat("SourceFlatMod");
            var typeStat = CreateStat("TypeFlatMod");

            var type = MockDamageType.Create("TestType", typeStat);
            var source = MockDamageSource.Create("TestSource", sourceStat);

            var (target, dealer) = MakeEntities(
                genericModValue: -10, sourceModValue: -5, typeModValue: 20,
                genericStat: genericStat, sourceStat: sourceStat, typeStat: typeStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericFlatDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, type, source, target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(105, info.Amounts.Current); // net: -10 -5 +20 = +5 → 105
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_ClampsDamageToZero_WhenNegativeResult()
        {
            const long raw = 50;
            var genericStat = CreateStat("GenericFlatMod");

            var (target, dealer) = MakeEntities(genericModValue: -100, genericStat: genericStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericFlatDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current); // 50 - 100 clamped to 0
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_DoesNotModify_WhenNoConfigProvided()
        {
            const long raw = 100;
            var (target, dealer) = MakeEntities();

            AstraHealthConfigProvider.Instance = null;

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(raw, info.Amounts.Current);
        }

        [Test]
        public void ApplyFlatDmgModifiersStep_HandlesZeroModifications()
        {
            const long raw = 100;
            var genericStat = CreateStat("GenericFlatMod");

            var (target, dealer) = MakeEntities(genericModValue: 0, genericStat: genericStat);

            AstraHealthConfigProvider.Instance = new MockConfig
            {
                GenericFlatDamageModificationStat = genericStat
            };

            var info = MakeDamageInfo(raw, MockDamageType.Create(), MockDamageSource.Create(), target, dealer);
            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(100, info.Amounts.Current);
        }
    }
}
