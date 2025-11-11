using System.Reflection;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraRpgHealth;
using ElectricDrill.AstraRpgHealth.Damage;
using ElectricDrill.AstraRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraRpgHealth.Events;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.AstraRpgHealthTests.DamagePipeline
{
    public class ApplyBarrierStepTests
    {
        private class MockDamageType : DamageType {
            public static MockDamageType Create(bool ignoresBarrier = false) {
                var t = CreateInstance<MockDamageType>();
                t.IgnoresBarrier = ignoresBarrier;
                return t;
            }
        }
        private class MockDamageSource : DamageSource {
            public static MockDamageSource Create() {
                var s = CreateInstance<MockDamageSource>();
                s.name = "BarrierTestSource";
                return s;
            }
        }

        private (EntityHealth eh, EntityCore core) MakeEntity(long maxHp, long barrierVal)
        {
            var go = new GameObject("BarrierTarget");
            var core = go.AddComponent<EntityCore>();
            var stats = go.AddComponent<EntityStats>();
            var eh = go.AddComponent<EntityHealth>();

            eh._baseMaxHp = new LongRef { UseConstant = true, ConstantValue = maxHp };
            eh._totalMaxHp = new LongRef { UseConstant = true };
            eh._hp = new LongRef { UseConstant = true, ConstantValue = maxHp };
            eh._barrier = new LongRef { UseConstant = true, ConstantValue = barrierVal };
            eh._deathThreshold = LongVarFactory.CreateLongVar(0);
            eh.OverrideOnDeathStrategy = ScriptableObject.CreateInstance<DestroyImmediateOnDeathStrategy>();

            // Mandatory events
            void SetEvt(string field, ScriptableObject so) =>
                typeof(EntityHealth).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(eh, so);

            SetEvt("_preDmgInfoEvent", ScriptableObject.CreateInstance<PreDmgGameEvent>());
            SetEvt("_damageResolutionEvent", ScriptableObject.CreateInstance<DamageResolutionGameEvent>());
            SetEvt("_entityDiedEvent", ScriptableObject.CreateInstance<EntityDiedGameEvent>());
            SetEvt("_maxHealthChangedEvent", ScriptableObject.CreateInstance<EntityMaxHealthChangedGameEvent>());
            SetEvt("_gainedHealthEvent", ScriptableObject.CreateInstance<EntityGainedHealthGameEvent>());
            SetEvt("_lostHealthEvent", ScriptableObject.CreateInstance<EntityLostHealthGameEvent>());
            SetEvt("_preHealEvent", ScriptableObject.CreateInstance<PreHealGameEvent>());
            SetEvt("_entityHealedEvent", ScriptableObject.CreateInstance<EntityHealedGameEvent>());

            eh._entityCore = core;
            eh._entityStats = stats;
            eh.SetupMaxHp();
            return (eh, core);
        }

        private DamageInfo MakeDamageInfo(long raw, DamageType type, EntityCore target, EntityCore dealer)
        {
            var pre = PreDamageInfo.Builder
                .WithAmount(raw)
                .WithType(type)
                .WithSource(MockDamageSource.Create())
                .WithTarget(target)
                .WithDealer(dealer)
                .Build();
            return new DamageInfo(pre);
        }

        [TearDown]
        public void Cleanup() {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(go);
        }

        [Test]
        public void ApplyBarrierStep_ReducesDamage_ConsumesBarrier()
        {
            const long RAW = 50;
            const long BARRIER = 20;
            var (eh, core) = MakeEntity(100, BARRIER);
            var info = MakeDamageInfo(RAW, MockDamageType.Create(), core, core);

            var step = new ApplyBarrierStep();
            step.Process(info);

            Assert.AreEqual(RAW - BARRIER, info.Amounts.Current);
            Assert.AreEqual(0, eh.Barrier);
        }

        [Test]
        public void ApplyBarrierStep_DamageLowerThanBarrier_RemainsPartial()
        {
            const long RAW = 15;
            const long BARRIER = 50;
            var (eh, core) = MakeEntity(100, BARRIER);
            var info = MakeDamageInfo(RAW, MockDamageType.Create(), core, core);

            new ApplyBarrierStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.AreEqual(BARRIER - RAW, eh.Barrier);
        }

        [Test]
        public void ApplyBarrierStep_Ignored_WhenTypeIgnoresBarrier()
        {
            const long RAW = 40;
            const long BARRIER = 25;
            var (eh, core) = MakeEntity(100, BARRIER);
            var info = MakeDamageInfo(RAW, MockDamageType.Create(ignoresBarrier: true), core, core);

            new ApplyBarrierStep().Process(info);

            Assert.AreEqual(RAW, info.Amounts.Current);
            Assert.AreEqual(BARRIER, eh.Barrier);
        }

        [Test]
        public void ApplyBarrierStep_BarrierNeverNegative()
        {
            const long RAW = 200;
            const long BARRIER = 30;
            var (eh, core) = MakeEntity(100, BARRIER);
            var info = MakeDamageInfo(RAW, MockDamageType.Create(), core, core);

            new ApplyBarrierStep().Process(info);

            Assert.AreEqual(RAW - BARRIER, info.Amounts.Current);
            Assert.AreEqual(0, eh.Barrier);
        }
    }
}
