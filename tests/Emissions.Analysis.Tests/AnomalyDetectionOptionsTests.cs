using Microsoft.Extensions.Options;

namespace Emissions.Analysis.Tests;

// Se valida a través de DataAnnotationValidateOptions, que es exactamente la clase que
// registra ValidateDataAnnotations() en ADR-06. Así el test comprueba el mecanismo real
// que correrá al arrancar la API, y no una reimplementación paralela de la validación.
public sealed class AnomalyDetectionOptionsTests
{
    private const double Tolerance = 1e-4;

    private static ValidateOptionsResult Validate(AnomalyDetectionOptions options) =>
        new DataAnnotationValidateOptions<AnomalyDetectionOptions>(Options.DefaultName)
            .Validate(Options.DefaultName, options);

    [Fact]
    public void RN07_LaConfiguracionPorDefectoEsValida()
    {
        Assert.True(Validate(new AnomalyDetectionOptions()).Succeeded);
    }

    // Los valores de 002 §3. Si alguien cambia un umbral sin tocar la spec, este test lo
    // delata: los umbrales son criterio de producto y hay que poder defenderlos.
    [Fact]
    public void RN07_LosValoresPorDefectoSonLosDeLaEspecificacion()
    {
        var options = new AnomalyDetectionOptions();

        Assert.Equal(3.5, options.RobustZScoreThreshold, Tolerance);
        Assert.Equal(0.25, options.MinimumRelativeDeviation, Tolerance);
        Assert.Equal(3, options.MinimumBaselineSize);
        Assert.Equal(0.05, options.MinCarbonIntensity, Tolerance);
        Assert.Equal(0.80, options.MaxCarbonIntensity, Tolerance);
        Assert.Equal(0.40, options.IntensityRelativeTolerance, Tolerance);
        Assert.Equal(2.0, options.HighSeverityRelativeDeviation, Tolerance);
        Assert.Equal(1.0, options.MediumSeverityRelativeDeviation, Tolerance);
        Assert.Equal(1.0, options.IntensityHighSeverityDeviation, Tolerance);
        Assert.Equal(0.7, options.IntensityMediumSeverityDeviation, Tolerance);
    }

    // Con la banda invertida o vacía, toda intensidad quedaría fuera de rango y RF-04a
    // marcaría el lote entero.
    [Theory]
    [InlineData(0.9, 0.8)]
    [InlineData(0.8, 0.8)]
    public void RN07_BandaDeIntensidadInvertidaOVacia_SeRechaza(double min, double max)
    {
        var options = new AnomalyDetectionOptions { MinCarbonIntensity = min, MaxCarbonIntensity = max };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(nameof(AnomalyDetectionOptions.MaxCarbonIntensity), string.Join(" ", result.Failures));
    }

    [Fact]
    public void RN07_UmbralMedioDeConsumoPorEncimaDelAlto_SeRechaza()
    {
        var options = new AnomalyDetectionOptions
        {
            MediumSeverityRelativeDeviation = 3.0,
            HighSeverityRelativeDeviation = 2.0,
        };

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void RN07_UmbralMedioDeIntensidadPorEncimaDelAlto_SeRechaza()
    {
        var options = new AnomalyDetectionOptions
        {
            IntensityMediumSeverityDeviation = 1.5,
            IntensityHighSeverityDeviation = 1.0,
        };

        Assert.True(Validate(options).Failed);
    }

    // Un umbral de cero o negativo no es una calibración agresiva: es una configuración
    // sin significado que produciría marcas sobre cualquier dato.
    [Fact]
    public void RN07_UmbralNoPositivo_SeRechaza()
    {
        Assert.True(Validate(new AnomalyDetectionOptions { RobustZScoreThreshold = 0 }).Failed);
        Assert.True(Validate(new AnomalyDetectionOptions { MinimumRelativeDeviation = -0.1 }).Failed);
    }

    // Con un solo punto el MAD sale cero por construcción, no porque la serie sea estable.
    [Fact]
    public void RN07_BaseMinimaMenorQueDos_SeRechaza()
    {
        Assert.True(Validate(new AnomalyDetectionOptions { MinimumBaselineSize = 1 }).Failed);
    }
}
