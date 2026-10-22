using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Extraction;
using Carimbo.Llm;

namespace Carimbo.Agent;

/// <summary>The pinned system prompt and the Decision schema sent with every agent step, with their identifying hashes.</summary>
public sealed record AgentContract(
    string PromptVersion,
    string SystemPrompt,
    string OutputSchemaJson,
    string OutputSchemaSha256)
{
    /// <summary>The current contract: prompt agent-001 over the model-facing Decision schema, built once.</summary>
    public static AgentContract Default { get; } = Build();

    /// <summary>
    /// SHA-256 (lowercase hex) of a canonical JSON array of <c>{name, description_sha256, input_schema_sha256}</c>,
    /// in the order given. It identifies the tool list a run saw, because the list is part of every cache key.
    /// </summary>
    public static string ToolsSha256(IReadOnlyList<LlmToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var tool in tools)
            {
                writer.WriteStartObject();
                writer.WriteString("name", tool.Name);
                writer.WriteString("description_sha256", Sha256(tool.Description));
                writer.WriteString("input_schema_sha256", Sha256(tool.InputSchemaJson));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>
    /// Null when the outcome-cause pair is one the model may use: approve or reject with cause none, escalate with
    /// extraction_not_success, policy_warning or over_limit. Otherwise a message naming the two wire names and
    /// nothing else (tool_failed and step_budget_exhausted are assigned by code only, D-35).
    /// </summary>
    public static string? ModelDecisionViolation(Decision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var allowed = decision.Outcome switch
        {
            DecisionOutcome.Approve or DecisionOutcome.Reject => decision.Cause == DecisionCause.None,
            DecisionOutcome.Escalate => decision.Cause is DecisionCause.ExtractionNotSuccess or DecisionCause.PolicyWarning or DecisionCause.OverLimit,
            _ => false,
        };
        return allowed
            ? null
            : $"The outcome {WireName(decision.Outcome)} does not allow the cause {WireName(decision.Cause)}.";
    }

    private static string WireName<T>(T value) => JsonSerializer.Serialize(value, Wire.Options).Trim('"');

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static AgentContract Build()
    {
        var prompt = AgentPromptCatalog.TryGet(AgentPromptCatalog.DefaultVersion, out var text)
            ? text
            : throw new InvalidOperationException("The default agent prompt version is not in the catalog.");
        var schema = ModelFacingSchema.Json(typeof(Decision));
        return new AgentContract(AgentPromptCatalog.DefaultVersion, prompt, schema, Sha256(schema));
    }
}
