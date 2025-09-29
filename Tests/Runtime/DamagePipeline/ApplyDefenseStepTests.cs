using System.Linq;
using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgFramework.Stats;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.SoapRpgHealthTests.DamagePipeline
{
    public class ApplyDefenseStepTests
    {
        private class MockFlatDmgReductionFn : FlatDmgReductionFn
        {
            private long _result;
            public void Set(long r) => _result = r;
            public override long ReducedDmg(long amount, double defensiveStatValue) => _result;
        }

        private class MockFlatDefReductionFn : FlatDefReductionFn
        {
            private long _result;
            public void Set(long r) => _result = r;
            public override double ReducedDef(long piercingStatValue, long piercedStatValue) => _result;
        }

        private class MockDmgType : DmgType
        {
            public static MockDmgType Create(Stat def = null, DmgReductionFn dmgFn = null, Stat pierce = null, DefReductionFn defFn = null) {
                var t = CreateInstance<MockDmgType>();
                t.ReducedBy = def;
                t.DmgReductionFn = dmgFn;
                t.DefensiveStatPiercedBy = pierce;
                t.DefReductionFn = defFn;
                return t;
            }
        }

        private class MockSource : Source
        {
            public static MockSource Create() {
                var s = CreateInstance<MockSource>();
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

        private DamageInfo MakeDamageInfo(long raw, DmgType type, EntityCore target, EntityCore dealer)
        {
            // Build the required PreDmgInfo first (new DamageInfo ctor requirement)
            var pre = PreDmgInfo.Builder
                .WithAmount(raw)
                .WithType(type)
                .WithSource(MockSource.Create())
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
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
                Object.DestroyImmediate(go);
        }

        [Test]
        public void ApplyDefenseStep_ReducesDamage_WithDefensiveStat()
        {
            const long RAW = 100;
            const long DEF_VAL = 30;
            const long EXPECTED = 70;

            var defStat = ScriptableObject.CreateInstance<Stat>();
            var dmgFn = ScriptableObject.CreateInstance<MockFlatDmgReductionFn>();
            dmgFn.Set(EXPECTED);

            var (target, dealer, _, _) = MakeEntities(defensiveValue: DEF_VAL, defensiveStat: defStat);

            var dmgType = MockDmgType.Create(def: defStat, dmgFn: dmgFn);
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

            var defFn = ScriptableObject.CreateInstance<MockFlatDefReductionFn>();
            defFn.Set(REDUCED_DEF);

            var dmgFn = ScriptableObject.CreateInstance<MockFlatDmgReductionFn>();
            dmgFn.Set(EXPECTED);

            var (target, dealer, _, _) = MakeEntities(
                defensiveValue: DEF_VAL,
                piercingValue: PIERCING_VAL,
                defensiveStat: defStat,
                piercingStat: pierceStat);

            var dmgType = MockDmgType.Create(def: defStat, dmgFn: dmgFn, pierce: pierceStat, defFn: defFn);
            var info = MakeDamageInfo(RAW, dmgType, target, dealer);

            var step = new ApplyDefenseStep();
            var processed = step.Process(info);

            Assert.AreEqual(EXPECTED, processed.Amounts.Current);
            var rec2 = processed.Amounts.Records.Last();
            Assert.AreEqual(RAW, rec2.Pre);
            Assert.AreEqual(EXPECTED, rec2.Post);
        }
    }
}
