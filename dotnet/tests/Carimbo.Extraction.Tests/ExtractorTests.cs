using System.Reflection;
using System.Text.Json.Nodes;
using Carimbo.Llm;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Extraction.Tests;

public class ExtractorTests
{
    private const string AccessKey = "35260311222333000181550010000001231000012346";
    private const string OversizedAmount = "99999999999999999999999999999999.00";

    private static readonly byte[] Pdf = "%PDF-1.4\nsynthetic"u8.ToArray();

    private static readonly ExtractionContext Context = new(new DateOnly(2026, 10, 1));

    private static readonly ExtractionSettings Settings = new() { Model = "model-under-test", MaxTokens = 1234 };

    public static TheoryData<LlmFailureKind> FailureKinds()
    {
        var data = new TheoryData<LlmFailureKind>();
        foreach (var kind in Enum.GetValues<LlmFailureKind>())
        {
            data.Add(kind);
        }

        return data;
    }

    // ---------------------------------------------------------------- success

    [Fact]
    public async Task Valid_output_becomes_a_typed_invoice_and_keeps_the_raw_text()
    {
        var text = ValidJson();
        var result = await ExtractAsync(Respond(text));

        var success = Assert.IsType<ExtractionOutcome.Success>(result.Outcome);
        Assert.Equal(AccessKey, success.Invoice.AccessKey);
        Assert.Equal(123, success.Invoice.Number);
        Assert.Equal(1, success.Invoice.Series);
        Assert.Equal(new DateOnly(2026, 3, 15), success.Invoice.IssueDate);
        Assert.Equal("11222333000181", success.Invoice.Issuer.Cnpj);
        Assert.Equal("EMITENTE SINTETICA LTDA", success.Invoice.Issuer.Name);
        Assert.Equal("12ABC34501DE35", success.Invoice.Recipient.TaxId);
        Assert.Equal("DESTINATARIA SINTETICA SA", success.Invoice.Recipient.Name);
        Assert.Equal("155.00", success.Invoice.Totals.InvoiceTotal.ToString());
        Assert.Equal(2, success.Invoice.Items.Count);
        Assert.Equal(2, success.Invoice.Installments.Count);
        Assert.Equal(text, result.RawOutput);
    }

    [Fact]
    public async Task A_cpf_recipient_with_kind_cpf_is_a_success()
    {
        var text = ValidJson(o =>
        {
            SetPath(o, "recipient.tax_id", "52998224725");
            SetPath(o, "recipient.tax_id_kind", "cpf");
        });

        var result = await ExtractAsync(Respond(text));

        var success = Assert.IsType<ExtractionOutcome.Success>(result.Outcome);
        Assert.Equal("52998224725", success.Invoice.Recipient.TaxId);
        Assert.Equal(Carimbo.Domain.TaxIdKind.Cpf, success.Invoice.Recipient.TaxIdKind);
    }

    [Fact]
    public async Task Result_carries_the_requested_model_prompt_version_and_schema_hash()
    {
        var result = await ExtractAsync(Respond(ValidJson()));

        Assert.Equal(Settings.Model, result.ModelRequested);
        Assert.Equal(ExtractionContract.Default.PromptVersion, result.PromptVersion);
        Assert.Equal(ExtractionContract.Default.OutputSchemaSha256, result.SchemaSha256);
    }

