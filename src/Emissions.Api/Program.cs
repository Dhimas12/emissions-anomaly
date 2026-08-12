using System.Text.Json;
using System.Text.Json.Serialization;
using Emissions.Analysis;
using Emissions.Domain;
using Microsoft.AspNetCore.Mvc;

// RF-07. La API es transporte y nada más: no decide, no valida criterio y no conoce ninguna
// regla. Todo el análisis vive en Emissions.Analysis, que no sabe que existe ASP.NET
// (002 §1), y por eso este fichero se puede sustituir por un worker en una tarde.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAnomalyDetection(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;

    // Severidades como "High" y no como 3: la salida la lee un analista, no un depurador.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

    options.SerializerOptions.WriteIndented = builder.Environment.IsDevelopment();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "Emissions Anomaly Detection",
    Version = "v1",
    Description = "Detección de anomalías en consumo energético y emisiones de CO₂ por sede. "
        + "El sistema no corrige ni descarta datos: los enruta a revisión humana con la "
        + "evidencia numérica que produjo la marca.",
}));

var app = builder.Build();

// Una excepción no controlada no puede filtrar la traza: el cuerpo de la respuesta acaba
// en manos de quien llama a la API.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;

    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "No se ha podido completar el análisis.",
        Detail = "El error queda registrado en el servidor.",
    });
}));

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Emissions Anomaly Detection v1");
    options.RoutePrefix = string.Empty;
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithSummary("Sonda de vida.");

app.MapPost("/api/v1/analysis", (
        List<EmissionRecord>? records,
        bool? onlyFlagged,
        IAnomalyDetectionEngine engine) =>
    {
        if (records is null || records.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["records"] = ["El cuerpo debe contener al menos un registro que analizar."],
            });
        }

        var result = engine.Analyze(records);

        // El resumen sigue describiendo el lote completo aunque se filtre la lista: si
        // contase solo los devueltos, `onlyFlagged=true` daría siempre el 100 % marcado.
        return Results.Ok(onlyFlagged == true
            ? result with { Results = result.Results.Where(r => r.RequiresReview).ToList() }
            : result);
    })
    .WithName("AnalyzeBatch")
    .WithSummary("Analiza el lote de registros del cuerpo de la petición.");

app.MapGet("/api/v1/analysis/sample", (IAnomalyDetectionEngine engine) =>
    {
        var records = SampleRecords.Load();

        return Results.Ok(engine.Analyze(records));
    })
    .WithName("AnalyzeSample")
    .WithSummary("Analiza el dataset del enunciado.");

app.Run();

// El dataset se lee del fichero copiado al output y no se incrusta en el código, para que
// los tests de aceptación y este endpoint no puedan divergir (004 T12).
internal static class SampleRecords
{
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<EmissionRecord> Load()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Data", "sample-records.json");

        return JsonSerializer.Deserialize<List<EmissionRecord>>(File.ReadAllText(ruta), Opciones)
            ?? throw new InvalidOperationException($"No se pudo leer el dataset de ejemplo en {ruta}.");
    }
}

// Para que los tests puedan referenciar el host si hiciera falta más adelante.
public partial class Program;
