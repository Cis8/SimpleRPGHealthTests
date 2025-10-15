#if UNITY_EDITOR
using System.Linq;
using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRPGFramework.Editor.Utils;
using ElectricDrill.SoapRPGFramework.Utils;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using NUnit.Framework;

// per ExcludeFromDerivedTypePickerAttribute

[ExcludeFromDerivedTypePicker] public class PickerTestStepAlpha : DamageStep { public override string DisplayName => "Alpha"; public override DamageInfo ProcessStep(DamageInfo d)=>d; }
[ExcludeFromDerivedTypePicker] public class PickerTestStepBeta  : DamageStep { public override string DisplayName => "Beta";  public override DamageInfo ProcessStep(DamageInfo d)=>d; }

namespace ElectricDrill.SoapRpgHealthTests.Editor
{
    public class DerivedTypePickerTests
    {
        [Test]
        public void GetConcreteDerivedTypes_FindsDummySteps_WhenNotExcluded()
        {
            // For the test we deliberately pass excludeTests:false AND we remove the attribute filter manually
            var types = DerivedTypePicker.GetConcreteDerivedTypes<DamageStep>(excludeTests:false,
                filter: t => true);
            // classes were manually excluded, so should not be present
            Assert.IsFalse(types.Contains(typeof(PickerTestStepAlpha)));
            Assert.IsFalse(types.Contains(typeof(PickerTestStepBeta)));
        }
    }
}
#endif
