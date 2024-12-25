using ElectricDrill.SimpleRpgHealth;
using UnityEngine;

namespace ElectricDrill.SimpleRpgHealthTests
{
    public class DestroyImmediateOnDeathStrategy : OnDeathStrategy
    {
        public override void Die(EntityHealth entityHealth) {
            DestroyImmediate(entityHealth.gameObject);
        }
    }
}