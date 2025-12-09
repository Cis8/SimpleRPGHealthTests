using ElectricDrill.AstraRpgHealth.Damage;
using ElectricDrill.AstraRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraRpgHealth.Heal;
using NUnit.Framework;

namespace ElectricDrill.AstraRpgHealthTests.Runtime.Heal
{
    public class LifestealAmountSelectorTests
    {
        private class DefStep : DamageStep
        {
            public override string DisplayName => "DefStep";
            public override DamageInfo ProcessStep(DamageInfo data) => data;
        }
        private class BarrierStep : DamageStep
        {
            public override string DisplayName => "BarrierStep";
            public override DamageInfo ProcessStep(DamageInfo data) => data;
        }

        private DamageAmountInfo BuildAmounts()
        {
            var a = new DamageAmountInfo(100);
            // Simulate defense reducing 100 -> 70
            a.RecordStep(typeof(DefStep), 100, 70);
            // Simulate barrier reducing 70 -> 40
            a.RecordStep(typeof(BarrierStep), 70, 40);
            a.Current = 40;
            return a;
        }

        [Test]
        public void Evaluate_InitialMode_ReturnsInitial()
        {
            var amounts = BuildAmounts();
            var selector = new LifestealAmountSelector();
            // use public property instead of reflection
            selector.Mode = LifestealBasisMode.Initial;
            Assert.AreEqual(100, selector.Evaluate(amounts));
        }

        [Test]
        public void Evaluate_FinalMode_ReturnsCurrent()
        {
            var amounts = BuildAmounts();
            var selector = new LifestealAmountSelector();
            // default mode is Final
            Assert.AreEqual(40, selector.Evaluate(amounts));
        }

        [Test]
        public void Evaluate_StepMode_PreAndPost()
        {
            var amounts = BuildAmounts();
            var selector = new LifestealAmountSelector();
            // configure for Step / DefStep / Pre using public properties
            selector.Mode = LifestealBasisMode.Step;
            selector.StepType = typeof(DefStep);
            selector.StepPoint = StepValuePoint.Pre;
            Assert.AreEqual(100, selector.Evaluate(amounts));
            // Post
            selector.StepPoint = StepValuePoint.Post;
            Assert.AreEqual(70, selector.Evaluate(amounts));
        }

        [Test]
        public void Evaluate_StepMode_MissingStep_FallbackToFinal()
        {
            var amounts = BuildAmounts();
            var selector = new LifestealAmountSelector();
            selector.Mode = LifestealBasisMode.Step;
            selector.StepType = typeof(UnusedStep);
            Assert.AreEqual(40, selector.Evaluate(amounts));
        }

        private class UnusedStep : DamageStep
        {
            public override string DisplayName => "Unused";
            public override DamageInfo ProcessStep(DamageInfo data) => data;
        }
    }
}
