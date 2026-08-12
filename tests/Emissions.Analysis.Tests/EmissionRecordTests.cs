using Emissions.Domain;

namespace Emissions.Analysis.Tests;

public sealed class EmissionRecordTests
{
    // 003 §6: tolerancia explícita, nunca igualdad exacta sobre valores dorados.
    private const double Tolerance = 1e-4;

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-900d)]
    public void RF04_SinEnergiaPositiva_LaIntensidadNoEsCalculable(double? energyKwh)
    {
        var record = new EmissionRecord(1, "Madrid", "2026-01", energyKwh, 2800);

        Assert.Null(record.CarbonIntensity);
    }

    [Fact]
    public void RF04_SinCo2_LaIntensidadNoEsCalculable()
    {
        var record = new EmissionRecord(1, "Madrid", "2026-01", 12000, null);

        Assert.Null(record.CarbonIntensity);
    }

    // NaN hace falsas todas las comparaciones, así que una intensidad NaN atravesaría la
    // banda física de RF-04a como si fuese correcta. El invariante es del tipo: no basta
    // con que RF-01 marque el registro por NON_FINITE antes de llegar a la regla.
    [Theory]
    [InlineData(double.NaN, 2800d)]
    [InlineData(double.PositiveInfinity, 2800d)]
    [InlineData(double.NegativeInfinity, 2800d)]
    [InlineData(12000d, double.NaN)]
    [InlineData(12000d, double.PositiveInfinity)]
    [InlineData(12000d, double.NegativeInfinity)]
    public void RF04_ConValoresNoFinitos_LaIntensidadNoEsCalculable(double energyKwh, double co2Kg)
    {
        var record = new EmissionRecord(1, "Madrid", "2026-01", energyKwh, co2Kg);

        Assert.Null(record.CarbonIntensity);
    }

    // Un CO₂ de cero sí es calculable: significa "no emitió", no "no lo sabemos". Es la
    // distinción que sostiene EMISSIONS_WITHOUT_ENERGY y el resto de RF-01.
    [Fact]
    public void RF04_ConCo2Cero_LaIntensidadEsCero()
    {
        var record = new EmissionRecord(1, "Madrid", "2026-01", 12000, 0);

        Assert.NotNull(record.CarbonIntensity);
        Assert.Equal(0d, record.CarbonIntensity!.Value, Tolerance);
    }

    // Intensidades de la tabla de valores dorados de 003 §6, sobre el dataset del enunciado.
    [Theory]
    [InlineData(1, 12000d, 2800d, 0.2333)]
    [InlineData(2, 12500d, 2900d, 0.2320)]
    [InlineData(3, 12800d, 2950d, 0.2305)]
    [InlineData(4, 79000d, 18200d, 0.2304)]
    [InlineData(8, 8900d, 8500d, 0.9551)]
    public void RF04_LaIntensidadReproduceLosValoresDorados(
        int id, double energyKwh, double co2Kg, double expected)
    {
        var record = new EmissionRecord(id, "Madrid", "2026-01", energyKwh, co2Kg);

        Assert.NotNull(record.CarbonIntensity);
        Assert.Equal(expected, record.CarbonIntensity!.Value, Tolerance);
    }
}
