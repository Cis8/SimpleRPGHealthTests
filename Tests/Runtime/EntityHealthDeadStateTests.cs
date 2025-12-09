using System;
using ElectricDrill.AstraRpgFramework;
using ElectricDrill.AstraRpgFramework.Experience;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraRpgHealth;
using ElectricDrill.AstraRpgHealth.Config;
using ElectricDrill.AstraRpgHealth.Damage;
using ElectricDrill.AstraRpgHealth.Damage.CalculationPipeline;
using ElectricDrill.AstraRpgHealth.Death;
using ElectricDrill.AstraRpgHealth.Events;
using ElectricDrill.AstraRpgHealth.Exceptions;
using ElectricDrill.AstraRpgHealth.Heal;
using Moq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ElectricDrill.AstraRpgHealthTests.Tests.Runtime
{
    public class EntityHealthDeadStateTests
    {
        private const long MaxHp = 100;

        private class MockDamageSource : DamageSource
        {
            public static MockDamageSource Create()
            {
                var s = CreateInstance<MockDamageSource>();
                s.name = "TestDmgSource";
                return s;
            }
        }
        
        private class MockHealSource : HealSource
        {
            public static MockHealSource Create()
            {
                var s = CreateInstance<MockHealSource>();
                s.name = "TestHealSource";
                return s;
            }
        }

        private class MockDamageType : DamageType
        {
            public static MockDamageType Create()
            {
                var t = CreateInstance<MockDamageType>();
                return t;
            }
        }

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
        private MockDamageSource _damageSource;
        private MockDamageType _damageType;
        private MockHealSource _healSource;

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

            _entityHealth._baseMaxHp = new LongRef { UseConstant = true, ConstantValue = MaxHp };
            _entityHealth._totalMaxHp = new LongRef { UseConstant = true, ConstantValue = MaxHp };
            _entityHealth._hp = new LongRef { UseConstant = true, ConstantValue = MaxHp };
            _entityHealth._deathThreshold = LongVarFactory.CreateLongVar(0);
            _entityHealth._barrier = new LongRef { UseConstant = true };
            _entityHealth.OverrideOnDeathStrategy = ScriptableObject.CreateInstance<DoNothingOnDeathStrategy>();

            _damageSource = MockDamageSource.Create();
            _damageType = MockDamageType.Create();
            _healSource = MockHealSource.Create();

            // Create minimal config
            var config = ScriptableObject.CreateInstance<AstraRpgHealthConfig>();
            config.DefaultDamageCalculationCalculationStrategy = TestDamageCalculationStrategy.Create(d => d);
            AstraRpgHealthConfigProvider.Instance = config;

            // Create events
            var preDmgEvent = ScriptableObject.CreateInstance<PreDmgGameEvent>();
            var dmgResolutionEvent = ScriptableObject.CreateInstance<DamageResolutionGameEvent>();
            var maxHealthChangedEvent = ScriptableObject.CreateInstance<EntityMaxHealthChangedGameEvent>();
            var gainedHealthEvent = ScriptableObject.CreateInstance<EntityGainedHealthGameEvent>();
            var lostHealthEvent = ScriptableObject.CreateInstance<EntityLostHealthGameEvent>();
            var diedEvent = ScriptableObject.CreateInstance<EntityDiedGameEvent>();
            var preHealEvent = ScriptableObject.CreateInstance<PreHealGameEvent>();
            var healedEvent = ScriptableObject.CreateInstance<EntityHealedGameEvent>();
            var resurrectEvent = ScriptableObject.CreateInstance<EntityResurrectedGameEvent>();

            // Use reflection to set private event fields
            var healthType = typeof(EntityHealth);
            healthType.GetField("_preDmgInfoEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, preDmgEvent);
            healthType.GetField("_damageResolutionEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, dmgResolutionEvent);
            healthType.GetField("_maxHealthChangedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, maxHealthChangedEvent);
            healthType.GetField("_gainedHealthEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, gainedHealthEvent);
            healthType.GetField("_lostHealthEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, lostHealthEvent);
            healthType.GetField("_entityDiedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, diedEvent);
            healthType.GetField("_preHealEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, preHealEvent);
            healthType.GetField("_entityHealedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, healedEvent);
            healthType.GetField("_entityResurrectedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_entityHealth, resurrectEvent);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_damageSource);
            Object.DestroyImmediate(_damageType);
            Object.DestroyImmediate(_healSource);
        }

        private PreDamageInfo CreateDamageInfo(long amount)
        {
            return PreDamageInfo.Builder
                .WithAmount(amount)
                .WithType(_damageType)
                .WithSource(_damageSource)
                .WithTarget(_mockEntityCore.Object)
                .WithDealer(_mockDealerCore.Object)
                .Build();
        }

        private PreHealInfo CreateHealInfo(long amount)
        {
            return PreHealInfo.Builder
                .WithAmount(amount)
                .WithSource(_healSource)
                .WithHealer(_mockEntityCore.Object)
                .Build();
        }

        [Test]
        public void TestHealingDeadEntityThrowsException()
        {
            // Kill the entity
            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            Assert.IsTrue(_entityHealth.IsDead());

            // Try to heal dead entity
            var ex = Assert.Throws<DeadEntityException>(() => 
                _entityHealth.Heal(CreateHealInfo(50))
            );
            
            Assert.IsNotNull(ex);
            Assert.AreEqual("Heal", ex.AttemptedOperation);
            Assert.AreEqual(0, ex.CurrentHp);
            Assert.AreEqual(_go.name, ex.EntityName);
        }

        [Test]
        public void TestSetHpToMaxOnDeadEntityThrowsException()
        {
            // Kill the entity
            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            Assert.IsTrue(_entityHealth.IsDead());

            // Try to set HP to max on dead entity
            var ex = Assert.Throws<DeadEntityException>(() => 
                _entityHealth.SetHpToMax()
            );
            
            Assert.IsNotNull(ex);
            Assert.AreEqual("SetHpToMax", ex.AttemptedOperation);
        }

        [Test]
        public void TestTakeDamageOnDeadEntityIsPrevent()
        {
            // Kill the entity
            var firstDamage = _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            Assert.IsTrue(_entityHealth.IsDead());
            Assert.AreEqual(DamageOutcome.Applied, firstDamage.Outcome);

            // Try to damage dead entity
            var secondDamage = _entityHealth.TakeDamage(CreateDamageInfo(50));
            
            Assert.AreEqual(DamageOutcome.Prevented, secondDamage.Outcome);
            Assert.IsTrue((secondDamage.Reasons & DamagePreventionReason.EntityDead) != 0);
            Assert.AreEqual(0, _entityHealth.Hp);
        }

        [Test]
        public void TestIsDeadReturnsTrueAfterFatalDamage()
        {
            Assert.IsFalse(_entityHealth.IsDead());
            Assert.IsTrue(_entityHealth.IsAlive());

            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));

            Assert.IsTrue(_entityHealth.IsDead());
            Assert.IsFalse(_entityHealth.IsAlive());
        }

        [Test]
        public void TestResurrectWithHpRestoresEntity()
        {
            // Kill the entity
            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            Assert.IsTrue(_entityHealth.IsDead());
            Assert.AreEqual(0, _entityHealth.Hp);

            // Resurrect with 50 HP
            _entityHealth.Resurrect(50);

            Assert.IsFalse(_entityHealth.IsDead());
            Assert.IsTrue(_entityHealth.IsAlive());
            Assert.AreEqual(50, _entityHealth.Hp);
        }

        [Test]
        public void TestResurrectWithPercentageRestoresEntity()
        {
            // Kill the entity
            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            Assert.IsTrue(_entityHealth.IsDead());

            // Resurrect with 75% HP
            _entityHealth.Resurrect(new Percentage(75));

            Assert.IsFalse(_entityHealth.IsDead());
            Assert.AreEqual(75, _entityHealth.Hp);
        }

        [Test]
        public void TestResurrectAliveEntityThrowsException()
        {
            Assert.IsFalse(_entityHealth.IsDead());

            var ex = Assert.Throws<InvalidOperationException>(() => 
                _entityHealth.Resurrect(50)
            );
            
            Assert.IsNotNull(ex);
            StringAssert.Contains("already alive", ex.Message);
        }

        [Test]
        public void TestHealingAfterResurrectionWorks()
        {
            // Kill and resurrect
            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            _entityHealth.Resurrect(50);
            Assert.AreEqual(50, _entityHealth.Hp);

            // Heal should work now
            _entityHealth.Heal(CreateHealInfo(30));
            Assert.AreEqual(80, _entityHealth.Hp);
        }

        [Test]
        public void TestDamageAfterResurrectionWorks()
        {
            // Kill and resurrect
            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            _entityHealth.Resurrect(50);

            // Damage should work now
            var dmgResult = _entityHealth.TakeDamage(CreateDamageInfo(20));
            Assert.AreEqual(DamageOutcome.Applied, dmgResult.Outcome);
            Assert.AreEqual(30, _entityHealth.Hp);
        }

        [Test]
        public void TestMaxHpChangeOnDeadEntitySkipsHealthAdjustment()
        {
            // Kill the entity
            _entityHealth.TakeDamage(CreateDamageInfo(MaxHp));
            Assert.IsTrue(_entityHealth.IsDead());
            Assert.AreEqual(0, _entityHealth.Hp);

            // Change max HP
            _entityHealth.AddMaxHpFlatModifier(50, EntityHealth.HpBehaviourOnMaxHpIncrease.AddHealthUpToMaxHp);

            // HP should still be 0 (dead), not adjusted
            Assert.AreEqual(0, _entityHealth.Hp);
            Assert.AreEqual(150, _entityHealth.MaxHp);
            Assert.IsTrue(_entityHealth.IsDead());
        }
    }
}

