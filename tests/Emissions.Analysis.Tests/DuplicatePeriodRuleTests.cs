using Emissions.Analysis.Rules;
using Emissions.Domain;

namespace Emissions.Analysis.Tests;

// RF-02 no dispara sobre el dataset del enunciado, así que todos los casos que la ejercen
// están construidos a propósito. Un test que solo pasase el dataset bueno no probaría nada.
public sealed class DuplicatePeriodRuleTests
{
    private static readonly DuplicatePeriodRule Regla = new();

    private static SiteHistory Historia(string site, params EmissionRecord[] records) =>
        new(site, records, records.Where(StructuralValidationRule.IsStructurallyValid).ToList());

    private static RuleEvaluation Evaluar(SiteHistory historia, EmissionRecord record) =>
        Assert.Single(Regla.Evaluate(record, historia));

    private static IReadOnlyList<int> IdsDuplicados(RuleEvaluation evaluacion) =>
        (IReadOnlyList<int>)evaluacion.Evidence!["duplicateRecordIds"]!;

    [Fact]
    public void RF02_DosRegistrosDelMismoPeriodo_AmbosSeMarcanAlto()
    {
        var primero = new EmissionRecord(1, "Madrid", "2026-01", 12000, 2800);
        var segundo = new EmissionRecord(2, "Madrid", "2026-01", 12100, 2810);
        var historia = Historia("Madrid", primero, segundo);

        var a = Evaluar(historia, primero);
        var b = Evaluar(historia, segundo);

        Assert.True(a.IsAnomaly);
        Assert.True(b.IsAnomaly);
        Assert.Equal(Severity.High, a.Severity);
        Assert.Equal(Severity.High, b.Severity);
    }

    [Fact]
    public void RF02_CadaRegistroReferenciaAlOtroEnDuplicateRecordIds()
    {
        var primero = new EmissionRecord(1, "Madrid", "2026-01", 12000, 2800);
        var segundo = new EmissionRecord(2, "Madrid", "2026-01", 12100, 2810);
        var historia = Historia("Madrid", primero, segundo);

        Assert.Equal(new[] { 2 }, IdsDuplicados(Evaluar(historia, primero)));
        Assert.Equal(new[] { 1 }, IdsDuplicados(Evaluar(historia, segundo)));
    }

    [Fact]
    public void RF02_TresRegistrosDelMismoPeriodo_CadaUnoReferenciaALosOtrosDos()
    {
        var uno = new EmissionRecord(1, "Madrid", "2026-01", 12000, 2800);
        var dos = new EmissionRecord(2, "Madrid", "2026-01", 12100, 2810);
        var tres = new EmissionRecord(3, "Madrid", "2026-01", 12200, 2820);
        var historia = Historia("Madrid", uno, dos, tres);

        Assert.Equal(new[] { 2, 3 }, IdsDuplicados(Evaluar(historia, uno)));
        Assert.Equal(new[] { 1, 3 }, IdsDuplicados(Evaluar(historia, dos)));
        Assert.Equal(new[] { 1, 2 }, IdsDuplicados(Evaluar(historia, tres)));
    }

    [Fact]
    public void RF02_SinDuplicados_Pasa()
    {
        var enero = new EmissionRecord(1, "Madrid", "2026-01", 12000, 2800);
        var febrero = new EmissionRecord(2, "Madrid", "2026-02", 12500, 2900);
        var historia = Historia("Madrid", enero, febrero);

        var evaluacion = Evaluar(historia, enero);

        Assert.Equal(RuleOutcome.Passed, evaluacion.Outcome);
        Assert.Null(evaluacion.Severity);
        Assert.Null(evaluacion.Evidence);
    }

    // La evidencia tiene que dejar reproducir la decisión sin leer el código (RN-05).
    [Fact]
    public void RN05_LaEvidenciaLlevaSedePeriodoEIds()
    {
        var primero = new EmissionRecord(1, "Madrid", "2026-01", 12000, 2800);
        var segundo = new EmissionRecord(2, "Madrid", "2026-01", 12100, 2810);

        var evidencia = Evaluar(Historia("Madrid", primero, segundo), primero).Evidence!;

        Assert.Equal("Madrid", evidencia["site"]);
        Assert.Equal("2026-01", evidencia["month"]);
        Assert.Equal(new[] { 2 }, (IReadOnlyList<int>)evidencia["duplicateRecordIds"]!);
    }

