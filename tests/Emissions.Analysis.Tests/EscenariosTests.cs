using Emissions.Domain;

namespace Emissions.Analysis.Tests;

// Fija los números de ESCENARIOS.md. Sin esto, la respuesta al Escenario A es una
// afirmación escrita en un documento que se desfasa en silencio en cuanto alguien
// recalibra un umbral; con esto, es una propiedad verificable del sistema.
public sealed class EscenariosTests
{
    private const double Tolerance = 1e-4;

    // El histórico real de Madrid, incluido el mes de abril: es estructuralmente válido
    // —solo estadísticamente extremo—, así que RN-01 no lo excluye de la línea base.
    private static readonly EmissionRecord[] MadridConMayo =
    [
        new(1, "Madrid", "2026-01", 12000, 2800),
        new(2, "Madrid", "2026-02", 12500, 2900),
        new(3, "Madrid", "2026-03", 12800, 2950),
        new(4, "Madrid", "2026-04", 79000, 18200),
        new(99, "Madrid", "2026-05", 25000, 5900),
    ];

    // Escenario A · la fábrica de Madrid se amplió en mayo.
    //
    //   consumo     25.000 kWh   mediana 12.650 · MAD 400 · z = 20,83 · desviación +97,6 %
    //   intensidad  0,2360       mediana 0,2312 · desviación +2,06 %, tolerancia 40 %
    //
    // Dispara RF-03 y no dispara RF-04, que es la firma del crecimiento legítimo: una sede
    // que produce más consume más manteniendo su factor de emisión. Y lo hace con severidad
    // Low, la mínima, porque el 97,6 % se queda 2,37 puntos por debajo del umbral del 100 %
    // que separa Low de Medium. El motor clasifica el caso como la alerta menos urgente de
    // la cola sin saber nada de la ampliación.
    [Fact]
    public void EscenarioA_CrecimientoLegitimo_DisparaConsumoBajoYNoIntensidad()
    {
        var resultado = ServiceCollectionExtensions.CreateDefaultEngine().Analyze(MadridConMayo);
        var mayo = Assert.Single(resultado.Results, r => r.Id == 99);

        Assert.True(mayo.RequiresReview);
        Assert.Equal(Severity.Low, mayo.Severity);

        var consumo = Assert.Single(mayo.Findings, f => f.RuleId == "CONSUMPTION_DEVIATION");
        Assert.True(consumo.IsAnomaly);
        Assert.Equal(12650d, (double)consumo.Evidence!["baselineMedianKwh"]!, Tolerance);
        Assert.Equal(400d, (double)consumo.Evidence["baselineMad"]!, Tolerance);
        Assert.Equal(20.8252, (double)consumo.Evidence["modifiedZScore"]!, Tolerance);
        Assert.Equal(0.9763, (double)consumo.Evidence["relativeDeviation"]!, Tolerance);

        // Las dos evaluaciones de intensidad pasan: ni fuera de banda ni fuera de su
        // histórico. Es la mitad del patrón que separa el crecimiento del dato corrupto.
        Assert.Equal(
            RuleOutcome.Passed,
            Assert.Single(mayo.Findings, f => f.RuleId == "CARBON_INTENSITY_BAND").Outcome);
        Assert.Equal(
            RuleOutcome.Passed,
            Assert.Single(mayo.Findings, f => f.RuleId == "CARBON_INTENSITY_HISTORY").Outcome);
    }

    // Los dos números que el documento cita y que la evidencia de una evaluación `Passed`
    // no lleva, porque las que pasan no la incluyen.
    [Fact]
    public void EscenarioA_LaIntensidadDeMayoSeParaceALaHistoricaDeMadrid()
    {
        var mayo = MadridConMayo[^1];
        var historia = new SiteHistory("Madrid", MadridConMayo, MadridConMayo);

        var baseIntensidad = historia.BaselineExcluding(mayo, r => r.CarbonIntensity);
        var mediana = RobustStatistics.Median(baseIntensidad);

        Assert.Equal(0.2360, mayo.CarbonIntensity!.Value, Tolerance);
        Assert.Equal(0.2312, mediana, Tolerance);
        Assert.Equal(
            0.0206,
            RobustStatistics.RelativeDeviation(mayo.CarbonIntensity.Value, mediana)!.Value,
            Tolerance);
    }

    // El margen es estrecho a propósito y conviene que se vea: si el 97,6 % subiese al
    // 100 %, la severidad pasaría a Medium y el argumento del documento —"la alerta menos
    // urgente de la cola"— dejaría de ser cierto.
    [Fact]
    public void EscenarioA_LaSeveridadBajaSeApoyaEnUnMargenDeDosPuntosYMedio()
    {
        var umbralMedio = new AnomalyDetectionOptions().MediumSeverityRelativeDeviation;

        Assert.Equal(1.0, umbralMedio, Tolerance);
        Assert.True(0.9763 < umbralMedio);
        Assert.Equal(0.0237, umbralMedio - 0.9763, Tolerance);
    }
}