    [Fact]
    public void The_contract_is_prompt_version_extract_002_and_the_prompt_names_every_v2_field()
    {
        var contract = ExtractionContract.Default;
        Assert.Equal("extract-002", contract.PromptVersion);

        var schema = JsonNode.Parse(contract.OutputSchemaJson)!.AsObject();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        CollectPropertyNames(schema, schema["$defs"]!.AsObject(), names);

        Assert.Contains("invoice_total", names);
        Assert.Contains("tax_id_kind", names);
        foreach (var name in names)
        {
            Assert.True(
                System.Text.RegularExpressions.Regex.IsMatch(contract.Prompt, $@"\b{name}\b"),
                $"The extraction prompt does not mention the field '{name}'.");
        }

        Assert.Contains("0.00", contract.Prompt, StringComparison.Ordinal);
        Assert.Contains("183737.44", contract.Prompt, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- schema_invalid

    [Theory]
    [InlineData("unknown_property")]
    [InlineData("missing_property")]
    [InlineData("missing_installments")]
    [InlineData("truncated_json")]
    [InlineData("prose")]
    [InlineData("pt_br_money")]
    [InlineData("two_decimal_quantity")]
    [InlineData("signed_rate")]
    [InlineData("json_null")]
    [InlineData("trailing_garbage")]
    public async Task Invalid_model_output_is_schema_invalid_and_the_raw_text_is_kept(string scenario)
    {
        var text = scenario switch
        {
            "unknown_property" => ValidJson(o => o["extra_field"] = "surprise"),
            "missing_property" => ValidJson(o => o.Remove("series")),
            "missing_installments" => ValidJson(o => o.Remove("installments")),
            "truncated_json" => ValidJson()[..40],
            "prose" => "Sorry, I could not read that invoice.",
            "pt_br_money" => ValidJson(o => SetPath(o, "totals.invoice_total", "12,34")),
            "two_decimal_quantity" => ValidJson(o => SetPath(o, "items[0].quantity", "2.00")),
            "signed_rate" => ValidJson(o => SetPath(o, "items[0].icms_rate", "-18.00")),
            "json_null" => "null",
            "trailing_garbage" => ValidJson() + " trailing words",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var result = await ExtractAsync(Respond(text));

        var invalid = Assert.IsType<ExtractionOutcome.SchemaInvalid>(result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(invalid.Error));
        Assert.Equal(text, result.RawOutput);
    }

    [Fact]
    public async Task An_amount_too_large_for_a_decimal_is_schema_invalid_and_the_raw_text_is_kept()
    {
        var text = ValidJson(o => SetPath(o, "totals.invoice_total", OversizedAmount));

        var result = await ExtractAsync(Respond(text));

        var invalid = Assert.IsType<ExtractionOutcome.SchemaInvalid>(result.Outcome);
        Assert.Contains("fits in a decimal", invalid.Error, StringComparison.Ordinal);
        Assert.Equal(text, result.RawOutput);
    }

    [Theory]
    [InlineData("access_key", "bad")]
    [InlineData("access_key", "35260311222333000181550010000001231000012346\n")]
    [InlineData("access_key", "3526 0311 2223 3300 0181 5500 1000 0001 2310 0001 2346")]
    [InlineData("access_key", "352603ab1c2d3e000130550010000001231000012346")]
    [InlineData("access_key", "")]
    [InlineData("issuer.cnpj", "11.222.333/0001-81")]
    [InlineData("issuer.cnpj", " ")]
    [InlineData("issuer.uf", "sp")]
    [InlineData("recipient.tax_id", "x")]
    [InlineData("recipient.tax_id", "")]
    [InlineData("recipient.tax_id", "529.982.247-25")]
    [InlineData("recipient.uf", "SPP")]
    [InlineData("items[0].ncm", "7318150")]
    [InlineData("items[1].ncm", "7318150")]
    [InlineData("items[0].cfop", "51020")]
    [InlineData("items[0].cst_csosn", "00")]
    public async Task A_value_violating_its_schema_pattern_is_schema_invalid_and_names_the_field(string path, string value)
    {
        var text = ValidJson(o => SetPath(o, path, value));

        var result = await ExtractAsync(Respond(text));

        var invalid = Assert.IsType<ExtractionOutcome.SchemaInvalid>(result.Outcome);
        Assert.Contains(path, invalid.Error, StringComparison.Ordinal);
        Assert.Equal(text, result.RawOutput);
    }

    [Fact]
    public async Task Alphanumeric_access_key_and_cnpj_forms_are_accepted()
    {
        var text = ValidJson(o =>
        {
            o["access_key"] = "35260312ABC34501DE35550010000001231000012347";
            SetPath(o, "issuer.cnpj", "12ABC34501DE35");
            SetPath(o, "recipient.tax_id", "11222333000181");
        });

        var result = await ExtractAsync(Respond(text));

        var success = Assert.IsType<ExtractionOutcome.Success>(result.Outcome);
        Assert.Equal("12ABC34501DE35", success.Invoice.Issuer.Cnpj);
        Assert.Equal("11222333000181", success.Invoice.Recipient.TaxId);
    }

    [Fact]
    public async Task Every_pattern_in_the_model_facing_schema_is_enforced_by_the_extractor()
    {
        var schema = JsonNode.Parse(ExtractionContract.Default.OutputSchemaJson)!.AsObject();
        var defs = schema["$defs"]!.AsObject();
        var patternPaths = new List<string>();
        CollectPatternPaths(schema, string.Empty, defs, patternPaths);

        // A walker that finds nothing must fail; a new pattern in the schema must be enforced and listed here.
        string[] expected =
        [
            "access_key",
            "issuer.cnpj",
            "issuer.uf",
            "recipient.tax_id",
            "recipient.uf",
            "items[0].ncm",
            "items[0].cst_csosn",
            "items[0].cfop",
            "items[0].quantity",
            "items[0].unit_price",
            "items[0].total",
            "items[0].icms_base",
            "items[0].icms_rate",
            "items[0].icms_amount",
            "items[0].ipi_rate",
            "items[0].ipi_amount",
            "totals.icms_base",
            "totals.icms_amount",
            "totals.icms_st_base",
            "totals.icms_st_amount",
            "totals.products_total",
            "totals.freight",
            "totals.insurance",
            "totals.discount",
            "totals.other_expenses",
            "totals.ipi_amount",
            "totals.invoice_total",
            "installments[0].amount",
        ];
        Assert.NotEmpty(patternPaths);
        Assert.Equal(expected.Order(StringComparer.Ordinal), patternPaths.Order(StringComparer.Ordinal));

        foreach (var path in patternPaths)
        {
            var text = ValidJson(o => SetPath(o, path, "bad"));

            var result = await ExtractAsync(Respond(text));

            Assert.True(
                result.Outcome is ExtractionOutcome.SchemaInvalid,
                $"The schema pattern at '{path}' is not enforced: a violating value gave {result.Outcome.Status}.");
        }
    }


    // ---------------------------------------------------------------- stop reason first (EXT-02)

    [Fact]
    public async Task Refusal_is_refused_even_when_the_text_is_valid_invoice_json()
    {
        var gateway = Respond(ValidJson(), LlmStopReason.Refusal, stopDetail: "policy");
        var result = await ExtractAsync(gateway);

        var refused = Assert.IsType<ExtractionOutcome.Refused>(result.Outcome);
        Assert.Equal("policy", refused.Detail);
        Assert.NotNull(Assert.Single(result.Attempts).Response);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Max_tokens_is_truncated_even_when_the_text_looks_like_complete_json()
    {
        var gateway = Respond(ValidJson(), LlmStopReason.MaxTokens);
        var result = await ExtractAsync(gateway);

        Assert.IsType<ExtractionOutcome.Truncated>(result.Outcome);
        Assert.NotNull(Assert.Single(result.Attempts).Response);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(LlmStopReason.StopSequence)]
    [InlineData(LlmStopReason.Other)]
    public async Task Other_stop_reasons_are_parsed_like_end_turn(LlmStopReason stopReason)
    {
        // Flagged assumption for EXT-02: only refusal and max_tokens bypass parsing.
        var result = await ExtractAsync(Respond(ValidJson(), stopReason));

        Assert.IsType<ExtractionOutcome.Success>(result.Outcome);
    }

    // ---------------------------------------------------------------- infrastructure failures

    [Theory]
    [MemberData(nameof(FailureKinds))]
    public async Task Every_gateway_failure_kind_becomes_a_typed_infrastructure_failure(LlmFailureKind kind)
    {
        var gateway = new CapturingGateway(_ => throw new LlmGatewayException(kind, "boom", 529, "req_123"));
        var result = await ExtractAsync(gateway);

        var failure = Assert.IsType<ExtractionOutcome.InfrastructureFailure>(result.Outcome);
        Assert.Equal(kind, failure.Kind);
        Assert.Equal(529, failure.HttpStatus);
        Assert.Equal("req_123", failure.RequestId);
        Assert.Equal("boom", failure.Message);
        Assert.Null(result.RawOutput);
        var attempt = Assert.Single(result.Attempts);
        Assert.Null(attempt.Response);
        Assert.Null(attempt.RawOutput);
        Assert.Empty(attempt.Findings);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_becoming_an_outcome()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var gateway = new CapturingGateway((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Response(ValidJson());
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewExtractor(gateway).ExtractAsync(Pdf, Context, cts.Token));
    }

    // ---------------------------------------------------------------- provider request content (EXT-01)

    [Fact]
    public async Task Provider_request_holds_only_the_document_prompt_and_schema()
    {
        var gateway = Respond(ValidJson());
        await ExtractAsync(gateway);

        var request = Assert.Single(gateway.Requests);
        var contract = ExtractionContract.Default;
        Assert.Equal(Settings.Model, request.Model);
        Assert.Equal(Settings.MaxTokens, request.MaxTokens);
        Assert.Equal(contract.Prompt, request.Prompt);
        Assert.Equal(contract.OutputSchemaJson, request.OutputSchemaJson);
        Assert.Equal("application/pdf", request.Document.MediaType);
        Assert.True(Pdf.AsSpan().SequenceEqual(request.Document.Content.Span));
    }

    [Fact]
    public void Provider_request_type_has_no_member_that_could_carry_eval_metadata()
    {
        Assert.Equal(
            ["CacheDocument", "Document", "FollowUps", "MaxTokens", "Model", "OutputSchemaJson", "Prompt"],
            PublicInstanceProperties(typeof(LlmRequest)));
        Assert.Equal(["Role", "Text"], PublicInstanceProperties(typeof(LlmTurn)));
        Assert.Equal(["Content", "MediaType"], PublicInstanceProperties(typeof(LlmDocument)));

        var extract = typeof(IInvoiceExtractor).GetMethod(nameof(IInvoiceExtractor.ExtractAsync))!;
        Assert.Equal(
            [typeof(ReadOnlyMemory<byte>), typeof(ExtractionContext), typeof(CancellationToken)],
            extract.GetParameters().Select(p => p.ParameterType).ToArray());

        // The context may carry the reference date and nothing else about the eval case.
        Assert.Equal(["ReferenceDate"], PublicInstanceProperties(typeof(ExtractionContext)));
    }

    // ---------------------------------------------------------------- validation (VAL-01, D-14)

    [Fact]
    public async Task A_clean_invoice_is_a_success_with_no_findings_and_one_initial_attempt()
    {
        var result = await ExtractAsync(Respond(ValidJson()));

        Assert.IsType<ExtractionOutcome.Success>(result.Outcome);
        Assert.Empty(result.Findings);
        Assert.Equal(Context.ReferenceDate, result.ReferenceDate);
        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(0, attempt.Index);
        Assert.Equal(AttemptKind.Initial, attempt.Kind);
        Assert.Equal(ExtractionContract.Default.PromptVersion, attempt.PromptVersion);
        Assert.Same(result.Outcome, attempt.Outcome);
        Assert.NotNull(attempt.Response);
        Assert.Equal(result.RawOutput, attempt.RawOutput);
    }

    [Fact]
    public async Task An_error_finding_is_validation_failed_and_keeps_the_candidate_the_findings_and_the_raw_text()
    {
        var text = ValidJson(o => SetPath(o, "totals.invoice_total", "156.00"));

        var result = await ExtractAsync(Respond(text));

        var failed = Assert.IsType<ExtractionOutcome.ValidationFailed>(result.Outcome);
        Assert.Equal("156.00", failed.Candidate.Totals.InvoiceTotal.ToString());
        Assert.Equal(
            [RuleIds.TOTAL_VNF_FORMULA, RuleIds.DUP_SUM],
            failed.Findings.Select(f => f.RuleId).ToArray());
        Assert.All(failed.Findings, f => Assert.Equal(FindingSeverity.Error, f.Severity));
        Assert.Equal(failed.Findings, result.Findings);
        Assert.Equal(text, result.RawOutput);
        Assert.Equal("validation_failed", result.Outcome.Status);
        Assert.Single(result.Attempts);
    }

    [Fact]
    public async Task A_warning_only_invoice_is_a_success_that_carries_its_warnings()
    {
        var text = ValidJson(o => SetPath(o, "items[0].cst_csosn", "010"));

        var result = await ExtractAsync(Respond(text));

        Assert.IsType<ExtractionOutcome.Success>(result.Outcome);
        var warning = Assert.Single(result.Findings);
        Assert.Equal(RuleIds.TAX_CODE_UNSUPPORTED, warning.RuleId);
        Assert.Equal(FindingSeverity.Warning, warning.Severity);
        Assert.Equal(result.Findings, Assert.Single(result.Attempts).Findings);
    }

    [Fact]
    public async Task The_reference_date_decides_date_plausibility_and_is_echoed()
    {
        var farFuture = new ExtractionContext(new DateOnly(2006, 5, 1));

        var result = await NewExtractor(Respond(ValidJson())).ExtractAsync(Pdf, farFuture, TestContext.Current.CancellationToken);

        var failed = Assert.IsType<ExtractionOutcome.ValidationFailed>(result.Outcome);
        Assert.Contains(failed.Findings, f => f.RuleId == RuleIds.DATE_PLAUSIBLE);
        Assert.Equal(farFuture.ReferenceDate, result.ReferenceDate);
    }

    [Fact]
    public async Task The_reference_date_never_reaches_the_provider_request()
    {
        var gateway = Respond(ValidJson());
        await ExtractAsync(gateway);

        var request = Assert.Single(gateway.Requests);
        Assert.DoesNotContain("2026-10-01", request.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-10-01", request.OutputSchemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_schema_invalid_answer_is_never_validated()
    {
        var result = await ExtractAsync(Respond("Sorry, I could not read that invoice."));

        Assert.IsType<ExtractionOutcome.SchemaInvalid>(result.Outcome);
        Assert.Empty(result.Findings);
        var attempt = Assert.Single(result.Attempts);
        Assert.Equal("schema_invalid", attempt.Outcome.Status);
        Assert.Empty(attempt.Findings);
    }

    // ---------------------------------------------------------------- helpers

    private static string[] PublicInstanceProperties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)];

    /// <summary>The shared hand-checked fixture, read fresh per call so a test can mutate it.</summary>
    private static string ValidJson(Action<JsonObject>? mutate = null)
    {
        var invoice = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "vectors", "valid-invoice.json")))!.AsObject();
        mutate?.Invoke(invoice);
        return invoice.ToJsonString();
    }

    /// <summary>Walks up from the test binary to the directory holding <c>dotnet/Carimbo.slnx</c>.</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet", "Carimbo.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repo root (dotnet/Carimbo.slnx) above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Sets a dotted snake_case JSON path such as <c>issuer.cnpj</c> or <c>items[1].ncm</c> on the invoice
    /// object; a segment may carry an <c>[index]</c>.
    /// </summary>
    private static void SetPath(JsonObject invoice, string path, string value)
    {
        var segments = path.Split('.');
        JsonObject target = invoice;
        foreach (var segment in segments[..^1])
        {
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            if (bracket < 0)
            {
                target = target[segment]!.AsObject();
            }
            else
            {
                var index = int.Parse(segment[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
                target = target[segment[..bracket]]!.AsArray()[index]!.AsObject();
            }
        }

        target[segments[^1]] = value;
    }

    /// <summary>
    /// Collects the path of every node carrying a <c>pattern</c>, following <c>$ref</c> into the
    /// definitions at any depth and array <c>items</c> (index 0), for example <c>items[0].ncm</c>.
    /// </summary>
    private static void CollectPatternPaths(JsonObject schema, string path, JsonObject defs, List<string> found)
    {
        if (schema["$ref"] is JsonNode reference)
        {
            const string prefix = "#/$defs/";
            var target = reference.GetValue<string>();
            Assert.StartsWith(prefix, target, StringComparison.Ordinal);
            CollectPatternPaths(defs[target[prefix.Length..]]!.AsObject(), path, defs, found);
            return;
        }

        if (schema["pattern"] is not null)
        {
            found.Add(path);
        }

        if (schema["items"] is JsonObject items)
        {
            CollectPatternPaths(items, $"{path}[0]", defs, found);
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, property) in properties)
            {
                CollectPatternPaths(property!.AsObject(), path.Length == 0 ? name : $"{path}.{name}", defs, found);
            }
        }
    }

    /// <summary>Collects every property name that appears anywhere in the schema, through $ref and arrays.</summary>
    private static void CollectPropertyNames(JsonObject schema, JsonObject defs, SortedSet<string> names)
    {
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, property) in properties)
            {
                names.Add(name);
                CollectPropertyNames(property!.AsObject(), defs, names);
            }
        }

