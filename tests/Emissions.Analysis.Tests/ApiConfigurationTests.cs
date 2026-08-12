using System.Globalization;
using System.Text.Json;

namespace Emissions.Analysis.Tests;

// RN-07 tiene dos mitades: los umbrales viven en configuración, y esa configuración se ve.
// Hoy `appsettings.json` y los valores por defecto de `AnomalyDetectionOptions` coinciden,
// pero coincidir por casualidad no es una garantía: si alguien tocase el fichero, el
// comportamiento cambiaría y la tabla de aceptación de 001 §6 se caería sin que nada más
// lo delatase antes del gate del CI.
public sealed class ApiConfigurationTests
{
    private const double Tolerance = 1e-9;

    private static JsonElement Seccion()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Api.appsettings.json");
        var documento = JsonDocument.Parse(File.ReadAllText(ruta));

        Assert.True(
            documento.RootElement.TryGetProperty(AnomalyDetectionOptions.SectionName, out var seccion),
            $"Falta la sección {AnomalyDetectionOptions.SectionName} en appsettings.json.");

        return seccion;
    }

    // Recorre las propiedades por reflexión en vez de enumerarlas a mano: así, una opción
    // nueva que se olvide en el appsettings.json rompe este test sin que nadie tenga que
    // acordarse de venir a añadirla aquí.
    [Fact]
    public void RN07_CadaUmbralDelAppsettingsCoincideConSuValorPorDefecto()
    {
        var seccion = Seccion();
        var porDefecto = new AnomalyDetectionOptions();

        foreach (var propiedad in typeof(AnomalyDetectionOptions).GetProperties())
        {
            Assert.True(
                seccion.TryGetProperty(propiedad.Name, out var valor),
                $"Falta {propiedad.Name} en la sección {AnomalyDetectionOptions.SectionName}.");

            var esperado = Convert.ToDouble(propiedad.GetValue(porDefecto), CultureInfo.InvariantCulture);

            Assert.Equal(esperado, valor.GetDouble(), Tolerance);
        }
    }

    [Fact]
    public void RN07_ElAppsettingsNoDeclaraUmbralesQueElMotorNoConoce()
    {
        var conocidas = typeof(AnomalyDetectionOptions).GetProperties().Select(p => p.Name).ToHashSet();

        var declaradas = Seccion().EnumerateObject().Select(p => p.Name).ToList();

        Assert.All(declaradas, nombre => Assert.Contains(nombre, conocidas));
    }
}
