using System.Collections;
using ElectricDrill.SoapRpgFramework.Stats;
using ElectricDrill.SoapRpgFramework.Utils;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRpgHealth.Config;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.SoapRpgHealth.DamageReductionFunctions.DamageReductionFunctions;
using ElectricDrill.SoapRpgHealth.DefenseReductionFunctions.DefenseReductionFunctions;
using ElectricDrill.SoapRpgHealth.Heal;
using NUnit.Framework;
using Tests.PlayMode.Utils;
using UnityEngine;
using UnityEngine.TestTools;
using static Tests.PlayMode.Utils.TestHealthFactory;

public class AdditionalDamageEdgeCasesPlayModeTests
{
    private HealthEntityBundle _atk;
    private HealthEntityBundle _tgt;
    private DamageCalculationStrategy _strategy;
    private Tests.PlayMode.Utils.TestHealthFactory.HealthEventsBundle _sharedEvents;
    private readonly System.Collections.Generic.List<Object> _temp = new();

    private void Reg(Object o) { if (o) _temp.Add(o); }

    [SetUp]
    public void SetUp()
    {
        _sharedEvents = CreateSharedEvents();
        _atk = CreateEntity("Atk",
            initializeStats: true,
            sharedEvents: _sharedEvents);

        _tgt = CreateEntity("Tgt",
            sharedConfig: _atk.Config,
            initializeStats: true,
            sharedEvents: _sharedEvents);

        _strategy = CreateCritBarrierDefenseWeaknessStrategy();
        _atk.Health._customDamageCalculationStrategy = _strategy;
        _tgt.Health._overrideDamageCalculationStrategy = _strategy;

        // Track for destruction
        Reg(_atk.Go); Reg(_tgt.Go);
        Reg(_atk.Config);
        Reg(_atk.DefaultDamageType); Reg(_atk.DefaultDamageSource);
        Reg(_tgt.DefaultDamageType); Reg(_tgt.DefaultDamageSource);
        Reg(_strategy);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var o in _temp)
            Object.DestroyImmediate(o);
        _temp.Clear();
    }

    [UnityTest]
    public IEnumerator Barrier_Fully_Absorbs_NoHpDamage()
    {
        _tgt.Health._barrier.Value = 40;

        var pre = BuildPre(40, _atk, _tgt);
        _tgt.Health.TakeDamage(pre);

        Assert.AreEqual(0, _tgt.Health.Barrier);
        Assert.AreEqual(100, _tgt.Health.Hp, "HP unchanged because fully absorbed by barrier.");
        yield break;
    }

    [UnityTest]
    public IEnumerator Barrier_Partial_Remaining()
    {
        _tgt.Health._barrier.Value = 50;

        var pre = BuildPre(30, _atk, _tgt);
        _tgt.Health.TakeDamage(pre);

        Assert.AreEqual(20, _tgt.Health.Barrier, "Remaining barrier 20.");
        Assert.AreEqual(100, _tgt.Health.Hp, "Damage does not pass barrier.");
        yield break;
    }

    [UnityTest]
    public IEnumerator Defense_Fully_Nulls_Damage()
    {
        var defStat = ScriptableObject.CreateInstance<Stat>(); defStat.name = "BigDef"; Reg(defStat);
        InjectFlatStat(_tgt.Stats, defStat, 999);

        var dmgType = _atk.DefaultDamageType;
        dmgType.ReducedBy = defStat;
        dmgType.DamageReductionFn = ScriptableObject.CreateInstance<FlatDamageReductionFn>(); Reg(dmgType.DamageReductionFn);

        var pre = BuildPre(80, _atk, _tgt);
        _tgt.Health.TakeDamage(pre);

        Assert.AreEqual(100, _tgt.Health.Hp, "Damage reduced to 0.");
        yield break;
    }

    [UnityTest]
    public IEnumerator Piercing_100_Ignores_Defense()
    {
        var defStat = ScriptableObject.CreateInstance<Stat>(); defStat.name = "Def"; Reg(defStat);
        var pierceStat = ScriptableObject.CreateInstance<Stat>(); pierceStat.name = "Pierce"; Reg(pierceStat);

        InjectFlatStat(_tgt.Stats, defStat, 50);
        InjectPercentageStat(_atk.Stats, pierceStat, new Percentage(100));

        var dmgType = _atk.DefaultDamageType;
        dmgType.ReducedBy = defStat;
        dmgType.DamageReductionFn = ScriptableObject.CreateInstance<FlatDamageReductionFn>(); Reg(dmgType.DamageReductionFn);
        dmgType.DefensiveStatPiercedBy = pierceStat;
        dmgType.DefenseReductionFn = ScriptableObject.CreateInstance<PercentageDefenseReductionFn>(); Reg(dmgType.DefenseReductionFn);

        var pre = BuildPre(40, _atk, _tgt);
        var res = _tgt.Health.TakeDamage(pre);

        var defStep = res.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyDefenseStep));
        Assert.AreEqual(defStep.Pre, defStep.Post, "Full piercing: defense ignored.");
        Assert.AreEqual(60, _tgt.Health.Hp, "Final damage 40 (no reduction).");
        yield break;
    }
    
    [UnityTest]
    public IEnumerator Piercing_200_Ignores_Defense()
    {
        var defStat = ScriptableObject.CreateInstance<Stat>(); defStat.name = "Def"; Reg(defStat);
        var pierceStat = ScriptableObject.CreateInstance<Stat>(); pierceStat.name = "Pierce"; Reg(pierceStat);

        InjectFlatStat(_tgt.Stats, defStat, 50);
        InjectPercentageStat(_atk.Stats, pierceStat, new Percentage(200));

        var dmgType = _atk.DefaultDamageType;
        dmgType.ReducedBy = defStat;
        dmgType.DamageReductionFn = ScriptableObject.CreateInstance<FlatDamageReductionFn>(); Reg(dmgType.DamageReductionFn);
        dmgType.DefensiveStatPiercedBy = pierceStat;
        dmgType.DefenseReductionFn = ScriptableObject.CreateInstance<PercentageDefenseReductionFn>(); Reg(dmgType.DefenseReductionFn);

        var pre = BuildPre(40, _atk, _tgt);
        var res = _tgt.Health.TakeDamage(pre);

        var defStep = res.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyDefenseStep));
        Assert.AreEqual(defStep.Pre, defStep.Post, "Full piercing: defense ignored.");
        Assert.AreEqual(60, _tgt.Health.Hp, "Final damage 40 (no reduction).");
        yield break;
    }

    [UnityTest]
    public IEnumerator Lifesteal_Does_Not_Exceed_MaxHp()
    {
        var lsStat = ScriptableObject.CreateInstance<Stat>(); lsStat.name = "LS"; Reg(lsStat);
        InjectPercentageStat(_atk.Stats, lsStat, new Percentage(50));

        var healSource = ScriptableObject.CreateInstance<HealSource>(); Reg(healSource);
        var cfg = AssignLifestealMapping(_atk.Config, _atk.DefaultDamageType, lsStat, healSource); Reg(cfg);
        ConfigureLifestealBasisAfterCritical(cfg, _atk.DefaultDamageType, lsStat, healSource);
        SoapRpgHealthConfigProvider.Instance.LifestealConfig = cfg;

        _atk.Health._hp.Value = 95L;

        var pre = BuildPre(20, _atk, _tgt, crit: true, critMult: 2);
        _tgt.Health.TakeDamage(pre);

        Assert.AreEqual(100, _atk.Health.Hp, "Overheal prevented.");
        yield break;
    }

    [UnityTest]
    public IEnumerator Ignored_Damage_No_Effect()
    {
        _tgt.Health._barrier.Value = 10;

        var pre = BuildPre(999, _atk, _tgt, ignore: true);
        _tgt.Health.TakeDamage(pre);

        Assert.AreEqual(100, _tgt.Health.Hp);
        Assert.AreEqual(10, _tgt.Health.Barrier);
        yield break;
    }

    [UnityTest]
    public IEnumerator Overkill_Clamped_To_Zero_No_Negatives()
    {
        _tgt.Health.HealthCanBeNegative = false;
        _tgt.Health._deathThreshold.Value = 0;

        var pre = BuildPre(500, _atk, _tgt);
        _tgt.Health.TakeDamage(pre);

        Assert.AreEqual(0, _tgt.Health.Hp);
        yield break;
    }

    [UnityTest]
    public IEnumerator Overkill_With_Negative_HP_Allowed()
    {
        _tgt.Health.HealthCanBeNegative = true;
        _tgt.Health._deathThreshold.Value = -9999;

        var pre = BuildPre(500, _atk, _tgt);
        _tgt.Health.TakeDamage(pre);

        Assert.Less(_tgt.Health.Hp, 0, "HP can go below zero.");
        Assert.AreEqual(-400, _tgt.Health.Hp);
        yield break;
    }
}

// Utility extension for concise cast
internal static class ObjectExtensions
{
    public static T As<T>(this object o) => (T)o;
}
