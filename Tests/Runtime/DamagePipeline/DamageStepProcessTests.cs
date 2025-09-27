using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.SoapRpgHealthTests.DamagePipeline
{
    public class DamageStepProcessTests
    {
        // Minimal mocks
        private class MockDmgType : DmgType { public static MockDmgType Create() { return CreateInstance<MockDmgType>(); } }
        private class MockSource : Source { public static MockSource Create() { return CreateInstance<MockSource>(); } }

        // Steps used to manipulate NetAmount
        private class ZeroDamageStep : DamageStep {
            public override string DisplayName => "Zero";
            public override DamageInfo ProcessStep(DamageInfo data) {
                data.Amounts.NetAmount = 0;
                return data;
            }
        }
        private class AnotherZeroDamageStep : DamageStep {
            public override string DisplayName => "Zero2";
            public override DamageInfo ProcessStep(DamageInfo data) {
                data.Amounts.NetAmount = 0;
                return data;
            }
        }
        private class RaiseDamageStep : DamageStep {
            long _value;
            public RaiseDamageStep(long value) { _value = value; }
            public override string DisplayName => "Raise";
            public override DamageInfo ProcessStep(DamageInfo data) {
                data.Amounts.NetAmount = _value;
                return data;
            }
        }
        private class PartialReduceStep : DamageStep {
            long _reduceTo;
            public PartialReduceStep(long reduceTo) { _reduceTo = reduceTo; }
            public override string DisplayName => "Partial";
            public override DamageInfo ProcessStep(DamageInfo data) {
                data.Amounts.NetAmount = _reduceTo; // keep > 0
                return data;
            }
        }

        private DamageInfo MakeInfo(long amount) {
            var goTarget = new GameObject("Target");
            var goDealer = new GameObject("Dealer");
            var targetCore = goTarget.AddComponent<EntityCore>();
            var dealerCore = goDealer.AddComponent<EntityCore>();

            var pre = PreDmgInfo.Builder
                .WithAmount(amount)
                .WithType(MockDmgType.Create())
                .WithSource(MockSource.Create())
                .WithTarget(targetCore)
                .WithDealer(dealerCore)
                .Build();

            return new DamageInfo(pre);
        }

        [TearDown]
        public void Cleanup() {
            foreach (var g in GameObject.FindObjectsOfType<GameObject>())
                Object.DestroyImmediate(g);
        }

        [Test]
        public void Process_SetsReducedToZeroByStep_WhenDamageGoesToZero() {
            var info = MakeInfo(10);
            Assert.IsNull(info.ReducedToZeroByStep);

            var step = new ZeroDamageStep();
            step.Process(info);

            Assert.AreEqual(typeof(ZeroDamageStep), info.ReducedToZeroByStep);
        }

        [Test]
        public void Process_DoesNotOverrideReducedToZero_WhenRemainsZero_WithDifferentZeroingStep() {
            var info = MakeInfo(5);
            var first = new ZeroDamageStep();
            first.Process(info);
            Assert.AreEqual(typeof(ZeroDamageStep), info.ReducedToZeroByStep);

            var second = new AnotherZeroDamageStep();
            second.Process(info); // Should NOT change ReducedToZeroByStep

            Assert.AreEqual(typeof(ZeroDamageStep), info.ReducedToZeroByStep, "Second zeroing step should not overwrite original step type.");
        }

        [Test]
        public void Process_ClearsReducedToZeroStep_WhenDamageReturnsAboveZero() {
            var info = MakeInfo(8);
            var zero = new ZeroDamageStep();
            zero.Process(info);
            Assert.AreEqual(typeof(ZeroDamageStep), info.ReducedToZeroByStep);

            var raise = new RaiseDamageStep(3);
            raise.Process(info);

            Assert.IsNull(info.ReducedToZeroByStep);
        }

        [Test]
        public void Process_DoesNotSetReducedToZero_WhenDamageStaysAboveZero() {
            var info = MakeInfo(12);
            var partial = new PartialReduceStep(5);
            partial.Process(info);
            Assert.IsNull(info.ReducedToZeroByStep);
        }
    }
}
