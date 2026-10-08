using System.Collections;
using System.Reflection;
using Carimbo.Domain;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>
/// VAL-01: the validator never throws, whatever a parsed invoice holds. Hostile values are written straight
/// into the records (bypassing the wire patterns, which a directly built invoice can violate) at every leaf
/// of the shared fixture: strings, wire decimals, dates, integers, enums and lists. The mutation loop is
/// seeded, so a failure reproduces.
/// </summary>
public class NeverThrowsTests
{
    private const int Seed = 20261008;
    private const int Iterations = 2000;

    private static readonly HashSet<string> Catalogue = typeof(RuleIds)
        .GetFields()
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet();

    private static readonly string HugeString = new('A', 100000);

    private static readonly decimal[] HostileDecimals =
    [
        decimal.MaxValue,
        decimal.MinValue,
        -0.01m,
        0m,
        0.01m,
        0.005m,
        decimal.MaxValue / 2m,
        decimal.MaxValue / 100m,
        0.0000000000000000000000000001m,
    ];

    private static readonly DateOnly[] HostileDates =
    [
        DateOnly.MinValue,
        DateOnly.MaxValue,
        new DateOnly(2006, 3, 31),
        new DateOnly(2006, 4, 1),
        new DateOnly(2026, 3, 14),
        new DateOnly(2026, 10, 2),
    ];

    private static readonly int[] HostileInts = [int.MinValue, int.MaxValue, 0, -1, 1];

    private static readonly DateOnly[] ReferenceDates =
    [
        RepoFiles.Reference,
        DateOnly.MinValue,
        DateOnly.MaxValue,
        new DateOnly(2006, 4, 1),
        new DateOnly(2026, 3, 14),
    ];

    private sealed record Leaf(IReadOnlyList<object> Path, Type Type, object? Current)
    {
        public string Describe() => string.Join(
            ".",
            Path.Select(step => step is PropertyInfo property ? property.Name : "[" + step + "]"));
    }

    [Fact]
    public void Two_thousand_seeded_mutations_never_throw_and_only_use_catalogued_rule_ids()
    {
        var random = new Random(Seed);
        var seen = new HashSet<string>();

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            object invoice = RepoFiles.LoadValidInvoice();
            var applied = new List<string>();
            var mutations = 1 + random.Next(3);
            for (var m = 0; m < mutations; m++)
            {
                var leaves = Leaves(invoice);
                var leaf = leaves[random.Next(leaves.Count)];
                var candidates = Candidates(leaf).ToList();
                var value = candidates[random.Next(candidates.Count)];
                invoice = SetAt(invoice, leaf.Path, 0, value);
                applied.Add(leaf.Describe());
            }

            var reference = ReferenceDates[random.Next(ReferenceDates.Length)];
            AssertWellFormed((Invoice)invoice, reference, $"iteration {iteration}, mutated {string.Join(", ", applied)}", seen);
        }

