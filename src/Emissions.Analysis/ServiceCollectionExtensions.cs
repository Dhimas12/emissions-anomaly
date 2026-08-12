using Emissions.Analysis.Rules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Emissions.Analysis;

// ADR-05, ADR-06. Las reglas se registran como implementaciones de la misma interfaz, así
// que añadir una es una línea aquí y nada más: el motor las recibe todas y no las conoce
// por su tipo.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAnomalyDetection(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ValidateOnStart convierte una configuración incoherente en un fallo inmediato al
        // arrancar, en lugar de un análisis silenciosamente equivocado que acaba publicado.
        services.AddOptions<AnomalyDetectionOptions>()
            .Bind(configuration.GetSection(AnomalyDetectionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IAnomalyRule, StructuralValidationRule>();
        services.AddSingleton<IAnomalyRule, DuplicatePeriodRule>();
        services.AddSingleton<IAnomalyRule, ConsumptionDeviationRule>();
        services.AddSingleton<IAnomalyRule, CarbonIntensityRule>();

        services.AddSingleton<IAnomalyDetectionEngine, AnomalyDetectionEngine>();

        return services;
    }

    // Motor con umbrales por defecto, para tests y uso embebido. Es la puerta que permite
    // usar el motor desde un worker o un job sin levantar un contenedor de DI (002 §1).
    public static IAnomalyDetectionEngine CreateDefaultEngine(AnomalyDetectionOptions? options = null)
    {
        var opciones = Options.Create(options ?? new AnomalyDetectionOptions());

        return new AnomalyDetectionEngine(
        [
            new StructuralValidationRule(),
            new DuplicatePeriodRule(),
            new ConsumptionDeviationRule(opciones),
            new CarbonIntensityRule(opciones),
        ]);
    }
}