    // Un duplicado con cifras corruptas sigue produciendo doble conteo, así que la regla
    // tiene que verlo pese a que el registro no supere RF-01.
    [Fact]
    public void RF02_UnRegistroInvalidoDuplicado_TambienSeMarca()
    {
        var corrupto = new EmissionRecord(1, "Barcelona", "2026-03", -900, -210);
        var bueno = new EmissionRecord(2, "Barcelona", "2026-03", 8700, 2000);
        var historia = Historia("Barcelona", corrupto, bueno);

        Assert.Empty(historia.ValidRecords.Where(r => r.Id == 1));
        Assert.True(Evaluar(historia, corrupto).IsAnomaly);
        Assert.Equal(new[] { 1 }, IdsDuplicados(Evaluar(historia, bueno)));
    }

    // Sin periodo no hay pareja (sede, mes) que comparar. No es "comprobado y correcto":
    // es "no comprobable", y ADR-05 tiene un estado propio para eso.
    [Fact]
    public void RF06_RegistroSinPeriodo_NoSeEvalua()
    {
        var sinMes = new EmissionRecord(1, "Madrid", null, 12000, 2800);
        var otro = new EmissionRecord(2, "Madrid", "2026-01", 12500, 2900);

        var evaluacion = Evaluar(Historia("Madrid", sinMes, otro), sinMes);

        Assert.Equal(RuleOutcome.NotEvaluated, evaluacion.Outcome);
        Assert.False(evaluacion.IsAnomaly);
    }

    // El periodo de un registro inválido es texto libre, y ahí la capitalización sí puede
    // variar entre dos fuentes.
    [Fact]
    public void RF02_PeriodoConDistintaCapitalizacion_SigueSiendoDuplicado()
    {
        var uno = new EmissionRecord(1, "Madrid", "2026-ENE", 12000, 2800);
        var dos = new EmissionRecord(2, "Madrid", "2026-ene", 12100, 2810);

        Assert.Equal(new[] { 2 }, IdsDuplicados(Evaluar(Historia("Madrid", uno, dos), uno)));
    }

    // Mismo mes en sedes distintas no es duplicado: cada sede tiene su propia historia.
    [Fact]
    public void RF02_MismoPeriodoEnSedesDistintas_NoEsDuplicado()
    {
        var madrid = new EmissionRecord(1, "Madrid", "2026-01", 12000, 2800);
        var barcelona = new EmissionRecord(2, "Barcelona", "2026-01", 8500, 1950);

        Assert.Equal(RuleOutcome.Passed, Evaluar(Historia("Madrid", madrid), madrid).Outcome);
        Assert.Equal(RuleOutcome.Passed, Evaluar(Historia("Barcelona", barcelona), barcelona).Outcome);
    }

    // 001 §4: "No dispara en el dataset de la prueba". Si algún día lo hiciera, la tabla de
    // aceptación de 001 §6 dejaría de cumplirse y conviene enterarse aquí.
    [Fact]
    public void RF02_ElDatasetDelEnunciado_NoTieneDuplicados()
    {
        var madrid = Historia("Madrid",
            new EmissionRecord(1, "Madrid", "2026-01", 12000, 2800),
            new EmissionRecord(2, "Madrid", "2026-02", 12500, 2900),
            new EmissionRecord(3, "Madrid", "2026-03", 12800, 2950),
            new EmissionRecord(4, "Madrid", "2026-04", 79000, 18200));
        var barcelona = Historia("Barcelona",
            new EmissionRecord(5, "Barcelona", "2026-01", 8500, 1950),
            new EmissionRecord(6, "Barcelona", "2026-02", 8700, 2000),
            new EmissionRecord(7, "Barcelona", "2026-03", -900, -210),
            new EmissionRecord(8, "Barcelona", "2026-04", 8900, 8500));
        var valencia = Historia("Valencia",
            new EmissionRecord(9, "Valencia", "2026-01", 6200, 1450),
            new EmissionRecord(10, "Valencia", "2026-02", 6250, 1460));

        foreach (var historia in new[] { madrid, barcelona, valencia })
        {
            Assert.All(
                historia.AllRecords,
                record => Assert.Equal(RuleOutcome.Passed, Evaluar(historia, record).Outcome));
        }
    }

    [Fact]
    public void ADR05_LaReglaSeDeclaraSegunLaTablaDeContratos()
    {
        IAnomalyRule regla = new DuplicatePeriodRule();

        Assert.Equal("DUPLICATE_PERIOD", regla.RuleId);
        Assert.Equal("RF-02", regla.RequirementId);
        Assert.Equal(20, regla.Priority);
        Assert.True(regla.AppliesToInvalidRecords);
    }
}
