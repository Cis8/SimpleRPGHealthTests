using System;
using System.Reflection;
using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgFramework.Stats;
using ElectricDrill.SoapRpgFramework.Utils;
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
        const long MAX_HP = 100;

        public class MockSource : Source
        {
            public static MockSource Create()
            {
                var source = CreateInstance<MockSource>();
                source.name = "TestSource";
                return source;
            }
        }

        public class MockDmgType : DmgType 
        {
            public static MockDmgType Create(
                Stat reducedBy = null,
                DmgReductionFn dmgReductionFn = null,
                Stat piercedBy = null,
                DefReductionFn defReductionFn = null,
                bool ignoresBarrier = false)
            {
                var dmgType = CreateInstance<MockDmgType>();
                dmgType.name = "TestDmgType";
                dmgType.ReducedBy = reducedBy;
                dmgType.DmgReductionFn = dmgReductionFn;
                dmgType.DefensiveStatPiercedBy = piercedBy;
                dmgType.DefReductionFn = defReductionFn;
                dmgType.IgnoresBarrier = ignoresBarrier;
                return dmgType;
            }
        }
    
        public class MockStatSet : StatSet
        {
            public static MockStatSet Create()
            {
                var statSet = CreateInstance<MockStatSet>();
                statSet.name = "TestStatSet";
                return statSet;
            }
            
            public void Add(Stat stat) => _stats.Add(stat);
        }
        
        public class MockFlatDmgReductionFn : FlatDmgReductionFn
        {
            private long _reducedDmg;
            
            public static MockFlatDmgReductionFn Create(long reducedDmgAmount)
            {
                var flatDmgReductionFn = CreateInstance<MockFlatDmgReductionFn>();
                flatDmgReductionFn.name = "TestFlatDmgReductionFn";
                flatDmgReductionFn._reducedDmg = reducedDmgAmount;
                return flatDmgReductionFn;
            }

            public override long ReducedDmg(long amount, double defensiveStatValue)
            {
                return _reducedDmg;
            }
        }
        
        public class MockFlatDefReductionFn : FlatDefReductionFn
        {
            private long _reducedDef;
            
            public static MockFlatDefReductionFn Create(long reducedDefAmount)
            {
                var flatDefReductionFn = CreateInstance<MockFlatDefReductionFn>();
                flatDefReductionFn.name = "TestFlatDefReductionFn";
                flatDefReductionFn._reducedDef = reducedDefAmount;
                return flatDefReductionFn;
            }

            public override double ReducedDef(long piercingStatValue, long piercedStatValue)
            {
                return _reducedDef;
            }
        }

        // mocks for the entity that is taking damage
        private GameObject gameObject;
        private EntityHealth entityHealth;
        private Mock<EntityCore> mockEntityCore;
        private Mock<EntityStats> mockEntityStats;
        
        // mock for the entity that is dealing damage
        private Mock<EntityCore> mockDealerEntityCore;
        private Mock<EntityStats> mockDealerEntityStats;

        // Test double for damage strategy (avoids Moq on ScriptableObject)
        private class TestDamageStrategy : ConfigurableDamageStrategy
        {
            private Func<PreDmgInfo, DamageInfo> _fn;
            public static TestDamageStrategy Create(Func<PreDmgInfo, DamageInfo> fn) {
                var s = CreateInstance<TestDamageStrategy>();
                s._fn = fn;
                return s;
            }
            public override DamageInfo CalculateDamage(PreDmgInfo pre) => _fn(pre);
        }

        [SetUp]
        public void Setup() {
            // setup entity that is taking damage
            gameObject = new GameObject();
            
            mockEntityCore = new Mock<EntityCore>();
            mockEntityStats = new Mock<EntityStats>();

            mockEntityCore.Setup(x => x.Level).Returns(new EntityLevel());
            mockEntityStats.Setup(x => x.StatSet).Returns(MockStatSet.Create());
            mockEntityStats.Setup(x => x.Get(It.IsAny<Stat>())).Returns(0L);
            
            // Add mocks before adding EntityHealth so Awake can find them
            gameObject.AddComponent(mockEntityCore.GetType());
            gameObject.AddComponent(mockEntityStats.GetType());

            gameObject.AddComponent<EntityHealth>();
            entityHealth = gameObject.GetComponent<EntityHealth>();

            entityHealth._stats = mockEntityStats.Object;
            entityHealth._core = mockEntityCore.Object;
            
            // setup entity that is dealing damage
            mockDealerEntityCore = new Mock<EntityCore>();
            mockDealerEntityStats = new Mock<EntityStats>();
            
            mockDealerEntityCore.Setup(x => x.Level).Returns(new EntityLevel());
            mockDealerEntityCore.Setup(x => x.Stats).Returns(mockDealerEntityStats.Object);
            mockDealerEntityStats.Setup(x => x.StatSet).Returns(MockStatSet.Create());
            mockDealerEntityStats.Setup(x => x.Get(It.IsAny<Stat>())).Returns(0L);
            
            // setup health long refs since would be null otherwise
            entityHealth.baseMaxHp = new LongRef { UseConstant = true, ConstantValue = MAX_HP };
            entityHealth.totalMaxHp = new LongRef { UseConstant = true };
            entityHealth.hp = new LongRef() { UseConstant = true, ConstantValue = MAX_HP };
            entityHealth.deathThreshold = LongVarFactory.CreateLongVar(0);
            entityHealth.barrier = new LongRef { UseConstant = true, ConstantValue = 0 };
            
            // Use DestroyImmediateOnDeathStrategy for testing
            entityHealth.OnDeathStrategy = ScriptableObject.CreateInstance<DestroyImmediateOnDeathStrategy>();

            // Initialize required game events to prevent null reference exceptions
            var preDmgGameEvent = ScriptableObject.CreateInstance<PreDmgGameEvent>();
            var takenDmgGameEvent = ScriptableObject.CreateInstance<TakenDmgGameEvent>();
            var entityDiedGameEvent = ScriptableObject.CreateInstance<EntityDiedGameEvent>();
            var maxHealthChangedEvent = ScriptableObject.CreateInstance<EntityMaxHealthChangedGameEvent>();
            var gainedHealthEvent = ScriptableObject.CreateInstance<EntityGainedHealthGameEvent>();
            var lostHealthEvent = ScriptableObject.CreateInstance<EntityLostHealthGameEvent>();
            var preHealEvent = ScriptableObject.CreateInstance<PreHealGameEvent>();
            var entityHealedEvent = ScriptableObject.CreateInstance<EntityHealedGameEvent>();
            var preventedDmgEvent = ScriptableObject.CreateInstance<PreventedDmgGameEvent>();

            // Use reflection to set the private fields
            var type = typeof(EntityHealth);
            type.GetField("_preDmgInfoEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, preDmgGameEvent);
            type.GetField("_takenDmgInfoEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, takenDmgGameEvent);
            type.GetField("_preventedDmgEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, preventedDmgEvent);
            type.GetField("_entityDiedEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, entityDiedGameEvent);
            type.GetField("_maxHealthChangedEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, maxHealthChangedEvent);
            type.GetField("_gainedHealthEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, gainedHealthEvent);
            type.GetField("_lostHealthEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, lostHealthEvent);
            type.GetField("_preHealEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, preHealEvent);
            type.GetField("_entityHealedEvent", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(entityHealth, entityHealedEvent);

            entityHealth.SetupBaseMaxHp();

            // Inject override damage strategy so tests are isolated from pipeline steps
            var defaultStrategy = TestDamageStrategy.Create(pre => {
                var info = new DamageInfo(pre);
                info.Amounts.RawAmount = pre.Amount;
                info.Amounts.DefReducedAmount = pre.Amount;
                info.Amounts.DefBarrierReducedAmount = pre.Amount;
                info.Amounts.NetAmount = pre.Amount;
                return info;
            });
            typeof(EntityHealth)
                .GetField("_overrideDamageStrategy", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(entityHealth, defaultStrategy);
        }

        [TearDown]
        public void Teardown() {
            Object.DestroyImmediate(gameObject);
        }

        // ==== NO DEF / BARRIER DIRECT STEP TESTS HERE ANYMORE ========================================
        // Tests for defense & barrier step logic moved to:
        // ApplyDefenseStepTests.cs and ApplyBarrierStepTests.cs

        [Test]
        public void TakeDamage_WithMockedSourceAndType() {
            const long DMG_AMOUNT = 25;

            // Arrange
            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create();

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(25)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithTarget(mockDealerEntityCore.Object)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP - DMG_AMOUNT, entityHealth.Hp);
        }

        [Test]
        public void TotalMaxHp_CalculatedCorrectly_WithFlatAndPercentageModifiers() {
            // Arrange
            const long BASE_MAX_HP = 100;
            const long FLAT_MODIFIER = 50;
            const long PERCENTAGE_MODIFIER = 20; // 20%
            
            var percentageModifier = new Percentage(PERCENTAGE_MODIFIER);
            entityHealth.baseMaxHp = new LongRef { UseConstant = true, ConstantValue = BASE_MAX_HP };
            entityHealth.totalMaxHp = new LongRef { UseConstant = true };
            entityHealth.hp = new LongRef() { UseConstant = true, ConstantValue = BASE_MAX_HP };
            entityHealth.barrier = new LongRef { UseConstant = true, ConstantValue = 0 };

            // Act
            entityHealth.AddMaxHpFlatModifier(FLAT_MODIFIER);
            entityHealth.AddMaxHpPercentageModifier(PERCENTAGE_MODIFIER);

            // Assert
            long expectedTotalMaxHp = BASE_MAX_HP + FLAT_MODIFIER;
            expectedTotalMaxHp += (long)(expectedTotalMaxHp * percentageModifier);
            Assert.AreEqual(expectedTotalMaxHp, entityHealth.MaxHp);
        }
        
        [Test]
        public void Health_CannotBeNegative_WhenHealthCanBeNegativeIsFalse() {
            // Arrange
            const long DMG_AMOUNT = 150;
            entityHealth.HealthCanBeNegative = false;

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create();

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithTarget(entityHealth.Core)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(0, entityHealth.Hp);
        }
        
        [Test]
        public void Health_CanBeNegative_WhenHealthCanBeNegativeIsTrue() {
            // Arrange
            const long DMG_AMOUNT = 150;
            entityHealth.HealthCanBeNegative = true;

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create();

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithTarget(entityHealth.Core)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP - DMG_AMOUNT, entityHealth.Hp);
        }
        
        [Test]
        public void Health_IsRestoredCorrectly_WhenHealed() {
            // Arrange
            const long DMG_AMOUNT = 50;
            const long HEAL_AMOUNT = 30;

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create();

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithTarget(entityHealth.Core)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            entityHealth.TakeDamage(preDmgInfo);

            var preHealInfo = PreHealInfo.Builder
                .WithAmount(HEAL_AMOUNT)
                .WithSource(mockSource)
                .WithHealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.Heal(preHealInfo);

            // Assert
            Assert.AreEqual(MAX_HP - DMG_AMOUNT + HEAL_AMOUNT, entityHealth.Hp);
        }
        
        // ==== IMMUNITY TESTS ========================================
        [Test]
        public void TakeDamage_WhenImmune_NoDamageTaken()
        {
            // Arrange
            const long DMG_AMOUNT = 50;
            entityHealth.IsImmune = true;

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create();

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithTarget(entityHealth.Core)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP, entityHealth.Hp);
        }

        [Test]
        public void TakeDamage_WhenImmune_PreventedDmgEventRaisedWithCorrectCause()
        {
            // Arrange
            const long DMG_AMOUNT = 50;
            entityHealth.IsImmune = true;
            bool eventRaised = false;
            DmgPreventedInfo eventInfo = null;

            var preventedDmgEvent = (PreventedDmgGameEvent)typeof(EntityHealth).GetField("_preventedDmgEvent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entityHealth);
            preventedDmgEvent.OnEventRaised += (info) => {
                eventRaised = true;
                eventInfo = info;
            };

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create();

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithTarget(entityHealth.Core)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.IsTrue(eventRaised, "PreventedDmgEvent was not raised.");
            Assert.IsNotNull(eventInfo);
            Assert.AreEqual(DamagePreventedCause.EntityImmune, eventInfo.Cause);
        }

        [Test]
        public void TakeDamage_UsesOverrideDamageStrategyNetAmount()
        {
            // Arrange
            const long RAW = 80;
            const long NET = 35;

            var customStrategy = TestDamageStrategy.Create(pre => {
                var info = new DamageInfo(pre);
                info.Amounts.RawAmount = pre.Amount;
                info.Amounts.DefReducedAmount = pre.Amount;          // pretend no def reduction
                info.Amounts.DefBarrierReducedAmount = NET;          // pretend barrier trimmed it
                info.Amounts.NetAmount = NET;                        // final applied dmg
                return info;
            });
            typeof(EntityHealth)
                .GetField("_overrideDamageStrategy", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(entityHealth, customStrategy);

            var pre = PreDmgInfo.Builder
                .WithAmount(RAW)
                .WithType(MockDmgType.Create())
                .WithSource(MockSource.Create())
                .WithTarget(entityHealth.Core)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(pre);

            // Assert
            Assert.AreEqual(MAX_HP - NET, entityHealth.Hp);
        }
    }
}
