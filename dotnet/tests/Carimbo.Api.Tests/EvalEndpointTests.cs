using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Api;
using Carimbo.Extraction;
using Carimbo.Llm;
using Carimbo.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Carimbo.Api.Tests;

public class EvalEndpointTests
{
    internal const string Key = "test-eval-key-4f9c1b7e2a";
    private const string Route = "/eval/extractions";
    private const string AccessKey = "35260311222333000181550010000001231000012346";
    private const string ReferenceDate = "2026-10-01";
    private const string OversizedAmount = "99999999999999999999999999999999.00";

    internal static readonly byte[] Pdf = "%PDF-1.4\nsynthetic"u8.ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- availability

    [Fact]
    public async Task Production_with_key_and_gateway_has_no_eval_route_but_still_serves_healthz()
    {
        await using var host = await TestHost.StartAsync("Production", Key, new ScriptedGateway());

        var eval = await host.PostAsync(Body(), Key);
        var health = await host.Client.GetAsync("/healthz", Ct);

        Assert.Equal(HttpStatusCode.NotFound, eval.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Development_without_a_configured_key_has_no_eval_route()
    {
        await using var host = await TestHost.StartAsync("Development", key: null, new ScriptedGateway());

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Development_with_a_key_but_no_gateway_has_no_eval_route()
    {
        await using var host = await TestHost.StartAsync("Development", Key, gateway: null);

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- authentication

    [Theory]
    [InlineData("Development")]
    [InlineData("Eval")]
    public async Task Missing_or_wrong_key_is_unauthorized_and_the_right_key_is_accepted(string environment)
    {
        await using var host = await TestHost.StartAsync(environment, Key, new ScriptedGateway());

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.PostAsync(Body(), key: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.PostAsync(Body(), "wrong-key")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Body(), Key)).StatusCode);
    }

    [Fact]
    public async Task Authentication_is_checked_before_the_body_is_read()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostRawAsync("{ not json", "wrong-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------- validation

    [Theory]
    [InlineData("invalid_base64", "content_base64")]
    [InlineData("not_a_pdf", "content_base64")]
    [InlineData("png_media_type", "media_type")]
    [InlineData("contract_version_1", "contract_version")]
    [InlineData("contract_version_2", "contract_version")]
    [InlineData("unknown_request_field", "body")]
    [InlineData("unknown_document_field", "body")]
    [InlineData("missing_case_id", "body")]
    public async Task Malformed_requests_are_bad_requests_that_name_the_field(string scenario, string expectedField)
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());
        var body = scenario switch
        {
            "invalid_base64" => Body(mutate: o => o["document"]!["content_base64"] = "not base64 !!"),
            "not_a_pdf" => Body(pdf: "<html>definitely not a pdf</html>"u8.ToArray()),
            "png_media_type" => Body(mutate: o => o["document"]!["media_type"] = "image/png"),
            "contract_version_1" => Body(mutate: o => o["contract_version"] = "1"),
            "contract_version_2" => Body(mutate: o => o["contract_version"] = "2"),
            "unknown_request_field" => Body(mutate: o => o["extra"] = "surprise"),
            "unknown_document_field" => Body(mutate: o => o["document"]!["extra"] = "surprise"),
            "missing_case_id" => Body(mutate: o => o.Remove("case_id")),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var response = await host.PostAsync(body, Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedField, text);
        Assert.DoesNotContain(Convert.ToBase64String(Pdf), text);
    }

    [Fact]
    public async Task A_request_declaring_more_than_ten_megabytes_is_rejected_with_413()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());
        var oversized = Body(mutate: o => o["document"]!["content_base64"] = new string('A', (10 * 1024 * 1024) + 1));

        var response = await host.PostAsync(oversized, Key);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task The_eval_route_carries_a_ten_megabyte_server_size_limit()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var endpoint = host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == Route);

