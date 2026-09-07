using System;
using System.Collections;
using ElectricDrill.AstraHealth.Damage;
using ElectricDrill.AstraHealth.Heal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static ElectricDrill.AstraRpgHealthTests.Tests.PlayMode.TestHealthFactory;

namespace ElectricDrill.AstraRpgHealthTests.Tests.PlayMode
{
    /// <summary>
    /// Managed-heap allocation benchmark for the damage / heal hot path. Measures bytes allocated
    /// per <see cref="ElectricDrill.AstraHealth.Core.EntityHealth.TakeDamage"/> and per
    /// <see cref="ElectricDrill.AstraHealth.Core.EntityHealth.Heal(PreHealContext)"/> instance.
    ///
    /// <para>
    /// Measurement: the garbage collector is disabled for the measured window
    /// (<see cref="GarbageCollector"/>) so the managed heap only grows, and the delta of
    /// <see cref="GC.GetTotalMemory(bool)"/> across N iterations divided by N is the bytes/instance.
    /// (<c>GC.GetAllocatedBytesForCurrentThread</c> is not implemented on every Unity Mono build, so it
    /// is not used here.)
    /// </para>
    /// <para>
    /// The ceilings are deliberately loose regression guards. The value is the logged
    /// "≈ N bytes / instance" figure: run before and after a hot-path change to read the delta, and to
    /// have a number for the docs. Editor-measured (Mono, development); IL2CPP / release figures differ.
    /// If heap measurement turns out to be unavailable, the test is <see cref="Assert.Ignore(string)"/>-d.
    /// </para>
    /// </summary>
    public class HotPathAllocationBenchmarkTests
    {
        private const int Warmup = 200;
        private const int Iterations = 10_000;

        // Loose regression ceilings (bytes per instance, editor/Mono). Bump deliberately, with a note,
        // if a feature genuinely adds hot-path allocation.
        private const long DamageBytesCeiling = 8_192;
        private const long HealBytesCeiling = 4_096;

        private HealthEntityBundle _attacker;
        private HealthEntityBundle _target;
        private HealSourceSO _healSource;

        // Sink so the JIT cannot treat the measured allocations as dead code.
        private object _sink;

        [SetUp]
        public void SetUp()
        {
            _attacker = CreateEntity("BenchAttacker", initializeStats: true, maxHp: 1_000_000_000);
            _target = CreateEntity("BenchTarget", sharedConfig: _attacker.Config,
                initializeStats: true, maxHp: 1_000_000_000);
            _healSource = ScriptableObject.CreateInstance<HealSourceSO>();
            _healSource.name = "BenchHealSource";
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_healSource);
            Object.DestroyImmediate(_attacker.Go);
            Object.DestroyImmediate(_attacker.DefaultDamageType);
            Object.DestroyImmediate(_attacker.DefaultDamageSource);
            Object.DestroyImmediate(_target.Go);
            Object.DestroyImmediate(_target.DefaultDamageType);
            Object.DestroyImmediate(_target.DefaultDamageSource);
            Object.DestroyImmediate(_attacker.Config);
        }

        /// <summary>
        /// Runs <paramref name="body"/> <paramref name="iterations"/> times with the GC disabled and
        /// returns the managed-heap growth per iteration, in bytes.
        /// </summary>
        private static double MeasureBytesPerIteration(int iterations, Action body)
        {
            var previousMode = GarbageCollector.GCMode;
            GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
            try
            {
                long before = GC.GetTotalMemory(false);
                for (int i = 0; i < iterations; i++)
                    body();
                long after = GC.GetTotalMemory(false);
                return (after - before) / (double)iterations;
            }
            finally
            {
                GarbageCollector.GCMode = previousMode;
            }
        }

        /// <summary>
        /// Confirms the heap-delta measurement actually registers a known allocation on this runtime.
        /// </summary>
        private bool HeapMeasurementWorks()
        {
            double perIteration = MeasureBytesPerIteration(1_000, () => _sink = new byte[1024]);
            // 1000 x ~1040 B with GC disabled must show clearly; allow slack for block-size rounding.
            return perIteration > 512.0;
        }

