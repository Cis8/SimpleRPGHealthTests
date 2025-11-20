using System.Collections;
using System.Reflection;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Attributes;
using ElectricDrill.AstraRpgFramework.Events;
using ElectricDrill.AstraRpgFramework.Scaling.ScalingComponents;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static ElectricDrill.AstraRpgHealthTests.Tests.PlayMode.TestHealthFactory;

public class EntityHealthMaxHpScalingTests
{
    private HealthEntityBundle _entity;
    private Stat _testStat;
    private Attribute _testAttribute;
    private StatChangedGameEvent _statChangedEvt;
    private AttributeChangedGameEvent _attributeChangedEvt;

    // Dynamic stat scaling component
    private class DynamicStatScaling : StatsScalingComponent
    {
        public Stat Target;
        public int Mult;
        public static DynamicStatScaling Create(StatSet set, Stat target, int mult) {
            var inst = CreateInstance<DynamicStatScaling>();
            typeof(StatsScalingComponent)
                .GetField("_set", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(inst, set);
            inst.Target = target;
            inst.Mult = mult;
            return inst;
        }
        public override long CalculateValue(EntityCore entityCore) => entityCore.Stats.Get(Target) * Mult;
    }

    // Fixed attribute scaling component
    private class FixedAttributeScaling : AttributesScalingComponent
    {
        public Attribute Target;
        public long Amount;
        public static FixedAttributeScaling Create(AttributeSet set, Attribute target, long amount) {
            var inst = CreateInstance<FixedAttributeScaling>();
            typeof(AttributesScalingComponent)
                .GetField("_set", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(inst, set);
            inst.Set._attributes.Add(target);
            inst.Target = target;
            inst.Amount = amount;
            return inst;
        }
        public override long CalculateValue(EntityCore entityCore) => Amount;
        public override double Get(Attribute attribute) => attribute == Target ? Amount : 0d;
    }

