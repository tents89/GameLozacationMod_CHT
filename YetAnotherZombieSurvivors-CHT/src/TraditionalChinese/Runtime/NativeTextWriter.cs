extern alias Il2Cpp;
using I2.Loc;
using Il2CppInterop.Runtime;
using NativeObject = Il2Cpp::Il2CppSystem.Object;
using NativeArray = Il2Cpp::Il2CppSystem.Array;

namespace YetAnotherZombieSurvivors.TraditionalChinese.Runtime;

// The current Il2CppStringArray setter stores a raw pointer without a write
// barrier. Keep each translated native string rooted and use the game's native
// Array.SetValue instead; no hardcoded array layout or direct pointer writes.
internal sealed class NativeTextWriter
{
    private readonly Dictionary<string, NativeObject> roots = new(StringComparer.Ordinal);
    private readonly int mainThread = Environment.CurrentManagedThreadId;

    internal void Set(TermData term, int index, string value)
    {
        if (Environment.CurrentManagedThreadId != mainThread)
            throw new InvalidOperationException("Native I2 writes must run on the Unity main thread.");
        var languages = term.Languages;
        if (languages == null || (uint)index >= (uint)languages.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (!roots.TryGetValue(value, out NativeObject? native))
        {
            native = new NativeObject(IL2CPP.ManagedStringToIl2Cpp(value));
            roots.Add(value, native); // wrapper holds a strong il2cpp_gchandle
        }
        var array = new NativeArray(languages.Pointer);
        array.SetValue(native, index);
        if (languages[index] != value)
            throw new InvalidOperationException("Native I2 text write did not round-trip.");
        GC.KeepAlive(array);
        GC.KeepAlive(native);
    }
}
