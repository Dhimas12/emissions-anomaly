using Emissions.Domain;

namespace Emissions.Analysis.Tests;

public sealed class SiteHistoryTests
{
    private const double Tolerance = 1e-4;

    // Los cuatro registros de Madrid del dataset del enunciado.
    private static readonly EmissionRecord[] Madrid =
    [
        new(1, "Madrid", "2026-01", 12000, 2800),
        new(2, "Madrid", "2026-02", 12500, 2900),
        new(3, "Madrid", "2026-03", 12800, 2950),
        new(4, "Madrid", "2026-04", 79000, 18200),
    ];

    private static SiteHistory MadridHistory() => new("Madrid", Madrid, Madrid);

    // El test que da sentido a la tarea. Si esto falla, el id 4 entra en su propia mediana,
    // la desplaza y se enmascara: el detector deja de detectar justo el caso del enunciado.
    [Fact]
    public void RN02_LaBaseDeUnRegistroNuncaLoContiene()
    {
        var history = MadridHistory();

        foreach (var record in Madrid)
        {
            var baseline = history.BaselineExcluding(record, r => r.EnergyKwh);

            Assert.Equal(Madrid.Length - 1, baseline.Count);
            Assert.DoesNotContain(record.EnergyKwh!.Value, baseline);
        }
    }

    // n base = 3 y los valores de la tabla de RF-03 de 003 §6.
    [Fact]
    public void RN02_LaBaseDelIdCuatroSonLosTresValoresSanos()
    {
        var baseline = MadridHistory().BaselineExcluding(Madrid[3], r => r.EnergyKwh);

        Assert.Equal(3, baseline.Count);
        Assert.Equal([12000d, 12500d, 12800d], baseline.Order());
    }

    [Fact]
    public void RN02_LosValoresNulosQuedanFuera()
    {
        var records = new EmissionRecord[]
        {
            new(1, "Valencia", "2026-01", 6200, 1450),
            new(2, "Valencia", "2026-02", null, 1460),
            new(3, "Valencia", "2026-03", 6300, 1470),
        };
        var history = new SiteHistory("Valencia", records, records);

        var baseline = history.BaselineExcluding(records[0], r => r.EnergyKwh);

        Assert.Equal([6300d], baseline);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RN02_LosValoresNoFinitosQuedanFuera(double corrupted)
    {
        var records = new EmissionRecord[]
        {
            new(1, "Valencia", "2026-01", 6200, 1450),
            new(2, "Valencia", "2026-02", corrupted, 1460),
            new(3, "Valencia", "2026-03", 6300, 1470),
        };
        var history = new SiteHistory("Valencia", records, records);

        var baseline = history.BaselineExcluding(records[0], r => r.EnergyKwh);

        Assert.Equal([6300d], baseline);
    }

    // RN-01: el registro inválido sigue estando en AllRecords porque RF-02 lo necesita
    // —un duplicado lo es aunque el consumo sea negativo—, pero no define la normalidad.
    [Fact]
    public void RN01_ElRegistroInvalidoSiguePresentePeroNoEntraEnLaBase()
    {
        var barcelona = new EmissionRecord[]
        {
            new(5, "Barcelona", "2026-01", 8500, 1950),
            new(6, "Barcelona", "2026-02", 8700, 2000),
            new(7, "Barcelona", "2026-03", -900, -210),
            new(8, "Barcelona", "2026-04", 8900, 8500),
        };
        var valid = barcelona.Where(r => r.Id != 7).ToArray();
        var history = new SiteHistory("Barcelona", barcelona, valid);

        Assert.Equal(4, history.AllRecords.Count);
        Assert.Contains(history.AllRecords, r => r.Id == 7);

        // 003 §6: Barcelona tiene 3 válidos, así que cada base leave-one-out es de 2.
        var baseline = history.BaselineExcluding(barcelona[0], r => r.EnergyKwh);

        Assert.Equal(2, baseline.Count);
        Assert.DoesNotContain(-900d, baseline);
    }

    // EmissionRecord es un record: dos lecturas distintas con las mismas cifras son
    // iguales por valor. Si la exclusión fuese por igualdad y no por Id, la base se
    // encogería en silencio justo en el caso que RF-02 existe para detectar.
    [Fact]
    public void RN02_LaExclusionVaPorIdYNoPorIgualdadDeValor()
    {
        var duplicados = new EmissionRecord[]
        {
            new(1, "Madrid", "2026-01", 12000, 2800),
            new(2, "Madrid", "2026-01", 12000, 2800),
            new(3, "Madrid", "2026-02", 12500, 2900),
        };
        var history = new SiteHistory("Madrid", duplicados, duplicados);

        var baseline = history.BaselineExcluding(duplicados[0], r => r.EnergyKwh);

        Assert.Equal(2, baseline.Count);
        Assert.Contains(12000d, baseline);
    }

    [Fact]
    public void RN02_LaBaseDeIntensidadUsaElMismoSelector()
    {
        var baseline = MadridHistory().BaselineExcluding(Madrid[3], r => r.CarbonIntensity);

        // 003 §6: mediana base de intensidad para el id 4 = 0,2320.
        Assert.Equal(3, baseline.Count);
        Assert.Equal(0.2320, RobustStatistics.Median(baseline), Tolerance);
    }
}
