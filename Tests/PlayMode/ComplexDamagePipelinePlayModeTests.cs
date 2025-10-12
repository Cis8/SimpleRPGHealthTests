using System.Collections;
using System.Reflection;
using ElectricDrill.SoapRpgFramework.Stats;
using ElectricDrill.SoapRpgFramework.Utils;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRPGHealth;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.SoapRpgHealth.Heal;
using NUnit.Framework;
using Tests.PlayMode.Utils;
using UnityEngine;
using UnityEngine.TestTools;
using static Tests.PlayMode.Utils.TestHealthFactory;

public class ComplexDamagePipelinePlayModeTests
{
    private HealthEntityBundle _attacker;
    private HealthEntityBundle _target;

    private Stat _defensiveStat;
    private Stat _piercingStat;
    private Stat _lifestealStat;
    private Stat _genericModStat;

    private FlatDamageReductionFn _flatDmgFn;
    private PercentageDefenseReductionFn _percDefFn;

    private LifestealConfig _lifestealCfg;

    [SetUp]
    public void SetUp()
    {
        var sharedEvents = CreateSharedEvents();
        _attacker = CreateEntity("Attacker",
            initializeStats: true,
            sharedEvents: sharedEvents);

        _target = CreateEntity("Target",
            sharedConfig: _attacker.Config,
            initializeStats: true,
            barrierAmount: 30, // barrier to test barrier step
            sharedEvents: sharedEvents);

        // Stats
        _defensiveStat = ScriptableObject.CreateInstance<Stat>(); _defensiveStat.name = "DefenseStat";
        _piercingStat = ScriptableObject.CreateInstance<Stat>(); _piercingStat.name = "PiercingStat";
        _lifestealStat = ScriptableObject.CreateInstance<Stat>(); _lifestealStat.name = "LifestealStat";
        _genericModStat = ScriptableObject.CreateInstance<Stat>(); _genericModStat.name = "GenericModStat";

        // Inject stats
        InjectPercentageStat(_attacker.Stats, _lifestealStat, new Percentage(25)); // lifesteal 25%
        InjectPercentageStat(_attacker.Stats, _piercingStat, new Percentage(20));  // 20% pierce
        InjectFlatStat(_target.Stats, _defensiveStat, 25);                         // flat defensive value
        // For weakness/resistance generic modification assume 150% (weakness +50%)
        InjectPercentageStat(_target.Stats, _genericModStat, new Percentage(50));
        InjectPercentageStat(_attacker.Stats, _genericModStat, new Percentage(0)); // no mod for attacker

        // Reduction functions
        _flatDmgFn = ScriptableObject.CreateInstance<FlatDamageReductionFn>();
        _percDefFn = ScriptableObject.CreateInstance<PercentageDefenseReductionFn>();

        // Configure DamageType
        var dmgType = _attacker.DefaultDamageType;
        dmgType.ReducedBy = _defensiveStat;
        dmgType.DamageReductionFn = _flatDmgFn;
        dmgType.DefensiveStatPiercedBy = _piercingStat;
        dmgType.DefenseReductionFn = _percDefFn;

        // Assign generic modification stat to config (weakness/resistance step usage)
        _attacker.Config.GenericDamageModificationStat = _genericModStat;
        _target.Config.GenericDamageModificationStat = _genericModStat;

        // Create custom strategy (Critical -> Barrier -> Defense -> Weak/Res)
        var strategy = CreateCritBarrierDefenseWeaknessStrategy();
        // Give attacker a base strategy to prevent "No Damage Calculation Strategy" error
        _attacker.Health._customDamageCalculationStrategy = strategy;
        // Override target pipeline (defender uses chosen strategy)
        _target.Health._overrideDamageCalculationStrategy = strategy;

        // Lifesteal mapping + configure basis after critical step post (overwrite with Step/Post)
        var lifestealSource = ScriptableObject.CreateInstance<HealSource>();
        _lifestealCfg = AssignLifestealMapping(_attacker.Config, dmgType, _lifestealStat, lifestealSource);
        ConfigureLifestealBasisAfterCritical(_lifestealCfg, dmgType, _lifestealStat, lifestealSource);
        SoapRpgHealthConfigProvider.Instance.LifestealConfig = _lifestealCfg;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_attacker.Go);
        Object.DestroyImmediate(_target.Go);
        Object.DestroyImmediate(_attacker.DefaultDamageType);
        Object.DestroyImmediate(_attacker.DefaultDamageSource);
        Object.DestroyImmediate(_target.DefaultDamageType);
        Object.DestroyImmediate(_target.DefaultDamageSource);
        Object.DestroyImmediate(_attacker.Config);
        Object.DestroyImmediate(_defensiveStat);
        Object.DestroyImmediate(_piercingStat);
        Object.DestroyImmediate(_lifestealStat);
        Object.DestroyImmediate(_genericModStat);
        Object.DestroyImmediate(_flatDmgFn);
        Object.DestroyImmediate(_percDefFn);
        if (_lifestealCfg) Object.DestroyImmediate(_lifestealCfg);
    }

    [UnityTest]
    public IEnumerator CriticalBarrierDefenseWeakness_Lifesteal_AppliesInOrder()
    {
        yield return null;
        
        // Set attacker _hp to 50 to see lifesteal effect (direct set to avoid modifiers)
        var hpField = typeof(EntityHealth).GetField("_hp", BindingFlags.Instance | BindingFlags.NonPublic);
        var attackerHpRef = (LongRef)hpField.GetValue(_attacker.Health);
        attackerHpRef.Value = 50;
        Assert.AreEqual(50, _attacker.Health.Hp, "Attacker HP should be 50 after direct set.");
        
        // Sanity
        Assert.AreEqual(100, _target.Health.Hp);
        Assert.AreEqual(30, _target.Health.Barrier);

        // Base damage 40, critical multiplier 2 => 80 before barrier
        var pre = BuildPre(40, _attacker, _target, crit: true, critMult: 2d);
        DamageResolution damageDealt = _target.Health.TakeDamage(pre);
        
        // --- Step-by-step checks ---
        // Critical step checks: pre 40, post 80
        Assert.AreEqual(40, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyCriticalMultiplierStep)).Pre, "Damage before critical should be 40.");
        Assert.AreEqual(80, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyCriticalMultiplierStep)).Post, "Damage after critical should be 80.");

        // Barrier step checks: pre 80, post 50 (30 absorbed by barrier)
        Assert.AreEqual(80, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyBarrierStep)).Pre, "Damage before barrier should be 80.");
        Assert.AreEqual(50, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyBarrierStep)).Post, "Damage after barrier should be 50.");
        
        // Defense step checks: pre 50, post 30 (25 flat defense, 20% piercing => 20 defense applied)
        Assert.AreEqual(50, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyDefenseStep)).Pre, "Damage before defense should be 50.");
        Assert.AreEqual(30, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyDefenseStep)).Post, "Damage after defense should be 30.");
        
        // Weakness/Resistance step checks: pre 30, post 45 (50% generic mod)
        Assert.AreEqual(30, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyDmgModifiersStep)).Pre, "Damage before weakness/resistance should be 30.");
        Assert.AreEqual(45, damageDealt.FinalDamageInfo.Amounts.GetStepAmount(typeof(ApplyDmgModifiersStep)).Post, "Damage after weakness/resistance should be 45.");
        // --- End of step-by-step checks ---
        
        // Barrier must be consumed first (after crit) => barrier zero
        Assert.AreEqual(0, _target.Health.Barrier, "Barrier should be fully consumed after barrier step.");

        // Target HP should be 100 - 45 = 55
        Assert.AreEqual(55, _target.Health.Hp, "Target HP should be 55 after damage.");

        // Attacker lifesteal (25% of damage after crit step, i.e. 80 * 25% = 20)
        Assert.AreEqual(70, _attacker.Health.Hp, "Attacker HP should be 70 after lifesteal.");

        yield break;
    }
}
