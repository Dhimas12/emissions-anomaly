namespace Emissions.Analysis.Tests;

// Todos los valores esperados salen de la tabla "Valores unitarios de RobustStatistics"
// de 003 §6. Están calculados a mano en la spec y no se recalculan aquí.
//
// Las comparaciones van con tolerancia explícita (003 §6): por igualdad exacta, el test
// mediría la aritmética de coma flotante en lugar del criterio de detección, y cualquier
// valor cercano a un punto medio de redondeo se convertiría en un fallo intermitente.
public sealed class RobustStatisticsTests
{
    private const double Tolerance = 1e-4;

    [Fact]
    public void ADR01_MuestraVacia_LaMedianaLanza()
    {
        Assert.Throws<ArgumentException>(() => RobustStatistics.Median(Array.Empty<double>()));
    }

    [Theory]
    [InlineData(new[] { 1d, 2d, 3d, 4d }, 2.5)]
    [InlineData(new[] { 12000d, 12500d, 12800d }, 12500d)]
    [InlineData(new[] { 5d, 5d, 5d }, 5d)]
    public void ADR01_LaMedianaReproduceLosValoresDorados(double[] values, double expected)
    {
        Assert.Equal(expected, RobustStatistics.Median(values), Tolerance);
    }

    [Fact]
    public void ADR01_LaMedianaNoDependeDelOrdenDeEntrada()
    {
        Assert.Equal(12500d, RobustStatistics.Median(new[] { 12800d, 12000d, 12500d }), Tolerance);
    }

    [Theory]
    [InlineData(new[] { 1d, 2d, 3d, 4d }, 2.5, 1d)]
    [InlineData(new[] { 12000d, 12500d, 12800d }, 12500d, 300d)]
    [InlineData(new[] { 5d, 5d, 5d }, 5d, 0d)]
    public void ADR01_ElMadReproduceLosValoresDorados(double[] values, double median, double expected)
    {
        Assert.Equal(expected, RobustStatistics.MedianAbsoluteDeviation(values, median), Tolerance);
    }

    [Theory]
    [InlineData(10d, 2.5, 1d, 5.05875)]
    [InlineData(79000d, 12500d, 300d, 149.5142)]
    public void ADR01_ElZScoreModificadoReproduceLosValoresDorados(
        double value, double median, double mad, double expected)
    {
        var z = RobustStatistics.ModifiedZScore(value, median, mad);

        Assert.NotNull(z);
        Assert.Equal(expected, z!.Value, Tolerance);
    }

    [Fact]
    public void ADR01_MadCero_NoHayZScore()
    {
        Assert.Null(RobustStatistics.ModifiedZScore(9000d, 5000d, 0d));
    }

    [Fact]
    public void RN03_LaDesviacionRelativaReproduceElValorDorado()
    {
        var rel = RobustStatistics.RelativeDeviation(79000d, 12500d);

        Assert.NotNull(rel);
        Assert.Equal(5.32, rel!.Value, Tolerance);
    }

    [Fact]
    public void RN03_MedianaCero_NoHayDesviacionRelativa()
    {
        Assert.Null(RobustStatistics.RelativeDeviation(10d, 0d));
    }
}
