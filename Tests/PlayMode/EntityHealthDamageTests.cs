using System.Collections;
using ElectricDrill.AstraRpgHealthTests.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static ElectricDrill.AstraRpgHealthTests.Tests.PlayMode.TestHealthFactory;

namespace ElectricDrill.SimpleRpgHealthTests
{
    public class EntityHealthDamageTests
    {
        private HealthEntityBundle _attacker;
        private HealthEntityBundle _target;

        [SetUp]
        public void SetUp()
        {
            // Create two entities (attacker & target) so we mirror live damage scenario
            _attacker = CreateEntity("Attacker");
            _target = CreateEntity("Target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_attacker.Go);
            Object.DestroyImmediate(_attacker.DefaultDamageType);
            Object.DestroyImmediate(_attacker.DefaultDamageSource);
            Object.DestroyImmediate(_target.Go);
            Object.DestroyImmediate(_target.DefaultDamageType);
            Object.DestroyImmediate(_target.DefaultDamageSource);
            Object.DestroyImmediate(_attacker.Config); // shared config is the attacker's
        }

        [UnityTest]
        public IEnumerator TestBasicDamageReducesHp()
        {
            yield return null;
            Assert.AreEqual(100, _target.Health.Hp);
            _target.Health.TakeDamage(BuildPre(25, _attacker, _target));
            Assert.AreEqual(75, _target.Health.Hp);
        }

        [UnityTest]
        public IEnumerator TestDamageDoesNotGoBelowZero()
        {
            yield return null;
            _target.Health.TakeDamage(BuildPre(150, _attacker, _target));
            Assert.AreEqual(0, _target.Health.Hp);
        }

        [UnityTest]
        public IEnumerator TestIgnoreDamageFlag()
        {
            yield return null;
            _target.Health.TakeDamage(BuildPre(30, _attacker, _target, ignore: true));
            Assert.AreEqual(100, _target.Health.Hp);
        }

        [UnityTest]
        public IEnumerator TestCriticalDamageMultiplier()
        {
            yield return null;
            // 10 * 2.5 = 25 -> 100 - 25 = 75
            _target.Health.TakeDamage(BuildPre(10, _attacker, _target, crit: true, critMult: 2.5));
            Assert.AreEqual(75, _target.Health.Hp);
        }

        [UnityTest]
        public IEnumerator TestNegativeHealthAllowed()
        {
            yield return null;
            // Re-create target allowing negative health
            Object.DestroyImmediate(_target.Go);
            _target = TestHealthFactory.CreateEntity("TargetNeg", allowNegative: true);
            _target.Health.TakeDamage(BuildPre(150, _attacker, _target));
            Assert.Less(_target.Health.Hp, 0);
        }
    }
}
