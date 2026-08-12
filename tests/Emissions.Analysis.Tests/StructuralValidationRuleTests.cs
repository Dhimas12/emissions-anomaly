using Emissions.Analysis.Rules;
using Emissions.Domain;

namespace Emissions.Analysis.Tests;

public sealed class StructuralValidationRuleTests
{
    private static EmissionRecord Sano(int id = 1) => new(id, "Madrid", "2026-01", 12000, 2800);

    private static List<string> Codigos(EmissionRecord record) =>
        StructuralValidationRule.Validate(record)
            .Where(e => e.IsAnomaly)
            .Select(e => (string)e.Evidence!["code"]!)
            .ToList();

    [Fact]
    public void RF01_RegistroSano_Pasa()
    {
        var evaluaciones = StructuralValidationRule.Validate(Sano());

        var unica = Assert.Single(evaluaciones);
        Assert.Equal(RuleOutcome.Passed, unica.Outcome);
        Assert.Null(unica.Severity);
        Assert.True(StructuralValidationRule.IsStructurallyValid(Sano()));
    }

    [Theory]
    [InlineData(null, "2026-01", 12000d, 2800d, "site")]
    [InlineData("   ", "2026-01", 12000d, 2800d, "site")]
    [InlineData("Madrid", null, 12000d, 2800d, "month")]
    [InlineData("Madrid", "", 12000d, 2800d, "month")]
    [InlineData("Madrid", "2026-01", null, 2800d, "energyKwh")]
    [InlineData("Madrid", "2026-01", 12000d, null, "co2Kg")]
    public void RF01_CampoAusente_SeMarcaMissingField(
        string? site, string? month, double? energy, double? co2, string campo)
    {
        var record = new EmissionRecord(1, site, month, energy, co2);

        var anomalia = Assert.Single(StructuralValidationRule.Validate(record).Where(e => e.IsAnomaly));

        Assert.Equal("MISSING_FIELD", anomalia.Evidence!["code"]);
        Assert.Equal(campo, anomalia.Evidence["field"]);
        Assert.Equal(Severity.High, anomalia.Severity);
    }

    [Fact]
    public void RF01_ConsumoNegativo_SeMarcaNegativeEnergy()
    {
        Assert.Equal(new[] { "NEGATIVE_ENERGY" }, Codigos(new EmissionRecord(1, "Madrid", "2026-01", -900, 2800)));
    }

    [Fact]
    public void RF01_EmisionesNegativas_SeMarcaNegativeCo2()
    {
        Assert.Equal(new[] { "NEGATIVE_CO2" }, Codigos(new EmissionRecord(1, "Madrid", "2026-01", 12000, -210)));
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-1")]
    [InlineData("26-01")]
    [InlineData("2026/01")]
    [InlineData("enero de 2026")]
    public void RF01_PeriodoInvalido_SeMarcaInvalidPeriod(string month)
    {
        Assert.Equal(new[] { "INVALID_PERIOD" }, Codigos(new EmissionRecord(1, "Madrid", month, 12000, 2800)));
    }

    [Theory]
    [InlineData("2026-01")]
    [InlineData("2026-12")]
    public void RF01_PeriodoValido_NoSeMarca(string month)
    {
        Assert.Empty(Codigos(new EmissionRecord(1, "Madrid", month, 12000, 2800)));
    }

    [Fact]
    public void RF01_EmitirSinConsumir_SeMarcaEmissionsWithoutEnergy()
    {
        Assert.Equal(new[] { "EMISSIONS_WITHOUT_ENERGY" }, Codigos(new EmissionRecord(1, "Madrid", "2026-01", 0, 500)));
    }

    // Cero y cero es una sede parada, no una contradicción.
    [Fact]
    public void RF01_ConsumoCeroSinEmisiones_NoSeMarca()
    {
        Assert.Empty(Codigos(new EmissionRecord(1, "Madrid", "2026-01", 0, 0)));
    }

    [Theory]
    [InlineData(double.NaN, 2800d, "energyKwh")]
    [InlineData(double.PositiveInfinity, 2800d, "energyKwh")]
    [InlineData(12000d, double.NaN, "co2Kg")]
    [InlineData(12000d, double.NegativeInfinity, "co2Kg")]
    public void RF01_ValorNoFinito_SeMarcaNonFinite(double energy, double co2, string campo)
    {
        var record = new EmissionRecord(1, "Madrid", "2026-01", energy, co2);

        var anomalia = Assert.Single(StructuralValidationRule.Validate(record).Where(e => e.IsAnomaly));

        Assert.Equal("NON_FINITE", anomalia.Evidence!["code"]);
        Assert.Equal(campo, anomalia.Evidence["field"]);
    }

    // Un -infinito cumple también "< 0". Se reporta una sola vez, con el código que
    // describe la causa real: el dato está corrupto, no es que la sede consumiese de menos.
    [Fact]
    public void RF01_MenosInfinito_SoloProduceNonFinite()
    {
        Assert.Equal(new[] { "NON_FINITE" }, Codigos(new EmissionRecord(1, "Madrid", "2026-01", double.NegativeInfinity, 2800)));
    }

    // El id 7 del dataset del enunciado.
    [Fact]
    public void RF01_ElIdSiete_ProduceDosAnomalias()
    {
        var id7 = new EmissionRecord(7, "Barcelona", "2026-03", -900, -210);

        var codigos = Codigos(id7);

        Assert.Equal(new[] { "NEGATIVE_ENERGY", "NEGATIVE_CO2" }, codigos);
        Assert.False(StructuralValidationRule.IsStructurallyValid(id7));
        Assert.All(
            StructuralValidationRule.Validate(id7),
            e => Assert.Equal(Severity.High, e.Severity));
    }

    // RN-05: la evidencia tiene que permitir reproducir la decisión sin leer el código.
    [Fact]
    public void RN05_LaEvidenciaLlevaCodigoCampoYValor()
    {
        var anomalia = Assert.Single(
            StructuralValidationRule.Validate(new EmissionRecord(1, "Madrid", "2026-01", -900, 2800))
                .Where(e => e.IsAnomaly));

        Assert.Equal("NEGATIVE_ENERGY", anomalia.Evidence!["code"]);
        Assert.Equal("energyKwh", anomalia.Evidence["field"]);
        Assert.Equal(-900d, anomalia.Evidence["value"]);
    }

    [Fact]
    public void ADR05_LaReglaSeDeclaraSegunLaTablaDeContratos()
    {
        IAnomalyRule regla = new StructuralValidationRule();

        Assert.Equal("STRUCTURAL_VALIDATION", regla.RuleId);
        Assert.Equal("RF-01", regla.RequirementId);
        Assert.Equal(10, regla.Priority);
        Assert.True(regla.AppliesToInvalidRecords);
    }
}
