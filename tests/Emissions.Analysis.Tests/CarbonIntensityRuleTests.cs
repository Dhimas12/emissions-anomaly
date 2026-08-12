using Emissions.Analysis.Rules;
using Emissions.Domain;
using Microsoft.Extensions.Options;

namespace Emissions.Analysis.Tests;

public sealed class CarbonIntensityRuleTests
{
    private const double Tolerance = 1e-4;

    private static readonly CarbonIntensityRule Regla =
        new(Options.Create(new AnomalyDetectionOptions()));

    private static readonly EmissionRecord[] Madrid =
    [
        new(1, "Madrid", "2026-01", 12000, 2800),
        new(2, "Madrid", "2026-02", 12500, 2900),
        new(3, "Madrid", "2026-03", 12800, 2950),
        new(4, "Madrid", "2026-04", 79000, 18200),
    ];

    private static SiteHistory MadridHistory() => new("Madrid", Madrid, Madrid);

    private static RuleEvaluation Banda(SiteHistory h, EmissionRecord r) =>
        Assert.Single(Regla.Evaluate(r, h), e => e.RuleId == "CARBON_INTENSITY_BAND");

    private static RuleEvaluation Historico(SiteHistory h, EmissionRecord r) =>
        Assert.Single(Regla.Evaluate(r, h), e => e.RuleId == "CARBON_INTENSITY_HISTORY");

    // ---------------------------------------------------------------- RF-04a · banda

    // El id 8 del dataset. Barcelona solo tiene 3 registros válidos, así que su base
    // leave-one-out es de 2 y RF-04b no puede evaluarse: si la banda física no existiera,
    // este registro pasaría sin que nadie lo mirase.
    [Fact]
    public void RF04a_IntensidadFueraDeBanda_SeMarcaAlto()
    {
        var barcelona = new EmissionRecord[]
        {
            new(5, "Barcelona", "2026-01", 8500, 1950),
            new(6, "Barcelona", "2026-02", 8700, 2000),
            new(8, "Barcelona", "2026-04", 8900, 8500),
        };
        var historia = new SiteHistory("Barcelona", barcelona, barcelona);
        var id8 = barcelona[2];

        Assert.Equal(0.9551, id8.CarbonIntensity!.Value, Tolerance);

        var banda = Banda(historia, id8);

        Assert.True(banda.IsAnomaly);
        Assert.Equal(Severity.High, banda.Severity);
        Assert.Equal(0.9551, (double)banda.Evidence!["carbonIntensityKgPerKwh"]!, Tolerance);
        Assert.Equal(new[] { 0.05, 0.80 }, (IEnumerable<double>)banda.Evidence["plausibleRange"]!);

        // Y RF-04b no llega a pronunciarse: la banda es la única defensa aquí.
        Assert.Equal(RuleOutcome.NotEvaluated, Historico(historia, id8).Outcome);
    }

