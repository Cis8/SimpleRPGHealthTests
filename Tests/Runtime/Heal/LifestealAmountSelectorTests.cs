using System.Reflection;
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
            typeof(LifestealAmountSelector).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(selector, LifestealBasisMode.Initial);
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
            // configure for Step / DefStep / Pre
            SetPrivate(selector, "_mode", LifestealBasisMode.Step);
            selector.StepType = typeof(DefStep);
            SetPrivate(selector, "_stepPoint", StepValuePoint.Pre);
            Assert.AreEqual(100, selector.Evaluate(amounts));
            // Post
            SetPrivate(selector, "_stepPoint", StepValuePoint.Post);
            Assert.AreEqual(70, selector.Evaluate(amounts));
        }

        [Test]
        public void Evaluate_StepMode_MissingStep_FallbackToFinal()
        {
            var amounts = BuildAmounts();
            var selector = new LifestealAmountSelector();
            SetPrivate(selector, "_mode", LifestealBasisMode.Step);
            selector.StepType = typeof(UnusedStep);
            Assert.AreEqual(40, selector.Evaluate(amounts));
        }

        private class UnusedStep : DamageStep
        {
            public override string DisplayName => "Unused";
            public override DamageInfo ProcessStep(DamageInfo data) => data;
        }

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);
    }
}

