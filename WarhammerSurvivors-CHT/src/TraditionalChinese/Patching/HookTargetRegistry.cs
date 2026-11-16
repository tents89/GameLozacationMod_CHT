using System.Reflection;

namespace WarhammerSurvivors.TraditionalChinese.Patching;

/// <summary>
/// Keep Harmony targets tied to their actual declaration. Reflected MethodInfo
/// instances obtained from different child types can be unequal while naming the
/// same native IL2CPP method. Installing a detour for each can recurse forever.
/// </summary>
public sealed class HookTargetRegistry
{
    public const BindingFlags DeclaredMethods = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly HashSet<(Module Module, int Token, Type DeclaringType)> reserved = new();
    public int Count => reserved.Count;

    public static MethodInfo? DeclaredSetter(Type type, string propertyName) =>
        type.GetProperty(propertyName, DeclaredMethods)?.GetSetMethod(nonPublic: true);

    public static MethodInfo? DeclaredMethod(Type type, string name, params Type[] parameters) =>
        type.GetMethod(name, DeclaredMethods, binder: null, types: parameters, modifiers: null);

    public static MethodInfo Canonicalize(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        Type declaringType = method.DeclaringType ?? throw new ArgumentException("A hook must have a declaring type.", nameof(method));
        if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters)
            throw new ArgumentException("Abstract or generic hook targets are not supported.", nameof(method));

        // Do not use GetBaseDefinition(): a real override has its own body and
        // must stay distinct. Resolve the exact metadata definition on its owner.
        MethodInfo declared = declaringType.GetMethods(DeclaredMethods).Single(candidate =>
            candidate.Module == method.Module && candidate.MetadataToken == method.MetadataToken);
        if (declared.ReflectedType != declared.DeclaringType)
            throw new InvalidOperationException("Could not normalize the hook to its actual declaration.");
        return declared;
    }

    public bool TryReserve(MethodInfo method, out MethodInfo declared)
    {
        declared = Canonicalize(method);
        // Registration happens before Harmony is called. A failed native patch
        // may be partially installed, so it must not be retried under an alias.
        return reserved.Add((declared.Module, declared.MetadataToken, declared.DeclaringType!));
    }
}