        // The loop must actually bite: these families are reachable only through hostile values.
        foreach (var expected in new[]
        {
            RuleIds.ARITH_OVERFLOW,
            RuleIds.ITEMS_EMPTY,
            RuleIds.CNPJ_FORMAT,
            RuleIds.KEY_FORMAT,
            RuleIds.UF_UNKNOWN,
            RuleIds.DATE_PLAUSIBLE,
            RuleIds.ITEM_ARITH,
        })
        {
            Assert.Contains(expected, seen);
        }
    }

    [Fact]
    public void Every_hostile_value_at_every_leaf_of_the_fixture_returns_a_list()
    {
        var fixture = RepoFiles.LoadValidInvoice();
        var seen = new HashSet<string>();
        var leaves = Leaves(fixture);

        Assert.True(leaves.Count >= 50, "expected the fixture to expose every member as a leaf, found " + leaves.Count);
        foreach (var leaf in leaves)
        {
            foreach (var value in Candidates(leaf))
            {
                var mutated = (Invoice)SetAt(fixture, leaf.Path, 0, value);
                AssertWellFormed(mutated, RepoFiles.Reference, "mutated " + leaf.Describe(), seen);
            }
        }
    }

    [Fact]
    public void A_hundred_thousand_character_identifier_is_a_format_finding_not_a_hang()
    {
        var invoice = RepoFiles.LoadValidInvoice();
        var hostile = invoice with
        {
            AccessKey = HugeString,
            Issuer = invoice.Issuer with { Cnpj = HugeString },
            Recipient = invoice.Recipient with { TaxId = HugeString },
        };

        var ids = RepoFiles.RuleIdsOf(RepoFiles.Validate(hostile));

        Assert.Contains(RuleIds.CNPJ_FORMAT, ids);
        Assert.Contains(RuleIds.KEY_FORMAT, ids);
    }

    [Fact]
    public void Non_ascii_digits_and_trailing_newlines_are_format_findings()
    {
        var invoice = RepoFiles.LoadValidInvoice();
        var key = invoice.AccessKey;

        Assert.Contains(RuleIds.KEY_FORMAT, RepoFiles.RuleIdsOf(RepoFiles.Validate(invoice with { AccessKey = key + "\n" })));
        Assert.Contains(RuleIds.KEY_FORMAT, RepoFiles.RuleIdsOf(RepoFiles.Validate(invoice with { AccessKey = "٣٥" + key[2..] })));
        Assert.Contains(
            RuleIds.CNPJ_FORMAT,
            RepoFiles.RuleIdsOf(RepoFiles.Validate(invoice with { Issuer = invoice.Issuer with { Cnpj = invoice.Issuer.Cnpj + "\n" } })));
        Assert.Contains(
            RuleIds.CPF_FORMAT,
            RepoFiles.RuleIdsOf(RepoFiles.Validate(invoice with
            {
                Recipient = invoice.Recipient with { TaxId = "٥٢٩٩٨٢٢٤٧٢٥", TaxIdKind = TaxIdKind.Cpf },
            })));
    }

    private static void AssertWellFormed(Invoice invoice, DateOnly reference, string context, HashSet<string> seen)
    {
        IReadOnlyList<ValidationFinding> first;
        try
        {
            first = RepoFiles.Validate(invoice, reference);
        }
        catch (Exception ex)
        {
            Assert.Fail($"{context}: the validator threw {ex.GetType().Name}: {ex.Message}");
            return;
        }

        foreach (var finding in first)
        {
            Assert.False(string.IsNullOrEmpty(finding.Field), context + ": empty field");
            Assert.True(Catalogue.Contains(finding.RuleId), context + ": rule id outside the catalogue: " + finding.RuleId);
            Assert.True(Enum.IsDefined(finding.Severity), context + ": undefined severity");
            Assert.NotNull(finding.Expected);
            Assert.NotNull(finding.Actual);
            seen.Add(finding.RuleId);
        }

        Assert.True(first.SequenceEqual(RepoFiles.Validate(invoice, reference)), context + ": the second run differed");
    }

    // ---- reflection over the record graph ------------------------------------------------------

    private static IEnumerable<PropertyInfo> PropertiesOf(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.Name != "EqualityContract" && property.GetIndexParameters().Length == 0)
        .OrderBy(property => property.MetadataToken);

    private static bool IsLeafType(Type type) =>
        type == typeof(string)
        || type == typeof(DateOnly)
        || type == typeof(int)
        || type.IsEnum
        || type == typeof(Money)
        || type == typeof(Decimal4)
        || type == typeof(Rate);

    private static List<Leaf> Leaves(object root)
    {
        var leaves = new List<Leaf>();
        Collect(root, [], leaves);
        return leaves;
    }

    private static void Collect(object node, IReadOnlyList<object> path, List<Leaf> leaves)
    {
        foreach (var property in PropertiesOf(node.GetType()))
        {
            var value = property.GetValue(node);
            var here = path.Append(property).ToList();
            if (IsLeafType(property.PropertyType))
            {
                leaves.Add(new Leaf(here, property.PropertyType, value));
            }
            else if (value is IList list)
            {
                leaves.Add(new Leaf(here, property.PropertyType, value));
                for (var i = 0; i < list.Count; i++)
                {
                    Collect(list[i]!, here.Append(i).ToList(), leaves);
                }
            }
            else if (value is not null)
            {
                Collect(value, here, leaves);
            }
        }
    }

    private static IEnumerable<object?> Candidates(Leaf leaf)
    {
        var last = (PropertyInfo)leaf.Path[^1];
        var type = leaf.Type;

        if (type == typeof(string))
        {
            var current = (string?)leaf.Current ?? string.Empty;
            yield return string.Empty;
            yield return "\n";
            yield return current + "\n";
            yield return current.ToLowerInvariant();
            yield return "٣٥٢٦";
            yield return HugeString;
            yield return "00000000000000";
            yield return "00000000000";
            yield return new string('0', 44);
            yield return "12ABC34501DE35";
            if (last.Name == "Ie")
            {
                yield return null;
            }
        }
        else if (type == typeof(DateOnly))
        {
            foreach (var date in HostileDates)
            {
                yield return date;
            }
        }
        else if (type == typeof(int))
        {
            foreach (var number in HostileInts)
            {
                yield return number;
            }
        }
        else if (type.IsEnum)
        {
            foreach (var value in Enum.GetValues(type))
            {
                yield return value;
            }

            yield return Enum.ToObject(type, 99);
        }
        else if (type == typeof(Money) || type == typeof(Decimal4) || type == typeof(Rate))
        {
            foreach (var amount in HostileDecimals)
            {
                yield return Activator.CreateInstance(type, amount);
            }
        }
        else
        {
            var elementType = type.GetGenericArguments()[0];
            var current = (IList)leaf.Current!;
            yield return NewList(elementType, []);
            if (current.Count > 0)
            {
                yield return NewList(elementType, Enumerable.Repeat(current[0]!, 200));
                yield return NewList(elementType, Enumerable.Repeat(current[0]!, 50));
            }
        }
    }

    private static IList NewList(Type elementType, IEnumerable<object> items)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    // Rebuilds the record graph with the leaf at path replaced; records are rebuilt through their one public
    // constructor so nothing depends on the wire converters.
    private static object SetAt(object node, IReadOnlyList<object> path, int depth, object? replacement)
    {
        var property = (PropertyInfo)path[depth];
        object? updated;
        if (depth == path.Count - 1)
        {
            updated = replacement;
        }
        else if (path[depth + 1] is int index)
        {
            var source = (IList)property.GetValue(node)!;
            var copy = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(property.PropertyType.GetGenericArguments()[0]))!;
            for (var i = 0; i < source.Count; i++)
            {
                copy.Add(i == index ? SetAt(source[i]!, path, depth + 2, replacement) : source[i]);
            }

            updated = copy;
        }
        else
        {
            updated = SetAt(property.GetValue(node)!, path, depth + 1, replacement);
        }

        return Rebuild(node, property, updated);
    }

    private static object Rebuild(object node, PropertyInfo changed, object? value)
    {
        var type = node.GetType();
        var constructor = type.GetConstructors().Single();
        var properties = PropertiesOf(type).ToList();
        var arguments = constructor.GetParameters()
            .Select(parameter =>
            {
                var property = properties.Single(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                return property == changed ? value : property.GetValue(node);
            })
            .ToArray();
        return constructor.Invoke(arguments);
    }
}