        [UnityTest]
        public IEnumerator Benchmark_TakeDamage_PerInstanceAllocation()
        {
            yield return null;

            if (!HeapMeasurementWorks())
                Assert.Ignore("Managed-heap allocation measurement is unavailable on this runtime.");

            for (int i = 0; i < Warmup; i++)
                _sink = _target.Health.TakeDamage(BuildPre(1, _attacker, _target));

            double perInstance = MeasureBytesPerIteration(Iterations,
                () => _sink = _target.Health.TakeDamage(BuildPre(1, _attacker, _target)));

            TestContext.WriteLine(
                $"[AstraHealth benchmark] TakeDamage ≈ {perInstance:F1} bytes / damage instance " +
                $"(includes the PreDamageContext built per call); {Iterations} iterations, editor/Mono.");

            Assert.Greater(perInstance, 0.0, "Expected TakeDamage to allocate something measurable.");
            Assert.Less(perInstance, DamageBytesCeiling,
                $"TakeDamage allocated ≈ {perInstance:F1} B/instance, above the {DamageBytesCeiling} B regression ceiling.");
        }

        [UnityTest]
        public IEnumerator Benchmark_Heal_PerInstanceAllocation()
        {
            yield return null;

            if (!HeapMeasurementWorks())
                Assert.Ignore("Managed-heap allocation measurement is unavailable on this runtime.");

            // Drop HP well below max so every Heal exercises the full path without capping.
            _target.Health.Hp = 1_000;

            for (int i = 0; i < Warmup; i++)
                _sink = _target.Health.Heal(PreHealContext.Create(1, _healSource, _target.Core, _attacker.Core));

            double perInstance = MeasureBytesPerIteration(Iterations,
                () => _sink = _target.Health.Heal(PreHealContext.Create(1, _healSource, _target.Core, _attacker.Core)));

            TestContext.WriteLine(
                $"[AstraHealth benchmark] Heal ≈ {perInstance:F1} bytes / heal instance " +
                $"(PreHealContext.Create, heal events on); {Iterations} iterations, editor/Mono.");

            Assert.Greater(perInstance, 0.0, "Expected Heal to allocate something measurable.");
            Assert.Less(perInstance, HealBytesCeiling,
                $"Heal allocated ≈ {perInstance:F1} B/instance, above the {HealBytesCeiling} B regression ceiling.");
        }

        [Test]
        public void Benchmark_PreHealContext_Create_DoesNotExceedBuilder()
        {
            if (!HeapMeasurementWorks())
                Assert.Ignore("Managed-heap allocation measurement is unavailable on this runtime.");

            for (int i = 0; i < Warmup; i++)
            {
                _sink = PreHealContext.Builder.WithAmount(1).WithSource(_healSource).WithTarget(_target.Core).WithPerformer(_attacker.Core).Build();
                _sink = PreHealContext.Create(1, _healSource, _target.Core, _attacker.Core);
            }

            double builderBytes = MeasureBytesPerIteration(Iterations,
                () => _sink = PreHealContext.Builder.WithAmount(1).WithSource(_healSource).WithTarget(_target.Core).WithPerformer(_attacker.Core).Build());
            double createBytes = MeasureBytesPerIteration(Iterations,
                () => _sink = PreHealContext.Create(1, _healSource, _target.Core, _attacker.Core));

            TestContext.WriteLine(
                $"[AstraHealth benchmark] PreHealContext: Builder ≈ {builderBytes:F1} B, Create ≈ {createBytes:F1} B per instance.");

            // Create builds the same context without the intermediate step-builder object, so it must
            // never allocate more than the fluent Builder. A small tolerance absorbs measurement noise.
            Assert.LessOrEqual(createBytes, builderBytes + 8.0,
                "PreHealContext.Create should not allocate more than the fluent Builder.");
        }
    }
}
