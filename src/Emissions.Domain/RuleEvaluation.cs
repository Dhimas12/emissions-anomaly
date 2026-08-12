namespace Emissions.Domain;

// RN-05. Cada evaluación viaja con la evidencia numérica que la produjo para que un
// auditor ESG pueda reproducir la decisión con una calculadora, sin leer el código.
// El `RuleId` es propio de la evaluación y no de la regla: `CarbonIntensityRule` emite
// dos, la de banda física y la de coherencia con el histórico (RF-04a y RF-04b).
public sealed record RuleEvaluation(
    string RuleId,
    string RequirementId,
    RuleOutcome Outcome,
    string Message,
    Severity? Severity = null,
    IReadOnlyDictionary<string, object?>? Evidence = null)
{
    public bool IsAnomaly => Outcome == RuleOutcome.Anomaly;

    public static RuleEvaluation Passed(string ruleId, string requirementId, string message) =>
        new(ruleId, requirementId, RuleOutcome.Passed, message);

    public static RuleEvaluation NotEvaluated(string ruleId, string requirementId, string message) =>
        new(ruleId, requirementId, RuleOutcome.NotEvaluated, message);

    public static RuleEvaluation Anomaly(
        string ruleId,
        string requirementId,
        string message,
        Severity severity,
        IReadOnlyDictionary<string, object?> evidence) =>
        new(ruleId, requirementId, RuleOutcome.Anomaly, message, severity, evidence);
}
