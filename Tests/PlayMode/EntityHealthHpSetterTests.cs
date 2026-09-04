using System.Collections;
using ElectricDrill.AstraRpgHealthTests.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static ElectricDrill.AstraRpgHealthTests.Tests.PlayMode.TestHealthFactory;

namespace ElectricDrill.SimpleRpgHealthTests
{
    /// <summary>
    /// The raw <c>Hp</c> setter: the seam for restoring an entity's health wholesale, chiefly when a
    /// pooled entity is recycled into a new life. Everything it deliberately does <i>not</i> do —
    /// heal modifiers, resurrection events, barrier changes — is as much the contract as what it does.
    /// </summary>
    public class EntityHealthHpSetterTests
    {
        private HealthEntityBundle _entity;

        [SetUp]
        public void SetUp() => _entity = CreateEntity("HpSetterTarget");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_entity.Go);
            Object.DestroyImmediate(_entity.DefaultDamageType);
            Object.DestroyImmediate(_entity.DefaultDamageSource);
            Object.DestroyImmediate(_entity.Config);
        }

        [UnityTest]
        public IEnumerator Hp_SetToMaxOnADeadEntity_RestoresFullHpAndClearsTheDeadState()
        {
            yield return null;

            var attacker = CreateEntity("Attacker", _entity.Config);
            _entity.Health.TakeDamage(BuildPre(500, attacker, _entity));
            Assert.IsTrue(_entity.Health.IsDead(), "Precondition: the entity has to actually be dead.");

            _entity.Health.Hp = _entity.Health.MaxHp;

            Assert.AreEqual(_entity.Health.MaxHp, _entity.Health.Hp);
            Assert.IsFalse(_entity.Health.IsDead(),
                "A recycled entity has to come back alive without going through Resurrect, which would " +
                "apply heal modifiers and raise the resurrection channel.");

            Object.DestroyImmediate(attacker.Go);
        }

        [UnityTest]
        public IEnumerator Hp_SetToZero_MarksTheEntityDeadWithoutRoutingThroughDamage()
        {
            yield return null;

            _entity.Health.Hp = 0;

            Assert.AreEqual(0, _entity.Health.Hp);
            Assert.IsTrue(_entity.Health.IsDead());
        }

        [UnityTest]
        public IEnumerator Hp_SetOnADeactivatedEntity_ResolvesTheDeadStateOnItsNextEnable()
        {
            // The ordering a pool depends on: the reset runs while the instance is inactive, and
            // EntityHealth.OnEnable recomputes the dead state from HP when it comes back.
            yield return null;

            var attacker = CreateEntity("Attacker", _entity.Config);
            _entity.Health.TakeDamage(BuildPre(500, attacker, _entity));
            Assert.IsTrue(_entity.Health.IsDead());

            _entity.Go.SetActive(false);
            _entity.Health.Hp = _entity.Health.MaxHp;
            _entity.Go.SetActive(true);

            yield return null;

            Assert.IsFalse(_entity.Health.IsDead());
            Assert.AreEqual(_entity.Health.MaxHp, _entity.Health.Hp);

            Object.DestroyImmediate(attacker.Go);
        }

        [UnityTest]
        public IEnumerator Hp_SetToMax_LeavesTheBarrierUntouched()
        {
            // Worth pinning: a recycled entity must have its barrier cleared explicitly, because this
            // setter will not do it. Astra Pooling's EnemyPoolReset calls RemoveBarrier for that reason.
            yield return null;

            _entity.Health.AddBarrier(40);
            _entity.Health.Hp = _entity.Health.MaxHp;

            Assert.AreEqual(40, _entity.Health.Barrier);
        }
    }
}