    // Por debajo del mínimo: el caso de las emisiones sin imputar. No es un dato "bueno y
    // bajo", es un dato al que probablemente le falta una parte.
    [Fact]
    public void RF04a_IntensidadDemasiadoBaja_SeMarcaAlto()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Oslo", "2026-01", 10000, 100),
            new(2, "Oslo", "2026-02", 10000, 2000),
            new(3, "Oslo", "2026-03", 10000, 2000),
            new(4, "Oslo", "2026-04", 10000, 2000),
        };
        var historia = new SiteHistory("Oslo", registros, registros);

        Assert.Equal(0.01, registros[0].CarbonIntensity!.Value, Tolerance);

        var banda = Banda(historia, registros[0]);

        Assert.True(banda.IsAnomaly);
        Assert.Equal(Severity.High, banda.Severity);
        Assert.Contains("faltar emisiones por imputar", banda.Message);
    }

    [Fact]
    public void RF04a_IntensidadDentroDeBanda_Pasa()
    {
        Assert.Equal(RuleOutcome.Passed, Banda(MadridHistory(), Madrid[0]).Outcome);
    }

    [Fact]
    public void RF04a_SinIntensidadCalculable_UnaSolaEvaluacionNoEvaluada()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Madrid", "2026-01", 12000, null),
            new(2, "Madrid", "2026-02", 12500, 2900),
            new(3, "Madrid", "2026-03", 12800, 2950),
            new(4, "Madrid", "2026-04", 12000, 2800),
        };
        var historia = new SiteHistory("Madrid", registros, registros);

        var evaluacion = Assert.Single(Regla.Evaluate(registros[0], historia));

        Assert.Equal("CARBON_INTENSITY_BAND", evaluacion.RuleId);
        Assert.Equal(RuleOutcome.NotEvaluated, evaluacion.Outcome);
    }

    // ------------------------------------------------------------- RF-04b · histórico

    // EL TEST CLAVE (004 T9). El id 4 escala consumo y emisiones a la vez: su intensidad
    // sigue siendo la de Madrid, así que dispara RF-03 y NO dispara RF-04. Es lo que separa
    // "la sede creció de verdad" de "el dato está mal", y el puente entre el código y la
    // respuesta al Escenario A.
    //
    //     intensidad(id 4)   = 18200 / 79000 = 0,230380
    //     mediana de la base = mediana{0,233333 · 0,232 · 0,230469} = 0,232
    //     rel                = (0,230380 − 0,232) / 0,232 = −0,006983  →  −0,70 %
    //
    // −0,70 % frente a una tolerancia del 40 %: pasa con muchísimo margen.
    //
    // Verificado por mutación, para saber qué protege de verdad y qué no:
    //
    //   · Si RF-04b comparase volumen en lugar de intensidad —la violación de ADR-04, el
    //     score único que colapsa las dos señales—, este test cae. Es lo que protege.
    //   · Si RF-04b perdiese el leave-one-out, este test **sigue pasando**: al meter el id 4
    //     en su propia base la mediana solo se mueve a 0,2312 y la desviación queda en
    //     −0,37 %, igual de inmaterial. RN-02 lo cubren SiteHistoryTests y, aquí, los tests
    //     de evidencia y de Barcelona, que sí ven cambiar el `baselineSize`.
    //
    // El margen de −0,70 % es tan holgado que este test no fija ninguna frontera. Eso lo
    // hace RF04b_LaFronteraEstaEnLaToleranciaConfigurada.
    [Fact]
    public void RF04b_IdCuatroEscalaConsumoYEmisiones_NoSeMarca()
    {
        var historia = MadridHistory();
        var id4 = Madrid[3];

        var baseline = historia.BaselineExcluding(id4, r => r.CarbonIntensity);
        var mediana = RobustStatistics.Median(baseline);

        // Los valores dorados de 003 §6 para el id 4, visibles en el test.
        Assert.Equal(0.2304, id4.CarbonIntensity!.Value, Tolerance);
        Assert.Equal(0.2320, mediana, Tolerance);
        Assert.Equal(-0.0070, RobustStatistics.RelativeDeviation(id4.CarbonIntensity.Value, mediana)!.Value, Tolerance);

        Assert.Equal(RuleOutcome.Passed, Historico(historia, id4).Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RF04b_LosDemasRegistrosDeMadrid_TampocoSeMarcan(int indice)
    {
        Assert.Equal(RuleOutcome.Passed, Historico(MadridHistory(), Madrid[indice]).Outcome);
    }

    // El test del id 4 confirma que no se marca, pero con −0,70 % pasaría igual con una
    // tolerancia del 5 % o del 60 %: no fija dónde está la frontera. Estos dos sí.
    //
    // Base de intensidad constante en 0,2000 (10000 kWh y 2000 kg en los tres registros).
    //
    //     2780 / 10000 = 0,2780  →  rel = (0,2780 − 0,2) / 0,2 = +0,39  →  Passed
    //     2820 / 10000 = 0,2820  →  rel = (0,2820 − 0,2) / 0,2 = +0,41  →  Anomaly
    //
    // Las dos intensidades caen dentro de la banda física, así que RF-04a no interfiere y
    // lo único que decide es IntensityRelativeTolerance. Si alguien la cambiase a 4,0 por
    // un dedazo, el segundo caso se volvería verde y este test lo delataría.
    [Theory]
    [InlineData(2780d, 0.39, RuleOutcome.Passed)]
    [InlineData(2820d, 0.41, RuleOutcome.Anomaly)]
    public void RF04b_LaFronteraEstaEnLaToleranciaConfigurada(
        double co2, double relEsperada, RuleOutcome esperado)
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", 10000, co2),
            new(2, "Sede", "2026-02", 10000, 2000),
            new(3, "Sede", "2026-03", 10000, 2000),
            new(4, "Sede", "2026-04", 10000, 2000),
        };
        var historia = new SiteHistory("Sede", registros, registros);
        var evaluado = registros[0];

        var mediana = RobustStatistics.Median(historia.BaselineExcluding(evaluado, r => r.CarbonIntensity));
        Assert.Equal(0.2, mediana, Tolerance);
        Assert.Equal(relEsperada, RobustStatistics.RelativeDeviation(evaluado.CarbonIntensity!.Value, mediana)!.Value, Tolerance);
        Assert.Equal(0.40, new AnomalyDetectionOptions().IntensityRelativeTolerance, Tolerance);

        Assert.Equal(esperado, Historico(historia, evaluado).Outcome);
    }

    // Escala de severidad propia de RF-04b (002 §3): 1,0 para High y 0,7 para Medium, que
    // no son las de RF-03 (2,0 y 1,0). El caso de rel = 1,5 es el que lo demuestra: con la
    // escala de consumo saldría Medium, y aquí sale High.
    //
    // Base constante en 0,2 (10000 kWh y 2000 kg). Los valores caen holgadamente dentro de
    // cada banda a propósito: un test en la frontera exacta mediría la aritmética de coma
    // flotante en lugar del criterio, que es lo que 003 §6 prohíbe.
    [Theory]
    [InlineData(3000d, 0.5, Severity.Low)]
    [InlineData(3700d, 0.85, Severity.Medium)]
    [InlineData(5000d, 1.5, Severity.High)]
    public void RF04b_LaSeveridadUsaLaEscalaDeIntensidadYNoLaDeConsumo(
        double co2Evaluado, double relEsperada, Severity esperada)
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", 10000, co2Evaluado),
            new(2, "Sede", "2026-02", 10000, 2000),
            new(3, "Sede", "2026-03", 10000, 2000),
            new(4, "Sede", "2026-04", 10000, 2000),
        };
        var historia = new SiteHistory("Sede", registros, registros);

        var evaluacion = Historico(historia, registros[0]);

        Assert.Equal(relEsperada, (double)evaluacion.Evidence!["relativeDeviation"]!, Tolerance);
        Assert.Equal(esperada, evaluacion.Severity);
    }

    // Valencia: dos registros, base leave-one-out de 1. El histórico no se puede evaluar,
    // pero la banda física sí, y eso es lo que protege a una sede recién dada de alta.
    [Fact]
    public void RF06_HistoricoInsuficiente_HistoricoNoSeEvaluaPeroLaBandaSi()
    {
        var valencia = new EmissionRecord[]
        {
            new(9, "Valencia", "2026-01", 6200, 1450),
            new(10, "Valencia", "2026-02", 6250, 1460),
        };
        var historia = new SiteHistory("Valencia", valencia, valencia);

        Assert.Equal(0.2339, valencia[0].CarbonIntensity!.Value, Tolerance);

        Assert.Equal(RuleOutcome.Passed, Banda(historia, valencia[0]).Outcome);

        var historico = Historico(historia, valencia[0]);
        Assert.Equal(RuleOutcome.NotEvaluated, historico.Outcome);
        Assert.Contains("no se ha podido comprobar", historico.Message);
    }

    [Fact]
    public void RN05_LaEvidenciaDelHistoricoLlevaLosCuatroDatos()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", 10000, 4000),
            new(2, "Sede", "2026-02", 10000, 2000),
            new(3, "Sede", "2026-03", 10000, 2000),
            new(4, "Sede", "2026-04", 10000, 2000),
        };
        var historia = new SiteHistory("Sede", registros, registros);

        var evidencia = Historico(historia, registros[0]).Evidence!;

        Assert.Equal(0.4, (double)evidencia["carbonIntensityKgPerKwh"]!, Tolerance);
        Assert.Equal(0.2, (double)evidencia["baselineMedianIntensity"]!, Tolerance);
        Assert.Equal(3, evidencia["baselineSize"]);
        Assert.Equal(1.0, (double)evidencia["relativeDeviation"]!, Tolerance);
    }

    [Fact]
    public void ADR05_LaReglaSeDeclaraSegunLaTablaDeContratos()
    {
        IAnomalyRule regla = Regla;

        Assert.Equal("RF-04", regla.RequirementId);
        Assert.Equal(40, regla.Priority);
        Assert.False(regla.AppliesToInvalidRecords);

        var evaluaciones = regla.Evaluate(Madrid[0], MadridHistory());
        Assert.Equal(2, evaluaciones.Count);
        Assert.Equal("CARBON_INTENSITY_BAND", evaluaciones[0].RuleId);
        Assert.Equal("RF-04a", evaluaciones[0].RequirementId);
        Assert.Equal("CARBON_INTENSITY_HISTORY", evaluaciones[1].RuleId);
        Assert.Equal("RF-04b", evaluaciones[1].RequirementId);
    }
}
