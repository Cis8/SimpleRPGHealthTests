using ElectricDrill.SoapRpgFramework.Utils;
using UnityEngine;

namespace ElectricDrill.SoapRpgHealthTests
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