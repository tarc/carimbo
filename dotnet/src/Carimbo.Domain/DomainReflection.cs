using System.Reflection;
using System.Text.Json;

namespace Carimbo.Domain;

/// <summary>
/// The reflection walker shared by <see cref="Invoice"/> and <see cref="Decision"/>: it lists the schema
/// properties of a domain record in declaration order and reports the null members the serializer lets
/// through. Internal, so neither the serializer nor the schema exporter sees it.
/// </summary>
internal static class DomainReflection
{
    /// <summary>The public instance properties of a record in declaration order, without the compiler-generated ones.</summary>
    public static IEnumerable<PropertyInfo> SchemaProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "EqualityContract" && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken);

    /// <summary>True for a class declared in the domain namespace (the records the walker descends into).</summary>
    public static bool IsDomainRecord(Type type) =>
        type.IsClass && type.Namespace == typeof(DomainReflection).Namespace;

    /// <summary>
    /// Appends the JSON path of every null member whose nullability annotation is not nullable, and of every
    /// null list element unless the list's element type is annotated nullable. Paths only, never values.
    /// </summary>
    public static void CollectNulls(object instance, string prefix, NullabilityInfoContext context, List<string> violations)
    {
        foreach (var property in SchemaProperties(instance.GetType()))
        {
            var path = prefix + JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            var value = property.GetValue(instance);
            var nullability = context.Create(property);
            if (value is null)
            {
                if (nullability.ReadState != NullabilityState.Nullable)
                {
                    violations.Add(path);
                }

                continue;
            }

            switch (value)
            {
                case string:
                    break;
                case System.Collections.IEnumerable elements:
                    var elementsMayBeNull = nullability.GenericTypeArguments.Length == 1
                        && nullability.GenericTypeArguments[0].ReadState == NullabilityState.Nullable;
                    var index = 0;
                    foreach (var element in elements)
                    {
                        if (element is null)
                        {
                            if (!elementsMayBeNull)
                            {
                                violations.Add($"{path}[{index}]");
                            }
                        }
                        else if (IsDomainRecord(element.GetType()))
                        {
                            CollectNulls(element, $"{path}[{index}].", context, violations);
                        }

                        index++;
                    }

                    break;
                default:
                    if (IsDomainRecord(value.GetType()))
                    {
                        CollectNulls(value, path + ".", context, violations);
                    }

                    break;
            }
        }
    }
}
