using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Llm;

namespace Carimbo.Agent;

/// <summary>Decides one case. The Decision is returned and nothing else happens (D-09).</summary>
public interface IDecisionAgent
{
    Task<AgentResult> DecideAsync(string caseId, AgentOverrides overrides, CancellationToken cancellationToken);
}

/// <summary>The hand-written decision loop (AGT-02, D-08, phase D-12, D-13).</summary>
public sealed class AgentRunner(ILlmGateway gateway, IAgentToolbox toolbox, AgentSettings settings, AgentContract contract)
    : IDecisionAgent
{
    public Task<AgentResult> DecideAsync(string caseId, AgentOverrides overrides, CancellationToken cancellationToken)
    {
        _ = (gateway, toolbox);
        return Task.FromResult(new AgentResult(
            AgentStatus.InfrastructureFailure,
            null,
            null,
            null,
            null,
            null,
            new AgentFailure("not_implemented", null, null, "The loop is not written yet."),
            null,
            [],
            settings.Model,
            contract.PromptVersion,
            settings.MaxSteps,
            contract.OutputSchemaSha256,
            null));
    }
}
