using System.Text.Json;

namespace Emissions.Analysis.Tests;

// T-CI-2 (006 §6). Comprueba que el dataset llega al directorio de salida: el endpoint de
// T12 lo lee de ahí, no del código fuente, así que un fallo de copia no rompe la
// compilación y solo se manifiesta en ejecución. Se lee con JsonDocument y no con
// EmissionRecord porque el modelo de dominio es T1.
public sealed class SampleDatasetTests
{
    private static readonly string DatasetPath =
        Path.Combine(AppContext.BaseDirectory, "Data", "sample-records.json");

    private static JsonDocument Load()
    {
        Assert.True(File.Exists(DatasetPath), $"El dataset no se copió al output: {DatasetPath}");
        return JsonDocument.Parse(File.ReadAllText(DatasetPath));
    }

    [Fact]
    public void TCI2_ElDatasetDelEnunciado_TieneDiezRegistros()
    {
        using var dataset = Load();

        Assert.Equal(10, dataset.RootElement.GetArrayLength());
    }

    [Fact]
    public void TCI2_ElRegistroSiete_TieneEnergiaYCo2Negativos()
    {
        using var dataset = Load();

        var record = dataset.RootElement
            .EnumerateArray()
            .Single(r => r.GetProperty("id").GetInt32() == 7);

        Assert.True(record.GetProperty("energyKwh").GetDouble() < 0);
        Assert.True(record.GetProperty("co2Kg").GetDouble() < 0);
    }
}
