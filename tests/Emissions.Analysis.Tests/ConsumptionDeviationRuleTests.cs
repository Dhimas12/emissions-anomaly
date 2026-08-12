using Emissions.Analysis.Rules;
using Emissions.Domain;
using Microsoft.Extensions.Options;

namespace Emissions.Analysis.Tests;

public sealed class ConsumptionDeviationRuleTests
{
    private const double Tolerance = 1e-4;

    // La tabla de RF-03 de 003 §6 da tres decimales, no cuatro. Compararla con 1e-4 sería
    // exigirle una precisión que no declara: z(12000) es −1,798667 y la tabla lo escribe
    // −1,799, que es su redondeo correcto. La tolerancia es media unidad del último dígito.
    private const double ToleranciaTablaRf03 = 5e-4;

    private static readonly ConsumptionDeviationRule Regla =
        new(Options.Create(new AnomalyDetectionOptions()));

    // Los cuatro registros de Madrid del dataset del enunciado.
    private static readonly EmissionRecord[] Madrid =
    [
        new(1, "Madrid", "2026-01", 12000, 2800),
        new(2, "Madrid", "2026-02", 12500, 2900),
        new(3, "Madrid", "2026-03", 12800, 2950),
        new(4, "Madrid", "2026-04", 79000, 18200),
    ];

    private static RuleEvaluation Evaluar(SiteHistory historia, EmissionRecord record) =>
        Assert.Single(Regla.Evaluate(record, historia));

    // Construye una historia donde `valor` se compara contra exactamente `baseValores`.
    private static (SiteHistory Historia, EmissionRecord Evaluado) Escenario(
        double valor, params double[] baseValores)
    {
        var records = new List<EmissionRecord> { new(0, "Madrid", "2026-12", valor, 100) };
        records.AddRange(baseValores.Select((v, i) =>
            new EmissionRecord(i + 1, "Madrid", $"2026-{i + 1:00}", v, 100)));

        var historia = new SiteHistory("Madrid", records, records);
        return (historia, records[0]);
    }

    [Fact]
    public void RF03_ConsumoMuySuperiorALaBase_SeMarcaAlto()
    {
        var historia = new SiteHistory("Madrid", Madrid, Madrid);

        var evaluacion = Evaluar(historia, Madrid[3]);

        Assert.True(evaluacion.IsAnomaly);
        Assert.Equal(Severity.High, evaluacion.Severity);

        // Valores dorados de 003 §6 para el id 4.
        var evidencia = evaluacion.Evidence!;
        Assert.Equal(3, evidencia["baselineSize"]);
        Assert.Equal(12500d, (double)evidencia["baselineMedianKwh"]!, Tolerance);
        Assert.Equal(300d, (double)evidencia["baselineMad"]!, Tolerance);
        Assert.Equal(149.5142, (double)evidencia["modifiedZScore"]!, Tolerance);
        Assert.Equal(5.32, (double)evidencia["relativeDeviation"]!, Tolerance);
    }

    // 003 §6, tabla de RF-03: los tres conviven con el atípico del id 4 y ninguno se marca.
    // Es lo que fallaría si alguien sustituyese la mediana por la media.
    [Theory]
    [InlineData(0, 12800d, 300d, -1.799, -0.0625)]
    [InlineData(1, 12800d, 800d, -0.253, -0.0234)]
    [InlineData(2, 12500d, 500d, 0.405, 0.0240)]
    public void RF03_ConsumoNormal_NoSeMarca(
        int indice, double mediana, double mad, double z, double rel)
    {
        var historia = new SiteHistory("Madrid", Madrid, Madrid);

        var evaluacion = Evaluar(historia, Madrid[indice]);

        Assert.Equal(RuleOutcome.Passed, evaluacion.Outcome);

        // La evaluación pasa, así que no lleva evidencia; los números de 003 §6 se
        // comprueban aquí directamente para que la tabla quede amarrada al código.
        var baseline = historia.BaselineExcluding(Madrid[indice], r => r.EnergyKwh);
        Assert.Equal(mediana, RobustStatistics.Median(baseline), Tolerance);
        Assert.Equal(mad, RobustStatistics.MedianAbsoluteDeviation(baseline, mediana), Tolerance);
        Assert.Equal(
            z,
            RobustStatistics.ModifiedZScore(Madrid[indice].EnergyKwh!.Value, mediana, mad)!.Value,
            ToleranciaTablaRf03);
        Assert.Equal(
            rel,
            RobustStatistics.RelativeDeviation(Madrid[indice].EnergyKwh!.Value, mediana)!.Value,
            ToleranciaTablaRf03);
    }

    // El test que documenta ADR-02, y el único que distingue la doble condición de un
    // criterio puramente estadístico. Con base [12000, 12500, 12800] la mediana es 12.500
    // y el MAD 300, así que 15.000 kWh da:
    //
    //     z   = 0,6745 × (15000 − 12500) / 300 = 5,6208   →  supera 3,5 con holgura
    //     rel = (15000 − 12500) / 12500        = 0,20     →  no llega al 25 %
    //
    // El z-score dice que sí y la materialidad dice que no, y no se marca. Comprobado por
    // mutación: al sustituir la doble condición por `if (!extremo)` caen este test y el de
    // serie constante, y solo esos dos. Ningún caso del dataset del enunciado lo detecta,
    // porque todos fallan las dos condiciones a la vez o cumplen las dos.
    //
    // Un +20 % mensual en energía lo explican el clima o el calendario laboral sin
    // necesidad de sospechar del dato.
    [Fact]
    public void RN03_DesviacionExtremaPeroInmaterial_NoSeMarca()
    {
        var (historia, evaluado) = Escenario(15000, 12000, 12500, 12800);

        var baseline = historia.BaselineExcluding(evaluado, r => r.EnergyKwh);
        var mediana = RobustStatistics.Median(baseline);
        var mad = RobustStatistics.MedianAbsoluteDeviation(baseline, mediana);

        // Las dos mitades del criterio, visibles y verificables a mano.
        Assert.Equal(5.6208, RobustStatistics.ModifiedZScore(15000, mediana, mad)!.Value, Tolerance);
        Assert.True(Math.Abs(RobustStatistics.ModifiedZScore(15000, mediana, mad)!.Value) > 3.5);
        Assert.Equal(0.20, RobustStatistics.RelativeDeviation(15000, mediana)!.Value, Tolerance);
        Assert.False(Math.Abs(RobustStatistics.RelativeDeviation(15000, mediana)!.Value) > 0.25);

        Assert.Equal(RuleOutcome.Passed, Evaluar(historia, evaluado).Outcome);
    }

