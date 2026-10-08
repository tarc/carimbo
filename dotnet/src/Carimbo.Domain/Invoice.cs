using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Carimbo.Domain;

/// <summary>
/// Regular expressions shared by the domain attributes and the exported JSON Schema.
/// Every digit class is the explicit <c>[0-9]</c>, never the digit shorthand class,
/// because in .NET the shorthand also matches non-ASCII digits.
/// All of them are linear: no nested quantifiers, and the <see cref="TaxId"/> alternation has two fixed lengths.
/// </summary>
public static class Patterns
{
    /// <summary>44 characters: 6 digits, 12 alphanumeric, 26 digits (admits the alphanumeric CNPJ form).</summary>
    public const string AccessKey = "^[0-9]{6}[A-Z0-9]{12}[0-9]{26}$";

    /// <summary>CNPJ without punctuation: 12 alphanumeric characters plus 2 check digits.</summary>
    public const string Cnpj = "^[A-Z0-9]{12}[0-9]{2}$";

    /// <summary>CPF without punctuation: 11 digits.</summary>
    public const string Cpf = "^[0-9]{11}$";

    /// <summary>A recipient identifier: a CPF (11 digits) or a CNPJ (14 characters).</summary>
    public const string TaxId = "^([0-9]{11}|[A-Z0-9]{12}[0-9]{2})$";

    /// <summary>Brazilian state code: two uppercase letters.</summary>
    public const string Uf = "^[A-Z]{2}$";

    /// <summary>NCM product classification: 8 digits.</summary>
    public const string Ncm = "^[0-9]{8}$";

    /// <summary>CFOP operation code: 4 digits.</summary>
    public const string Cfop = "^[0-9]{4}$";

    /// <summary>Origin digit plus CST (3 digits in total) or CSOSN (4 digits in total).</summary>
    public const string CstCsosn = "^[0-9]{3,4}$";

    /// <summary>Invariant decimal string with exactly two fraction digits.</summary>
    public const string Money = "^-?[0-9]+\\.[0-9]{2}$";

    /// <summary>Invariant non-negative decimal string with exactly four fraction digits.</summary>
    public const string Decimal4 = "^[0-9]+\\.[0-9]{4}$";

    /// <summary>Invariant non-negative decimal string with exactly two fraction digits.</summary>
    public const string Rate = "^[0-9]+\\.[0-9]{2}$";

    /// <summary>
    /// True when <paramref name="value"/> is matched by <paramref name="pattern"/> over its whole length.
    /// A plain <see cref="Regex.IsMatch(string, string)"/> is not enough: the .NET <c>$</c> anchor also
    /// matches before a final newline, which JSON Schema (ECMA-262) does not, so it would accept
    /// <c>"key\n"</c>. There is no IgnoreCase on purpose: <c>[A-Z]</c> must stay uppercase-only.
    /// </summary>
    public static bool IsFullMatch(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);

        var match = Regex.Match(value, pattern, RegexOptions.CultureInvariant);
        return match.Success && match.Index == 0 && match.Length == value.Length;
    }
}

