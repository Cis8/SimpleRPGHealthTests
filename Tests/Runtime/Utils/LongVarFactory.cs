using ElectricDrill.AstraRpgFramework.Utils;
using UnityEngine;

namespace ElectricDrill.AstraRpgHealthTests
{
    public static class LongVarFactory
    {
        public static LongVar CreateLongVar(long value)
        {
            var longVar = ScriptableObject.CreateInstance<LongVar>();
            longVar.Value = value;
            return longVar;
        }
    }
}