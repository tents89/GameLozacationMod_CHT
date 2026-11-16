using System.Reflection;

namespace WarhammerSurvivors.TraditionalChinese.Patching;

// Only one audited, static localization entry point may be detoured. Metadata
// deduplication alone cannot make all IL2CPP UI/virtual detours safe.
internal static class NativeHookPolicy
{
    internal static bool IsAllowed(MethodInfo method)
    {
        if (!method.IsStatic || method.IsVirtual || method.IsGenericMethod ||
            method.ContainsGenericParameters || method.ReturnType != typeof(bool) ||
            method.DeclaringType?.FullName != "I2.Loc.LocalizationManager" ||
            method.Name != "TryGetTranslation") return false;
        ParameterInfo[] p = method.GetParameters();
        return p.Length == 9 && p[0].ParameterType == typeof(string) &&
            p[1].ParameterType == typeof(string).MakeByRefType() &&
            p[2].ParameterType == typeof(bool) && p[3].ParameterType == typeof(int) &&
            p[4].ParameterType == typeof(bool) && p[5].ParameterType == typeof(bool) &&
            p[6].ParameterType.FullName == "UnityEngine.GameObject" &&
            p[7].ParameterType == typeof(string) && p[8].ParameterType == typeof(bool);
    }
}
