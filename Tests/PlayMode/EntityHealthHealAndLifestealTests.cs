using System.Collections;
using ElectricDrill.AstraRpgFramework.Stats;
using ElectricDrill.AstraRpgFramework.Utils;
using ElectricDrill.AstraRpgHealth.Config;
using ElectricDrill.AstraRpgHealth.Heal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Tests.PlayMode.Utils.TestHealthFactory;

namespace ElectricDrill.SimpleRpgHealthTests
{
    public class EntityHealthHealAndLifestealTests
    {
        private HealthEntityBundle _attacker;
        private HealthEntityBundle _target;

        private HealSource _genericHealSource;
        private HealSource _lifestealHealSource;
        private Stat _lifestealStat;
        private LifestealConfig _lifestealConfig;
        private HealthEventsBundle _sharedEvents;

        [SetUp]
        public void SetUp()
        {
            _sharedEvents = CreateSharedEvents();

            // First entity creates config and registers provider
            _attacker = CreateEntity(
                "Attacker",
                initializeStats: true,
                sharedEvents: _sharedEvents);

            // Second entity reuses same config + events
            _target = CreateEntity(
                "Target",
                sharedConfig: _attacker.Config,
                sharedEvents: _sharedEvents);

            _genericHealSource = ScriptableObject.CreateInstance<HealSource>();
            _genericHealSource.name = "GenericHealSource";
            _lifestealHealSource = ScriptableObject.CreateInstance<HealSource>();
            _lifestealHealSource.name = "LifestealHealSource";

            _lifestealStat = ScriptableObject.CreateInstance<Stat>();
            _lifestealStat.name = "LifestealStat";
            
            InjectPercentageStat(_attacker.Stats, _lifestealStat, new Percentage(25));

            // Assign lifesteal mapping on existing config (its lifesteal config auto-created or created here if missing)
            _lifestealConfig = AssignLifestealMapping(
                _attacker.Config,
                _attacker.DefaultDamageType,
                _lifestealStat,
                _lifestealHealSource
            );
            
            AstraRpgHealthConfigProvider.Instance.LifestealConfig = _lifestealConfig;
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

            Object.DestroyImmediate(_genericHealSource);
            Object.DestroyImmediate(_lifestealHealSource);
            Object.DestroyImmediate(_lifestealStat);
            Object.DestroyImmediate(_lifestealConfig);
            Object.DestroyImmediate(_attacker.Config); // destroy config once (shared)

            // Destroy shared events
            Object.DestroyImmediate(_sharedEvents.PreDmg);
            Object.DestroyImmediate(_sharedEvents.DamageResolution);
            Object.DestroyImmediate(_sharedEvents.MaxHpChanged);
            Object.DestroyImmediate(_sharedEvents.Gained);
            Object.DestroyImmediate(_sharedEvents.Lost);
            Object.DestroyImmediate(_sharedEvents.Died);
            Object.DestroyImmediate(_sharedEvents.PreHeal);
            Object.DestroyImmediate(_sharedEvents.Healed);
        }

        [UnityTest]
        public IEnumerator TestBasicHealClampedToMax()
        {
            yield return null;
            // Damage the attacker (40) -> 60 HP
            _attacker.Health.TakeDamage(BuildPre(40, _target, _attacker));
            Assert.AreEqual(60, _attacker.Health.Hp);

            // Heal 100 -> clamp at 100
            _attacker.Health.Heal(PreHealInfo.Builder
                .WithAmount(100)
                .WithSource(_genericHealSource)
                .WithHealer(_attacker.Core)
                .Build());

            Assert.AreEqual(100, _attacker.Health.Hp);
        }

        [UnityTest]
        public IEnumerator TestCriticalHealMultiplier()
        {
            yield return null;
            _attacker.Health.TakeDamage(BuildPre(50, _target, _attacker));
            Assert.AreEqual(50, _attacker.Health.Hp);

            // Critical heal: 20 * 2.5 = 50 -> full
            _attacker.Health.Heal(PreHealInfo.Builder
                .WithAmount(20)
                .WithSource(_genericHealSource)
                .WithHealer(_attacker.Core)
                .WithIsCritical(true)
                .WithCriticalMultiplier(2.5)
                .Build());

            Assert.AreEqual(100, _attacker.Health.Hp);
        }

        [UnityTest]
        public IEnumerator TestLifestealHealsAttacker()
        {
            yield return null;
            // Attacker loses 30 -> 70
            _attacker.Health.TakeDamage(BuildPre(30, _target, _attacker));
            Assert.AreEqual(70, _attacker.Health.Hp);

            // Attacker deals 40 -> lifesteal 25% = 10 -> 80
            _target.Health.TakeDamage(BuildPre(40, _attacker, _target));
            Assert.AreEqual(80, _attacker.Health.Hp);
        }

        [UnityTest]
        public IEnumerator TestLifestealAfterCriticalDamage()
        {
            yield return null;
            // Attacker loses 50 -> 50
            _attacker.Health.TakeDamage(BuildPre(50, _target, _attacker));
            Assert.AreEqual(50, _attacker.Health.Hp);

            // Critical damage: base 20 * 3 = 60 final => lifesteal 25% = 15 -> HP 65
            _target.Health.TakeDamage(BuildPre(20, _attacker, _target, crit: true, critMult: 3d));
            Assert.AreEqual(65, _attacker.Health.Hp);
        }
    }
}