/// <summary>
/// The DANFE-visible target of an extraction: header, issuer, recipient, items, totals and
/// installments. Every member is required; only the two state registrations (<c>ie</c>) can be null.
/// The invoice total lives only at <see cref="Totals.InvoiceTotal"/>.
/// </summary>
public sealed record Invoice(
    [property: Description("44-character NF-e access key (chave de acesso), digits and uppercase letters only, no spaces")]
    [property: RegularExpression(Patterns.AccessKey)]
    string AccessKey,
    [property: Description("Invoice number (Nº) as an integer, without the dots printed in the number box")]
    int Number,
    [property: Description("Invoice series (SÉRIE) as an integer")]
    int Series,
    [property: Description("Issue date (DATA DA EMISSÃO) as YYYY-MM-DD")]
    DateOnly IssueDate,
    [property: Description("Nature of the operation (NATUREZA DA OPERAÇÃO) exactly as printed")]
    string OperationNature,
    [property: Description("The issuing company (emitente)")]
    Party Issuer,
    [property: Description("The receiving company or person (destinatário)")]
    Recipient Recipient,
    [property: Description("Every line of the products table in the printed order, across all pages")]
    IReadOnlyList<LineItem> Items,
    [property: Description("The tax and amount totals (CÁLCULO DO IMPOSTO block)")]
    Totals Totals,
    [property: Description("The installments (FATURA / DUPLICATAS) in the printed order, an empty list when the block is absent")]
    IReadOnlyList<Installment> Installments)
{
    /// <summary>
    /// The dotted snake_case JSON paths, in schema property order, of string members that are not a
    /// full match of the pattern their <see cref="RegularExpressionAttribute"/> declares, with list
    /// indices for list elements (for example <c>items[1].ncm</c>). The serializer ignores those
    /// attributes (they only feed the exported schema), so this is what makes a parsed invoice
    /// schema-valid. It is driven by reflection over the attributes, so a new patterned member cannot
    /// be left unenforced. Money, <see cref="Decimal4"/> and <see cref="Rate"/> are not listed: their
    /// converters already enforce their patterns while parsing. This checks the schema's patterns only:
    /// per D-03 there is no check-digit validation here, which belongs to the validators.
    /// It skips null members and null list elements: <see cref="NullViolations"/> reports those.
    /// A method, not a property, so neither the serializer nor the schema exporter sees it.
    /// </summary>
    public IReadOnlyList<string> PatternViolations()
    {
        var violations = new List<string>();
        Collect(this, string.Empty, violations);
        return violations;
    }

    /// <summary>
    /// The dotted snake_case JSON paths, in schema property order (the same order as
    /// <see cref="PatternViolations"/>), where the invoice holds null although the schema requires a
    /// value: a member whose nullability annotation is not nullable (so the two <c>ie</c> members stay
    /// allowed) and a null element of a list member, reported as <c>items[0]</c>, unless the list's
    /// element type is annotated nullable. The serializer rejects a null member under
    /// <c>RespectNullableAnnotations</c> but accepts a null collection element, which is the hole this
    /// closes. Driven by the same reflection as <see cref="PatternViolations"/>, so a member added later
    /// is covered automatically. Paths only, never values. A method, not a property, so neither the
    /// serializer nor the schema exporter sees it.
    /// </summary>
    public IReadOnlyList<string> NullViolations()
    {
        var violations = new List<string>();
        CollectNulls(this, string.Empty, new NullabilityInfoContext(), violations);
        return violations;
    }

    private static IEnumerable<PropertyInfo> SchemaProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "EqualityContract" && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken);

    private static void CollectNulls(object instance, string prefix, NullabilityInfoContext context, List<string> violations)
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

    private static void Collect(object instance, string prefix, List<string> violations)
    {
        foreach (var property in SchemaProperties(instance.GetType()))
        {
            var value = property.GetValue(instance);
            if (value is null)
            {
                continue;
            }

            var path = prefix + JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            switch (value)
            {
                case string text:
                    var pattern = property.GetCustomAttribute<RegularExpressionAttribute>();
                    if (pattern is not null && !Patterns.IsFullMatch(pattern.Pattern, text))
                    {
                        violations.Add(path);
                    }

                    break;
                case System.Collections.IEnumerable elements:
                    var index = 0;
                    foreach (var element in elements)
                    {
                        if (element is not null && IsDomainRecord(element.GetType()))
                        {
                            Collect(element, $"{path}[{index}].", violations);
                        }

                        index++;
                    }

                    break;
                default:
                    if (IsDomainRecord(value.GetType()))
                    {
                        Collect(value, path + ".", violations);
                    }

                    break;
            }
        }
    }

    private static bool IsDomainRecord(Type type) =>
        type.IsClass && type.Namespace == typeof(Invoice).Namespace;
}

/// <summary>The issuing company. The issuer is always identified by a CNPJ.</summary>
public sealed record Party(
    [property: Description("CNPJ without punctuation: 12 alphanumeric characters plus 2 check digits")]
    [property: RegularExpression(Patterns.Cnpj)]
    string Cnpj,
    [property: Description("Company name (NOME / RAZÃO SOCIAL) exactly as printed")]
    string Name,
    [property: Description("State registration (INSCRIÇÃO ESTADUAL) exactly as printed, for example digits or ISENTO; null when the box is blank")]
    string? Ie,
    [property: Description("Two-letter state code (UF) of the company address, uppercase")]
    [property: RegularExpression(Patterns.Uf)]
    string Uf);

/// <summary>Whether a recipient is identified by a CNPJ or a CPF.</summary>
public enum TaxIdKind
{
    Cnpj,
    Cpf,
}

