using System.Linq;
using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using NUnit.Framework;
using UnityEngine;

namespace ElectricDrill.SoapRpgHealthTests.DamagePipeline
{
    public class ApplyCriticalMultiplierStepTests
    {
        private class MockDmgType : DmgType
        {
            public static MockDmgType Create() {
                var t = CreateInstance<MockDmgType>();
                return t;
            }
        }

        private class MockDmgSource : DmgSource
        {
            public static MockDmgSource Create() {
                var s = CreateInstance<MockDmgSource>();
                s.name = "CritSource";
                return s;
            }
        }

        private (EntityCore target, EntityCore dealer) MakeEntities()
        {
            var targetGo = new GameObject("TargetCrit");
            var dealerGo = new GameObject("DealerCrit");
            var targetCore = targetGo.AddComponent<EntityCore>();
            var dealerCore = dealerGo.AddComponent<EntityCore>();
            return (targetCore, dealerCore);
        }

        private DamageInfo MakeDamageInfo(long raw, bool crit, double mult, EntityCore target, EntityCore dealer)
        {
            var pre = PreDmgInfo.Builder
                .WithAmount(raw)
                .WithType(MockDmgType.Create())
                .WithSource(MockDmgSource.Create())
                .WithTarget(target)
                .WithDealer(dealer)
                .WithIsCritical(crit)
                .WithCriticalMultiplier(mult)
                .Build();
            return new DamageInfo(pre);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(go);
        }

        [Test]
        public void ApplyCriticalMultiplierStep_Applies_WhenCritical()
        {
            const long RAW = 100;
            const double MULT = 2.5;
            const long EXPECTED = 250;

            var (target, dealer) = MakeEntities();
            var info = MakeDamageInfo(RAW, true, MULT, target, dealer);

            var step = new ApplyCriticalMultiplierStep();
            var processed = step.Process(info);

            Assert.AreEqual(EXPECTED, processed.Amounts.Current);
            var rec = processed.Amounts.Records.Last();
            Assert.AreEqual(RAW, rec.Pre);
            Assert.AreEqual(EXPECTED, rec.Post);
        }

        [Test]
        public void ApplyCriticalMultiplierStep_DoesNotApply_WhenNotCritical()
        {
            const long RAW = 130;
            const double MULT = 3.0;

            var (target, dealer) = MakeEntities();
            var info = MakeDamageInfo(RAW, false, MULT, target, dealer);

            var step = new ApplyCriticalMultiplierStep();
            var processed = step.Process(info);

            Assert.AreEqual(RAW, processed.Amounts.Current);
            var rec = processed.Amounts.Records.Last(); // still recorded (no change)
            Assert.AreEqual(RAW, rec.Pre);
            Assert.AreEqual(RAW, rec.Post);
        }

        [Test]
        public void ApplyCriticalMultiplierStep_Ignores_InvalidMultiplier()
        {
            const long RAW = 90;
            const double MULT = 0; // invalid -> ignored

            var (target, dealer) = MakeEntities();
            var info = MakeDamageInfo(RAW, true, MULT, target, dealer);

            var step = new ApplyCriticalMultiplierStep();
            var processed = step.Process(info);

            Assert.AreEqual(RAW, processed.Amounts.Current);
        }
    }
}
