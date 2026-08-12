using Emissions.Analysis.Rules;
using Emissions.Domain;
using Microsoft.Extensions.Options;

namespace Emissions.Analysis.Tests;

public sealed class AnomalyDetectionEngineTests
{
    private const double Tolerance = 1e-4;

    private static readonly IAnomalyDetectionEngine Motor =
        ServiceCollectionExtensions.CreateDefaultEngine();

    private static RecordAnalysis Resultado(AnalysisResult resultado, int id) =>
        Assert.Single(resultado.Results, r => r.Id == id);

    private static IReadOnlyDictionary<string, object?> EvidenciaDe(RecordAnalysis analisis, string ruleId) =>
        Assert.Single(analisis.Findings, f => f.RuleId == ruleId).Evidence!;

    // ------------------------------------------------------- RN-01 · fase 1 antes que la 2

    // Madrid con un registro corrupto añadido. Si el motor construyese las líneas base
    // antes de saber qué registros son fiables, el −900 entraría en la base del id 4: la
    // mediana caería de 12.500 a 12.250 y el tamaño de base subiría de 3 a 4.
    //
    // Verificado por mutación: construyendo las SiteHistory con todos los registros en vez
    // de solo los válidos, este test cae por las dos aserciones de evidencia. El veredicto
    // del id 4 **no** cambia —seguiría siendo anomalía alta—, así que comprobar solo el
    // resultado no detectaría la inversión de fases. Hay que mirar la base.
    [Fact]
    public void RN01_ElRegistroInvalidoNoContaminaLaBaseDeSuSede()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Madrid", "2026-01", 12000, 2800),
            new(2, "Madrid", "2026-02", 12500, 2900),
            new(3, "Madrid", "2026-03", 12800, 2950),
            new(4, "Madrid", "2026-04", 79000, 18200),
            new(11, "Madrid", "2026-05", -900, -210),
        };

        var evidencia = EvidenciaDe(Resultado(Motor.Analyze(registros), 4), "CONSUMPTION_DEVIATION");

        Assert.Equal(3, evidencia["baselineSize"]);
        Assert.Equal(12500d, (double)evidencia["baselineMedianKwh"]!, Tolerance);
        Assert.Equal(300d, (double)evidencia["baselineMad"]!, Tolerance);
        Assert.Equal(149.5142, (double)evidencia["modifiedZScore"]!, Tolerance);
    }

    // --------------------------------------------------------------- RF-05 · agregación

    // Un registro con dos anomalías de distinta severidad: RF-03 la marca Low por una
    // desviación de consumo del 50 %, y RF-04b la marca High porque la intensidad se
    // duplica y media. El motivo tiene que ser el de la severidad mayor.
    [Fact]
    public void RF05_ConDosAnomaliasDeDistintaSeveridad_ElMotivoEsElDeLaMayor()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", 15000, 7500),
            new(2, "Sede", "2026-02", 10000, 2000),
            new(3, "Sede", "2026-03", 10000, 2000),
            new(4, "Sede", "2026-04", 10000, 2000),
        };

        var analisis = Resultado(Motor.Analyze(registros), 1);

        Assert.True(analisis.RequiresReview);
        Assert.Equal(Severity.High, analisis.Severity);

        var consumo = Assert.Single(analisis.Findings, f => f.RuleId == "CONSUMPTION_DEVIATION");
        var intensidad = Assert.Single(analisis.Findings, f => f.RuleId == "CARBON_INTENSITY_HISTORY");
        Assert.Equal(Severity.Low, consumo.Severity);
        Assert.Equal(Severity.High, intensidad.Severity);

        Assert.Equal(intensidad.Message, analisis.Reason);
    }

    // El dataset del enunciado no tiene ningún registro con dos anomalías de la misma
    // severidad, así que este caso está fabricado: el id 1 dispara validación estructural
    // (prioridad 10) y duplicidad de periodo (prioridad 20), las dos High. Gana la de menor
    // prioridad numérica, de modo que al analista se le explica primero que el consumo es
    // imposible y no que el periodo está repetido.
    [Fact]
    public void RF05_ADosAnomaliasDeIgualSeveridad_GanaLaDeMenorPrioridad()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", -900, -210),
            new(2, "Sede", "2026-01", 8700, 2000),
        };

        var analisis = Resultado(Motor.Analyze(registros), 1);

        var severidades = analisis.Findings.Where(f => f.IsAnomaly).Select(f => f.Severity).Distinct();
        Assert.Equal([Severity.High], severidades);
        Assert.Contains(analisis.Findings, f => f.RuleId == "STRUCTURAL_VALIDATION" && f.IsAnomaly);
        Assert.Contains(analisis.Findings, f => f.RuleId == "DUPLICATE_PERIOD" && f.IsAnomaly);

        Assert.Equal(Severity.High, analisis.Severity);
        Assert.Contains("consumo registrado es negativo", analisis.Reason);
        Assert.DoesNotContain("duplicado", analisis.Reason);
    }

    // El test anterior no protege el desempate por sí solo: `CreateDefaultEngine` registra
    // las reglas ya en orden ascendente de prioridad, así que pasaría igual aunque el motor
    // no ordenase nada. Este las registra **al revés** a propósito. Verificado por mutación:
    // sustituyendo `rules.OrderBy(r => r.Priority).ToList()` por `rules.ToList()` cae este
    // test y solo este, mientras el resto de la suite sigue en verde.
    [Fact]
    public void RF05_ElDesempateNoDependeDelOrdenDeRegistroDeLasReglas()
    {
        var opciones = Options.Create(new AnomalyDetectionOptions());
        var motorAlReves = new AnomalyDetectionEngine(
        [
            new CarbonIntensityRule(opciones),
            new ConsumptionDeviationRule(opciones),
            new DuplicatePeriodRule(),
            new StructuralValidationRule(),
        ]);

        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", -900, -210),
            new(2, "Sede", "2026-01", 8700, 2000),
        };

        var analisis = Resultado(motorAlReves.Analyze(registros), 1);

        Assert.Contains("consumo registrado es negativo", analisis.Reason);

        // Y `Findings` sale ordenado por prioridad pese al registro invertido (RN-06).
        Assert.Equal(
            new[]
            {
                "STRUCTURAL_VALIDATION", "STRUCTURAL_VALIDATION", "DUPLICATE_PERIOD",
                "CONSUMPTION_DEVIATION", "CARBON_INTENSITY_BAND",
            },
            analisis.Findings.Select(f => f.RuleId).ToList());
    }

    // El otro registro del par sí tiene la duplicidad como único motivo.
    [Fact]
    public void RF02_ElRegistroSanoDelParDuplicado_SeMarcaPorDuplicidad()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", -900, -210),
            new(2, "Sede", "2026-01", 8700, 2000),
        };

        var analisis = Resultado(Motor.Analyze(registros), 2);

        Assert.True(analisis.RequiresReview);
        Assert.Contains("duplicado", analisis.Reason);
    }

    // ------------------------------------------------------------------ RF-06 · silencio

    [Fact]
    public void RF06_LasReglasQueNoAplicanAUnRegistroInvalido_DejanNotaEnLugarDeSilencio()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Madrid", "2026-01", 12000, 2800),
            new(2, "Madrid", "2026-02", 12500, 2900),
            new(3, "Madrid", "2026-03", 12800, 2950),
            new(11, "Madrid", "2026-05", -900, -210),
        };

        var analisis = Resultado(Motor.Analyze(registros), 11);

        var noEvaluadas = analisis.Findings
            .Where(f => f.Outcome == RuleOutcome.NotEvaluated)
            .Select(f => f.RuleId)
            .ToList();

        Assert.Equal(new[] { "CONSUMPTION_DEVIATION", "CARBON_INTENSITY_BAND" }, noEvaluadas);
        Assert.All(
            analisis.Notes,
            nota => Assert.Equal("No se evalúa: el registro no supera la validación estructural.", nota));
        Assert.Equal(2, analisis.Notes.Count);
    }

    // ------------------------------------------------- agrupación por sede (003 §3, T7)

    // Dos fuentes distintas escriben la misma sede de tres formas. Si el motor agrupase por
    // igualdad ordinal, el id 4 se quedaría solo en su grupo, su base sería de cero y RF-03
    // devolvería NotEvaluated: el atípico del enunciado pasaría desapercibido en silencio.
    [Fact]
    public void RN01_LaAgrupacionPorSedeIgnoraMayusculasYEspacios()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Madrid", "2026-01", 12000, 2800),
            new(2, "MADRID", "2026-02", 12500, 2900),
            new(3, " madrid ", "2026-03", 12800, 2950),
            new(4, "Madrid", "2026-04", 79000, 18200),
        };

        var analisis = Resultado(Motor.Analyze(registros), 4);

        Assert.True(analisis.RequiresReview);
        Assert.Equal(3, EvidenciaDe(analisis, "CONSUMPTION_DEVIATION")["baselineSize"]);

        // Cada registro conserva su sede tal y como llegó: la normalización es solo la
        // clave de agrupación, no el dato que se le enseña al analista.
        Assert.Equal(" madrid ", Resultado(Motor.Analyze(registros), 3).Site);
    }

    // ------------------------------------------------------------------ resumen agregado

    [Fact]
    public void RF05_ElResumenCuentaRegistrosYNoHallazgos()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", -900, -210),
            new(2, "Sede", "2026-01", 8700, 2000),
            new(3, "Sede", "2026-02", 8600, 1980),
        };

        var resultado = Motor.Analyze(registros);

        // El id 1 acumula tres anomalías altas y sigue contando como un solo registro.
        Assert.True(Resultado(resultado, 1).Findings.Count(f => f.IsAnomaly) > 1);
        Assert.Equal(3, resultado.Summary.TotalRecords);
        Assert.Equal(2, resultado.Summary.RecordsRequiringReview);
        Assert.Equal(2, resultado.Summary.HighSeverity);
        Assert.Equal(0, resultado.Summary.MediumSeverity);
        Assert.Equal(0, resultado.Summary.LowSeverity);
    }

    [Fact]
    public void RF05_UnLoteVacioDevuelveUnResumenVacio()
    {
        var resultado = Motor.Analyze([]);

        Assert.Empty(resultado.Results);
        Assert.Equal(0, resultado.Summary.TotalRecords);
    }

    // ------------------------------------------------------------ ADR-06 · uso embebido

    [Fact]
    public void ADR06_CreateDefaultEngineAceptaUmbralesPropios()
    {
        var registros = new EmissionRecord[]
        {
            new(1, "Sede", "2026-01", 13000, 2600),
            new(2, "Sede", "2026-02", 10000, 2000),
            new(3, "Sede", "2026-03", 10000, 2000),
            new(4, "Sede", "2026-04", 10000, 2000),
        };

        // Con la materialidad por defecto (25 %) un +30 % se marca...
        Assert.True(Resultado(Motor.Analyze(registros), 1).RequiresReview);

        // ...y subiéndola al 50 % deja de marcarse, sin recompilar nada (RN-07).
        var tolerante = ServiceCollectionExtensions.CreateDefaultEngine(
            new AnomalyDetectionOptions { MinimumRelativeDeviation = 0.5 });

        Assert.False(Resultado(tolerante.Analyze(registros), 1).RequiresReview);
    }
}
