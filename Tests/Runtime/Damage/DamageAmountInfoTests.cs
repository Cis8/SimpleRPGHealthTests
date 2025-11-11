using ElectricDrill.AstraRpgHealth.Damage;
using ElectricDrill.AstraRpgHealth.Damage.CalculationPipeline;
using NUnit.Framework;

namespace ElectricDrill.SoapRpgHealthTests.Runtime.Damage
{
    public class DamageAmountInfoTests
    {
        private class StepA : DamageStep
        {
            public override string DisplayName => "StepA";
            public override DamageInfo ProcessStep(DamageInfo data) => data;
        }

        private class StepB : DamageStep
        {
            public override string DisplayName => "StepB";
            public override DamageInfo ProcessStep(DamageInfo data) => data;
        }

        [Test]
        public void RecordStep_StoresTypePrePost()
        {
            var amounts = new DamageAmountInfo(100);
            amounts.RecordStep(typeof(StepA), 100, 80);
            Assert.AreEqual(1, amounts.Records.Count);
            var r = amounts.Records[0];
            Assert.AreEqual(typeof(StepA), r.StepType);
            Assert.AreEqual(100, r.Pre);
            Assert.AreEqual(80, r.Post);
        }

        [Test]
        public void StepSequence_PreMatchesPreviousPost()
        {
            var amounts = new DamageAmountInfo(150);
            amounts.RecordStep(typeof(StepA), 150, 120);
            amounts.RecordStep(typeof(StepB), 120, 60);
            Assert.AreEqual(amounts.Records[0].Post, amounts.Records[1].Pre);
        }
    }
}

