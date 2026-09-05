using System;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraHealth.Barrier;
using ElectricDrill.AstraHealth.Config;
using ElectricDrill.AstraHealth.Damage;
using ElectricDrill.AstraHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraHealth.DamageMitigationFunctions;
using ElectricDrill.AstraHealth.DefensePenetrationFunctions;
using ElectricDrill.AstraRpgHealthTests.TestUtils;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricDrill.AstraRpgHealthTests.DamagePipeline
{
    /// <summary>
    /// Tests verifying abstraction boundaries in the damage calculation pipeline:
    /// <list type="bullet">
    /// <item><see cref="ApplyBarrierStep"/> operates on <see cref="IBarrierContainer"/> interface — no dependency on <c>EntityHealth</c> concrete type.</item>
    /// <item><see cref="DamageInfo"/> pre-resolves <see cref="DamageInfo.TargetBarrier"/>, <see cref="DamageInfo.TargetStats"/>
    /// and <see cref="DamageInfo.PerformerStats"/> once in the constructor, making them available to all steps.</item>
    /// <item><see cref="ApplyDefenseStep"/> handles null performer (environmental damage) gracefully without <c>NullReferenceException</c>.</item>
    /// <item>Stats-reading steps treat missing stat readers as safe no-ops, relying on <c>IStatReader.GetElseZero</c> to return 0 for absent values.</item>
    /// </list>
    /// </summary>
    public class DamageCalculationAbstractionTests
    {
        // ── Stubs ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimal MonoBehaviour implementing <see cref="IBarrierContainer"/>.
        /// Provides barrier logic without requiring the full <c>EntityHealth</c> setup.
        /// </summary>
        private class StubBarrierContainer : MonoBehaviour, IBarrierContainer
        {
            public long BarrierValue;
            public long Barrier => BarrierValue;
            public void AddBarrier(long amount) => BarrierValue += amount;
            public void RemoveBarrier(long amount) => BarrierValue = Math.Max(0, BarrierValue - amount);
        }

        private class StubDamageType : DamageTypeSO
        {
            public static StubDamageType Create(
                bool ignoresBarrier = false,
                StatSO def = null,
                DamageMitigationFnSO dmgFn = null,
                StatSO pierce = null,
                DefensePenetrationFnSO defenseFn = null)
            {
                var t = CreateInstance<StubDamageType>();
                t.IgnoresBarrier = ignoresBarrier;
                t.DefensiveStat = def;
                t.DamageMitigationFn = dmgFn;
                t.DefensiveStatPiercedBy = pierce;
                t.DefensePenetrationFn = defenseFn;
                return t;
            }
        }

        private class StubDamageSource : DamageSourceSO
        {
            public static StubDamageSource Create() => CreateInstance<StubDamageSource>();
        }

        /// <summary>
        /// Spy mitigation function. Captures <c>defensiveStatValue</c> for assertion
        /// and returns a configurable <see cref="Result"/>.
        /// </summary>
        private class SpyMitigationFn : DamageMitigationFnSO
        {
            public long Result;
            public double CapturedDefensiveValue = -1;

            public override long CalculateMitigatedDamage(long amount, double defensiveStatValue,
                RoundingMode roundingMode)
            {
                CapturedDefensiveValue = defensiveStatValue;
                return Result;
            }
        }

        /// <summary>
        /// Spy penetration function. Captures <c>piercingStatValue</c> for assertion
        /// and returns a configurable <see cref="Result"/>.
        /// </summary>
        private class SpyPenetrationFn : DefensePenetrationFnSO
        {
            public double Result;
            public long CapturedPiercingValue = -1;

            public override double CalculatePiercedDefense(
                long piercingStatValue, long defensiveStatValue, StatSO defensiveStat, bool applyClamp = true)
            {
                CapturedPiercingValue = piercingStatValue;
                return Result;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private EntityCore MakeCoreWithBarrierStub(long barrierVal, out StubBarrierContainer stub)
        {
            var go = new GameObject("Entity");
            var core = go.AddComponent<EntityCore>();
            stub = go.AddComponent<StubBarrierContainer>();
            stub.BarrierValue = barrierVal;
            return core;
        }

        private EntityCore MakeCoreOnly(string name = "Entity") =>
            new GameObject(name).AddComponent<EntityCore>();

        private DamageInfo MakeInfo(
            long amount,
            DamageTypeSO type,
            EntityCore target,
            EntityCore performer = null,
            IAstraHealthConfig config = null)
        {
            var builder = PreDamageContext.Builder
                .WithAmount(amount)
                .WithType(type)
                .WithSource(StubDamageSource.Create())
                .WithTarget(target);
            if (performer != null)
                builder.WithPerformer(performer);
            return new DamageInfo(builder.Build(), config);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                Object.DestroyImmediate(go);
            AstraHealthConfigProvider.Reset();
        }

        // ── DamageInfo pre-resolution ─────────────────────────────────────────────

        [Test]
        public void DamageInfo_TargetBarrier_IsPreResolved_WhenTargetHasBarrierComponent()
        {
            var core = MakeCoreWithBarrierStub(50, out var stub);
            var info = MakeInfo(100, StubDamageType.Create(), core);

            Assert.AreSame(stub, info.TargetBarrier);
        }

        [Test]
        public void DamageInfo_TargetBarrier_IsNull_WhenTargetHasNoBarrierComponent()
        {
            var info = MakeInfo(100, StubDamageType.Create(), MakeCoreOnly());

            Assert.IsNull(info.TargetBarrier);
        }

        [Test]
        public void DamageInfo_PerformerStats_IsNull_WhenPerformerIsNull()
        {
            var info = MakeInfo(100, StubDamageType.Create(), MakeCoreOnly());

            Assert.IsNull(info.PerformerStats);
        }

        [Test]
        public void DamageInfo_TargetStats_IsNotNull_WhenTargetExists()
        {
            var info = MakeInfo(100, StubDamageType.Create(), MakeCoreOnly());

            Assert.IsNotNull(info.TargetStats);
        }

        // ── ApplyBarrierStep — IBarrierContainer, no EntityHealth needed ──────────

        [Test]
        public void ApplyBarrierStep_PartialAbsorption_ReducesDamageByBarrierAmount()
        {
            const long raw = 80, barrier = 30;
            var core = MakeCoreWithBarrierStub(barrier, out var stub);
            var info = MakeInfo(raw, StubDamageType.Create(), core);

            new ApplyBarrierStep().Process(info);

            Assert.AreEqual(raw - barrier, info.Amounts.Current);
            Assert.AreEqual(0, stub.BarrierValue);
        }

        [Test]
        public void ApplyBarrierStep_FullAbsorption_ZerosDamageAndSetsBarrierAbsorbedReason()
        {
            const long raw = 20, barrier = 50;
            var core = MakeCoreWithBarrierStub(barrier, out var stub);
            var info = MakeInfo(raw, StubDamageType.Create(), core);

            new ApplyBarrierStep().Process(info);

            Assert.AreEqual(0, info.Amounts.Current);
            Assert.AreEqual(barrier - raw, stub.BarrierValue);
            Assert.IsTrue(info.Reasons.HasFlag(DamagePreventionReason.BarrierAbsorbed));
            Assert.AreEqual(typeof(ApplyBarrierStep), info.TerminationStepType);
        }

        [Test]
        public void ApplyBarrierStep_IgnoresBarrier_DamageTypeBypassesStep()
        {
            const long raw = 60, barrier = 40;
            var core = MakeCoreWithBarrierStub(barrier, out var stub);
            var info = MakeInfo(raw, StubDamageType.Create(ignoresBarrier: true), core);

            new ApplyBarrierStep().Process(info);

            Assert.AreEqual(raw, info.Amounts.Current);
            Assert.AreEqual(barrier, stub.BarrierValue);
        }

        [Test]
        public void ApplyBarrierStep_NoBarrierComponent_LeavesDataUnchanged()
        {
            const long raw = 100;
            var info = MakeInfo(raw, StubDamageType.Create(), MakeCoreOnly());

            new ApplyBarrierStep().Process(info);

            Assert.AreEqual(raw, info.Amounts.Current);
            Assert.AreEqual(DamagePreventionReason.None, info.Reasons);
        }

        // ── ApplyDefenseStep — null Performer (environmental damage) ──────────────

        [Test]
        public void ApplyDefenseStep_NullPerformer_DoesNotThrow()
        {
            var defStat = ScriptableObject.CreateInstance<StatSO>();
            var pierceStat = ScriptableObject.CreateInstance<StatSO>();
            var spyMit = ScriptableObject.CreateInstance<SpyMitigationFn>();
            var spyPen = ScriptableObject.CreateInstance<SpyPenetrationFn>();
            spyMit.Result = 70;
            spyPen.Result = 0;

            var dmgType = StubDamageType.Create(def: defStat, dmgFn: spyMit, pierce: pierceStat, defenseFn: spyPen);
            var info = MakeInfo(100, dmgType, MakeCoreOnly()); // no performer

            Assert.DoesNotThrow(() => new ApplyDefenseStep().Process(info));
        }

        [Test]
        public void ApplyDefenseStep_NullPerformer_PiercingTreatedAsZero()
        {
            const long raw = 100;
            const long expectedResult = 65;

            var defStat = ScriptableObject.CreateInstance<StatSO>();
            var pierceStat = ScriptableObject.CreateInstance<StatSO>();
            var spyMit = ScriptableObject.CreateInstance<SpyMitigationFn>();
            spyMit.Result = expectedResult;
            var spyPen = ScriptableObject.CreateInstance<SpyPenetrationFn>();
            spyPen.Result = 0;

            var dmgType = StubDamageType.Create(def: defStat, dmgFn: spyMit, pierce: pierceStat, defenseFn: spyPen);
            var info = MakeInfo(raw, dmgType, MakeCoreOnly()); // performer = null

            new ApplyDefenseStep().Process(info);

            Assert.AreEqual(0, spyPen.CapturedPiercingValue, "Null performer must yield piercingStatValue = 0");
            Assert.AreEqual(expectedResult, info.Amounts.Current);
        }

        // ── Stats-reading steps — null target stats path (GetElseZero safe) ─────────

        [Test]
        public void ApplyFlatDmgModifiersStep_TargetWithNoStatsComponent_NoModification()
        {
            const long raw = 100;
            var genericStat = ScriptableObject.CreateInstance<StatSO>();
            var config = new MockAstraHealthConfig { GenericFlatDamageModificationStat = genericStat };

            // EntityCore with no EntityStats component: GetElseZero must return 0
            var info = MakeInfo(raw, StubDamageType.Create(), MakeCoreOnly(), config: config);

            new ApplyFlatDmgModifiersStep().Process(info);

            Assert.AreEqual(raw, info.Amounts.Current);
        }

        [Test]
        public void ApplyPercentageDmgModifiersStep_TargetWithNoStatsComponent_NoModification()
        {
            const long raw = 100;
            var genericStat = ScriptableObject.CreateInstance<StatSO>();
            var config = new MockAstraHealthConfig { GenericPercentageDamageModificationStat = genericStat };

            var info = MakeInfo(raw, StubDamageType.Create(), MakeCoreOnly(), config: config);

            new ApplyPercentageDmgModifiersStep().Process(info);

            Assert.AreEqual(raw, info.Amounts.Current);
            Assert.AreEqual(DamagePreventionReason.None, info.Reasons);
        }
    }
}
