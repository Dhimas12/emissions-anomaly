using System.ComponentModel.DataAnnotations;

namespace Emissions.Analysis;

// RN-07, ADR-06. Ningún umbral incrustado en código: son criterio de negocio, no lógica,
// y cambiarlos no debe exigir recompilar. Se validan al arrancar para que una
// configuración incoherente sea un fallo inmediato y ruidoso en lugar de un análisis
// silenciosamente equivocado que acaba en un informe ESG publicado.
public sealed class AnomalyDetectionOptions : IValidatableObject
{
    // Nombre de la sección en appsettings.json. Como constante para que el enlace y la
    // configuración no puedan separarse por una errata en una cadena suelta.
    public const string SectionName = "AnomalyDetection";

    // ADR-01: umbral convencional del z-score modificado de Iglewicz–Hoaglin.
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double RobustZScoreThreshold { get; set; } = 3.5;

    // ADR-02: la materialidad. Por debajo de esta desviación relativa no se molesta a un
    // analista por muy raro que sea el valor en términos estadísticos.
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double MinimumRelativeDeviation { get; set; } = 0.25;

    // El valor de producto es 3. El suelo de 2 solo impide lo que no significa nada: con
    // un único punto el MAD sale cero por construcción, no porque la serie sea estable.
    //
    // Conviene ser honesto sobre lo que este rango NO protege. Un valor de 500 en un
    // sistema con doce meses por sede dejaría todas las reglas estadísticas en
    // NotEvaluated para siempre: el detector no marcaría nada, en silencio y con el CI en
    // verde. Ningún techo arbitrario atrapa eso —99 pasaría igual con un límite de 100—.
    // Lo atrapa la observabilidad, no la validación: alertar si la proporción de
    // NotEvaluated supera un umbral en producción. Anotado como evolución en T13.
    [Range(2, int.MaxValue)]
    public int MinimumBaselineSize { get; set; } = 3;

    // La banda por defecto asume red eléctrica europea. Una red nórdica (~0,02) o una
    // intensiva en carbón (~0,9) exigen recalibrarla: es configuración por región, no una
    // constante universal, y reconocerlo es parte del diseño (ADR-06).
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double MinCarbonIntensity { get; set; } = 0.05;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double MaxCarbonIntensity { get; set; } = 0.80;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double IntensityRelativeTolerance { get; set; } = 0.40;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double HighSeverityRelativeDeviation { get; set; } = 2.0;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double MediumSeverityRelativeDeviation { get; set; } = 1.0;

    // La intensidad tiene su propia escala y no la del volumen (ADR-04), porque las dos
    // magnitudes tienen fuentes de variación legítima distintas. El consumo se mueve con
    // la actividad real —producción, ocupación, temperatura, calendario laboral— y
    // duplicarlo puede ser normal; la intensidad solo se mueve si cambia el mix de la red
    // o la comercializadora. La misma desviación relativa pesa más como evidencia en
    // intensidad porque quedan muchas menos explicaciones inocentes que descartar.
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double IntensityHighSeverityDeviation { get; set; } = 1.0;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double IntensityMediumSeverityDeviation { get; set; } = 0.7;

    // Coherencias cruzadas. Cada `Range` solo ve su propia propiedad, y son estas tres
    // relaciones las que hacen que la configuración signifique algo: una banda invertida
    // marcaría todos los registros del lote, y un umbral medio por encima del alto deja
    // la severidad `Medium` inalcanzable sin que nada lo delate.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinCarbonIntensity >= MaxCarbonIntensity)
        {
            yield return new ValidationResult(
                $"{nameof(MinCarbonIntensity)} ({MinCarbonIntensity}) debe ser estrictamente " +
                $"menor que {nameof(MaxCarbonIntensity)} ({MaxCarbonIntensity}): con la banda " +
                "invertida o vacía, toda intensidad de carbono quedaría fuera de rango.",
                [nameof(MinCarbonIntensity), nameof(MaxCarbonIntensity)]);
        }

        if (MediumSeverityRelativeDeviation > HighSeverityRelativeDeviation)
        {
            yield return new ValidationResult(
                $"{nameof(MediumSeverityRelativeDeviation)} ({MediumSeverityRelativeDeviation}) " +
                $"no puede superar a {nameof(HighSeverityRelativeDeviation)} " +
                $"({HighSeverityRelativeDeviation}): la severidad media quedaría inalcanzable.",
                [nameof(MediumSeverityRelativeDeviation), nameof(HighSeverityRelativeDeviation)]);
        }

        if (IntensityMediumSeverityDeviation > IntensityHighSeverityDeviation)
        {
            yield return new ValidationResult(
                $"{nameof(IntensityMediumSeverityDeviation)} ({IntensityMediumSeverityDeviation}) " +
                $"no puede superar a {nameof(IntensityHighSeverityDeviation)} " +
                $"({IntensityHighSeverityDeviation}): la severidad media quedaría inalcanzable.",
                [nameof(IntensityMediumSeverityDeviation), nameof(IntensityHighSeverityDeviation)]);
        }
    }
}
