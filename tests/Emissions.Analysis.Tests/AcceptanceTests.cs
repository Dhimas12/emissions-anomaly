using Emissions.Domain;

namespace Emissions.Analysis.Tests;

// La tabla de aceptación de 001 §6, registro a registro. Es la definición operativa de
// "funciona": si esto pasa, la entrega cumple lo que promete.
public sealed class AcceptanceTests
{
    private const double Tolerance = 1e-4;

    private static readonly AnalysisResult Resultado =
        ServiceCollectionExtensions.CreateDefaultEngine().Analyze(SampleDataset.Records);

    private static RecordAnalysis Analisis(int id) => Assert.Single(Resultado.Results, r => r.Id == id);

    private static List<string> ReglasQueDisparan(RecordAnalysis analisis) =>
        analisis.Findings.Where(f => f.IsAnomaly).Select(f => f.RuleId).Distinct().ToList();

    // Un aserto por registro. Comprobar solo el resumen agregado dejaría pasar un sistema
    // que marcase tres registros equivocados: los números cuadrarían igual.
    [Theory]
    [InlineData(1, "Madrid", false, null, null)]
    [InlineData(2, "Madrid", false, null, null)]
    [InlineData(3, "Madrid", false, null, null)]
    [InlineData(4, "Madrid", true, Severity.High, "CONSUMPTION_DEVIATION")]
    [InlineData(5, "Barcelona", false, null, null)]
    [InlineData(6, "Barcelona", false, null, null)]
    [InlineData(7, "Barcelona", true, Severity.High, "STRUCTURAL_VALIDATION")]
    [InlineData(8, "Barcelona", true, Severity.High, "CARBON_INTENSITY_BAND")]
    [InlineData(9, "Valencia", false, null, null)]
    [InlineData(10, "Valencia", false, null, null)]
    public void RF05_CadaRegistroReproduceLaTablaDeAceptacion(
        int id, string sede, bool requiereRevision, Severity? severidad, string? regla)
    {
        var analisis = Analisis(id);

        Assert.Equal(sede, analisis.Site);
        Assert.Equal(requiereRevision, analisis.RequiresReview);
        Assert.Equal(severidad, analisis.Severity);

        if (regla is null)
        {
            Assert.Empty(ReglasQueDisparan(analisis));
            Assert.Null(analisis.Reason);
        }
        else
        {
            Assert.Equal(new[] { regla }, ReglasQueDisparan(analisis));
            Assert.NotNull(analisis.Reason);
        }
    }

    [Fact]
    public void RF05_ElResumenAgregadoEsElDeLaTabla()
    {
        Assert.Equal(10, Resultado.Summary.TotalRecords);
        Assert.Equal(3, Resultado.Summary.RecordsRequiringReview);
        Assert.Equal(3, Resultado.Summary.HighSeverity);
        Assert.Equal(0, Resultado.Summary.MediumSeverity);
        Assert.Equal(0, Resultado.Summary.LowSeverity);

        Assert.Equal([4, 7, 8], Resultado.Results.Where(r => r.RequiresReview).Select(r => r.Id));
    }

    // 001 §6: "consumo 6,3× su línea base". El motivo que ve el analista tiene que decir
    // eso, con los números dentro, y no una fórmula que haya que ir a buscar al código.
    [Fact]
    public void RF03_ElMotivoDelIdCuatroCitaLaLineaBaseYLaDesviacion()
    {
        var motivo = Analisis(4).Reason!;

        Assert.Contains("79000 kWh", motivo);
        Assert.Contains("mediana de 12500 kWh", motivo);
        Assert.Contains("6,3x", motivo);
        Assert.Contains("desviación 532 %", motivo);
    }