        var limit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        Assert.NotNull(limit);
        Assert.Equal(10 * 1024 * 1024, limit.MaxRequestBodySize);
    }

    // ---------------------------------------------------------------- outcomes stay at 200

    [Fact]
    public async Task A_valid_extraction_returns_the_typed_invoice()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostAsync(Body(caseId: "case-042"), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("3", (string?)json["contract_version"]);
        Assert.Equal("case-042", (string?)json["case_id"]);
        Assert.Equal("success", (string?)json["outcome"]!["status"]);
        Assert.Equal(AccessKey, (string?)json["outcome"]!["invoice"]!["access_key"]);
        Assert.Equal("155.00", (string?)json["outcome"]!["invoice"]!["totals"]!["invoice_total"]);
        Assert.Equal("12ABC34501DE35", (string?)json["outcome"]!["invoice"]!["recipient"]!["tax_id"]);
    }

    [Fact]
    public async Task A_refusal_is_reported_as_refused_with_http_200()
    {
        var gateway = new ScriptedGateway(_ => Task.FromResult(
            Response(ValidInvoiceJson(), LlmStopReason.Refusal, "policy")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("refused", (string?)json["outcome"]!["status"]);
        Assert.Equal("refused", (string?)json["outcome"]!["failure"]!["kind"]);
        Assert.Equal("refusal", (string?)json["stop_reason"]);
    }

    [Fact]
    public async Task A_gateway_failure_is_a_typed_infrastructure_failure_not_a_server_error()
    {
        var gateway = new ScriptedGateway(_ => throw new LlmGatewayException(
            LlmFailureKind.Overloaded, "provider overloaded", 529, "req_abc"));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("infrastructure_failure", (string?)json["outcome"]!["status"]);
        Assert.Equal("overloaded", (string?)json["outcome"]!["failure"]!["kind"]);
        Assert.Equal(529, (int?)json["outcome"]!["failure"]!["http_status"]);
        Assert.Equal("req_abc", (string?)json["outcome"]!["failure"]!["request_id"]);
    }

    [Fact]
    public async Task An_amount_too_large_for_a_decimal_is_schema_invalid_with_http_200_and_its_cost()
    {
        var text = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = OversizedAmount);
        var gateway = new ScriptedGateway(_ => Task.FromResult(
            Response(text, model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["status"]);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["failure"]!["kind"]);
        Assert.Null(json["outcome"]!["invoice"]);
        Assert.Equal(text, (string?)json["outcome"]!["raw_output"]);
        Assert.Equal("0.00200000", (string?)json["cost_usd"]);
    }

    [Fact]
    public async Task A_null_items_element_is_schema_invalid_with_http_200_and_its_paid_attempt()
    {
        var text = ValidInvoiceJson(invoice => invoice["items"] = new JsonArray(JsonValue.Create<string?>(null)));
        var gateway = new ScriptedGateway(_ => Task.FromResult(
            Response(text, model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["status"]);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["failure"]!["kind"]);
        Assert.Contains("items[0]", (string?)json["outcome"]!["failure"]!["message"]);
        Assert.Null(json["outcome"]!["invoice"]);
        Assert.Empty(json["outcome"]!["findings"]!.AsArray());
        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Equal("initial", (string?)attempt["kind"]);
        Assert.Equal("schema_invalid", (string?)attempt["status"]);
        Assert.Equal(text, (string?)attempt["raw_output"]);
        Assert.Empty(attempt["findings"]!.AsArray());
        Assert.Equal("0.00200000", (string?)attempt["cost_usd"]);
        Assert.Equal("0.00200000", (string?)json["cost_usd"]);
        AssertTopLevelEqualsSums(json);
    }

    [Theory]
    [InlineData("\"CNPJ\"")]
    [InlineData("0")]
    public async Task A_tax_id_kind_outside_the_schema_is_schema_invalid_with_http_200(string rawJsonValue)
    {
        var text = ValidInvoiceJson(invoice => invoice["recipient"]!.AsObject()["tax_id_kind"] = JsonNode.Parse(rawJsonValue));
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(text, model: "claude-haiku-4-5")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["status"]);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["failure"]!["kind"]);
        Assert.Null(json["outcome"]!["invoice"]);
        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Equal("schema_invalid", (string?)attempt["status"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_null_installments_element_is_schema_invalid_with_http_200()
    {
        var text = ValidInvoiceJson(invoice => invoice["installments"] = new JsonArray((JsonNode?)null));
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(text, model: "claude-haiku-4-5")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["status"]);
        Assert.Contains("installments[0]", (string?)json["outcome"]!["failure"]!["message"]);
        Assert.Null(json["outcome"]!["invoice"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_null_items_element_in_the_middle_is_schema_invalid_with_http_200()
    {
        var text = ValidInvoiceJson(invoice =>
        {
            var items = invoice["items"]!.AsArray();
            invoice["items"] = new JsonArray(items[0]!.DeepClone(), null, items[1]!.DeepClone());
        });
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(text, model: "claude-haiku-4-5")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("schema_invalid", (string?)json["outcome"]!["status"]);
        Assert.Contains("items[1]", (string?)json["outcome"]!["failure"]!["message"]);
        Assert.Null(json["outcome"]!["invoice"]);
    }

    [Fact]
    public async Task A_null_element_in_a_repair_attempt_keeps_every_paid_attempt_in_the_response()
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var nullInstallment = ValidInvoiceJson(invoice => invoice["installments"] = new JsonArray((JsonNode?)null));
        var gateway = new ScriptedGateway(request => Task.FromResult(request.FollowUps.Count == 0
            ? Response(wrong, model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))
            : Response(nullInstallment, model: "claude-haiku-4-5", usage: new LlmUsage(500, 100, 0, 0, 0))));
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway, new Dictionary<string, string?> { ["Extraction:MaxRepairs"] = "1" });

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("validation_failed", (string?)json["outcome"]!["status"]);
        Assert.Equal("156.00", (string?)json["outcome"]!["invoice"]!["totals"]!["invoice_total"]);
        var attempts = json["attempts"]!.AsArray();
        Assert.Equal(["validation_failed", "schema_invalid"], attempts.Select(a => (string?)a!["status"]).ToArray());
        Assert.Equal(["initial", "repair"], attempts.Select(a => (string?)a!["kind"]).ToArray());
        Assert.Equal("0.00200000", (string?)attempts[0]!["cost_usd"]);
        Assert.Equal("0.00100000", (string?)attempts[1]!["cost_usd"]);
        Assert.Equal(nullInstallment, (string?)attempts[1]!["raw_output"]);
        Assert.Equal("0.00300000", (string?)json["cost_usd"]);
        AssertTopLevelEqualsSums(json);
    }

    // ---------------------------------------------------------------- contract 2: findings and attempts (tracer)

    [Fact]
    public async Task A_clean_extraction_reports_no_findings_one_initial_attempt_and_the_reference_date_used()
    {
        var gateway = new ScriptedGateway(_ => Task.FromResult(
            Response(ValidInvoiceJson(), model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("success", (string?)json["outcome"]!["status"]);
        Assert.Empty(json["outcome"]!["findings"]!.AsArray());
        Assert.Equal(ReferenceDate, (string?)json["effective"]!["reference_date"]);

        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Equal(0, (int?)attempt["index"]);
        Assert.Equal("initial", (string?)attempt["kind"]);
        Assert.Equal("extract-003", (string?)attempt["prompt_version"]);
        Assert.Equal("success", (string?)attempt["status"]);
        Assert.Empty(attempt["findings"]!.AsArray());
        Assert.Equal(AccessKey, (string?)attempt["invoice"]!["access_key"]);
        Assert.Equal(ValidInvoiceJson(), (string?)attempt["raw_output"]);
        Assert.Equal("end_turn", (string?)attempt["stop_reason"]);

        AssertUsageEquals(attempt["usage"]!, json["usage"]!);
        Assert.Equal("0.00200000", (string?)attempt["cost_usd"]);
        Assert.Equal((string?)attempt["cost_usd"], (string?)json["cost_usd"]);
    }

    [Fact]
    public async Task A_total_that_does_not_add_up_is_validation_failed_with_http_200_and_the_candidate_returned()
    {
        var text = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(text)));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("validation_failed", (string?)json["outcome"]!["status"]);
        Assert.Equal("156.00", (string?)json["outcome"]!["invoice"]!["totals"]!["invoice_total"]);
        Assert.Null(json["outcome"]!["failure"]);
        Assert.Equal(text, (string?)json["outcome"]!["raw_output"]);

        var findings = json["outcome"]!["findings"]!.AsArray();
        Assert.Equal(["TOTAL_VNF_FORMULA", "DUP_SUM"], findings.Select(f => (string?)f!["rule_id"]).ToArray());
        Assert.All(findings, f => Assert.Equal("error", (string?)f!["severity"]));
        var first = findings[0]!;
        Assert.Equal("totals.invoice_total", (string?)first["field"]);
        Assert.Equal("155.00", (string?)first["expected"]);
        Assert.Equal("156.00", (string?)first["actual"]);

        // The same wrong answer on every call exhausts the default repair budget: 1 initial + 2 repairs.
        var attempts = json["attempts"]!.AsArray();
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, a =>
        {
            Assert.Equal("validation_failed", (string?)a!["status"]);
            Assert.Equal(2, a["findings"]!.AsArray().Count);
        });
    }

    // ---------------------------------------------------------------- cost

    [Fact]
    public async Task A_priced_model_reports_cost_usd_to_eight_places_and_the_pricing_version()
    {
        var gateway = new ScriptedGateway(_ => Task.FromResult(
            Response(ValidInvoiceJson(), model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("0.00200000", (string?)json["cost_usd"]);
        Assert.Null((string?)json["cost_warning"]);
        Assert.Equal("anthropic-2026-10-04", (string?)json["effective"]!["pricing_version"]);
    }

    [Fact]
    public async Task An_unpriced_model_reports_null_cost_and_an_explicit_warning_never_zero()
    {
        var gateway = new ScriptedGateway(_ => Task.FromResult(
            Response(ValidInvoiceJson(), model: "mystery-model")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Null((string?)json["cost_usd"]);
        Assert.Equal("unpriced_model:mystery-model", (string?)json["cost_warning"]);
        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Null(attempt["cost_usd"]);
        Assert.Equal("unpriced_model:mystery-model", (string?)attempt["cost_warning"]);
        AssertTopLevelEqualsSums(json);
        Assert.Equal("anthropic-2026-10-04", (string?)json["effective"]!["pricing_version"]);
    }

    [Fact]
    public async Task An_infrastructure_failure_has_no_cost_and_no_warning_but_still_names_the_pricing_version()
    {
        var gateway = new ScriptedGateway(_ => throw new LlmGatewayException(
            LlmFailureKind.Overloaded, "provider overloaded", 529, "req_abc"));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("infrastructure_failure", (string?)json["outcome"]!["status"]);
        Assert.Null((string?)json["cost_usd"]);
        Assert.Null((string?)json["cost_warning"]);
        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Equal(0, (int?)attempt["index"]);
        Assert.Equal("initial", (string?)attempt["kind"]);
        Assert.Equal("infrastructure_failure", (string?)attempt["status"]);
        Assert.Null(attempt["usage"]);
        Assert.Null(attempt["cost_usd"]);
        Assert.Null(attempt["cost_warning"]);
        Assert.Null(attempt["latency_ms"]);
        Assert.Null(attempt["stop_reason"]);
        Assert.Null(attempt["raw_output"]);
        Assert.Equal("overloaded", (string?)attempt["failure"]!["kind"]);
        Assert.Equal(0, (long?)json["usage"]!["input_tokens"]);
        AssertTopLevelEqualsSums(json);
        Assert.Equal("anthropic-2026-10-04", (string?)json["effective"]!["pricing_version"]);
    }

    // ---------------------------------------------------------------- contract 2: reference date (D-11, T-02-23)

    [Theory]
    [InlineData("\"2026-13-01\"")]
    [InlineData("\"01/10/2026\"")]
    [InlineData("\"2026-10-1\"")]
    [InlineData("\"2026-10-01T00:00:00Z\"")]
    [InlineData("\" 2026-10-01\"")]
    [InlineData("\"\"")]
    [InlineData("20261001")]
    [InlineData("true")]
    public async Task A_malformed_reference_date_is_a_bad_request_naming_the_field_and_never_echoing_the_value(string rawJson)
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());
        var submitted = JsonNode.Parse(rawJson);

        var response = await host.PostAsync(Body(mutate: o => o["reference_date"] = submitted), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reference_date", text);
        var echoed = submitted!.GetValueKind() == JsonValueKind.String ? submitted.GetValue<string>() : rawJson;
        if (echoed.Length > 0)
        {
            Assert.DoesNotContain(echoed, text);
        }

        Assert.DoesNotContain(Convert.ToBase64String(Pdf), text);
    }

    [Fact]
    public async Task Without_a_reference_date_the_utc_date_of_the_registered_time_provider_is_used()
    {
        // 23:59:59 UTC: a local-time or next-day mistake would show as 2031-05-07 or 2031-05-05.
        var clock = new FixedTimeProvider(new DateTimeOffset(2031, 5, 6, 23, 59, 59, TimeSpan.Zero));
        await using var host = await TestHost.StartAsync(
            "Development", Key, new ScriptedGateway(), configureServices: services => services.AddSingleton<TimeProvider>(clock));

        var json = await ReadJsonAsync(await host.PostAsync(Body(mutate: o => o.Remove("reference_date")), Key));

        Assert.Equal("2031-05-06", (string?)json["effective"]!["reference_date"]);
        Assert.Equal("success", (string?)json["outcome"]!["status"]);
    }

    [Fact]
    public async Task The_default_reference_date_decides_date_plausibility()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero));
        await using var host = await TestHost.StartAsync(
            "Development", Key, new ScriptedGateway(), configureServices: services => services.AddSingleton<TimeProvider>(clock));

        // The fixture is issued on 2026-03-15, two days after the clock (one day of slack is allowed): not plausible.
        var json = await ReadJsonAsync(await host.PostAsync(Body(mutate: o => o.Remove("reference_date")), Key));

        Assert.Equal("validation_failed", (string?)json["outcome"]!["status"]);
        Assert.Contains(
            "DATE_PLAUSIBLE",
            json["outcome"]!["findings"]!.AsArray().Select(f => (string?)f!["rule_id"]));
    }

    [Fact]
    public async Task An_explicit_reference_date_wins_over_the_clock()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2031, 5, 6, 12, 0, 0, TimeSpan.Zero));
        await using var host = await TestHost.StartAsync(
            "Development", Key, new ScriptedGateway(), configureServices: services => services.AddSingleton<TimeProvider>(clock));

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal(ReferenceDate, (string?)json["effective"]!["reference_date"]);
    }

    [Fact]
    public async Task The_reference_date_never_reaches_the_provider()
    {
        string? prompt = null;
        string? schema = null;
        var gateway = new ScriptedGateway(request =>
        {
            prompt = request.Prompt;
            schema = request.OutputSchemaJson;
            return Task.FromResult(Response(ValidInvoiceJson()));
        });
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        await host.PostAsync(Body(caseId: "case-secret-id", mutate: o => o["reference_date"] = "2029-02-03"), Key);

        Assert.NotNull(prompt);
        Assert.DoesNotContain("2029-02-03", prompt);
        Assert.DoesNotContain("2029-02-03", schema);
        Assert.DoesNotContain("case-secret-id", prompt);
    }

    // ---------------------------------------------------------------- contract 2: typed outcomes are never validated

    [Fact]
    public async Task A_refusal_whose_text_is_valid_invoice_json_is_refused_and_not_validated()
    {
        var text = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(text, LlmStopReason.Refusal, "policy")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("refused", (string?)json["outcome"]!["status"]);
        Assert.Empty(json["outcome"]!["findings"]!.AsArray());
        Assert.Null(json["outcome"]!["invoice"]);
        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Equal("refused", (string?)attempt["status"]);
        Assert.Empty(attempt["findings"]!.AsArray());
        Assert.Null(attempt["invoice"]);
        Assert.Equal("refused", (string?)attempt["failure"]!["kind"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_schema_invalid_answer_has_no_findings_and_a_schema_invalid_attempt()
    {
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response("Sorry, I could not read that invoice.")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("schema_invalid", (string?)json["outcome"]!["status"]);
        Assert.Empty(json["outcome"]!["findings"]!.AsArray());
        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Equal("schema_invalid", (string?)attempt["status"]);
        Assert.Empty(attempt["findings"]!.AsArray());
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_truncated_answer_is_never_validated()
    {
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(ValidInvoiceJson(), LlmStopReason.MaxTokens)));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("truncated", (string?)json["outcome"]!["status"]);
        Assert.Empty(json["outcome"]!["findings"]!.AsArray());
        Assert.Equal("max_tokens", (string?)json["stop_reason"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_warning_only_invoice_is_a_success_whose_findings_hold_the_warning()
    {
        var text = ValidInvoiceJson(invoice => invoice["items"]![0]!["cst_csosn"] = "010");
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(text)));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("success", (string?)json["outcome"]!["status"]);
        var warning = Assert.Single(json["outcome"]!["findings"]!.AsArray())!;
        Assert.Equal("TAX_CODE_UNSUPPORTED", (string?)warning["rule_id"]);
        Assert.Equal("warning", (string?)warning["severity"]);
        var attempt = Assert.Single(json["attempts"]!.AsArray())!;
        Assert.Equal("success", (string?)attempt["status"]);
        Assert.Equal("TAX_CODE_UNSUPPORTED", (string?)Assert.Single(attempt["findings"]!.AsArray())!["rule_id"]);
        AssertTopLevelEqualsSums(json);
    }

    // ---------------------------------------------------------------- contract 2: configuration bounds

    [Theory]
    [InlineData("0", "1.00")]
    [InlineData("-0.01", "1.00")]
    [InlineData("0.50", "0.10")]
    public void A_non_positive_tolerance_or_a_cap_below_it_stops_startup(string tolerance, string cap)
    {
        Assert.Throws<InvalidOperationException>(() => CarimboApi.CreateApp(
            ["--environment", "Development"],
            configureServices: null,
            configureBuilder: builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("Validation:Tolerance", tolerance),
                    new KeyValuePair<string, string?>("Validation:SumToleranceCap", cap),
                ]);
            }));
    }

    [Fact]
    public async Task Validation_options_from_configuration_reach_the_validator()
    {
        // A tolerance of 10.00 turns the 1.00 total error into a pass: proof the bound options are the ones used.
        var text = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(text)));
        await using var host = await TestHost.StartAsync(
            "Development",
            Key,
            gateway,
            new Dictionary<string, string?>
            {
                ["Validation:Tolerance"] = "10.00",
                ["Validation:SumToleranceCap"] = "10.00",
            });

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("success", (string?)json["outcome"]!["status"]);
    }

    [Fact]
    public async Task The_eval_route_resolves_the_registered_validator_and_extractor_singletons()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var validator = host.App.Services.GetRequiredService<InvoiceValidator>();

        Assert.Same(validator, host.App.Services.GetRequiredService<InvoiceValidator>());
        Assert.Same(
            host.App.Services.GetRequiredService<IInvoiceExtractor>(),
            host.App.Services.GetRequiredService<IInvoiceExtractor>());
        Assert.Equal(0.01m, validator.Options.Tolerance);
        Assert.Equal(1.00m, validator.Options.SumToleranceCap);
    }

    // ---------------------------------------------------------------- trace id

    [Fact]
    public async Task The_response_trace_id_is_the_trace_id_of_the_inbound_traceparent()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostAsync(
            Body(),
            Key,
            traceparent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", (string?)json["trace_id"]);
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task Concurrent_requests_each_keep_their_case_id_and_get_their_own_trace_id()
    {
        const int count = 8;
        var arrived = 0;
        var allInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new ScriptedGateway(async _ =>
        {
            // Hold every request open until all eight are inside the handler at once.
            if (Interlocked.Increment(ref arrived) == count)
            {
                allInFlight.TrySetResult();
            }

            await allInFlight.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return Response(ValidInvoiceJson());
        });
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var caseIds = Enumerable.Range(1, count).Select(i => $"case-{i:000}").ToArray();
        var responses = await Task.WhenAll(caseIds.Select(async caseId =>
        {
            var response = await host.PostAsync(Body(caseId: caseId), Key);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ReadJsonAsync(response);
        }));

        Assert.Equal(caseIds, responses.Select(r => (string?)r["case_id"]).ToArray());
        var traceIds = responses.Select(r => (string?)r["trace_id"]).ToArray();
        Assert.All(traceIds, id => Assert.Matches("^[0-9a-f]{32}$", id!));
        Assert.Equal(count, traceIds.Distinct().Count());
    }

    // ---------------------------------------------------------------- secrecy

    [Fact]
    public async Task The_eval_key_appears_in_no_response_and_no_log_message()
    {
        var gateway = new ScriptedGateway();
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var responses = new List<HttpResponseMessage>
        {
            await host.PostAsync(Body(), Key),
            await host.PostAsync(Body(), "wrong-key"),
            await host.PostAsync(Body(), key: null),
            await host.PostAsync(Body(mutate: o => o["contract_version"] = "1"), Key),
            await host.PostAsync(Body(pdf: "<html/>"u8.ToArray()), Key),
        };

        foreach (var response in responses)
        {
            var headers = string.Join('\n', response.Headers.Concat(response.Content.Headers)
                .Select(h => $"{h.Key}: {string.Join(',', h.Value)}"));
            Assert.DoesNotContain(Key, headers);
            Assert.DoesNotContain(Key, await response.Content.ReadAsStringAsync(Ct));
        }

        Assert.NotEmpty(host.Logs.Messages);
        Assert.All(host.Logs.Messages, message => Assert.DoesNotContain(Key, message));
    }

    // ---------------------------------------------------------------- repair: bounds, echo and sums

    [Theory]
    [InlineData("Extraction:MaxRepairs", "-1")]
    [InlineData("Extraction:MaxRepairs", "6")]
    [InlineData("Extraction:MaxTokens", "0")]
    public void An_out_of_range_extraction_setting_stops_startup_naming_the_key(string key, string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CarimboApi.CreateApp(
            ["--environment", "Development"],
            configureServices: null,
            configureBuilder: builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(key, value)]);
            }));

        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    public async Task The_edges_of_the_repair_budget_start(string maxRepairs)
    {
        await using var host = await TestHost.StartAsync(
            "Development",
            Key,
            new ScriptedGateway(),
            new Dictionary<string, string?> { ["Extraction:MaxRepairs"] = maxRepairs });

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal(int.Parse(maxRepairs, System.Globalization.CultureInfo.InvariantCulture), (int?)json["effective"]!["max_repairs"]);
        Assert.Equal("repair-001", (string?)json["effective"]!["repair_prompt_version"]);
    }

    [Fact]
    public async Task A_configured_repair_budget_is_echoed_and_caps_the_attempts()
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var calls = 0;
        var gateway = new ScriptedGateway(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Response(wrong));
        });
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway, new Dictionary<string, string?> { ["Extraction:MaxRepairs"] = "1" });

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal(1, (int?)json["effective"]!["max_repairs"]);
        Assert.Equal("repair-001", (string?)json["effective"]!["repair_prompt_version"]);
        Assert.Equal("validation_failed", (string?)json["outcome"]!["status"]);
        Assert.Equal(2, json["attempts"]!.AsArray().Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_repaired_extraction_reports_usage_and_cost_as_exact_sums_over_its_attempts()
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(request => Task.FromResult(request.FollowUps.Count == 0
            ? Response(wrong, model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))
            : Response(ValidInvoiceJson(), model: "claude-haiku-4-5", usage: new LlmUsage(500, 100, 0, 0, 0))));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("success", (string?)json["outcome"]!["status"]);
        var attempts = json["attempts"]!.AsArray();
        Assert.Equal(["initial", "repair"], attempts.Select(a => (string?)a!["kind"]).ToArray());
        Assert.Equal(["validation_failed", "success"], attempts.Select(a => (string?)a!["status"]).ToArray());
        Assert.Equal(1500, (long?)json["usage"]!["input_tokens"]);
        Assert.Equal(300, (long?)json["usage"]!["output_tokens"]);
        Assert.Equal("0.00200000", (string?)attempts[0]!["cost_usd"]);
        Assert.Equal("0.00100000", (string?)attempts[1]!["cost_usd"]);
        Assert.Equal("0.00300000", (string?)json["cost_usd"]);
        Assert.Null((string?)json["cost_warning"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task An_unpriced_repair_attempt_makes_the_total_unknown_while_attempt_zero_keeps_its_cost()
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(request => Task.FromResult(request.FollowUps.Count == 0
            ? Response(wrong, model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))
            : Response(ValidInvoiceJson(), model: "mystery-model")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        var attempts = json["attempts"]!.AsArray();
        Assert.Equal("0.00200000", (string?)attempts[0]!["cost_usd"]);
        Assert.Null(attempts[1]!["cost_usd"]);
        Assert.Null((string?)json["cost_usd"]);
        Assert.Equal("unpriced_model:mystery-model", (string?)json["cost_warning"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task The_default_provider_timeout_is_300_seconds_per_attempt_and_is_configurable()
    {
        await using var defaulted = await TestHost.StartAsync(
            "Development", Key, gateway: null, WithKeys(DummyProviderKey, null, unreachable: false));
        await using var configured = await TestHost.StartAsync(
            "Development", Key, gateway: null, WithKeys(DummyProviderKey, null));

        Assert.Contains(defaulted.Logs.Messages, m => m.Contains("timeout 300s per attempt"));
        Assert.Contains(configured.Logs.Messages, m => m.Contains("timeout 5s per attempt"));
        Assert.DoesNotContain(defaulted.Logs.Messages, m => m.Contains(DummyProviderKey));
    }

    // ---------------------------------------------------------------- provider key and gateway registration (D-10)

    internal const string DummyProviderKey = "test-key-not-real";

    // Nothing listens on port 1, so a call through the real adapter fails fast as a network failure.
    internal static readonly Dictionary<string, string?> UnreachableProvider = new()
    {
        ["Llm:Anthropic:BaseUrl"] = "http://127.0.0.1:1",
        ["Llm:Anthropic:TimeoutSeconds"] = "5",
    };

    internal static Dictionary<string, string?> WithKeys(string? carimbo, string? generic, bool unreachable = true)
    {
        var config = unreachable ? new Dictionary<string, string?>(UnreachableProvider) : [];
        if (carimbo is not null)
        {
            config["CARIMBO_ANTHROPIC_API_KEY"] = carimbo;
        }

        if (generic is not null)
        {
            config["ANTHROPIC_API_KEY"] = generic;
        }

        return config;
    }

    [Fact]
    public async Task A_provider_key_registers_the_cost_priced_anthropic_gateway_and_the_route_exists()
    {
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway: null, WithKeys(DummyProviderKey, null));

        var unauthenticated = await host.PostAsync(Body(), key: null);
        var response = await host.PostAsync(Body(), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        // Tracing is the outermost decorator (D-31); the cache is off in a test host, so it wraps cost accounting directly.
        Assert.IsType<TracingLlmGateway>(host.App.Services.GetRequiredService<ILlmGateway>());

        // Only the real adapter can fail like this; a scripted gateway would have succeeded.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("infrastructure_failure", (string?)json["outcome"]!["status"]);
        Assert.Equal("network", (string?)json["outcome"]!["failure"]!["kind"]);
    }

    [Fact]
    public async Task Without_any_provider_key_and_no_override_the_eval_route_is_unavailable()
    {
        await using var host = await TestHost.StartAsync("Development", Key, gateway: null);

        var response = await host.PostAsync(Body(), Key);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(host.Logs.Messages, m => m.Contains("model gateway: none (provider key missing)"));
    }

    [Fact]
    public async Task A_gateway_registered_by_the_host_wins_over_the_anthropic_registration()
    {
        await using var host = await TestHost.StartAsync(
            "Development", Key, new ScriptedGateway(), WithKeys(DummyProviderKey, null));

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("success", (string?)json["outcome"]!["status"]);
        Assert.DoesNotContain(host.Logs.Messages, m => m.Contains("model gateway: anthropic"));
    }

    [Theory]
    [InlineData("carimbo-only", "CARIMBO_ANTHROPIC_API_KEY")]
    [InlineData("both", "CARIMBO_ANTHROPIC_API_KEY")]
    [InlineData("generic-only", "ANTHROPIC_API_KEY")]
    public async Task The_provider_key_resolves_from_the_carimbo_variable_first_and_only_its_source_is_logged(
        string scenario, string expectedSource)
    {
        var (carimbo, generic) = scenario switch
        {
            "carimbo-only" => (DummyProviderKey, null),
            "both" => (DummyProviderKey, "other-key-not-real"),
            _ => (null, "other-key-not-real"),
        };
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway: null, WithKeys(carimbo, generic));

        Assert.Contains(host.Logs.Messages, m => m.Contains($"model gateway: anthropic (key from {expectedSource})"));
        Assert.All(host.Logs.Messages, m =>
        {
            Assert.DoesNotContain(DummyProviderKey, m);
            Assert.DoesNotContain("other-key-not-real", m);
        });
    }

    [Fact]
    public async Task A_blank_provider_key_counts_as_missing()
    {
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway: null, WithKeys("   ", string.Empty));

        Assert.Equal(HttpStatusCode.NotFound, (await host.PostAsync(Body(), Key)).StatusCode);
    }

    [Fact]
    public async Task The_provider_key_appears_in_no_response_and_no_log_message()
    {
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway: null, WithKeys(DummyProviderKey, null));

        var response = await host.PostAsync(Body(), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain(DummyProviderKey, text);
        Assert.DoesNotContain(DummyProviderKey, string.Join('\n', response.Headers.Select(h => $"{h.Key}: {string.Join(',', h.Value)}")));
        Assert.NotEmpty(host.Logs.Messages);
        Assert.All(host.Logs.Messages, m => Assert.DoesNotContain(DummyProviderKey, m));
    }

    // ---------------------------------------------------------------- response cache (D-19, D-27)

    internal static Dictionary<string, string?> CacheConfig(string directory, string mode) => new()
    {
        ["Llm:Cache:Directory"] = directory,
        ["Llm:Cache:Mode"] = mode,
    };

    internal static ScriptedGateway CountingGateway(Counter calls) =>
        new(_ =>
        {
            Interlocked.Increment(ref calls.Value);
            return Task.FromResult(
                Response(ValidInvoiceJson(), model: "claude-haiku-4-5") with { Latency = TimeSpan.FromMilliseconds(1234) });
        });

    [Fact]
    public async Task A_read_write_request_is_replayed_by_a_fresh_read_only_host_with_zero_model_calls()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            JsonNode first;
            await using (var hostA = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write")))
            {
                first = await ReadJsonAsync(await hostA.PostAsync(Body(), Key));
            }

            JsonNode second;
            await using (var hostB = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_only")))
            {
                second = await ReadJsonAsync(await hostB.PostAsync(Body(), Key));
            }

            Assert.Equal(1, calls.Value);
            Assert.Equal(1, (int?)first["attempts"]![0]!["http_attempts"]);
            Assert.Equal(0, (int?)second["attempts"]![0]!["http_attempts"]);
            Assert.Equal(1234, (long?)second["attempts"]![0]!["latency_ms"]);
            Assert.Equal("0.00011000", (string?)first["attempts"]![0]!["cost_usd"]);
            Assert.Equal((string?)first["attempts"]![0]!["cost_usd"], (string?)second["attempts"]![0]!["cost_usd"]);
            Assert.Equal((string?)first["outcome"]!["status"], (string?)second["outcome"]!["status"]);
            Assert.Equal("success", (string?)second["outcome"]!["status"]);
            Assert.Equal(first["outcome"]!["invoice"]!.ToJsonString(), second["outcome"]!["invoice"]!.ToJsonString());

            var file = Assert.Single(directory.GetFiles());
            Assert.Matches("^[0-9a-f]{64}\\.json$", file.Name);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_read_only_host_on_an_empty_directory_reports_a_cache_miss_without_calling_the_model()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_only"));

            var response = await host.PostAsync(Body(), Key);
            var json = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("infrastructure_failure", (string?)json["outcome"]!["status"]);
            Assert.Equal("cache_miss", (string?)json["outcome"]!["failure"]!["kind"]);
            Assert.Equal(0, calls.Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_test_host_without_cache_settings_runs_with_the_cache_off()
    {
        var calls = new Counter();
        await using var host = await TestHost.StartAsync("Development", Key, CountingGateway(calls));

        await host.PostAsync(Body(), Key);
        await host.PostAsync(Body(), Key);

        Assert.Equal(2, calls.Value);
        Assert.Contains(host.Logs.Messages, m => m.Contains("response cache: off (Llm:Cache:Mode off)"));
    }

    // ---------------------------------------------------------------- contract 3: cache overrides

    [Fact]
    public async Task The_response_echoes_the_effective_cache_mode_and_each_attempt_carries_its_cache_block()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write"));

            var first = await ReadJsonAsync(await host.PostAsync(Body(), Key));
            var second = await ReadJsonAsync(await host.PostAsync(Body(), Key));

            Assert.Equal("read_write", (string?)first["effective"]!["cache_mode"]);
            Assert.Null(first["effective"]!["replicate_salt"]);
            var miss = first["attempts"]![0]!["cache"]!;
            Assert.Equal("read_write", (string?)miss["mode"]);
            Assert.Equal("miss", (string?)miss["status"]);
            Assert.Matches("^[0-9a-f]{64}$", (string?)miss["key"]);
            Assert.True((bool?)miss["stored"]);
            var hit = second["attempts"]![0]!["cache"]!;
            Assert.Equal("hit", (string?)hit["status"]);
            Assert.Equal((string?)miss["key"], (string?)hit["key"]);
            Assert.False((bool?)hit["stored"]);
            Assert.Equal(1, calls.Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_per_request_cache_mode_overrides_the_hostDefault()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write"));
            await host.PostAsync(Body(), Key);

            var replay = await ReadJsonAsync(await host.PostAsync(
                Body(mutate: o => o["overrides"] = new JsonObject { ["cache_mode"] = "read_only" }), Key));
            Assert.Equal(1, calls.Value);
            Assert.Equal("read_only", (string?)replay["effective"]!["cache_mode"]);
            Assert.Equal(0, (int?)replay["attempts"]![0]!["http_attempts"]);
            Assert.Equal("hit", (string?)replay["attempts"]![0]!["cache"]!["status"]);

            var refreshed = await ReadJsonAsync(await host.PostAsync(
                Body(mutate: o => o["overrides"] = new JsonObject { ["cache_mode"] = "refresh" }), Key));
            Assert.Equal(2, calls.Value);
            Assert.Equal("refresh", (string?)refreshed["effective"]!["cache_mode"]);
            Assert.Equal(1, (int?)refreshed["attempts"]![0]!["http_attempts"]);
            Assert.Equal("refresh", (string?)refreshed["attempts"]![0]!["cache"]!["status"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_host_with_the_cache_off_reports_cache_mode_off_and_no_cache_block()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("off", (string?)json["effective"]!["cache_mode"]);
        Assert.Null(json["attempts"]![0]!["cache"]);
    }

    private static JsonObject Overrides(string member, JsonNode? value) => new() { [member] = value };

    [Theory]
    [InlineData("a")]
    [InlineData("A-z_0.9")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")]
    public async Task A_valid_replicate_salt_reaches_every_attempt_and_is_echoed(string salt)
    {
        Assert.InRange(salt.Length, 1, 64);
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var seen = new ConcurrentQueue<string?>();
        var gateway = new ScriptedGateway(request =>
        {
            seen.Enqueue(request.ReplicateSalt);
            return Task.FromResult(Response(request.FollowUps.Count == 0 ? wrong : ValidInvoiceJson()));
        });
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("replicate_salt", salt)), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(salt, (string?)json["effective"]!["replicate_salt"]);
        Assert.Equal(2, json["attempts"]!.AsArray().Count);
        Assert.Equal([salt, salt], seen.ToArray());
    }

    [Fact]
    public async Task Without_a_replicate_salt_the_effective_salt_is_null_and_no_request_carries_one()
    {
        var seen = new ConcurrentQueue<string?>();
        var gateway = new ScriptedGateway(request =>
        {
            seen.Enqueue(request.ReplicateSalt);
            return Task.FromResult(Response(ValidInvoiceJson()));
        });
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));
        var withNullOverrides = await host.PostAsync(Body(mutate: o => o["overrides"] = null), Key);

        Assert.Null(json["effective"]!["replicate_salt"]);
        Assert.Equal(HttpStatusCode.OK, withNullOverrides.StatusCode);
        Assert.All(seen, salt => Assert.Null(salt));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"a b\"")]
    [InlineData("\"SALT-SENTINEL/../x\"")]
    [InlineData("\"a\\n\"")]
    [InlineData("\"caf\\u00e9\"")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("{\"SALT-SENTINEL\":1}")]
    [InlineData("[\"SALT-SENTINEL\"]")]
    public async Task An_invalid_replicate_salt_is_a_bad_request_naming_the_field_and_never_echoing_the_value(string rawJson)
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("replicate_salt", JsonNode.Parse(rawJson))), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrides.replicate_salt", text);
        Assert.DoesNotContain("SALT-SENTINEL", text);
        Assert.DoesNotContain("../x", text);
    }

    [Fact]
    public async Task A_sixty_five_character_replicate_salt_is_a_bad_request()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("replicate_salt", new string('s', 65))), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrides.replicate_salt", text);
        Assert.DoesNotContain(new string('s', 65), text);
    }

    [Theory]
    [InlineData("\"READ_ONLY\"")]
    [InlineData("\"readonly\"")]
    [InlineData("\"read-only\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    [InlineData("[\"read_only\"]")]
    public async Task An_invalid_cache_mode_is_a_bad_request_naming_the_field_and_never_echoing_the_value(string rawJson)
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            await using var host = await TestHost.StartAsync(
                "Development", Key, new ScriptedGateway(), CacheConfig(directory.FullName, "read_write"));

            var response = await host.PostAsync(
                Body(mutate: o => o["overrides"] = Overrides("cache_mode", JsonNode.Parse(rawJson))), Key);
            var text = await response.Content.ReadAsStringAsync(Ct);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("overrides.cache_mode", text);
            if (rawJson.StartsWith('"') && rawJson.Length > 2)
            {
                Assert.DoesNotContain(rawJson.Trim('"'), text);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("read_write")]
    [InlineData("read_only")]
    [InlineData("refresh")]
    public async Task A_cache_mode_other_than_off_is_refused_on_a_host_whose_cache_is_off(string mode)
    {
        var calls = new Counter();
        await using var host = await TestHost.StartAsync("Development", Key, CountingGateway(calls));

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("cache_mode", mode)), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrides.cache_mode", text);
        Assert.Equal(0, calls.Value);
    }

    [Fact]
    public async Task Cache_mode_off_is_accepted_on_a_host_whose_cache_is_off()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("cache_mode", "off")), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("off", (string?)json["effective"]!["cache_mode"]);
        Assert.Null(json["attempts"]![0]!["cache"]);
    }

    [Fact]
    public async Task Cache_mode_off_bypasses_a_host_cache_that_is_on()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write"));

            var json = await ReadJsonAsync(await host.PostAsync(
                Body(mutate: o => o["overrides"] = Overrides("cache_mode", "off")), Key));

            Assert.Equal("off", (string?)json["effective"]!["cache_mode"]);
            Assert.Equal(1, calls.Value);
            Assert.Empty(directory.GetFiles());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("temperature")]
    [InlineData("top_p")]
    public async Task An_unknown_member_of_overrides_is_a_bad_request_naming_the_body(string member)
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides(member, "x")), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("body", text);
    }

    [Fact]
    public async Task A_replicate_salt_changes_the_cache_key_and_a_repeated_salt_hits()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write"));

            JsonObject WithOverrides(string mode, string salt) => Body(mutate: o => o["overrides"] = new JsonObject
            {
                ["cache_mode"] = mode,
                ["replicate_salt"] = salt,
            });

            await host.PostAsync(WithOverrides("read_write", "s1"), Key);
            await host.PostAsync(WithOverrides("read_write", "s2"), Key);
            Assert.Equal(2, calls.Value);
            Assert.Equal(2, directory.GetFiles().Length);

            var missed = await ReadJsonAsync(await host.PostAsync(WithOverrides("read_only", "s3"), Key));
            Assert.Equal("infrastructure_failure", (string?)missed["outcome"]!["status"]);
            Assert.Equal("cache_miss", (string?)missed["outcome"]!["failure"]!["kind"]);
            Assert.Equal("s3", (string?)missed["effective"]!["replicate_salt"]);

            var hit = await ReadJsonAsync(await host.PostAsync(WithOverrides("read_only", "s1"), Key));
            Assert.Equal("success", (string?)hit["outcome"]!["status"]);
            Assert.Equal("hit", (string?)hit["attempts"]![0]!["cache"]!["status"]);
            Assert.Equal(2, calls.Value);
            Assert.Equal(2, directory.GetFiles().Length);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- contract 3: prompt version (EXT-05, D-30)

    [Fact]
    public async Task The_prompt_version_override_reaches_the_model_and_is_echoed_in_effective_and_the_initial_attempt()
    {
        var seen = new ConcurrentQueue<(string? Version, string Prompt)>();
        var gateway = new ScriptedGateway(request =>
        {
            seen.Enqueue((request.PromptVersion, request.Prompt));
            return Task.FromResult(Response(ValidInvoiceJson()));
        });
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("prompt_version", "extract-002")), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("extract-002", (string?)json["effective"]!["prompt_version"]);
        Assert.Equal("extract-002", (string?)json["attempts"]![0]!["prompt_version"]);
        Assert.Equal("repair-001", (string?)json["effective"]!["repair_prompt_version"]);
        Assert.True(PromptCatalog.TryGet("extract-002", out var extract002));
        var (version, prompt) = Assert.Single(seen);
        Assert.Equal("extract-002", version);
        Assert.Equal(extract002, prompt);
    }

    [Fact]
    public async Task Without_a_prompt_version_override_the_default_prompt_is_used_and_echoed()
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal(PromptCatalog.DefaultVersion, (string?)json["effective"]!["prompt_version"]);
        Assert.Equal(PromptCatalog.DefaultVersion, (string?)json["attempts"]![0]!["prompt_version"]);
    }

    [Theory]
    [InlineData("\"extract-999\"")]
    [InlineData("\"EXTRACT-002\"")]
    [InlineData("\"\"")]
    [InlineData("2")]
    [InlineData("true")]
    [InlineData("{\"PROMPT-SENTINEL\":1}")]
    public async Task An_uncatalogued_prompt_version_is_a_bad_request_naming_the_field_and_never_echoing_it(string rawJson)
    {
        var calls = new Counter();
        await using var host = await TestHost.StartAsync("Development", Key, CountingGateway(calls));

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("prompt_version", JsonNode.Parse(rawJson))), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrides.prompt_version", text);
        Assert.Contains("extract-002, extract-003", text);
        Assert.DoesNotContain("extract-999", text);
        Assert.DoesNotContain("EXTRACT-002", text);
        Assert.DoesNotContain("PROMPT-SENTINEL", text);
        Assert.Equal(0, calls.Value);
    }

    [Fact]
    public async Task A_prompt_version_changes_the_cache_key_and_a_repeated_version_hits()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write"));

            JsonObject With(string? version, string mode) => Body(mutate: o => o["overrides"] = new JsonObject
            {
                ["prompt_version"] = version,
                ["cache_mode"] = mode,
            });

            // The default version is recorded; the same document under extract-002 is another key.
            var recorded = await ReadJsonAsync(await host.PostAsync(Body(), Key));
            Assert.Equal("success", (string?)recorded["outcome"]!["status"]);
            Assert.Equal(1, calls.Value);

            var missed = await ReadJsonAsync(await host.PostAsync(With("extract-002", "read_only"), Key));
            Assert.Equal("infrastructure_failure", (string?)missed["outcome"]!["status"]);
            Assert.Equal("cache_miss", (string?)missed["outcome"]!["failure"]!["kind"]);
            Assert.Equal("extract-002", (string?)missed["effective"]!["prompt_version"]);
            Assert.Equal(1, calls.Value);

            // Recorded under extract-002, it is a hit, and the default version's entry is still its own.
            await host.PostAsync(With("extract-002", "read_write"), Key);
            Assert.Equal(2, calls.Value);
            Assert.Equal(2, directory.GetFiles().Length);

            var hit = await ReadJsonAsync(await host.PostAsync(With("extract-002", "read_only"), Key));
            Assert.Equal("success", (string?)hit["outcome"]!["status"]);
            Assert.Equal("hit", (string?)hit["attempts"]![0]!["cache"]!["status"]);
            Assert.Equal("extract-002", (string?)hit["attempts"]![0]!["prompt_version"]);

            var defaultHit = await ReadJsonAsync(await host.PostAsync(With(null, "read_only"), Key));
            Assert.Equal("hit", (string?)defaultHit["attempts"]![0]!["cache"]!["status"]);
            Assert.Equal(2, calls.Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- contract 3: model and repair budget (API-02)

    private static ScriptedGateway AlwaysInvalidGateway(Action<LlmRequest>? seen = null, int delayMs = 0)
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        return new ScriptedGateway(async request =>
        {
            seen?.Invoke(request);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, Ct);
            }

            return Response(wrong, model: request.Model);
        });
    }

    [Theory]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("claude-haiku-4-5-20251001")]
    public async Task The_model_override_reaches_the_gateway_and_is_echoed_in_effective(string model)
    {
        var seen = new ConcurrentQueue<string>();
        var gateway = new ScriptedGateway(request =>
        {
            seen.Enqueue(request.Model);
            return Task.FromResult(Response(ValidInvoiceJson(), model: request.Model));
        });
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var response = await host.PostAsync(Body(mutate: o => o["overrides"] = Overrides("model", model)), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(model, (string?)json["effective"]!["model"]);
        Assert.Equal([model], seen.ToArray());
    }

    [Fact]
    public async Task Without_a_model_override_the_host_model_is_used_and_echoed()
    {
        var seen = new ConcurrentQueue<string>();
        var gateway = new ScriptedGateway(request =>
        {
            seen.Enqueue(request.Model);
            return Task.FromResult(Response(ValidInvoiceJson(), model: request.Model));
        });
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("claude-haiku-4-5", (string?)json["effective"]!["model"]);
        Assert.Equal(["claude-haiku-4-5"], seen.ToArray());
    }

    [Theory]
    [InlineData("\"MODEL-SENTINEL-x\"")]
    [InlineData("\"mystery-model\"")]
    [InlineData("\"claude-haiku-4-5 \"")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("{\"MODEL-SENTINEL-x\":1}")]
    public async Task A_model_the_pricing_table_cannot_price_is_a_bad_request_naming_the_field_and_never_echoing_it(string rawJson)
    {
        var calls = new Counter();
        await using var host = await TestHost.StartAsync("Development", Key, CountingGateway(calls));

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("model", JsonNode.Parse(rawJson))), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrides.model", text);
        Assert.DoesNotContain("MODEL-SENTINEL-x", text);
        Assert.DoesNotContain("mystery-model", text);
        Assert.Equal(0, calls.Value);
    }

    [Fact]
    public async Task A_repair_budget_of_zero_makes_one_attempt_does_not_cache_the_document_and_is_echoed()
    {
        var requests = new ConcurrentQueue<LlmRequest>();
        await using var host = await TestHost.StartAsync("Development", Key, AlwaysInvalidGateway(requests.Enqueue));

        var json = await ReadJsonAsync(await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("max_repairs", 0)), Key));

        Assert.Equal(0, (int?)json["effective"]!["max_repairs"]);
        Assert.Equal("validation_failed", (string?)json["outcome"]!["status"]);
        Assert.Single(json["attempts"]!.AsArray());
        var request = Assert.Single(requests);
        Assert.False(request.CacheDocument);
    }

    [Fact]
    public async Task A_repair_budget_of_five_allows_up_to_six_attempts_and_is_echoed()
    {
        var requests = new ConcurrentQueue<LlmRequest>();
        await using var host = await TestHost.StartAsync("Development", Key, AlwaysInvalidGateway(requests.Enqueue));

        var json = await ReadJsonAsync(await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("max_repairs", 5)), Key));

        Assert.Equal(5, (int?)json["effective"]!["max_repairs"]);
        Assert.Equal(6, json["attempts"]!.AsArray().Count);
        Assert.Equal(6, requests.Count);
        Assert.All(requests, request => Assert.True(request.CacheDocument));
    }

    [Fact]
    public async Task A_request_budget_overrides_the_host_budget_in_both_directions()
    {
        await using var host = await TestHost.StartAsync(
            "Development",
            Key,
            AlwaysInvalidGateway(),
            new Dictionary<string, string?> { ["Extraction:MaxRepairs"] = "1" });

        var more = await ReadJsonAsync(await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("max_repairs", 3)), Key));
        var fewer = await ReadJsonAsync(await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("max_repairs", 0)), Key));
        var hostDefault = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal(4, more["attempts"]!.AsArray().Count);
        Assert.Single(fewer["attempts"]!.AsArray());
        Assert.Equal(2, hostDefault["attempts"]!.AsArray().Count);
        Assert.Equal(1, (int?)hostDefault["effective"]!["max_repairs"]);
    }

    [Fact]
    public async Task A_null_repair_budget_counts_as_absent()
    {
        await using var host = await TestHost.StartAsync("Development", Key, AlwaysInvalidGateway());

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("max_repairs", null)), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (int?)json["effective"]!["max_repairs"]);
        Assert.Equal(3, json["attempts"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("6")]
    [InlineData("99999999999")]
    [InlineData("2.5")]
    [InlineData("2.0")]
    [InlineData("1E0")]
    [InlineData("\"2\"")]
    [InlineData("true")]
    [InlineData("{\"BUDGET-SENTINEL\":1}")]
    [InlineData("[2]")]
    public async Task A_repair_budget_that_is_not_an_integer_from_zero_to_five_is_a_bad_request_naming_the_field(string rawJson)
    {
        var calls = new Counter();
        await using var host = await TestHost.StartAsync("Development", Key, CountingGateway(calls));

        var response = await host.PostAsync(
            Body(mutate: o => o["overrides"] = Overrides("max_repairs", JsonNode.Parse(rawJson))), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrides.max_repairs", text);
        Assert.DoesNotContain("BUDGET-SENTINEL", text);
        Assert.Equal(0, calls.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task The_edges_of_the_request_repair_budget_are_accepted(int budget)
    {
        await using var host = await TestHost.StartAsync("Development", Key, new ScriptedGateway());

        var response = await host.PostAsync(Body(mutate: o => o["overrides"] = Overrides("max_repairs", budget)), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(budget, (int?)json["effective"]!["max_repairs"]);
    }

    [Fact]
    public async Task Concurrent_requests_with_different_repair_budgets_each_get_their_own()
    {
        await using var host = await TestHost.StartAsync("Development", Key, AlwaysInvalidGateway(delayMs: 40));

        var zero = host.PostAsync(Body(caseId: "case-a", mutate: o => o["overrides"] = Overrides("max_repairs", 0)), Key);
        var two = host.PostAsync(Body(caseId: "case-b", mutate: o => o["overrides"] = Overrides("max_repairs", 2)), Key);
        await Task.WhenAll(zero, two);
        var zeroJson = await ReadJsonAsync(await zero);
        var twoJson = await ReadJsonAsync(await two);

        Assert.Single(zeroJson["attempts"]!.AsArray());
        Assert.Equal(0, (int?)zeroJson["effective"]!["max_repairs"]);
        Assert.Equal(3, twoJson["attempts"]!.AsArray().Count);
        Assert.Equal(2, (int?)twoJson["effective"]!["max_repairs"]);
    }

    // ---------------------------------------------------------------- contract 3: incurred versus notional cost (D-29)

    [Fact]
    public async Task A_miss_incurs_its_cost_and_the_hit_of_the_same_request_incurs_nothing_while_keeping_the_original_cost()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write"));

            var miss = await ReadJsonAsync(await host.PostAsync(Body(), Key));
            var hit = await ReadJsonAsync(await host.PostAsync(
                Body(mutate: o => o["overrides"] = Overrides("cache_mode", "read_only")), Key));

            Assert.Equal("0.00011000", (string?)miss["cost_usd"]);
            Assert.Equal("0.00011000", (string?)miss["attempts"]![0]!["incurred_cost_usd"]);
            Assert.Equal("0.00011000", (string?)miss["incurred_cost_usd"]);

            Assert.Equal("hit", (string?)hit["attempts"]![0]!["cache"]!["status"]);
            Assert.Equal("0.00011000", (string?)hit["cost_usd"]);
            Assert.Equal("0.00011000", (string?)hit["attempts"]![0]!["cost_usd"]);
            Assert.Equal("0.00000000", (string?)hit["attempts"]![0]!["incurred_cost_usd"]);
            Assert.Equal("0.00000000", (string?)hit["incurred_cost_usd"]);
            AssertTopLevelEqualsSums(miss);
            AssertTopLevelEqualsSums(hit);
            Assert.Equal(1, calls.Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_refresh_calls_the_provider_so_it_incurs_its_cost()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_write"));
            await host.PostAsync(Body(), Key);

            var refreshed = await ReadJsonAsync(await host.PostAsync(
                Body(mutate: o => o["overrides"] = Overrides("cache_mode", "refresh")), Key));

            Assert.Equal("refresh", (string?)refreshed["attempts"]![0]!["cache"]!["status"]);
            Assert.Equal("0.00011000", (string?)refreshed["attempts"]![0]!["incurred_cost_usd"]);
            Assert.Equal("0.00011000", (string?)refreshed["incurred_cost_usd"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task An_unpriced_answered_miss_has_a_null_incurred_cost_at_both_levels()
    {
        var gateway = new ScriptedGateway(_ => Task.FromResult(
            Response(ValidInvoiceJson(), model: "mystery-model")));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Null(json["cost_usd"]);
        Assert.Null(json["attempts"]![0]!["incurred_cost_usd"]);
        Assert.Null(json["incurred_cost_usd"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_read_only_cache_miss_made_no_provider_call_so_it_incurs_zero_and_has_no_notional_cost()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-cache-");
        try
        {
            var calls = new Counter();
            await using var host = await TestHost.StartAsync(
                "Development", Key, CountingGateway(calls), CacheConfig(directory.FullName, "read_only"));

            var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

            Assert.Equal("cache_miss", (string?)json["outcome"]!["failure"]!["kind"]);
            Assert.Null(json["cost_usd"]);
            Assert.Null(json["attempts"]![0]!["cost_usd"]);
            Assert.Equal("0.00000000", (string?)json["attempts"]![0]!["incurred_cost_usd"]);
            Assert.Equal("0.00000000", (string?)json["incurred_cost_usd"]);
            AssertTopLevelEqualsSums(json);
            Assert.Equal(0, calls.Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_provider_failure_is_never_counted_as_free()
    {
        var gateway = new ScriptedGateway(_ => throw new LlmGatewayException(
            LlmFailureKind.Overloaded, "provider overloaded", 529, "req_abc"));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Equal("overloaded", (string?)json["outcome"]!["failure"]!["kind"]);
        Assert.Null(json["attempts"]![0]!["incurred_cost_usd"]);
        Assert.Null(json["incurred_cost_usd"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_provider_failure_after_an_answered_attempt_makes_the_total_incurred_unknown()
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(request => request.FollowUps.Count == 0
            ? Task.FromResult(Response(wrong, model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0)))
            : throw new LlmGatewayException(LlmFailureKind.Overloaded, "provider overloaded", 529, "req_abc"));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        var attempts = json["attempts"]!.AsArray();
        Assert.Equal(2, attempts.Count);
        Assert.Equal("0.00200000", (string?)attempts[0]!["incurred_cost_usd"]);
        Assert.Null(attempts[1]!["incurred_cost_usd"]);
        Assert.Null(json["incurred_cost_usd"]);
        AssertTopLevelEqualsSums(json);
    }

    [Fact]
    public async Task A_repair_chain_incurs_the_sum_of_its_attempts()
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(request => Task.FromResult(request.FollowUps.Count == 0
            ? Response(wrong, model: "claude-haiku-4-5", usage: new LlmUsage(1000, 200, 0, 0, 0))
            : Response(ValidInvoiceJson(), model: "claude-haiku-4-5", usage: new LlmUsage(500, 100, 0, 0, 0))));
        await using var host = await TestHost.StartAsync("Development", Key, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        var attempts = json["attempts"]!.AsArray();
        Assert.Equal("0.00200000", (string?)attempts[0]!["incurred_cost_usd"]);
        Assert.Equal("0.00100000", (string?)attempts[1]!["incurred_cost_usd"]);
        Assert.Equal("0.00300000", (string?)json["incurred_cost_usd"]);
        Assert.Equal("0.00300000", (string?)json["cost_usd"]);
        AssertTopLevelEqualsSums(json);
    }

    internal sealed class Counter
    {
        public int Value;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The shared hand-checked fixture, read fresh per call so a test can mutate it.</summary>
    internal static string ValidInvoiceJson(Action<JsonObject>? mutate = null)
    {
        var invoice = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "vectors", "valid-invoice.json")))!.AsObject();
        mutate?.Invoke(invoice);
        return invoice.ToJsonString();
    }

    /// <summary>Walks up from the test binary to the directory holding <c>dotnet/Carimbo.slnx</c>.</summary>
    internal static string FindRepoRoot()
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

    internal static LlmResponse Response(
        string text,
        LlmStopReason stopReason = LlmStopReason.EndTurn,
        string? stopDetail = null,
        string? model = null,
        LlmUsage? usage = null) =>
        new(
            text,
            stopReason,
            stopDetail,
            usage ?? new LlmUsage(10, 20, 0, 0, 0),
            model ?? "test-model",
            model ?? "returned-model",
            "msg_1",
            TimeSpan.FromMilliseconds(5),
            1);

    internal static JsonObject Body(
        string caseId = "case-001",
        byte[]? pdf = null,
        Action<JsonObject>? mutate = null)
    {
        var body = new JsonObject
        {
            ["contract_version"] = "3",
            ["case_id"] = caseId,
            ["reference_date"] = ReferenceDate,
            ["document"] = new JsonObject
            {
                ["media_type"] = "application/pdf",
                ["content_base64"] = Convert.ToBase64String(pdf ?? Pdf),
            },
        };
        mutate?.Invoke(body);
        return body;
    }

    /// <summary>The invariant of the attempts list: top-level usage and cost are sums over the attempts.</summary>
    internal static void AssertTopLevelEqualsSums(JsonNode json)
    {
        var attempts = json["attempts"]!.AsArray();
        Assert.NotEmpty(attempts);
        Assert.Equal(Enumerable.Range(0, attempts.Count), attempts.Select(a => (int)a!["index"]!));
        Assert.Equal("initial", (string?)attempts[0]!["kind"]);

        foreach (var name in new[] { "input_tokens", "output_tokens", "cache_read_tokens", "cache_write_5m_tokens", "cache_write_1h_tokens" })
        {
            var expected = attempts.Where(a => a!["usage"] is not null).Sum(a => (long)a!["usage"]![name]!);
            Assert.Equal(expected, (long)json["usage"]![name]!);
        }

        var answered = attempts.Where(a => a!["usage"] is not null).ToArray();
        if (answered.Length == 0 || answered.Any(a => a!["cost_usd"] is null))
        {
            Assert.Null(json["cost_usd"]);
        }
        else
        {
            var sum = answered.Sum(a => decimal.Parse((string)a!["cost_usd"]!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(
                sum,
                decimal.Parse((string)json["cost_usd"]!, System.Globalization.CultureInfo.InvariantCulture));
        }

        // Incurred cost: unknown (null) when any attempt's is unknown, otherwise the sum of the attempts'.
        if (attempts.Any(a => a!["incurred_cost_usd"] is null))
        {
            Assert.Null(json["incurred_cost_usd"]);
        }
        else
        {
            var incurred = attempts.Sum(a => decimal.Parse((string)a!["incurred_cost_usd"]!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(
                incurred,
                decimal.Parse((string)json["incurred_cost_usd"]!, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void AssertUsageEquals(JsonNode expected, JsonNode actual)
    {
        foreach (var name in new[] { "input_tokens", "output_tokens", "cache_read_tokens", "cache_write_5m_tokens", "cache_write_1h_tokens" })
        {
            Assert.Equal((long?)expected[name], (long?)actual[name]);
        }
    }

    internal static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

    internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class ScriptedGateway(Func<LlmRequest, Task<LlmResponse>>? script = null) : ILlmGateway
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            script is null ? Task.FromResult(Response(ValidInvoiceJson())) : script(request);
    }

    internal sealed class TestHost : IAsyncDisposable
    {
        private TestHost(WebApplication app, CapturingLoggerProvider logs)
        {
            App = app;
            Logs = logs;
            Client = app.GetTestClient();
        }

        public WebApplication App { get; }

        public HttpClient Client { get; }

        public CapturingLoggerProvider Logs { get; }

        public static async Task<TestHost> StartAsync(
            string environment,
            string? key,
            ILlmGateway? gateway,
            IReadOnlyDictionary<string, string?>? config = null,
            Action<IServiceCollection>? configureServices = null)
        {
            var logs = new CapturingLoggerProvider();
            var app = CarimboApi.CreateApp(
                ["--environment", environment],
                services =>
                {
                    if (gateway is not null)
                    {
                        services.AddSingleton(gateway);
                    }

                    configureServices?.Invoke(services);
                },
                builder =>
                {
                    builder.WebHost.UseTestServer();

                    // An in-memory empty value overrides any CARIMBO_EVAL_API_KEY in the developer's environment.
                    builder.Configuration.AddInMemoryCollection(
                        [new KeyValuePair<string, string?>("CARIMBO_EVAL_API_KEY", key ?? string.Empty)]);

                    // The same for the provider keys: a developer shell (or secretspec) may export a real one, and
                    // it must never decide which gateway a test host registers.
                    builder.Configuration.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("CARIMBO_ANTHROPIC_API_KEY", string.Empty),
                        new KeyValuePair<string, string?>("ANTHROPIC_API_KEY", string.Empty),
                    ]);

                    // D-27 turns the response cache on by default in Development and Eval. A test host runs with it
                    // off, overriding the developer's environment, unless the test passes its own per-test cache
                    // directory or mode, so no test reads entries another test or a developer run left behind.
                    var cacheConfigured = config is not null
                        && (config.ContainsKey("Llm:Cache:Directory") || config.ContainsKey("Llm:Cache:Mode"));
                    builder.Configuration.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("Llm:Cache:Directory", string.Empty),
                        new KeyValuePair<string, string?>("Llm:Cache:Mode", cacheConfigured ? string.Empty : "off"),
                    ]);
                    if (config is not null)
                    {
                        builder.Configuration.AddInMemoryCollection(config);
                    }

                    builder.Logging.ClearProviders();
                    builder.Logging.SetMinimumLevel(LogLevel.Trace);
                    builder.Logging.AddProvider(logs);
                });
            await app.StartAsync(Ct);
            return new TestHost(app, logs);
        }

        public Task<HttpResponseMessage> PostAsync(
            JsonObject body,
            string? key,
            string? traceparent = null) =>
            SendAsync(body.ToJsonString(), key, traceparent);

        public Task<HttpResponseMessage> PostRawAsync(string body, string? key) => SendAsync(body, key, null);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }

        private Task<HttpResponseMessage> SendAsync(string body, string? key, string? traceparent)
        {
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = content };
            if (key is not null)
            {
                request.Headers.Add("X-Api-Key", key);
            }

            if (traceparent is not null)
            {
                request.Headers.Add("traceparent", traceparent);
            }

            return Client.SendAsync(request, Ct);
        }
    }

    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> messages = new();

        public IReadOnlyCollection<string> Messages => messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                sink.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
