using System.Linq;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgHealth.Damage;
using ElectricDrill.AstraRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraRpgHealth.DamageReductionFunctions;
using ElectricDrill.AstraRpgHealth.DamageReductionFunctions.DamageReductionFunctions;
using ElectricDrill.AstraRpgHealth.DefenseReductionFunctions;
using ElectricDrill.AstraRpgHealth.DefenseReductionFunctions.DefenseReductionFunctions;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.AstraRpgHealthTests.DamagePipeline
{
    public class ApplyDefenseStepTests
    {
        private class MockFlatDamageReductionFn : FlatDamageReductionFn
        {
            private long _result;
            public void Set(long r) => _result = r;
            public override long ReducedDmg(long amount, double defensiveStatValue) => _result;
        }

        private class MockFlatDefenseReductionFn : FlatDefenseReductionFn
        {
            private long _result;
            public void Set(long r) => _result = r;
            public override double ReducedDef(long piercingStatValue, long defensiveStatValue, Stat defensiveStat, bool clampDef = true) => _result;
        }

        private class MockDamageType : DamageType
        {
            public static MockDamageType Create(Stat def = null, DamageReductionFn damageFn = null, Stat pierce = null, DefenseReductionFn defenseFn = null) {
                var t = CreateInstance<MockDamageType>();
                t.ReducedBy = def;
                t.DamageReductionFn = damageFn;
                t.DefensiveStatPiercedBy = pierce;
                t.DefenseReductionFn = defenseFn;
                return t;
            }
        }

        private class MockDamageSource : DamageSource
        {
            public static MockDamageSource Create() {
                var s = CreateInstance<MockDamageSource>();
                s.name = "TestSource";
                return s;
            }
        }

        // Concrete stats component to avoid null Stats and allow deterministic values
        private class TestStats : EntityStats
        {
            public long defensiveValue;
            public long piercingValue;
            public Stat defensiveStat;
            public Stat piercingStat;

            public override long Get(Stat stat)
            {
                if (stat == defensiveStat) return defensiveValue;
                if (stat == piercingStat) return piercingValue;
                return 0;
            }
        }

        private DamageInfo MakeDamageInfo(long raw, DamageType type, EntityCore target, EntityCore dealer)
        {
            // Build the required PreDamageInfo first (new DamageInfo ctor requirement)
            var pre = PreDamageInfo.Builder
                .WithAmount(raw)
                .WithType(type)
                .WithSource(MockDamageSource.Create())
                .WithTarget(target)
                .WithDealer(dealer)
                .Build();
            
            return new DamageInfo(pre);
        }

        private (EntityCore target, EntityCore dealer, TestStats targetStats, TestStats dealerStats) MakeEntities(
            long defensiveValue = 0,
            long piercingValue = 0,
            Stat defensiveStat = null,
            Stat piercingStat = null)
        {
            var targetGo = new GameObject("Target");
            var dealerGo = new GameObject("Dealer");

            var targetCore = targetGo.AddComponent<EntityCore>();
            var dealerCore = dealerGo.AddComponent<EntityCore>();

            var targetStats = targetGo.AddComponent<TestStats>();
            targetStats.defensiveValue = defensiveValue;
            targetStats.defensiveStat = defensiveStat;

            var dealerStats = dealerGo.AddComponent<TestStats>();
            dealerStats.piercingValue = piercingValue;
            dealerStats.piercingStat = piercingStat;

            // Ensure EntityCore.Stats returns these instances (assign internal field if accessible)
            targetCore._stats = targetStats;
            dealerCore._stats = dealerStats;

            return (targetCore, dealerCore, targetStats, dealerStats);
        }

        [TearDown]
        public void CleanupScene() {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(go);
        }

        [Test]
        public void ApplyDefenseStep_ReducesDamage_WithDefensiveStat()
        {
            const long RAW = 100;
            const long DEF_VAL = 30;
            const long EXPECTED = 70;

            var defStat = ScriptableObject.CreateInstance<Stat>();
            var dmgFn = ScriptableObject.CreateInstance<MockFlatDamageReductionFn>();
            dmgFn.Set(EXPECTED);

            var (target, dealer, _, _) = MakeEntities(defensiveValue: DEF_VAL, defensiveStat: defStat);

            var dmgType = MockDamageType.Create(def: defStat, damageFn: dmgFn);
            var info = MakeDamageInfo(RAW, dmgType, target, dealer);

            var step = new ApplyDefenseStep();

            var processed = step.Process(info);

            Assert.AreEqual(EXPECTED, processed.Amounts.Current);
            var rec1 = processed.Amounts.Records.Last();
            Assert.AreEqual(RAW, rec1.Pre);
            Assert.AreEqual(EXPECTED, rec1.Post);
        }

        [Test]
        public void ApplyDefenseStep_ReducesDamage_WithPiercing()
        {
            const long RAW = 120;
            const long DEF_VAL = 40;
            const long PIERCING_VAL = 10;
            const long REDUCED_DEF = 30; // after piercing
            const long EXPECTED = 90; // mocked final dmg

            var defStat = ScriptableObject.CreateInstance<Stat>();
            var pierceStat = ScriptableObject.CreateInstance<Stat>();

            var defFn = ScriptableObject.CreateInstance<MockFlatDefenseReductionFn>();
            defFn.Set(REDUCED_DEF);

            var dmgFn = ScriptableObject.CreateInstance<MockFlatDamageReductionFn>();
            dmgFn.Set(EXPECTED);

            var (target, dealer, _, _) = MakeEntities(
                defensiveValue: DEF_VAL,
                piercingValue: PIERCING_VAL,
                defensiveStat: defStat,
                piercingStat: pierceStat);

            var dmgType = MockDamageType.Create(def: defStat, damageFn: dmgFn, pierce: pierceStat, defenseFn: defFn);
            var info = MakeDamageInfo(RAW, dmgType, target, dealer);

            var step = new ApplyDefenseStep();
            var processed = step.Process(info);

            Assert.AreEqual(EXPECTED, processed.Amounts.Current);
            var rec2 = processed.Amounts.Records.Last();
            Assert.AreEqual(RAW, rec2.Pre);
            Assert.AreEqual(EXPECTED, rec2.Post);
        }

        [Test]
        public void ApplyDefenseStep_SetsDefenseAbsorbedReason_WhenDefenseFullyAbsorbsDamage()
        {
            const long RAW = 50;
            const long EXPECTED = 0; // fully absorbed

            var defStat = ScriptableObject.CreateInstance<Stat>();
            var dmgFn = ScriptableObject.CreateInstance<MockFlatDamageReductionFn>();
            dmgFn.Set(EXPECTED);

            var (target, dealer, _, _) = MakeEntities(defensiveValue: 100, defensiveStat: defStat);

            var dmgType = MockDamageType.Create(def: defStat, damageFn: dmgFn);
            var info = MakeDamageInfo(RAW, dmgType, target, dealer);

            var step = new ApplyDefenseStep();
            step.Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.IsTrue((info.Reasons & DamagePreventionReason.DefenseAbsorbed) != 0);
            Assert.AreEqual(typeof(ApplyDefenseStep), info.TerminationStepType);
        }

        [Test]
        public void ApplyDefenseStep_DoesNotSetDefenseAbsorbedReason_WhenDefensePartiallyReducesDamage()
        {
            const long RAW = 100;
            const long EXPECTED = 40; // partially reduced

            var defStat = ScriptableObject.CreateInstance<Stat>();
            var dmgFn = ScriptableObject.CreateInstance<MockFlatDamageReductionFn>();
            dmgFn.Set(EXPECTED);

            var (target, dealer, _, _) = MakeEntities(defensiveValue: 50, defensiveStat: defStat);

            var dmgType = MockDamageType.Create(def: defStat, damageFn: dmgFn);
            var info = MakeDamageInfo(RAW, dmgType, target, dealer);

            var step = new ApplyDefenseStep();
            step.Process(info);

            Assert.AreEqual(EXPECTED, info.Amounts.Current);
            Assert.IsFalse((info.Reasons & DamagePreventionReason.DefenseAbsorbed) != 0);
            Assert.IsNull(info.TerminationStepType);
        }

        [Test]
        public void ApplyDefenseStep_DoesNotSetDefenseAbsorbedReason_WhenNoDamageFunctionConfigured()
        {
            const long RAW = 80;

            var defStat = ScriptableObject.CreateInstance<Stat>();
            var (target, dealer, _, _) = MakeEntities(defensiveValue: 200, defensiveStat: defStat);

            // No damage reduction function configured
            var dmgType = MockDamageType.Create(def: defStat, damageFn: null);
            var info = MakeDamageInfo(RAW, dmgType, target, dealer);

            var step = new ApplyDefenseStep();
            step.Process(info);

            // Damage should remain unchanged
            Assert.AreEqual(RAW, info.Amounts.Current);
            Assert.IsFalse((info.Reasons & DamagePreventionReason.DefenseAbsorbed) != 0);
        }
    }
}
