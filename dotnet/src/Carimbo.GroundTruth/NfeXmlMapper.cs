using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Carimbo.Domain;

namespace Carimbo.GroundTruth;

/// <summary>Raised when an NF-e XML lacks an element the documented mapping needs, or holds a malformed value. The message names the element.</summary>
public sealed class NfeMappingException(string message) : Exception(message);

/// <summary>
/// Maps an NF-e XML to the <see cref="Invoice"/> extraction target by the rules of
/// <c>docs/DANFE-MAPPING.md</c> (DOM-02, D-05). It reproduces the printed form, quirks included
/// (<c>ISENTO</c> verbatim, <c>0.00</c> for absent tax tags, four-decimal quantities) and never
/// infers, corrects or balances a value: whether the numbers agree is the validators' business.
/// BCL only; the Python reader <c>carimbo_evals.ground_truth</c> follows the same rules.
/// </summary>
public static class NfeXmlMapper
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    /// <summary>
    /// Reads and maps an NF-e XML file. DTD processing is prohibited and no resolver is set, so
    /// external entities and entity expansion cannot be used against the loader (T-02-21).
    /// </summary>
    /// <exception cref="XmlException">The file is not well-formed XML, or it carries a DOCTYPE.</exception>
    /// <exception cref="NfeMappingException">A required element is missing or malformed.</exception>
    public static Invoice Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(path, settings);
        return Map(XDocument.Load(reader));
    }

    /// <summary>Maps an already loaded NF-e document (an <c>nfeProc</c>, an <c>NFe</c> or an <c>infNFe</c> root).</summary>
    /// <exception cref="NfeMappingException">A required element is missing or malformed.</exception>
    public static Invoice Map(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Root ?? throw new NfeMappingException("missing element 'infNFe' in NF-e XML (empty document)");
        var inf = root.Name == Nfe + "infNFe" ? root : root.Descendants(Nfe + "infNFe").FirstOrDefault()
            ?? throw new NfeMappingException("missing element 'infNFe' in NF-e XML");

        var emit = Child(inf, "emit") ?? throw Missing("emit");
        var dest = Child(inf, "dest") ?? throw Missing("dest");
        var icmsTot = Child(inf, "total/ICMSTot") ?? throw Missing("total/ICMSTot");
        var crt = Required(emit, "CRT", "emit/CRT");

        var recipientCnpj = Optional(dest, "CNPJ");
        var recipientCpf = Optional(dest, "CPF");
        string taxId;
        TaxIdKind kind;
        if (recipientCnpj is not null)
        {
            (taxId, kind) = (recipientCnpj, TaxIdKind.Cnpj);
        }
        else if (recipientCpf is not null)
        {
            (taxId, kind) = (recipientCpf, TaxIdKind.Cpf);
        }
        else
        {
            throw Missing("dest/CNPJ or dest/CPF");
        }

        var items = inf.Elements(Nfe + "det").Select(det => MapItem(det, crt)).ToList();
        var installments = inf.Elements(Nfe + "cobr").SelectMany(cobr => cobr.Elements(Nfe + "dup"))
            .Select(MapInstallment)
            .ToList();

        return new Invoice(
            AccessKey: AccessKeyOf(inf),
            Number: Integer(Required(inf, "ide/nNF"), "ide/nNF"),
            Series: Integer(Required(inf, "ide/serie"), "ide/serie"),
            IssueDate: IssueDateOf(Required(inf, "ide/dhEmi")),
            OperationNature: Required(inf, "ide/natOp"),
            Issuer: new Party(
                Cnpj: Required(emit, "CNPJ", "emit/CNPJ"),
                Name: Required(emit, "xNome", "emit/xNome"),
                Ie: Optional(emit, "IE"),
                Uf: Required(emit, "enderEmit/UF", "emit/enderEmit/UF")),
            Recipient: new Recipient(
                TaxId: taxId,
                TaxIdKind: kind,
                Name: Required(dest, "xNome", "dest/xNome"),
                Ie: Optional(dest, "IE"),
                Uf: Required(dest, "enderDest/UF", "dest/enderDest/UF")),
            Items: items,
            Totals: new Totals(
                IcmsBase: Amount(icmsTot, "vBC"),
                IcmsAmount: Amount(icmsTot, "vICMS"),
                IcmsStBase: Amount(icmsTot, "vBCST"),
                IcmsStAmount: Amount(icmsTot, "vST"),
                ProductsTotal: Amount(icmsTot, "vProd"),
                Freight: Amount(icmsTot, "vFrete"),
                Insurance: Amount(icmsTot, "vSeg"),
                Discount: Amount(icmsTot, "vDesc"),
                OtherExpenses: Amount(icmsTot, "vOutro"),
                IpiAmount: Amount(icmsTot, "vIPI"),
                InvoiceTotal: Amount(icmsTot, "vNF")),
            Installments: installments);
    }

    private static LineItem MapItem(XElement det, string crt)
    {
        var prod = Child(det, "prod") ?? throw Missing("det/prod");
        var group = IcmsGroup(det);
        var codeTag = crt is "1" or "4" ? "CSOSN" : "CST";
        var cstCsosn = Required(group, "orig", $"det/imposto/ICMS/{group.Name.LocalName}/orig")
            + Required(group, codeTag, $"det/imposto/ICMS/{group.Name.LocalName}/{codeTag}");

        return new LineItem(
            Code: Required(prod, "cProd", "det/prod/cProd"),
            Description: Required(prod, "xProd", "det/prod/xProd"),
            Ncm: Required(prod, "NCM", "det/prod/NCM"),
            CstCsosn: cstCsosn,
            Cfop: Required(prod, "CFOP", "det/prod/CFOP"),
            Unit: Required(prod, "uCom", "det/prod/uCom"),
            Quantity: new Decimal4(Round4(DecimalOf(Required(prod, "qCom", "det/prod/qCom"), "det/prod/qCom"))),
            UnitPrice: new Decimal4(Round4(DecimalOf(Required(prod, "vUnCom", "det/prod/vUnCom"), "det/prod/vUnCom"))),
            Total: new Money(Money.RoundHalfUp(DecimalOf(Required(prod, "vProd", "det/prod/vProd"), "det/prod/vProd"))),
            IcmsBase: Amount(group, "vBC"),
            IcmsRate: RateOf(group, "pICMS"),
            IcmsAmount: Amount(group, "vICMS"),
            IpiRate: RateOf(det, "imposto/IPI/IPITrib/pIPI"),
            IpiAmount: Amount(det, "imposto/IPI/IPITrib/vIPI"));
    }

    private static Installment MapInstallment(XElement dup) =>
        new(
            Number: Required(dup, "nDup", "cobr/dup/nDup"),
            DueDate: DateOf(Required(dup, "dVenc", "cobr/dup/dVenc"), "cobr/dup/dVenc"),
            Amount: new Money(Money.RoundHalfUp(DecimalOf(Required(dup, "vDup", "cobr/dup/vDup"), "cobr/dup/vDup"))));

    /// <summary>The single child of <c>imposto/ICMS</c> (ICMS00, ICMS20, ICMSSN101, ...).</summary>
    private static XElement IcmsGroup(XElement det)
    {
        var icms = Child(det, "imposto/ICMS");
        var groups = icms?.Elements().ToList() ?? [];
        return groups.Count == 1
            ? groups[0]
            : throw new NfeMappingException("element 'det/imposto/ICMS' must hold exactly one ICMS group");
    }

    private static string AccessKeyOf(XElement inf)
    {
        var id = inf.Attribute("Id")?.Value;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new NfeMappingException("missing attribute 'infNFe/@Id' in NF-e XML");
        }

        return id.StartsWith("NFe", StringComparison.Ordinal) ? id["NFe".Length..] : id;
    }

    /// <summary>The printed local date: the first 10 characters of <c>dhEmi</c>, never converted to UTC.</summary>
    private static DateOnly IssueDateOf(string dhEmi) =>
        dhEmi.Length >= 10
            ? DateOf(dhEmi[..10], "ide/dhEmi")
            : throw new NfeMappingException($"element 'ide/dhEmi' is not a date: '{dhEmi}'");

    private static DateOnly DateOf(string text, string element) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new NfeMappingException($"element '{element}' is not a date: '{text}'");

    private static int Integer(string text, string element) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new NfeMappingException($"element '{element}' is not an integer: '{text}'");

    private static decimal DecimalOf(string text, string element) =>
        decimal.TryParse(
            text,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new NfeMappingException($"element '{element}' is not a decimal number: '{text}'");

    /// <summary>Two decimals half away from zero; an absent element is <c>0.00</c> (the DANFE prints <c>0,00</c>).</summary>
    private static Money Amount(XElement parent, string path) =>
        new(Money.RoundHalfUp(OptionalDecimal(parent, path)));

    private static Rate RateOf(XElement parent, string path) =>
        new(Money.RoundHalfUp(OptionalDecimal(parent, path)));

    private static decimal OptionalDecimal(XElement parent, string path)
    {
        var text = Optional(parent, path);
        return text is null ? 0m : DecimalOf(text, path);
    }

    /// <summary>Four decimals half away from zero, with a negative zero normalised to a plain zero.</summary>
    private static decimal Round4(decimal value)
    {
        var rounded = decimal.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded == 0m ? 0m : rounded;
    }

    private static XElement? Child(XElement parent, string path)
    {
        var current = parent;
        foreach (var name in path.Split('/'))
        {
            var next = current.Element(Nfe + name);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>The trimmed text of the element at <paramref name="path"/>, or null when it is absent or blank.</summary>
    private static string? Optional(XElement parent, string path)
    {
        var text = Child(parent, path)?.Value.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string Required(XElement parent, string path, string? reported = null) =>
        Optional(parent, path) ?? throw Missing(reported ?? path);

    private static NfeMappingException Missing(string element) =>
        new($"missing element '{element}' in NF-e XML");
}