    // El id 4 escala consumo y emisiones a la vez: RF-03 dispara y RF-04 no. Es lo que
    // separa "la sede creció de verdad" de "el dato está mal", y sostiene el Escenario A.
    [Fact]
    public void ADR04_ElIdCuatroDisparaConsumoPeroNoIntensidad()
    {
        var analisis = Analisis(4);

        Assert.Contains(analisis.Findings, f => f.RuleId == "CONSUMPTION_DEVIATION" && f.IsAnomaly);
        Assert.DoesNotContain(analisis.Findings, f => f.RuleId.StartsWith("CARBON_INTENSITY") && f.IsAnomaly);
    }

    // El id 8 se detecta por la banda física, que no necesita histórico. Barcelona solo
    // tiene 3 registros válidos, así que su base leave-one-out es de 2 y RF-04b no llega a
    // evaluarse: sin RF-04a este registro pasaría sin que nadie lo mirase.
    [Fact]
    public void RF04a_ElIdOchoSeDetectaSinHistorico()
    {
        var analisis = Analisis(8);

        var banda = Assert.Single(analisis.Findings, f => f.RuleId == "CARBON_INTENSITY_BAND");
        Assert.True(banda.IsAnomaly);
        Assert.Equal(0.9551, (double)banda.Evidence!["carbonIntensityKgPerKwh"]!, Tolerance);

        var historico = Assert.Single(analisis.Findings, f => f.RuleId == "CARBON_INTENSITY_HISTORY");
        Assert.Equal(RuleOutcome.NotEvaluated, historico.Outcome);
    }

    [Fact]
    public void RF01_ElIdSieteProduceLasDosAnomaliasEstructurales()
    {
        var codigos = Analisis(7).Findings
            .Where(f => f.IsAnomaly)
            .Select(f => (string)f.Evidence!["code"]!)
            .ToList();

        Assert.Equal(new[] { "NEGATIVE_ENERGY", "NEGATIVE_CO2" }, codigos);
    }

    // RF-06 con verificación **positiva**. Que un registro no se marque puede significar
    // "comprobado y correcto" o "no comprobable", y la diferencia entre esas dos cosas es
    // media especificación: sin la declaración explícita, ambas comparten el mismo silencio
    // y ahí es donde nacen los falsos negativos de producción.
    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void RF06_LosRegistrosDeValenciaDeclaranSuHistoricoInsuficiente(int id)
    {
        var analisis = Analisis(id);

        Assert.False(analisis.RequiresReview);
        Assert.Equal(2, analisis.Notes.Count);
        Assert.All(analisis.Notes, nota => Assert.Contains("insuficiente", nota));
        Assert.All(analisis.Notes, nota => Assert.Contains("no se ha podido comprobar", nota));

        // Y las dos reglas estadísticas lo dicen con su propio identificador, no en bloque.
        var noEvaluadas = analisis.Findings
            .Where(f => f.Outcome == RuleOutcome.NotEvaluated)
            .Select(f => f.RuleId)
            .ToList();

        Assert.Equal(new[] { "CONSUMPTION_DEVIATION", "CARBON_INTENSITY_HISTORY" }, noEvaluadas);
    }

    // Los ids 5 y 6 tampoco se dan por buenos: Barcelona tiene base de 2 y no llega al
    // mínimo. La banda física sí los comprueba, y por eso su nota es una sola.
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void RF06_LosRegistrosSanosDeBarcelonaTampocoSeDanPorComprobados(int id)
    {
        var analisis = Analisis(id);

        Assert.False(analisis.RequiresReview);
        Assert.NotEmpty(analisis.Notes);
        Assert.Equal(
            RuleOutcome.Passed,
            Assert.Single(analisis.Findings, f => f.RuleId == "CARBON_INTENSITY_BAND").Outcome);
    }

    // ---------------------------------------------------------------------------- ADR-01

