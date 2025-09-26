using System.Reflection;
using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgFramework.Stats;
using ElectricDrill.SoapRpgFramework.Utils;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.SoapRpgHealth.Events;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.SoapRpgHealthTests.DamagePipeline
{
    public class ApplyBarrierStepTests
    {
        private class MockDmgType : DmgType
        {
            public static MockDmgType Create(bool ignoresBarrier = false) {
                var t = CreateInstance<MockDmgType>();
                t.IgnoresBarrier = ignoresBarrier;
                return t;
            }
        }

        private class MockSource : Source
        {
            public static MockSource Create() {
                var s = CreateInstance<MockSource>();
                s.name = "BarrierTestSource";
                return s;
            }
        }

        private (EntityHealth eh, EntityCore core) MakeEntity(long maxHp, long barrier)
        {
            var go = new GameObject("BarrierTarget");
            var core = go.AddComponent<EntityCore>();
            var stats = go.AddComponent<EntityStats>();
            var eh = go.AddComponent<EntityHealth>();

            // Inject minimal mandatory scriptable objects to avoid assertions (simplified)
            eh.baseMaxHp = new LongRef { UseConstant = true, ConstantValue = maxHp };
            eh.totalMaxHp = new LongRef { UseConstant = true };
            eh.hp = new LongRef { UseConstant = true, ConstantValue = maxHp };
            eh.barrier = new LongRef() { UseConstant = true, ConstantValue = barrier };
            eh.deathThreshold = LongVarFactory.CreateLongVar(0);
            eh.OnDeathStrategy = ScriptableObject.CreateInstance<DestroyImmediateOnDeathStrategy>();

            // Dummy events to satisfy validation
            typeof(EntityHealth).GetField("_preDmgInfoEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<PreDmgGameEvent>());
            typeof(EntityHealth).GetField("_takenDmgInfoEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<TakenDmgGameEvent>());
            typeof(EntityHealth).GetField("_preventedDmgEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<PreventedDmgGameEvent>());
            typeof(EntityHealth).GetField("_entityDiedEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<EntityDiedGameEvent>());
            typeof(EntityHealth).GetField("_maxHealthChangedEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<EntityMaxHealthChangedGameEvent>());
            typeof(EntityHealth).GetField("_gainedHealthEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<EntityGainedHealthGameEvent>());
            typeof(EntityHealth).GetField("_lostHealthEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<EntityLostHealthGameEvent>());
            typeof(EntityHealth).GetField("_preHealEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<PreHealGameEvent>());
            typeof(EntityHealth).GetField("_entityHealedEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(eh, ScriptableObject.CreateInstance<EntityHealedGameEvent>());

            eh._core = core;
            eh._stats = stats;
            eh.SetupBaseMaxHp();

            return (eh, core);
        }

        private DamageInfo MakeDamageInfo(long raw, DmgType type, EntityCore target, EntityCore dealer)
        {
            var pre = PreDmgInfo.Builder
                .WithAmount(raw)
                .WithType(type)
                .WithSource(MockSource.Create())
                .WithTarget(target)
                .WithDealer(dealer)
                .Build();

            return new DamageInfo(pre);
        }

        [TearDown]
        public void Cleanup() {
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
                Object.DestroyImmediate(go);
        }

        [Test]
        public void ApplyBarrierStep_ReducesDamage_ConsumesBarrier()
        {
            const long RAW = 50;
            const long BARRIER = 20;
            const long EXPECTED_NET = RAW - BARRIER;

            var (eh, core) = MakeEntity(100, BARRIER);
            var dmgType = MockDmgType.Create();
            var info = MakeDamageInfo(RAW, dmgType, core, core);

            var step = new ApplyBarrierStep();
            var processed = step.Process(info);

            Assert.AreEqual(EXPECTED_NET, processed.Amounts.NetAmount);
            Assert.AreEqual(EXPECTED_NET, processed.Amounts.DefBarrierReducedAmount);
            Assert.AreEqual(0, eh.Barrier);
        }

        [Test]
        public void ApplyBarrierStep_DamageLowerThanBarrier_AllBarrierNotConsumed()
        {
            const long RAW = 15;
            const long BARRIER = 50;
            const long EXPECTED_NET = 0;
            const long EXPECTED_REMAINING = BARRIER - RAW;

            var (eh, core) = MakeEntity(100, BARRIER);
            var dmgType = MockDmgType.Create();
            var info = MakeDamageInfo(RAW, dmgType, core, core);

            var step = new ApplyBarrierStep();
            var processed = step.Process(info);

            Assert.AreEqual(EXPECTED_NET, processed.Amounts.NetAmount);
            Assert.AreEqual(EXPECTED_NET, processed.Amounts.DefBarrierReducedAmount);
            Assert.AreEqual(EXPECTED_REMAINING, eh.Barrier);
        }

        [Test]
        public void ApplyBarrierStep_Ignored_WhenDmgTypeIgnoresBarrier()
        {
            const long RAW = 40;
            const long BARRIER = 25;

            var (eh, core) = MakeEntity(100, BARRIER);
            var dmgType = MockDmgType.Create(ignoresBarrier: true);
            var info = MakeDamageInfo(RAW, dmgType, core, core);

            var step = new ApplyBarrierStep();
            var processed = step.Process(info);

            Assert.AreEqual(RAW, processed.Amounts.NetAmount);
            Assert.AreEqual(0, processed.Amounts.DefBarrierReducedAmount); // unchanged
            Assert.AreEqual(BARRIER, eh.Barrier); // untouched
        }

        [Test]
        public void ApplyBarrierStep_BarrierCannotGoNegative()
        {
            const long RAW = 200;
            const long BARRIER = 30;
            const long EXPECTED_NET = RAW - BARRIER;

            var (eh, core) = MakeEntity(100, BARRIER);
            var dmgType = MockDmgType.Create();
            var info = MakeDamageInfo(RAW, dmgType, core, core);

            var step = new ApplyBarrierStep();
            var processed = step.Process(info);

            Assert.AreEqual(EXPECTED_NET, processed.Amounts.NetAmount);
            Assert.AreEqual(0, eh.Barrier);
        }
    }
}
