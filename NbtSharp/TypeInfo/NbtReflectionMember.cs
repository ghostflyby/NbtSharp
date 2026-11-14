using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NbtSharp;

[RequiresUnreferencedCode("Reflection-based member access is not trimming-safe.")]
internal sealed class NbtReflectionMember
{
    private readonly PropertyInfo? _property;
    private readonly FieldInfo? _field;

    public NbtReflectionMember(string name, PropertyInfo property)
    {
        Name = name;
        Type = property.PropertyType;
        _property = property;
    }

    public NbtReflectionMember(string name, FieldInfo field)
    {
        Name = name;
        Type = field.FieldType;
        _field = field;
    }

    public string Name { get; }

    public string ClrName => _property?.Name ?? _field!.Name;

    public Type Type { get; }

    public bool CanWrite => _property?.CanWrite == true || _field is { IsInitOnly: false, IsLiteral: false };

    public object? GetValue(object instance) =>
        _property is not null ? _property.GetValue(instance) : _field!.GetValue(instance);

    public void SetValue(object instance, object? value)
    {
        if (_property is not null)
        {
            _property.SetValue(instance, value);
        }
        else
        {
            _field!.SetValue(instance, value);
        }
    }
}
