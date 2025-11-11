using ElectricDrill.AstraRpgHealth;
using ElectricDrill.AstraRpgHealth.Death;

namespace ElectricDrill.SoapRpgHealthTests
{
    public class DestroyImmediateOnDeathStrategy : OnDeathStrategy
    {
        public override void Die(EntityHealth entityHealth) {
            DestroyImmediate(entityHealth.gameObject);
        }
    }
}