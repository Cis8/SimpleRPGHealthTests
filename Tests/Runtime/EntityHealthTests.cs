using System;
using ElectricDrill.SimpleRpgHealth;
using ElectricDrill.SoapRpgFramework;
using ElectricDrill.SoapRpgFramework.Stats;
using ElectricDrill.SoapRpgFramework.Utils;
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

        [SetUp]
        public void Setup() {
            // setup entity that is taking damage
            gameObject = new GameObject();
            
            mockEntityCore = new Mock<EntityCore>();
            mockEntityStats = new Mock<EntityStats>();

            mockEntityCore.Setup(x => x.Level).Returns(new EntityLevel());
            mockEntityStats.Setup(x => x.StatSet).Returns(MockStatSet.Create());
            mockEntityStats.Setup(x => x.Get(It.IsAny<Stat>())).Returns(0L);

            gameObject.AddComponent<EntityHealth>();
            entityHealth = gameObject.GetComponent<EntityHealth>();

            entityHealth._stats = mockEntityStats.Object;
            
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

            entityHealth.SetupBaseMaxHp();
        }

        [TearDown]
        public void Teardown() {
            Object.DestroyImmediate(gameObject);
        }

        // ==== NO DEF TESTS ========================================
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
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP - DMG_AMOUNT, entityHealth.Hp);
        }

        [Test]
        public void TakeDamage_WithBarrierReduction() {
            // Arrange
            const long DMG_AMOUNT = 25;
            const long BARRIER_AMOUNT = 10;

            entityHealth.AddBarrier(BARRIER_AMOUNT);

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create(); // doesn't ignore barrier by default

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP - (DMG_AMOUNT - BARRIER_AMOUNT), entityHealth.Hp);
            Assert.AreEqual(Math.Max(0, BARRIER_AMOUNT - DMG_AMOUNT), entityHealth.Barrier);
        }

        [Test]
        public void TakeDamage_IgnoringBarrier() {
            // Arrange
            const long DMG_AMOUNT = 25;
            const long BARRIER_AMOUNT = 10;

            entityHealth.AddBarrier(BARRIER_AMOUNT);

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create(ignoresBarrier: true);

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP - DMG_AMOUNT, entityHealth.Hp);
            Assert.AreEqual(BARRIER_AMOUNT, entityHealth.Barrier);
        }
        
        // ==== DEF TESTS ========================================
        [Test]
        public void TakeDamage_WithDefensiveStat() {
            // Arrange
            const long DMG_AMOUNT = 25;
            const long DEFENSE_AMOUNT = 10;

            var mockSource = MockSource.Create();
            var armorStat = ScriptableObject.CreateInstance<Stat>();
            armorStat.name = "Armor";
            var mockFlatDmgReductionFn = MockFlatDmgReductionFn.Create(DMG_AMOUNT - DEFENSE_AMOUNT);
            
            var mockDmgType = MockDmgType.Create(reducedBy: armorStat, dmgReductionFn: mockFlatDmgReductionFn);
            
            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP - (DMG_AMOUNT - DEFENSE_AMOUNT), entityHealth.Hp);
        }
        
        [Test]
        public void TakeDamage_WithDefensiveStatAndPiercingStat() {
            // Arrange
            const long DMG_AMOUNT = 25;
            const long DEFENSE_AMOUNT = 10;
            const long PIERCING_AMOUNT = 5;

            var mockSource = MockSource.Create();
            var armorStat = ScriptableObject.CreateInstance<Stat>();
            armorStat.name = "Armor";
            var piercingStat = ScriptableObject.CreateInstance<Stat>();
            piercingStat.name = "Piercing";
            var mockFlatDmgReductionFn = MockFlatDmgReductionFn.Create(DMG_AMOUNT - (DEFENSE_AMOUNT - PIERCING_AMOUNT));
            var mockFlatDefReductionFn = MockFlatDefReductionFn.Create(DEFENSE_AMOUNT - PIERCING_AMOUNT);
            
            var mockDmgType = MockDmgType.Create(
                reducedBy: armorStat,
                dmgReductionFn: mockFlatDmgReductionFn,
                piercedBy: piercingStat,
                defReductionFn: mockFlatDefReductionFn
            );
            
            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(MAX_HP - (DMG_AMOUNT - (DEFENSE_AMOUNT - PIERCING_AMOUNT)), entityHealth.Hp);
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
        
        [Test]
        public void Barrier_CannotBeNegative() {
            // Arrange
            const long BARRIER_AMOUNT = 10;
            const long DMG_AMOUNT = 20;

            entityHealth.AddBarrier(BARRIER_AMOUNT);

            var mockSource = MockSource.Create();
            var mockDmgType = MockDmgType.Create();

            var preDmgInfo = PreDmgInfo.Builder
                .WithAmount(DMG_AMOUNT)
                .WithType(mockDmgType)
                .WithSource(mockSource)
                .WithDealer(mockDealerEntityCore.Object)
                .Build();

            // Act
            entityHealth.TakeDamage(preDmgInfo);

            // Assert
            Assert.AreEqual(0, entityHealth.Barrier);
        }
    }
}