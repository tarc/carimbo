using System.Reflection;
using System.Text.Json.Nodes;
using Carimbo.Llm;
using Xunit;

namespace Carimbo.Extraction.Tests;

public class ExtractorTests
{
    private const string AccessKey = "35260112345678000195550010000001231000001230";
    private const string OversizedAmount = "99999999999999999999999999999999.00";

    private static readonly byte[] Pdf = "%PDF-1.4\nsynthetic"u8.ToArray();

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
        Assert.Equal("12345678000195", success.Invoice.Issuer.Cnpj);
        Assert.Equal("Emitente Sintetica Ltda", success.Invoice.Issuer.Name);
        Assert.Equal("98765432000110", success.Invoice.Recipient.Cnpj);
        Assert.Equal("Destinatario Sintetico SA", success.Invoice.Recipient.Name);
        Assert.Equal("1234.50", success.Invoice.TotalAmount.ToString());
        Assert.Equal(text, result.RawOutput);
    }

    [Fact]
    public async Task Result_carries_the_requested_model_prompt_version_and_schema_hash()
    {
        var result = await ExtractAsync(Respond(ValidJson()));

        Assert.Equal(Settings.Model, result.ModelRequested);
        Assert.Equal(ExtractionContract.Default.PromptVersion, result.PromptVersion);
        Assert.Equal(ExtractionContract.Default.OutputSchemaSha256, result.SchemaSha256);
    }

    // ---------------------------------------------------------------- schema_invalid

    [Theory]
    [InlineData("unknown_property")]
    [InlineData("missing_property")]
    [InlineData("truncated_json")]
    [InlineData("prose")]
    [InlineData("pt_br_money")]
    [InlineData("json_null")]
    [InlineData("trailing_garbage")]
    public async Task Invalid_model_output_is_schema_invalid_and_the_raw_text_is_kept(string scenario)
    {
        var text = scenario switch
        {
            "unknown_property" => ValidJson(o => o["extra_field"] = "surprise"),
            "missing_property" => ValidJson(o => o.Remove("series")),
            "truncated_json" => ValidJson()[..40],
            "prose" => "Sorry, I could not read that invoice.",
            "pt_br_money" => ValidJson(o => o["total_amount"] = "12,34"),
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
        var text = ValidJson(o => o["total_amount"] = OversizedAmount);

        var result = await ExtractAsync(Respond(text));

        var invalid = Assert.IsType<ExtractionOutcome.SchemaInvalid>(result.Outcome);
        Assert.Contains("fits in a decimal", invalid.Error, StringComparison.Ordinal);
        Assert.Equal(text, result.RawOutput);
    }

    // ---------------------------------------------------------------- stop reason first (EXT-02)

    [Fact]
    public async Task Refusal_is_refused_even_when_the_text_is_valid_invoice_json()
    {
        var gateway = Respond(ValidJson(), LlmStopReason.Refusal, stopDetail: "policy");
        var result = await ExtractAsync(gateway);

        var refused = Assert.IsType<ExtractionOutcome.Refused>(result.Outcome);
        Assert.Equal("policy", refused.Detail);
        Assert.NotNull(result.Response);
    }

    [Fact]
    public async Task Max_tokens_is_truncated_even_when_the_text_looks_like_complete_json()
    {
        var gateway = Respond(ValidJson(), LlmStopReason.MaxTokens);
        var result = await ExtractAsync(gateway);

        Assert.IsType<ExtractionOutcome.Truncated>(result.Outcome);
        Assert.NotNull(result.Response);
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
        Assert.Null(result.Response);
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
            () => NewExtractor(gateway).ExtractAsync(Pdf, cts.Token));
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
            ["Document", "MaxTokens", "Model", "OutputSchemaJson", "Prompt"],
            PublicInstanceProperties(typeof(LlmRequest)));
        Assert.Equal(["Content", "MediaType"], PublicInstanceProperties(typeof(LlmDocument)));

        var extract = typeof(IInvoiceExtractor).GetMethod(nameof(IInvoiceExtractor.ExtractAsync))!;
        Assert.Equal(
            [typeof(ReadOnlyMemory<byte>), typeof(CancellationToken)],
            extract.GetParameters().Select(p => p.ParameterType).ToArray());
    }

    // ---------------------------------------------------------------- helpers

    private static string[] PublicInstanceProperties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)];

    private static string ValidJson(Action<JsonObject>? mutate = null)
    {
        var invoice = new JsonObject
        {
            ["access_key"] = AccessKey,
            ["number"] = 123,
            ["series"] = 1,
            ["issue_date"] = "2026-03-15",
            ["issuer"] = new JsonObject { ["cnpj"] = "12345678000195", ["name"] = "Emitente Sintetica Ltda" },
            ["recipient"] = new JsonObject { ["cnpj"] = "98765432000110", ["name"] = "Destinatario Sintetico SA" },
            ["total_amount"] = "1234.50",
        };
        mutate?.Invoke(invoice);
        return invoice.ToJsonString();
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
        new(gateway, ExtractionContract.Default, Settings);

    private static Task<ExtractionResult> ExtractAsync(ILlmGateway gateway) =>
        NewExtractor(gateway).ExtractAsync(Pdf, TestContext.Current.CancellationToken);

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