    // RF-06: con dos puntos de base no se infiere ninguna normalidad. No se marca, pero
    // tampoco se afirma que el consumo sea correcto.
    [Fact]
    public void RF06_HistoricoInsuficiente_NoSeEvalua()
    {
        var (historia, evaluado) = Escenario(50000, 8500, 8700);

        var evaluacion = Evaluar(historia, evaluado);

        Assert.Equal(RuleOutcome.NotEvaluated, evaluacion.Outcome);
        Assert.False(evaluacion.IsAnomaly);
        Assert.Null(evaluacion.Severity);
        Assert.Contains("Histórico insuficiente", evaluacion.Message);
    }

    // ADR-02 en su caso límite. Con la serie constante el MAD es cero, ModifiedZScore no
    // devuelve nada y el criterio recae entero en la materialidad.
    [Fact]
    public void RF03_SerieConstante_MadCero_DecideLaMaterialidad()
    {
        var (historiaBaja, baja) = Escenario(5200, 5000, 5000, 5000);
        var (historiaAlta, alta) = Escenario(9000, 5000, 5000, 5000);

        // +4 %: el MAD cero hace "extremo" verdadero, pero la materialidad lo frena.
        Assert.Equal(RuleOutcome.Passed, Evaluar(historiaBaja, baja).Outcome);

        // +80 %: extremo y material, así que se marca.
        var marcada = Evaluar(historiaAlta, alta);
        Assert.True(marcada.IsAnomaly);
        Assert.Equal(Severity.Low, marcada.Severity);
    }

    // Un auditor tiene que poder distinguir "no se calculó" de "se olvidó incluirlo".
    [Fact]
    public void RN05_ConMadCero_LaEvidenciaLlevaModifiedZScoreExplicitamenteNulo()
    {
        var (historia, evaluado) = Escenario(9000, 5000, 5000, 5000);

        var evidencia = Evaluar(historia, evaluado).Evidence!;

        Assert.True(evidencia.ContainsKey("modifiedZScore"));
        Assert.Null(evidencia["modifiedZScore"]);
        Assert.Equal(0d, (double)evidencia["baselineMad"]!, Tolerance);
    }

    // El mensaje va dirigido a un analista, y citar un z-score que no existe sería
    // inventarse la justificación de la marca.
    [Fact]
    public void RN05_ConMadCero_ElMensajeNoMencionaNingunZScore()
    {
        var (historia, evaluado) = Escenario(9000, 5000, 5000, 5000);

        var mensaje = Evaluar(historia, evaluado).Message;

        Assert.DoesNotContain("z-score", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("z ", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("desviación 80 %", mensaje);
    }

    [Theory]
    [InlineData(0.30, Severity.Low)]
    [InlineData(0.99, Severity.Low)]
    [InlineData(1.00, Severity.Medium)]
    [InlineData(1.99, Severity.Medium)]
    [InlineData(2.00, Severity.High)]
    [InlineData(5.32, Severity.High)]
    public void RF03_LaSeveridadSaleDeLaDesviacionRelativa(double desviacion, Severity esperada)
    {
        var (historia, evaluado) = Escenario(5000 * (1 + desviacion), 5000, 5000, 5000);

        Assert.Equal(esperada, Evaluar(historia, evaluado).Severity);
    }

    // La regla mira volumen y solo volumen (ADR-04): una caída fuerte también es anomalía.
    [Fact]
    public void RF03_CaidaFuerteDelConsumo_TambienSeMarca()
    {
        var (historia, evaluado) = Escenario(1000, 12000, 12500, 12800);

        var evaluacion = Evaluar(historia, evaluado);

        Assert.True(evaluacion.IsAnomaly);
        Assert.Contains("por debajo", evaluacion.Message);
        Assert.True((double)evaluacion.Evidence!["relativeDeviation"]! < 0);
    }

    [Fact]
    public void RF03_SinConsumo_NoSeEvalua()
    {
        var records = new EmissionRecord[]
        {
            new(1, "Madrid", "2026-01", null, 2800),
            new(2, "Madrid", "2026-02", 12500, 2900),
            new(3, "Madrid", "2026-03", 12800, 2950),
            new(4, "Madrid", "2026-04", 12000, 2800),
        };
        var historia = new SiteHistory("Madrid", records, records);

        Assert.Equal(RuleOutcome.NotEvaluated, Evaluar(historia, records[0]).Outcome);
    }

    [Fact]
    public void ADR05_LaReglaSeDeclaraSegunLaTablaDeContratos()
    {
        IAnomalyRule regla = Regla;

        Assert.Equal("CONSUMPTION_DEVIATION", regla.RuleId);
        Assert.Equal("RF-03", regla.RequirementId);
        Assert.Equal(30, regla.Priority);
        Assert.False(regla.AppliesToInvalidRecords);
    }
}
