using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.SoapRpgHealthTests.DamagePipeline
{
    public class DamageStepProcessTests
    {
        private class MockDamageType : DamageType { public static MockDamageType Create() => CreateInstance<MockDamageType>(); }
        private class MockDamageSource : DamageSource { public static MockDamageSource Create() => CreateInstance<MockDamageSource>(); }

        private class ZeroDamageStep : DamageStep {
            public override string DisplayName => "Zero";
            public override DamageInfo ProcessStep(DamageInfo data) {
                data.Amounts.Current = 0;
                return data;
            }
        }
        private class AnotherZeroDamageStep : DamageStep {
            public override string DisplayName => "Zero2";
            public override DamageInfo ProcessStep(DamageInfo data) {
                data.Amounts.Current = 0;
                return data;
            }
        }
        private class RaiseDamageStep : DamageStep {
            private readonly long _value;
            public RaiseDamageStep(long value){ _value = value; }
            public override string DisplayName => "Raise";
            public override DamageInfo ProcessStep(DamageInfo data){
                data.Amounts.Current = _value;
                return data;
            }
        }
        private class PartialReduceStep : DamageStep {
            private readonly long _reduceTo;
            public PartialReduceStep(long reduceTo){ _reduceTo = reduceTo; }
            public override string DisplayName => "Partial";
            public override DamageInfo ProcessStep(DamageInfo data){
                data.Amounts.Current = _reduceTo;
                return data;
            }
        }

        private DamageInfo MakeInfo(long amount) {
            var goTarget = new GameObject("Target");
            var goDealer = new GameObject("Dealer");
            var targetCore = goTarget.AddComponent<EntityCore>();
            var dealerCore = goDealer.AddComponent<EntityCore>();

            var pre = PreDamageInfo.Builder
                .WithAmount(amount)
                .WithType(MockDamageType.Create())
                .WithSource(MockDamageSource.Create())
                .WithTarget(targetCore)
                .WithDealer(dealerCore)
                .Build();

            return new DamageInfo(pre);
        }

        [TearDown]
        public void Cleanup(){
            foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(g);
        }

        [Test]
        public void Process_SetsPipelineReducedToZero_WhenDamageGoesToZero() {
            var info = MakeInfo(10);
            Assert.AreEqual(DamagePreventionReason.None, info.Reasons);
            Assert.IsNull(info.TerminationStepType);

            var step = new ZeroDamageStep();
            step.Process(info);

            Assert.IsTrue((info.Reasons & DamagePreventionReason.PipelineReducedToZero) != 0);
            Assert.AreEqual(typeof(ZeroDamageStep), info.TerminationStepType);
        }

        [Test]
        public void Process_DoesNotOverrideTerminationStep_WhenAlreadyPrevented() {
            var info = MakeInfo(5);
            new ZeroDamageStep().Process(info);
            Assert.AreEqual(typeof(ZeroDamageStep), info.TerminationStepType);

            // Second zero step should early-return (IsPrevented true)
            new AnotherZeroDamageStep().Process(info);

            Assert.AreEqual(typeof(ZeroDamageStep), info.TerminationStepType);
        }

        [Test]
        public void Process_DoesNotSetPipelineReducedToZero_WhenDamageStaysAboveZero() {
            var info = MakeInfo(12);
            new PartialReduceStep(5).Process(info);

            Assert.AreEqual(DamagePreventionReason.None, info.Reasons);
            Assert.IsNull(info.TerminationStepType);
            Assert.IsFalse(info.IsPrevented);
        }
    }
}
