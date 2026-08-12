using System.Text.Json;
using Emissions.Domain;

namespace Emissions.Analysis.Tests;

// El dataset del enunciado, leído del **mismo fichero** que servirá la API en T12 y no
// reescrito a mano aquí. Si alguien tocase `Data/sample-records.json`, los tests de
// aceptación se enterarían; con las cifras copiadas en el test, el endpoint y la
// aceptación podrían divergir sin que nada lo delatase.
public static class SampleDataset
{
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<EmissionRecord> Records { get; } = Cargar();

    private static IReadOnlyList<EmissionRecord> Cargar()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Data", "sample-records.json");

        return JsonSerializer.Deserialize<List<EmissionRecord>>(File.ReadAllText(ruta), Opciones)
            ?? throw new InvalidOperationException($"No se pudo leer el dataset del enunciado en {ruta}.");
    }
}
