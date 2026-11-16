// Some generated IL2CPP references expose compiler attributes with native-only
// constructors. Supply managed versions so .NET 6 nullable metadata does not bind
// to those interop proxies. These types are internal to this plugin.
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
internal sealed class NullableAttribute : Attribute
{
    public readonly byte[] NullableFlags;
    public NullableAttribute(byte value) => NullableFlags = new[] { value };
    public NullableAttribute(byte[] values) => NullableFlags = values;
}

[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
internal sealed class NullableContextAttribute : Attribute
{
    public readonly byte Flag;
    public NullableContextAttribute(byte value) => Flag = value;
}
