using Carimbo.Domain;

namespace Carimbo.Validation;

/// <summary>
/// CNPJ, CPF and NF-e access-key identifier math, a port of <c>python/src/carimbo_datagen/ids.py</c>.
/// Check digits use the character value <c>ASCII - 48</c>, which is meaningful only for 0-9 and A-Z, so every
/// routine matches its input against an explicit pattern first and never does character arithmetic on
/// anything else (non-ASCII digits, lowercase, newlines, mask punctuation).
/// </summary>
public static class Identifiers
{
    private const string CnpjBasePattern = "^[A-Z0-9]{12}$";
    private const string CpfBasePattern = "^[0-9]{9}$";
    private const string AccessKeyBodyPattern = "^[0-9]{6}[A-Z0-9]{12}[0-9]{25}$";

    private static readonly int[] CnpjWeights1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjWeights2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CpfWeights1 = [10, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CpfWeights2 = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>The 27 federative units and their IBGE codes, as used in the first two access-key characters.</summary>
    public static IReadOnlyDictionary<string, string> UfCodes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RO"] = "11",
        ["AC"] = "12",
        ["AM"] = "13",
        ["RR"] = "14",
        ["PA"] = "15",
        ["AP"] = "16",
        ["TO"] = "17",
        ["MA"] = "21",
        ["PI"] = "22",
        ["CE"] = "23",
        ["RN"] = "24",
        ["PB"] = "25",
        ["PE"] = "26",
        ["AL"] = "27",
        ["SE"] = "28",
        ["BA"] = "29",
        ["MG"] = "31",
        ["ES"] = "32",
        ["RJ"] = "33",
        ["SP"] = "35",
        ["PR"] = "41",
        ["SC"] = "42",
        ["RS"] = "43",
        ["MS"] = "50",
        ["MT"] = "51",
        ["GO"] = "52",
        ["DF"] = "53",
    };

    /// <summary>The two check digits of a 12-character CNPJ base (8 root plus 4 branch characters).</summary>
    /// <exception cref="ArgumentException">The base is not 12 uppercase letters or digits.</exception>
    public static string CnpjCheckDigits(string base12)
    {
        RequireMatch(CnpjBasePattern, base12, nameof(base12));

        var first = Mod11Digit(WeightedSum(base12, CnpjWeights1, 0));
        var second = Mod11Digit(WeightedSum(base12, CnpjWeights2, 0) + first * CnpjWeights2[12]);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{first}{second}");
    }

    /// <summary>
    /// A bare 14-character CNPJ with matching check digits. Fourteen identical characters pass the arithmetic
    /// (00000000000000 computes 00) but are not a real identifier, so the project rejects them.
    /// </summary>
    public static bool IsValidCnpj(string? value) =>
        value is not null
        && Patterns.IsFullMatch(Patterns.Cnpj, value)
        && !IsRepeatedCharacter(value)
        && CnpjCheckDigits(value[..12]) == value[12..];

    /// <summary>The two check digits of a 9-digit CPF base.</summary>
    /// <exception cref="ArgumentException">The base is not 9 digits.</exception>
    public static string CpfCheckDigits(string base9)
    {
        RequireMatch(CpfBasePattern, base9, nameof(base9));

        var first = Mod11Digit(WeightedSum(base9, CpfWeights1, 0));
        var second = Mod11Digit(WeightedSum(base9, CpfWeights2, 0) + first * CpfWeights2[9]);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{first}{second}");
    }

    /// <summary>A bare 11-digit CPF with matching check digits; 11 identical digits are rejected.</summary>
    public static bool IsValidCpf(string? value) =>
        value is not null
        && Patterns.IsFullMatch(Patterns.Cpf, value)
        && !IsRepeatedCharacter(value)
        && CpfCheckDigits(value[..9]) == value[9..];

