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

    // ---- VAL-01: a null where the schema requires a value is an error finding, never an exception -------

    private const string NullValue = "NULL_VALUE";

    private static string[] NullValueFields(IReadOnlyList<ValidationFinding> findings) =>
        [.. findings.Select(finding => finding.Field)];

    private static Invoice WithItems(Invoice invoice, int count, params int[] nullPositions) => invoice with
    {
        Items = [.. Enumerable.Range(0, count).Select(i => nullPositions.Contains(i) ? null! : invoice.Items[0])],
    };

    private static IReadOnlyList<ValidationFinding> ValidateWellFormed(Invoice invoice)
    {
        AssertWellFormed(invoice, RepoFiles.Reference, "null case", []);
        return RepoFiles.Validate(invoice);
    }

    [Fact]
    public void A_single_null_items_element_yields_exactly_one_NULL_VALUE_at_items_0()
    {
        var invoice = RepoFiles.LoadValidInvoice() with { Items = [null!] };

        var finding = Assert.Single(ValidateWellFormed(invoice));

        Assert.Equal(NullValue, finding.RuleId);
        Assert.Equal("items[0]", finding.Field);
        Assert.Equal("a value", finding.Expected);
        Assert.Equal("null", finding.Actual);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void A_single_null_installments_element_yields_exactly_one_NULL_VALUE_at_installments_0()
    {
        var invoice = RepoFiles.LoadValidInvoice() with { Installments = [null!] };

        var finding = Assert.Single(ValidateWellFormed(invoice));

        Assert.Equal(NullValue, finding.RuleId);
        Assert.Equal("installments[0]", finding.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_null_at_the_first_middle_or_last_position_gives_one_NULL_VALUE_at_that_index_only(int position)
    {
        var invoice = WithItems(RepoFiles.LoadValidInvoice(), 3, position);

        var findings = ValidateWellFormed(invoice);

        var finding = Assert.Single(findings);
        Assert.Equal(NullValue, finding.RuleId);
        Assert.Equal($"items[{position}]", finding.Field);
    }

    [Fact]
    public void Adjacent_nulls_are_never_merged_and_every_items_path_comes_before_any_installments_path()
    {
        var fixture = RepoFiles.LoadValidInvoice();
        var invoice = WithItems(fixture, 2, 0, 1) with { Installments = [null!, fixture.Installments[0], null!] };

        var findings = ValidateWellFormed(invoice);

        Assert.All(findings, finding => Assert.Equal(NullValue, finding.RuleId));
        Assert.Equal(["items[0]", "items[1]", "installments[0]", "installments[2]"], NullValueFields(findings));
        Assert.True(findings.SequenceEqual(RepoFiles.Validate(invoice)), "the second run differed");
    }

    [Fact]
    public void A_null_list_a_null_nested_record_and_a_null_required_string_each_give_one_NULL_VALUE()
    {
        var fixture = RepoFiles.LoadValidInvoice();

        Assert.Equal(["items"], NullValueFields(ValidateWellFormed(fixture with { Items = null! })));
        Assert.Equal(["installments"], NullValueFields(ValidateWellFormed(fixture with { Installments = null! })));
        Assert.Equal(["issuer"], NullValueFields(ValidateWellFormed(fixture with { Issuer = null! })));
        Assert.Equal(["totals"], NullValueFields(ValidateWellFormed(fixture with { Totals = null! })));
        Assert.Equal(["access_key"], NullValueFields(ValidateWellFormed(fixture with { AccessKey = null! })));
        Assert.All(
            ValidateWellFormed(fixture with { AccessKey = null! }),
            finding => Assert.Equal(NullValue, finding.RuleId));
    }

    [Fact]
    public void A_null_state_registration_is_allowed_and_gives_no_finding()
    {
        var fixture = RepoFiles.LoadValidInvoice();
        var invoice = fixture with
        {
            Issuer = fixture.Issuer with { Ie = null },
            Recipient = fixture.Recipient with { Ie = null },
        };

        Assert.DoesNotContain(NullValue, RepoFiles.RuleIdsOf(ValidateWellFormed(invoice)));
    }

    [Fact]
    public void An_empty_items_list_is_ITEMS_EMPTY_and_an_empty_installments_list_has_no_finding()
    {
        var fixture = RepoFiles.LoadValidInvoice();

        var noItems = RepoFiles.RuleIdsOf(ValidateWellFormed(fixture with { Items = [] }));
        var noInstallments = ValidateWellFormed(fixture with { Installments = [] });

        Assert.Contains(RuleIds.ITEMS_EMPTY, noItems);
        Assert.DoesNotContain(NullValue, noItems);
        Assert.DoesNotContain(noInstallments, finding => finding.Field.StartsWith("installments", StringComparison.Ordinal));
        Assert.DoesNotContain(NullValue, RepoFiles.RuleIdsOf(noInstallments));
    }

    [Fact]
    public void A_null_invoice_reference_stays_an_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RepoFiles.Validate(null!));
    }

    [Fact]
    public void Nulls_at_every_string_and_list_leaf_never_throw_and_yield_only_NULL_VALUE_at_exactly_that_path()
    {
        var fixture = RepoFiles.LoadValidInvoice();
        var checkedLeaves = 0;

        foreach (var leaf in Leaves(fixture))
        {
            if (leaf.Type == typeof(string) && ((PropertyInfo)leaf.Path[^1]).Name != "Ie")
            {
                var mutated = (Invoice)SetAt(fixture, leaf.Path, 0, null);

                var findings = ValidateWellFormed(mutated);

                Assert.All(findings, finding => Assert.Equal(NullValue, finding.RuleId));
                Assert.Equal([SnakeCasePath(leaf.Path)], NullValueFields(findings));
                checkedLeaves++;
            }
            else if (leaf.Current is IList)
            {
                var elementType = leaf.Type.GetGenericArguments()[0];
                var current = (IList)leaf.Current!;
                for (var position = 0; position < 3; position++)
                {
                    var withNull = NewList(
                        elementType,
                        Enumerable.Range(0, 3).Select(i => i == position ? null! : current[0]!));
                    var mutated = (Invoice)SetAt(fixture, leaf.Path, 0, withNull);

                    var findings = ValidateWellFormed(mutated);

                    Assert.All(findings, finding => Assert.Equal(NullValue, finding.RuleId));
                    Assert.Equal([$"{SnakeCasePath(leaf.Path)}[{position}]"], NullValueFields(findings));
                    checkedLeaves++;
                }
            }
        }

        Assert.True(checkedLeaves >= 30, "expected many string and list leaves, checked " + checkedLeaves);
    }

    private static string SnakeCasePath(IReadOnlyList<object> path)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var step in path)
        {
            if (step is PropertyInfo property)
            {
                if (builder.Length > 0)
                {
                    builder.Append('.');
                }

                builder.Append(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
            }
            else
            {
                builder.Append('[').Append(step).Append(']');
            }
        }

        return builder.ToString();
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
                    if (list[i] is not null)
                    {
                        Collect(list[i]!, here.Append(i).ToList(), leaves);
                    }
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
