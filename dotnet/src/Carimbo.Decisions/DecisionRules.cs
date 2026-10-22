using Carimbo.Domain;

namespace Carimbo.Decisions;

/// <summary>The label a case gets from <see cref="DecisionRules.Label"/>, and the number of the rule that gave it.</summary>
public sealed record DecisionLabel(DecisionOutcome Outcome, DecisionCause Cause, int Rule);

/// <summary>
/// The labelling rule <c>decision-labels-1</c> (D-34), the single implementation. The generator labels every
/// case with it, the agent prompt states the same list in prose, and the grader reads its output from
/// <c>expected.json</c>.
/// </summary>
public static class DecisionRules
{
    /// <summary>The version string recorded in the manifest and in expected.json.</summary>
    public const string LabellingVersion = "decision-labels-1";

    /// <summary>Applies the ordered rules; the first match wins.</summary>
    public static DecisionLabel Label(DecisionCaseFile @case, DecisionPolicy policy) => throw new NotImplementedException();
}
