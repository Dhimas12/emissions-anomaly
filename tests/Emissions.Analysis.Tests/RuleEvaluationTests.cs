using Emissions.Domain;

namespace Emissions.Analysis.Tests;

public sealed class RuleEvaluationTests
{
    [Fact]
    public void RF05_SoloLaEvaluacionDeAnomaliaCuentaComoAnomalia()
    {
        var passed = RuleEvaluation.Passed("CARBON_INTENSITY_BAND", "RF-04a", "Dentro de banda.");
        var notEvaluated = RuleEvaluation.NotEvaluated(
            "CONSUMPTION_DEVIATION", "RF-03", "Histórico insuficiente en Valencia.");
        var anomaly = RuleEvaluation.Anomaly(
            "STRUCTURAL_VALIDATION", "RF-01", "Consumo negativo.",
            Severity.High, new Dictionary<string, object?> { ["code"] = "NEGATIVE_ENERGY" });

        Assert.False(passed.IsAnomaly);
        Assert.False(notEvaluated.IsAnomaly);
        Assert.True(anomaly.IsAnomaly);
    }

    // RF-06: una regla que no se pudo evaluar no lleva severidad ni evidencia, porque no
    // hay nada que un auditor pueda reproducir. Confundirla con Passed es el fallo que
    // ADR-05 existe para evitar.
    [Fact]
    public void RF06_LasEvaluacionesQueNoSonAnomaliaNoLlevanSeveridadNiEvidencia()
    {
        var passed = RuleEvaluation.Passed("CARBON_INTENSITY_BAND", "RF-04a", "Dentro de banda.");
        var notEvaluated = RuleEvaluation.NotEvaluated(
            "CONSUMPTION_DEVIATION", "RF-03", "Histórico insuficiente en Valencia.");

        Assert.Equal(RuleOutcome.Passed, passed.Outcome);
        Assert.Equal(RuleOutcome.NotEvaluated, notEvaluated.Outcome);

        Assert.Null(passed.Severity);
        Assert.Null(passed.Evidence);
        Assert.Null(notEvaluated.Severity);
        Assert.Null(notEvaluated.Evidence);
    }

    // El motor agrega por severidad máxima (RF-05): si alguien reordena el enum, la
    // agregación cambia en silencio y ningún otro test lo notaría.
    [Fact]
    public void RF05_LasSeveridadesOrdenanDeMenorAMayor()
    {
        Assert.True(Severity.Low < Severity.Medium);
        Assert.True(Severity.Medium < Severity.High);
    }
}