    /// <summary>
    /// The check digit of the first 43 characters of an access key: weights 2 to 9 from the right, cycling;
    /// a remainder below 2 gives 0, otherwise 11 minus the remainder.
    /// </summary>
    /// <exception cref="ArgumentException">The text is not 6 digits, 12 letters or digits, then 25 digits.</exception>
    public static int AccessKeyCheckDigit(string key43)
    {
        RequireMatch(AccessKeyBodyPattern, key43, nameof(key43));

        var total = 0;
        var weight = 2;
        for (var i = key43.Length - 1; i >= 0; i--)
        {
            total += CharValue(key43[i]) * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        return Mod11Digit(total);
    }

    /// <summary>A 44-character access key (the CNPJ part may be alphanumeric) with a matching check digit.</summary>
    public static bool IsValidAccessKey(string? value) =>
        value is not null
        && Patterns.IsFullMatch(Patterns.AccessKey, value)
        && value[43] - '0' == AccessKeyCheckDigit(value[..43]);

    /// <summary>True when every character of <paramref name="value"/> is the same (and it is not empty).</summary>
    internal static bool IsRepeatedCharacter(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != value[0])
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    // The ASCII code minus 48; valid only after a [0-9A-Z] match.
    private static int CharValue(char c) => c - '0';

    private static int Mod11Digit(int weightedSum)
    {
        var remainder = weightedSum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    // Weighted sum of the first weights.Length characters of text, starting at the given offset.
    private static int WeightedSum(string text, int[] weights, int offset)
    {
        var total = 0;
        for (var i = 0; i < weights.Length && offset + i < text.Length; i++)
        {
            total += CharValue(text[offset + i]) * weights[i];
        }

        return total;
    }

    private static void RequireMatch(string pattern, string? value, string parameterName)
    {
        if (value is null || !Patterns.IsFullMatch(pattern, value))
        {
            throw new ArgumentException("The value does not match " + pattern + ".", parameterName);
        }
    }
}

/// <summary>The identity rules: CNPJ, CPF, tax id kind, UF, and the access key's own format and check digit.</summary>
internal static class IdentityRules
{
    private const string CnpjExpected = "12 uppercase letters or digits followed by 2 digits";
    private const string CpfExpected = "11 digits";
    private const string KeyExpected = "44 characters: 6 digits, 12 uppercase letters or digits, 26 digits";
    private const string UfExpected = "one of the 27 UF codes";
    private const string NotRepeated = "not a single repeated character";

    public static void Append(Invoice invoice, List<ValidationFinding> findings)
    {
        CheckIssuerCnpj(invoice.Issuer.Cnpj, findings);
        CheckRecipient(invoice.Recipient, findings);
        CheckUf("issuer.uf", invoice.Issuer.Uf, findings);
        CheckUf("recipient.uf", invoice.Recipient.Uf, findings);
        CheckAccessKey(invoice.AccessKey, findings);
    }

    private static void CheckIssuerCnpj(string value, List<ValidationFinding> findings) =>
        CheckCnpj("issuer.cnpj", value, findings);

    // A matched CNPJ gets the check-digit rule; anything else is a format finding, so no malformed
    // identifier ever passes silently.
    private static void CheckCnpj(string field, string value, List<ValidationFinding> findings)
    {
        if (!Patterns.IsFullMatch(Patterns.Cnpj, value))
        {
            findings.Add(Finding.Error(field, RuleIds.CNPJ_FORMAT, CnpjExpected, value));
        }
        else if (Identifiers.IsRepeatedCharacter(value))
        {
            findings.Add(Finding.Error(field, RuleIds.CNPJ_CHECK_DIGIT, NotRepeated, value));
        }
        else
        {
            var computed = Identifiers.CnpjCheckDigits(value[..12]);
            if (computed != value[12..])
            {
                findings.Add(Finding.Error(field, RuleIds.CNPJ_CHECK_DIGIT, computed, value[12..]));
            }
        }
    }

    private static void CheckCpf(string field, string value, List<ValidationFinding> findings)
    {
        if (!Patterns.IsFullMatch(Patterns.Cpf, value))
        {
            findings.Add(Finding.Error(field, RuleIds.CPF_FORMAT, CpfExpected, value));
        }
        else if (Identifiers.IsRepeatedCharacter(value))
        {
            findings.Add(Finding.Error(field, RuleIds.CPF_CHECK_DIGIT, NotRepeated, value));
        }
        else
        {
            var computed = Identifiers.CpfCheckDigits(value[..9]);
            if (computed != value[9..])
            {
                findings.Add(Finding.Error(field, RuleIds.CPF_CHECK_DIGIT, computed, value[9..]));
            }
        }
    }

    // The identifier decides which rules run: a value that is shaped like a CNPJ gets the CNPJ rules,
    // one shaped like a CPF the CPF rules, and a value that is neither gets the FORMAT rule of the
    // declared kind. A declared kind that disagrees with the identifier's shape is its own finding.
    private static void CheckRecipient(Recipient recipient, List<ValidationFinding> findings)
    {
        const string field = "recipient.tax_id";
        var value = recipient.TaxId;

        TaxIdKind? shape = null;
        if (Patterns.IsFullMatch(Patterns.Cnpj, value))
        {
            shape = TaxIdKind.Cnpj;
            CheckCnpj(field, value, findings);
        }
        else if (Patterns.IsFullMatch(Patterns.Cpf, value))
        {
            shape = TaxIdKind.Cpf;
            CheckCpf(field, value, findings);
        }
        else if (recipient.TaxIdKind == TaxIdKind.Cpf)
        {
            findings.Add(Finding.Error(field, RuleIds.CPF_FORMAT, CpfExpected, value));
        }
        else
        {
            findings.Add(Finding.Error(field, RuleIds.CNPJ_FORMAT, CnpjExpected, value));
        }

        if (shape is { } actualShape && actualShape != recipient.TaxIdKind)
        {
            findings.Add(Finding.Error(
                "recipient.tax_id_kind",
                RuleIds.TAX_ID_KIND_MISMATCH,
                KindText(actualShape),
                KindText(recipient.TaxIdKind)));
        }
    }

    private static string KindText(TaxIdKind kind) => kind switch
    {
        TaxIdKind.Cnpj => "cnpj",
        TaxIdKind.Cpf => "cpf",
        _ => kind.ToString(),
    };

    private static void CheckUf(string field, string value, List<ValidationFinding> findings)
    {
        if (!Identifiers.UfCodes.ContainsKey(value))
        {
            findings.Add(Finding.Error(field, RuleIds.UF_UNKNOWN, UfExpected, value));
        }
    }

    private static void CheckAccessKey(string value, List<ValidationFinding> findings)
    {
        if (!Patterns.IsFullMatch(Patterns.AccessKey, value))
        {
            findings.Add(Finding.Error("access_key", RuleIds.KEY_FORMAT, KeyExpected, value));
            return;
        }

        var computed = Identifiers.AccessKeyCheckDigit(value[..43]);
        var printed = value[43] - '0';
        if (computed != printed)
        {
            findings.Add(Finding.Error(
                "access_key",
                RuleIds.KEY_CHECK_DIGIT,
                computed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                printed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
