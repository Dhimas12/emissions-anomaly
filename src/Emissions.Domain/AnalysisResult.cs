namespace Emissions.Domain;

// RF-05. `Findings` lleva **todas** las reglas evaluadas, no solo las que dispararon:
// sin las que pasaron y sin las que no se pudieron evaluar, la salida no distingue
// "comprobado y correcto" de "no comprobable" (RF-06).
public sealed record RecordAnalysis(
    int Id,
    bool RequiresReview,
    string? Reason,
    Severity? Severity,
    string? Site,
    string? Month,
    IReadOnlyList<RuleEvaluation> Findings,
    IReadOnlyList<string> Notes);

public sealed record AnalysisSummary(
    int TotalRecords,
    int RecordsRequiringReview,
    int HighSeverity,
    int MediumSeverity,
    int LowSeverity);

public sealed record AnalysisResult(
    AnalysisSummary Summary,
    IReadOnlyList<RecordAnalysis> Results);
