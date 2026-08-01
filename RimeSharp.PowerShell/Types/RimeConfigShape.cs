using System.Collections;
using System.Collections.Specialized;

namespace RimeSharp.PowerShell;

/// <summary>
/// Declares the shape of a homogeneous RIME config container.
/// </summary>
public sealed class RimeConfigShape
{
    internal RimeNormalizedConfigShape Shape { get; }

    private RimeConfigShape(RimeNormalizedConfigShape shape)
    {
        Shape = shape;
    }

    /// <summary>
    /// Declare a homogeneous list with the specified element shape.
    /// </summary>
    public static RimeConfigShape List(object elementShape)
        => new RimeConfigShape(RimeNormalizedConfigShape.List(
            RimeConfigShapeNormalizer.Normalize(elementShape)));

    /// <summary>
    /// Declare a map with runtime keys and homogeneous value shapes.
    /// </summary>
    public static RimeConfigShape MapOf(object valueShape)
        => new RimeConfigShape(RimeNormalizedConfigShape.MapOf(
            RimeConfigShapeNormalizer.Normalize(valueShape)));
}

internal enum RimeConfigShapeKind
{
    String,
    Boolean,
    Integer,
    Double,
    FixedMap,
    List,
    MapOf,
}

internal sealed class RimeNormalizedConfigShape
{
    internal RimeConfigShapeKind Kind { get; }
    internal IReadOnlyList<RimeConfigShapeMember> Members { get; }
    internal RimeNormalizedConfigShape? ValueShape { get; }

    internal string DisplayName => Kind switch
    {
        RimeConfigShapeKind.String => "String",
        RimeConfigShapeKind.Boolean => "Boolean",
        RimeConfigShapeKind.Integer => "Int32",
        RimeConfigShapeKind.Double => "Double",
        RimeConfigShapeKind.FixedMap => "FixedMap",
        RimeConfigShapeKind.List => "List",
        RimeConfigShapeKind.MapOf => "MapOf",
        _ => Kind.ToString(),
    };

    private RimeNormalizedConfigShape(
        RimeConfigShapeKind kind,
        IReadOnlyList<RimeConfigShapeMember>? members = null,
        RimeNormalizedConfigShape? valueShape = null)
    {
        Kind = kind;
        Members = members ?? Array.Empty<RimeConfigShapeMember>();
        ValueShape = valueShape;
    }

    internal static RimeNormalizedConfigShape Scalar(RimeConfigShapeKind kind)
        => new(kind);

    internal static RimeNormalizedConfigShape FixedMap(
        IReadOnlyList<RimeConfigShapeMember> members)
        => new(RimeConfigShapeKind.FixedMap, members);

    internal static RimeNormalizedConfigShape List(RimeNormalizedConfigShape elementShape)
        => new(RimeConfigShapeKind.List, valueShape: elementShape);

    internal static RimeNormalizedConfigShape MapOf(RimeNormalizedConfigShape valueShape)
        => new(RimeConfigShapeKind.MapOf, valueShape: valueShape);
}

internal sealed class RimeConfigShapeMember
{
    internal string Name { get; }
    internal RimeNormalizedConfigShape Shape { get; }

    internal RimeConfigShapeMember(string name, RimeNormalizedConfigShape shape)
    {
        Name = name;
        Shape = shape;
    }
}

internal static class RimeConfigShapeNormalizer
{
    internal static RimeNormalizedConfigShape Normalize(object? shape)
    {
        var activeObjects = new HashSet<object>(ReferenceIdentityComparer.Instance);
        return Normalize(shape, activeObjects);
    }

    private static RimeNormalizedConfigShape Normalize(
        object? shape,
        HashSet<object> activeObjects)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(
                nameof(shape),
                "A config shape cannot be null.");
        }

        if (shape is Type scalarType)
        {
            if (scalarType == typeof(string))
                return RimeNormalizedConfigShape.Scalar(RimeConfigShapeKind.String);
            if (scalarType == typeof(bool))
                return RimeNormalizedConfigShape.Scalar(RimeConfigShapeKind.Boolean);
            if (scalarType == typeof(int))
                return RimeNormalizedConfigShape.Scalar(RimeConfigShapeKind.Integer);
            if (scalarType == typeof(double))
                return RimeNormalizedConfigShape.Scalar(RimeConfigShapeKind.Double);

            throw new ArgumentException(
                $"Scalar type '{scalarType.FullName}' is not supported.",
                nameof(shape));
        }

        if (shape is RimeConfigShape containerShape)
        {
            return containerShape.Shape.Kind switch
            {
                RimeConfigShapeKind.List =>
                    RimeNormalizedConfigShape.List(containerShape.Shape.ValueShape!),
                RimeConfigShapeKind.MapOf =>
                    RimeNormalizedConfigShape.MapOf(containerShape.Shape.ValueShape!),
                _ => throw new ArgumentException(
                    "Only List and MapOf container markers are supported.",
                    nameof(shape)),
            };
        }

        if (shape is not Hashtable && shape is not OrderedDictionary)
        {
            throw new ArgumentException(
                $"Shape value type '{shape.GetType().FullName}' is not supported.",
                nameof(shape));
        }

        if (!activeObjects.Add(shape))
        {
            throw new ArgumentException(
                "Recursive config shapes are not supported.",
                nameof(shape));
        }

        try
        {
            var dictionary = (IDictionary)shape;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var members = new List<RimeConfigShapeMember>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string name)
                {
                    throw new ArgumentException(
                        "Every fixed-map shape key must be a string.",
                        nameof(shape));
                }

                if (!names.Add(name))
                {
                    throw new ArgumentException(
                        $"Fixed-map keys that differ only by case are not supported: '{name}'.",
                        nameof(shape));
                }

                members.Add(new RimeConfigShapeMember(
                    name,
                    Normalize(entry.Value, activeObjects)));
            }

            return RimeNormalizedConfigShape.FixedMap(members.ToArray());
        }
        finally
        {
            activeObjects.Remove(shape);
        }
    }
}

internal sealed class ReferenceIdentityComparer : IEqualityComparer<object>
{
    internal static ReferenceIdentityComparer Instance { get; } = new();

    private ReferenceIdentityComparer()
    {
    }

    public new bool Equals(object? left, object? right)
        => ReferenceEquals(left, right);

    public int GetHashCode(object value)
        => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
}