    [SetUp]
    public void SetUp()
    {
        _testStat = ScriptableObject.CreateInstance<Stat>();
        _testStat.name = "TestScalingStat";
        _testAttribute = ScriptableObject.CreateInstance<Attribute>();
        _testAttribute.name = "TestScalingAttribute";

        _statChangedEvt = ScriptableObject.CreateInstance<StatChangedGameEvent>();
        _attributeChangedEvt = ScriptableObject.CreateInstance<AttributeChangedGameEvent>();

        _entity = CreateEntity(
            "ScalingEntity",
            maxHp: 100,
            initializeStats: true,
            healthMutator: h =>
            {
                // Inject stat change event already set by factory -> overwrite with our reference for raising
                h.EntityStats.OnStatChanged = _statChangedEvt;

                // Reuse the existing EntityAttributes created by the factory (avoid adding a duplicate)
                var attrs = h.EntityAttributes;
                attrs.enabled = true;
                attrs.OnAttributeChanged = _attributeChangedEvt;

                // Create attribute set containing test attribute (reflection to internal field)
                var attrSet = ScriptableObject.CreateInstance<AttributeSet>();
                typeof(AttributeSet)
                    .GetField("_attributes", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(attrSet, new SerializableHashSet<Attribute> { _testAttribute });

                // Assign attribute set (try property then field)
                var attrType = typeof(EntityAttributes);
                var prop = attrType.GetProperty("AttributeSet", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite) prop.SetValue(attrs, attrSet);
                else attrType.GetField("_attributeSet", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(attrs, attrSet);
            }
        );

        // Ensure test stat present + base value 5
        InjectFlatStat(_entity.Stats, _testStat, 5);
        Assert.AreEqual(100, _entity.Health.MaxHp);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_entity.Go);
        Object.DestroyImmediate(_testStat);
        Object.DestroyImmediate(_testAttribute);
        Object.DestroyImmediate(_statChangedEvt);
        Object.DestroyImmediate(_attributeChangedEvt);
        Object.DestroyImmediate(_entity.DefaultDamageType);
        Object.DestroyImmediate(_entity.DefaultDamageSource);
        Object.DestroyImmediate(_entity.Config);
    }

    [UnityTest]
    public IEnumerator AddMaxHpScaling_IncreasesMaxHp()
    {
        yield return null;
        var scaling = DynamicStatScaling.Create(_entity.Stats.StatSet, _testStat, 2); // stat=5 -> +10
        _entity.Health.AddMaxHpScaling(scaling);
        Assert.AreEqual(110, _entity.Health.MaxHp);
    }

    [UnityTest]
    public IEnumerator RemoveMaxHpScaling_RestoresMaxHp()
    {
        yield return null;
        var scaling = DynamicStatScaling.Create(_entity.Stats.StatSet, _testStat, 3); // +15
        _entity.Health.AddMaxHpScaling(scaling);
        Assert.AreEqual(115, _entity.Health.MaxHp);
        _entity.Health.RemoveMaxHpScaling(scaling);
        Assert.AreEqual(100, _entity.Health.MaxHp);
    }

    [UnityTest]
    public IEnumerator ClearMaxHpScalings_RemovesAll()
    {
        yield return null;
        var s1 = DynamicStatScaling.Create(_entity.Stats.StatSet, _testStat, 2); // +10
        var s2 = DynamicStatScaling.Create(_entity.Stats.StatSet, _testStat, 1); // +5
        _entity.Health.AddMaxHpScaling(s1);
        _entity.Health.AddMaxHpScaling(s2);
        Assert.AreEqual(115, _entity.Health.MaxHp);
        _entity.Health.ClearMaxHpScalings();
        Assert.AreEqual(100, _entity.Health.MaxHp);
    }

    [UnityTest]
    public IEnumerator StatChangeEvent_Recalculates_WhenScalingPresent()
    {
        yield return null;
        var scaling = DynamicStatScaling.Create(_entity.Stats.StatSet, _testStat, 2); // 5 -> +10
        _entity.Health.AddMaxHpScaling(scaling);
        Assert.AreEqual(110, _entity.Health.MaxHp);

        // Change stat 5 -> 8
        InjectFlatStat(_entity.Stats, _testStat, 8);
        _statChangedEvt.Raise(new StatChangeInfo(_entity.Stats, _testStat, 5, 8));
        Assert.AreEqual(116, _entity.Health.MaxHp); // 100 + (8*2)=16
    }

    [UnityTest]
    public IEnumerator StatChangeEvent_Ignored_AfterLastScalingRemoved()
    {
        yield return null;
        var scaling = DynamicStatScaling.Create(_entity.Stats.StatSet, _testStat, 4); // +20
        _entity.Health.AddMaxHpScaling(scaling);
        Assert.AreEqual(120, _entity.Health.MaxHp);

        _entity.Health.RemoveMaxHpScaling(scaling);
        Assert.AreEqual(100, _entity.Health.MaxHp);

        // Change stat 5 -> 10, should not recalc (handler unsubscribed)
        InjectFlatStat(_entity.Stats, _testStat, 10);
        _statChangedEvt.Raise(new StatChangeInfo(_entity.Stats, _testStat, 5, 10));
        Assert.AreEqual(100, _entity.Health.MaxHp);
    }

    [UnityTest]
    public IEnumerator AttributeChangeEvent_Recalculates_WhenConfigured()
    {
        yield return null;
        // Create attribute set already on EntityAttributes, reuse it for scaling
        var attrSet = _entity.Health.EntityAttributes.AttributeSet;
        var attrScaling = FixedAttributeScaling.Create(attrSet, _testAttribute, 12); // +12
        _entity.Config.HealthAttributesScaling = attrScaling;

        // Recompute max HP with attribute scaling
        _entity.Health.SetupMaxHp();
        Assert.AreEqual(112, _entity.Health.MaxHp);

        // Change scaling amount and raise attribute change
        attrScaling.Amount = 25;
        _attributeChangedEvt.Raise(new AttributeChangeInfo(_entity.Attributes, _testAttribute, 0, 1));
        Assert.AreEqual(125, _entity.Health.MaxHp);
    }
}