/// <summary>The receiving party: a company (CNPJ) or a person (CPF).</summary>
public sealed record Recipient(
    [property: Description("CNPJ (14 characters) or CPF (11 digits) without punctuation, letters uppercase")]
    [property: RegularExpression(Patterns.TaxId)]
    string TaxId,
    TaxIdKind TaxIdKind,
    [property: Description("Recipient name (NOME / RAZÃO SOCIAL) exactly as printed")]
    string Name,
    [property: Description("State registration (INSCRIÇÃO ESTADUAL) exactly as printed, for example digits or ISENTO; null when the box is blank")]
    string? Ie,
    [property: Description("Two-letter state code (UF) of the recipient address, uppercase")]
    [property: RegularExpression(Patterns.Uf)]
    string Uf);

/// <summary>One row of the products table (DADOS DOS PRODUTOS / SERVIÇOS).</summary>
public sealed record LineItem(
    [property: Description("Product code (CÓDIGO) exactly as printed")]
    string Code,
    [property: Description("Product description (DESCRIÇÃO) exactly as printed")]
    string Description,
    [property: Description("NCM/SH classification, 8 digits")]
    [property: RegularExpression(Patterns.Ncm)]
    string Ncm,
    [property: Description("Origin digit plus CST (3 digits, Regime Normal) or CSOSN (4 digits, Simples Nacional) exactly as printed in the CST column")]
    [property: RegularExpression(Patterns.CstCsosn)]
    string CstCsosn,
    [property: Description("CFOP operation code, 4 digits")]
    [property: RegularExpression(Patterns.Cfop)]
    string Cfop,
    [property: Description("Unit of measure (UN.) exactly as printed")]
    string Unit,
    [property: Description("QTD. column with exactly four decimals and a dot")]
    Decimal4 Quantity,
    [property: Description("V.UNIT. column with exactly four decimals and a dot")]
    Decimal4 UnitPrice,
    [property: Description("V.TOTAL column with exactly two decimals and a dot")]
    Money Total,
    [property: Description("BC.ICMS column (ICMS tax base) with two decimals; 0.00 when blank or zero")]
    Money IcmsBase,
    [property: Description("%ICMS column (ICMS rate) with two decimals and no percent sign; 0.00 when blank or zero")]
    Rate IcmsRate,
    [property: Description("V.ICMS column (ICMS amount) with two decimals; 0.00 when blank or zero")]
    Money IcmsAmount,
    [property: Description("%IPI column (IPI rate) with two decimals and no percent sign; 0.00 when blank or zero")]
    Rate IpiRate,
    [property: Description("V.IPI column (IPI amount) with two decimals; 0.00 when blank or zero")]
    Money IpiAmount);

/// <summary>The totals boxes of the CÁLCULO DO IMPOSTO block.</summary>
public sealed record Totals(
    [property: Description("BASE DE CÁLCULO DO ICMS with two decimals")]
    Money IcmsBase,
    [property: Description("VALOR DO ICMS with two decimals")]
    Money IcmsAmount,
    [property: Description("BASE DE CÁLCULO DO ICMS SUBST. with two decimals")]
    Money IcmsStBase,
    [property: Description("VALOR DO ICMS SUBSTITUIÇÃO with two decimals")]
    Money IcmsStAmount,
    [property: Description("VALOR TOTAL DOS PRODUTOS with two decimals")]
    Money ProductsTotal,
    [property: Description("VALOR DO FRETE with two decimals")]
    Money Freight,
    [property: Description("VALOR DO SEGURO with two decimals")]
    Money Insurance,
    [property: Description("DESCONTO with two decimals")]
    Money Discount,
    [property: Description("OUTRAS DESPESAS ACESSÓRIAS with two decimals")]
    Money OtherExpenses,
    [property: Description("VALOR TOTAL DO IPI with two decimals")]
    Money IpiAmount,
    [property: Description("VALOR TOTAL DA NOTA with two decimals")]
    Money InvoiceTotal);

/// <summary>One installment of the FATURA / DUPLICATAS block.</summary>
public sealed record Installment(
    [property: Description("Installment number as printed, for example 001")]
    string Number,
    [property: Description("Due date (vencimento) as YYYY-MM-DD")]
    DateOnly DueDate,
    [property: Description("Installment amount with two decimals and a dot")]
    Money Amount);

/// <summary>The typed outcome of reviewing an invoice. Never reachable from <see cref="Invoice"/>.</summary>
public enum DecisionOutcome
{
    Approve,
    Reject,
    Escalate,
}

/// <summary>A typed approve / reject / escalate decision with the reason behind it.</summary>
public sealed record Decision(DecisionOutcome Outcome, string Reason);
