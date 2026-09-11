using NUnit.Framework;
using Raven.Config;
using Raven.Player.Core;
using UnityEngine;

namespace Raven.Player.Tests
{
    /// <summary>
    /// Unit tests for player subsystems.
    /// Run with: Window → TextMesh Pro → Test Runner
    /// </summary>
    [TestFixture]
    public class PlayerControllerTests
    {
        private PlayerDataConfig _config;
        private StatsSubsystem _statsSubsystem;

        [SetUp]
        public void Setup()
        {
            // Create a test config
            _config = ScriptableObject.CreateInstance<PlayerDataConfig>();
            _statsSubsystem = new StatsSubsystem(_config);
            _statsSubsystem.Initialize();
        }

        [TearDown]
        public void Teardown()
        {
            Object.Destroy(_config);
            _statsSubsystem.Dispose();
        }

        [Test]
        public void StatsSubsystem_Initialize_SetsHealthToMax()
        {
            Assert.AreEqual(_config.MaxHealthValue, _statsSubsystem.CurrentHealth);
        }

        [Test]
        public void StatsSubsystem_TakeDamage_ReducesHealth()
        {
            float startHealth = _statsSubsystem.CurrentHealth;
            _statsSubsystem.TakeDamage(10);
            
            Assert.AreEqual(startHealth - 10, _statsSubsystem.CurrentHealth);
        }

        [Test]
        public void StatsSubsystem_TakeDamage_FiresHealthChangedEvent()
        {
            bool eventFired = false;
            _statsSubsystem.OnHealthChanged += (health) => eventFired = true;
            
            _statsSubsystem.TakeDamage(10);
            
            Assert.IsTrue(eventFired);
        }

        [Test]
        public void StatsSubsystem_TakeDamage_ClampsHealthToZero()
        {
            _statsSubsystem.TakeDamage(1000);
            
            Assert.AreEqual(0, _statsSubsystem.CurrentHealth);
        }

        [Test]
        public void StatsSubsystem_TakeDamage_SetsIsAliveToFalse()
        {
            _statsSubsystem.TakeDamage(1000);
            
            Assert.IsFalse(_statsSubsystem.IsAlive);
        }

        [Test]
        public void StatsSubsystem_Heal_IncreasesHealth()
        {
            _statsSubsystem.TakeDamage(50);
            float healthAfterDamage = _statsSubsystem.CurrentHealth;
            
            _statsSubsystem.Heal(25);
            
            Assert.AreEqual(healthAfterDamage + 25, _statsSubsystem.CurrentHealth);
        }

        [Test]
        public void StatsSubsystem_Heal_ClampsHealthToMax()
        {
            _statsSubsystem.Heal(1000);
            
            Assert.AreEqual(_config.MaxHealthValue, _statsSubsystem.CurrentHealth);
        }

        [Test]
        public void StatsSubsystem_TryConsumeEnergy_ReturnsTrueWithEnough()
        {
            bool result = _statsSubsystem.TryConsumeEnergy(10);
            
            Assert.IsTrue(result);
        }

        [Test]
        public void StatsSubsystem_TryConsumeEnergy_ReturnsFalseWithoutEnough()
        {
            bool result = _statsSubsystem.TryConsumeEnergy(1000);
            
            Assert.IsFalse(result);
        }

        [Test]
        public void StatsSubsystem_TryConsumeEnergy_ReducesEnergyOnSuccess()
        {
            float startEnergy = _statsSubsystem.CurrentEnergy;
            _statsSubsystem.TryConsumeEnergy(10);
            
            Assert.AreEqual(startEnergy - 10, _statsSubsystem.CurrentEnergy);
        }
    }
}