        if (schema["items"] is JsonObject items)
        {
            CollectPropertyNames(items, defs, names);
        }

        if (schema["$ref"] is JsonNode reference)
        {
            CollectPropertyNames(defs[reference.GetValue<string>()["#/$defs/".Length..]]!.AsObject(), defs, names);
        }
    }

    private static LlmResponse Response(
        string text,
        LlmStopReason stopReason = LlmStopReason.EndTurn,
        string? stopDetail = null) =>
        new(
            text,
            stopReason,
            stopDetail,
            new LlmUsage(10, 20, 0, 0, 0),
            Settings.Model,
            "returned-model",
            "msg_1",
            TimeSpan.FromMilliseconds(5),
            1);

    private static CapturingGateway Respond(
        string text,
        LlmStopReason stopReason = LlmStopReason.EndTurn,
        string? stopDetail = null) =>
        new(_ => Response(text, stopReason, stopDetail));

    private static InvoiceExtractor NewExtractor(ILlmGateway gateway) =>
        new(gateway, ExtractionContract.Default, Settings, new InvoiceValidator(new ValidationOptions()));

    private static Task<ExtractionResult> ExtractAsync(ILlmGateway gateway) =>
        NewExtractor(gateway).ExtractAsync(Pdf, Context, TestContext.Current.CancellationToken);

    /// <summary>Records every request and answers with a scripted response or exception.</summary>
    private sealed class CapturingGateway(Func<LlmRequest, CancellationToken, LlmResponse> script) : ILlmGateway
    {
        public CapturingGateway(Func<LlmRequest, LlmResponse> script)
            : this((request, _) => script(request))
        {
        }

        public List<LlmRequest> Requests { get; } = [];

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(script(request, cancellationToken));
        }
    }
}