    // El test que documenta por qué existe ADR-01, con las dos estadísticas calculadas aquí
    // en lugar de citadas. Sobre la serie de Madrid [12000, 12500, 12800, 79000]:
    //
    //   media = 29.075 · σ = 28.825,63  →  z clásico del 79.000 = 1,732
    //   mediana LOO = 12.500 · MAD = 300 →  z modificado         = 149,5142
    //
    // Con estadística clásica el atípico queda por debajo del umbral habitual de 3 y **no
    // se detecta**: es el efecto de enmascaramiento, y con n = 4 no es teórico. Los tres
    // valores sanos, mientras tanto, quedan a media σ de una media que no representa a
    // ninguno. Si alguien sustituyese la mediana por la media, el ejercicio dejaría de
    // resolverse y este test es el que lo explicaría.
    [Fact]
    public void ADR01_MediaYDesviacionTipicaEnmascararianElAtipico()
    {
        var madrid = SampleDataset.Records
            .Where(r => r.Site == "Madrid")
            .Select(r => r.EnergyKwh!.Value)
            .ToList();

        var media = madrid.Average();
        var sigma = Math.Sqrt(madrid.Average(v => (v - media) * (v - media)));
        var zClasico = (79000d - media) / sigma;

        // Los valores de la tabla de 002 §ADR-01, que da media y σ al entero y z a dos
        // decimales; la tolerancia se ajusta a esa precisión (003 §6).
        Assert.Equal(29075d, media, Tolerance);
        Assert.Equal(28826d, sigma, 1d);
        Assert.Equal(1.73, zClasico, 5e-3);

        // Por debajo del umbral clásico de 3: no se detectaría.
        Assert.True(zClasico < 3d);

        // La robusta, sobre la misma serie y con base leave-one-out.
        var baseLoo = madrid.Where(v => v < 79000).ToList();
        var mediana = RobustStatistics.Median(baseLoo);
        var mad = RobustStatistics.MedianAbsoluteDeviation(baseLoo, mediana);
        var zRobusto = RobustStatistics.ModifiedZScore(79000, mediana, mad)!.Value;

        Assert.Equal(12500d, mediana, Tolerance);
        Assert.Equal(300d, mad, Tolerance);
        Assert.Equal(149.5142, zRobusto, Tolerance);
        Assert.True(zRobusto > 3.5);

        // Y el desenlace: el motor lo marca, y los tres valores sanos que conviven con él
        // siguen sin marcarse pese a estar a media σ de la media clásica.
        Assert.True(Analisis(4).RequiresReview);
        Assert.All(new[] { 1, 2, 3 }, id => Assert.False(Analisis(id).RequiresReview));
    }

    // La aceptación depende de MinimumBaselineSize, y conviene que se vea. Con 4, la base
    // leave-one-out de Madrid baja a 3 y se queda corta: RF-03 deja de evaluarse y el id 4
    // sale sin marcar. El detector no fallaría con estruendo, dejaría de detectar en
    // silencio — que es el motivo por el que T13 recoge la observabilidad de NotEvaluated.
    //
    // Verificado por mutación sobre el valor por defecto (3 → 4): caen 35 tests, cinco de
    // ellos aquí, y entre ellos la fila del id 4 de la tabla de aceptación. Las otras nueve
    // filas sobreviven, que es lo correcto: solo Madrid pierde su análisis estadístico.
    [Fact]
    public void RN07_ConUnaBaseMinimaDeCuatro_MadridSeQuedaSinAnalisisEstadistico()
    {
        var motor = ServiceCollectionExtensions.CreateDefaultEngine(
            new AnomalyDetectionOptions { MinimumBaselineSize = 4 });

        var resultado = motor.Analyze(SampleDataset.Records);
        var id4 = Assert.Single(resultado.Results, r => r.Id == 4);

        Assert.False(id4.RequiresReview);
        Assert.Equal(
            RuleOutcome.NotEvaluated,
            Assert.Single(id4.Findings, f => f.RuleId == "CONSUMPTION_DEVIATION").Outcome);

        // El id 7 y el id 8 sobreviven: ninguno depende de histórico.
        Assert.Equal(2, resultado.Summary.RecordsRequiringReview);
        Assert.Equal([7, 8], resultado.Results.Where(r => r.RequiresReview).Select(r => r.Id));
    }
}
