using System;
using System.Reflection;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Experience;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.SoapRpgHealth;
using ElectricDrill.SoapRpgHealth.Damage;
using ElectricDrill.SoapRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.SoapRpgHealth.Events;
using ElectricDrill.SoapRpgHealth.Heal;
using Moq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricDrill.SoapRpgHealthTests
{
    public class EntityHealthTests
    {
        private const long MAX_HP = 100;

        // Minimal mock ScriptableObjects
        public class MockDamageSource : DamageSource
        {
            public static MockDamageSource Create()
            {
                var s = CreateInstance<MockDamageSource>();
                s.name = "TestDmgSource";
                return s;
            }
        }
        
        public class MockHealSource : HealSource
        {
            public static MockHealSource Create()
            {
                var s = CreateInstance<MockHealSource>();
                s.name = "TestHealSource";
                return s;
            }
        }

        public class MockDamageType : DamageType
        {
            public static MockDamageType Create()
            {
                var t = CreateInstance<MockDamageType>();
                return t;
            }
        }

        // Test strategy (identity or custom transform)
        private class TestDamageCalculationStrategy : DamageCalculationStrategy
        {
            private Func<DamageInfo, DamageInfo> _fn;
            public static TestDamageCalculationStrategy Create(Func<DamageInfo, DamageInfo> fn)
            {
                var inst = CreateInstance<TestDamageCalculationStrategy>();
                inst._fn = fn;
                return inst;
            }
            public override DamageInfo CalculateDamage(DamageInfo data) => _fn?.Invoke(data) ?? data;
        }

        private GameObject _go;
        private EntityHealth _entityHealth;
        private Mock<EntityCore> _mockEntityCore;
        private Mock<EntityStats> _mockEntityStats;
        private Mock<EntityCore> _mockDealerCore;
        private Mock<EntityStats> _mockDealerStats;

        [SetUp]
        public void Setup()
        {
            _go = new GameObject("Entity");
            _mockEntityCore = new Mock<EntityCore>();
            _mockEntityStats = new Mock<EntityStats>();
            _mockEntityCore.Setup(c => c.Level).Returns(new EntityLevel());
            _mockEntityCore.Setup(c => c.Stats).Returns(_mockEntityStats.Object);
            _mockEntityStats.Setup(s => s.StatSet).Returns(ScriptableObject.CreateInstance<StatSet>());
            _mockEntityStats.Setup(s => s.Get(It.IsAny<Stat>())).Returns(0L);

            _mockDealerCore = new Mock<EntityCore>();
            _mockDealerStats = new Mock<EntityStats>();
            _mockDealerCore.Setup(c => c.Level).Returns(new EntityLevel());
            _mockDealerCore.Setup(c => c.Stats).Returns(_mockDealerStats.Object);
            _mockDealerStats.Setup(s => s.StatSet).Returns(ScriptableObject.CreateInstance<StatSet>());
            _mockDealerStats.Setup(s => s.Get(It.IsAny<Stat>())).Returns(0L);

            _go.AddComponent<EntityHealth>();
            _entityHealth = _go.GetComponent<EntityHealth>();
            _entityHealth._entityCore = _mockEntityCore.Object;
            _entityHealth._entityStats = _mockEntityStats.Object;

            _entityHealth._baseMaxHp = new LongRef { UseConstant = true, ConstantValue = MAX_HP };
            _entityHealth._totalMaxHp = new LongRef { UseConstant = true };
            _entityHealth._hp = new LongRef { UseConstant = true, ConstantValue = MAX_HP };
            _entityHealth._deathThreshold = LongVarFactory.CreateLongVar(0);
            _entityHealth._barrier = new LongRef { UseConstant = true, ConstantValue = 0 };
            _entityHealth.OverrideOnDeathStrategy = ScriptableObject.CreateInstance<DestroyImmediateOnDeathStrategy>();

            // Events
            SetPriv("_preDmgInfoEvent", ScriptableObject.CreateInstance<PreDmgGameEvent>());
            SetPriv("_damageResolutionEvent", ScriptableObject.CreateInstance<DamageResolutionGameEvent>());
            SetPriv("_entityDiedEvent", ScriptableObject.CreateInstance<EntityDiedGameEvent>());
            SetPriv("_maxHealthChangedEvent", ScriptableObject.CreateInstance<EntityMaxHealthChangedGameEvent>());
            SetPriv("_gainedHealthEvent", ScriptableObject.CreateInstance<EntityGainedHealthGameEvent>());
            SetPriv("_lostHealthEvent", ScriptableObject.CreateInstance<EntityLostHealthGameEvent>());
            SetPriv("_preHealEvent", ScriptableObject.CreateInstance<PreHealGameEvent>());
            SetPriv("_entityHealedEvent", ScriptableObject.CreateInstance<EntityHealedGameEvent>());

            _entityHealth.SetupMaxHp();

            var defaultStrategy = TestDamageCalculationStrategy.Create(info => info);
            _entityHealth._customDamageCalculationStrategy = defaultStrategy;
        }

        private void SetPriv(string field, object value) =>
            typeof(EntityHealth).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(_entityHealth, value);

        private DamageResolutionGameEvent GetResolutionEvent() =>
            (DamageResolutionGameEvent)typeof(EntityHealth)
                .GetField("_damageResolutionEvent", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_entityHealth);

        [TearDown]
        public void Teardown()
        {
            Object.DestroyImmediate(_go);
        }

        private PreDamageInfo BuildPre(long amount, bool ignore = false)
        {
            var pre = PreDamageInfo.Builder
                .WithAmount(amount)
                .WithType(MockDamageType.Create())
                .WithSource(MockDamageSource.Create())
                .WithTarget(_mockEntityCore.Object)
                .WithDealer(_mockDealerCore.Object)
                .Build();
            pre.Ignore = ignore;
            return pre;
        }

        [Test]
        public void TakeDamage_AppliesRawAmount()
        {
            const long DMG = 25;
            _entityHealth.TakeDamage(BuildPre(DMG));
            Assert.AreEqual(MAX_HP - DMG, _entityHealth.Hp);
        }

        [Test]
        public void TakeDamage_WhenImmune_PreventedWithEntityImmune()
        {
            _entityHealth.IsImmune = true;
            DamageResolution raised = null;
            GetResolutionEvent().OnEventRaised += r => raised = r;

            _entityHealth.TakeDamage(BuildPre(50));

            Assert.IsNotNull(raised);
            Assert.AreEqual(DamageOutcome.Prevented, raised.Outcome);
            Assert.IsTrue((raised.Reasons & DamagePreventionReason.EntityImmune) != 0);
            Assert.AreEqual(MAX_HP, _entityHealth.Hp);
        }

        [Test]
        public void TakeDamage_IgnoreFlag_PrePhaseIgnored()
        {
            DamageResolution res = null;
            GetResolutionEvent().OnEventRaised += r => res = r;

            _entityHealth.TakeDamage(BuildPre(40, ignore: true));

            Assert.IsNotNull(res);
            Assert.AreEqual(DamageOutcome.Prevented, res.Outcome);
            Assert.IsTrue((res.Reasons & DamagePreventionReason.PrePhaseIgnored) != 0);
            Assert.AreEqual(MAX_HP, _entityHealth.Hp);
        }

        [Test]
        public void TakeDamage_ZeroAmount_PrePhaseZero()
        {
            DamageResolution res = null;
            GetResolutionEvent().OnEventRaised += r => res = r;

            _entityHealth.TakeDamage(BuildPre(0));

            Assert.IsNotNull(res);
            Assert.AreEqual(DamageOutcome.Prevented, res.Outcome);
            Assert.IsTrue((res.Reasons & DamagePreventionReason.PrePhaseZeroAmount) != 0);
            Assert.AreEqual(MAX_HP, _entityHealth.Hp);
        }

        [Test]
        public void TakeDamage_OverrideStrategyChangesNet()
        {
            const long RAW = 80;
            const long NET = 10;
            var strat = TestDamageCalculationStrategy.Create(info =>
            {
                info.Amounts.Current = NET;
                return info;
            });
            SetPriv("_overrideDamageCalculationStrategy", strat);
            _entityHealth.TakeDamage(BuildPre(RAW));
            Assert.AreEqual(MAX_HP - NET, _entityHealth.Hp);
        }

        [Test]
        public void Heal_CappedAtMax()
        {
            _entityHealth.TakeDamage(BuildPre(30)); // HP 70
            _entityHealth.Heal(PreHealInfo.Builder
                .WithAmount(50)
                .WithSource(MockHealSource.Create())
                .WithHealer(_mockEntityCore.Object)
                .Build());
            Assert.AreEqual(MAX_HP, _entityHealth.Hp);
        }

        [Test]
        public void TotalMaxHp_WithFlatAndPercentage()
        {
            // Reconfigure
            _entityHealth._baseMaxHp = new LongRef { UseConstant = true, ConstantValue = 100 };
            _entityHealth._totalMaxHp = new LongRef { UseConstant = true };
            _entityHealth.SetupMaxHp();

            _entityHealth.AddMaxHpFlatModifier(50); // 150
            _entityHealth.AddMaxHpPercentageModifier(new Percentage(20)); // +30 = 180

            Assert.AreEqual(180, _entityHealth.MaxHp);
        }
    }
}
