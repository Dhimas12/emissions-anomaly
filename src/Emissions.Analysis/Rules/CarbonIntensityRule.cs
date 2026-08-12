using System.Globalization;
using Emissions.Domain;
using Microsoft.Extensions.Options;

namespace Emissions.Analysis.Rules;

// RF-04. Analiza la intensidad de carbono —kg CO₂ por kWh—, que es el factor de emisión
// implícito de la sede: comparable entre sedes de distinto tamaño y estable mientras no
// cambie el mix energético.
//
// Emite **dos** evaluaciones en una sola pasada, y por eso el `RuleId` vive en cada
// `RuleEvaluation` y no en la regla. Son preguntas distintas: la banda física (RF-04a) mira
// si el número es plausible en términos absolutos y no necesita histórico, así que protege
// también a las sedes nuevas; la coherencia con el histórico (RF-04b) mira si esa sede se
// parece a sí misma.
//
// ADR-04: esta regla nunca mira el volumen. Un score que combinase ambas señales colapsaría
// "la sede consumió más de verdad" y "el dato está mal" en un solo número, que es justo la
// información que el analista necesita para decidir.
public sealed class CarbonIntensityRule : IAnomalyRule
{
    private const string BandId = "CARBON_INTENSITY_BAND";
    private const string HistoryId = "CARBON_INTENSITY_HISTORY";
    private const string BandRequirement = "RF-04a";
    private const string HistoryRequirement = "RF-04b";

    private static readonly NumberFormatInfo Formato = new() { NumberDecimalSeparator = "," };

    private readonly AnomalyDetectionOptions _options;

    public CarbonIntensityRule(IOptions<AnomalyDetectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    // La regla agrupa RF-04a y RF-04b; cada evaluación lleva el suyo concreto.
    public string RuleId => BandId;

    public string RequirementId => "RF-04";

    public int Priority => 40;

    public IReadOnlyList<RuleEvaluation> Evaluate(EmissionRecord record, SiteHistory history)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(history);

        if (record.CarbonIntensity is not { } intensidad)
        {
            return
            [
                RuleEvaluation.NotEvaluated(BandId, BandRequirement,
                    "No se puede calcular la intensidad de carbono: falta el consumo o las "
                    + "emisiones, o el consumo no es un número positivo."),
            ];
        }

        return [Banda(intensidad), Historico(record, history, intensidad)];
    }

    // RF-04a. No depende de histórico, así que es la única defensa de una sede recién dada
    // de alta: el id 8 del dataset se detecta por aquí, no por comparación con su pasado.
    private RuleEvaluation Banda(double intensidad)
    {
        if (intensidad > _options.MaxCarbonIntensity)
        {
            return RuleEvaluation.Anomaly(BandId, BandRequirement,
                $"La intensidad de carbono es de {Cifra(intensidad)} kg CO₂/kWh, por encima del "
                + $"máximo plausible de {Cifra(_options.MaxCarbonIntensity)} para consumo de red "
                + "eléctrica. Suele indicar un error de unidades o emisiones que no corresponden "
                + "a esta energía.",
                Severity.High, EvidenciaBanda(intensidad));
        }

        if (intensidad < _options.MinCarbonIntensity)
        {
            return RuleEvaluation.Anomaly(BandId, BandRequirement,
                $"La intensidad de carbono es de {Cifra(intensidad)} kg CO₂/kWh, por debajo del "
                + $"mínimo plausible de {Cifra(_options.MinCarbonIntensity)}. Podrían faltar "
                + "emisiones por imputar, o la energía no proceder de la red eléctrica.",
                Severity.High, EvidenciaBanda(intensidad));
        }

        return RuleEvaluation.Passed(BandId, BandRequirement,
            $"La intensidad de carbono ({Cifra(intensidad)} kg CO₂/kWh) cae dentro del rango "
            + $"plausible para consumo de red ({Cifra(_options.MinCarbonIntensity)}–"
            + $"{Cifra(_options.MaxCarbonIntensity)}).");
    }

    // RF-04b.
    private RuleEvaluation Historico(EmissionRecord record, SiteHistory history, double intensidad)
    {
        var baseline = history.BaselineExcluding(record, r => r.CarbonIntensity);

        if (baseline.Count < _options.MinimumBaselineSize)
        {
            return RuleEvaluation.NotEvaluated(HistoryId, HistoryRequirement,
                $"Histórico de intensidad insuficiente en {history.Site}: hay {baseline.Count} "
                + $"registro(s) con los que comparar y hacen falta {_options.MinimumBaselineSize}. "
                + "La intensidad no se da por correcta, simplemente no se ha podido comprobar.");
        }

        var mediana = RobustStatistics.Median(baseline);

        if (RobustStatistics.RelativeDeviation(intensidad, mediana) is not { } relativa)
        {
            return RuleEvaluation.NotEvaluated(HistoryId, HistoryRequirement,
                $"Histórico de intensidad no utilizable en {history.Site}: la mediana es cero, "
                + "así que no hay base sobre la que expresar una desviación.");
        }

        var magnitud = Math.Abs(relativa);

        if (magnitud <= _options.IntensityRelativeTolerance)
        {
            return RuleEvaluation.Passed(HistoryId, HistoryRequirement,
                $"La intensidad de carbono de {history.Site} se mantiene en línea con su "
                + $"histórico ({Cifra(intensidad)} frente a una mediana de {Cifra(mediana)} "
                + $"kg CO₂/kWh, desviación {Porcentaje(magnitud)} %).");
        }

        return RuleEvaluation.Anomaly(HistoryId, HistoryRequirement,
            $"La intensidad de carbono de {history.Site} se aparta de su histórico "
            + $"({Cifra(intensidad)} frente a una mediana de {Cifra(mediana)} kg CO₂/kWh, "
            + $"desviación {Porcentaje(magnitud)} %). Un salto así viene de un cambio de mix "
            + "energético o de un dato mal imputado.",
            SeverityFor(magnitud),
            new Dictionary<string, object?>
            {
                ["carbonIntensityKgPerKwh"] = Redondear(intensidad),
                ["baselineMedianIntensity"] = Redondear(mediana),
                ["baselineSize"] = baseline.Count,
                ["relativeDeviation"] = Redondear(relativa),
            });
    }

    private Dictionary<string, object?> EvidenciaBanda(double intensidad) => new()
    {
        ["carbonIntensityKgPerKwh"] = Redondear(intensidad),
        ["plausibleRange"] = new[]
        {
            Redondear(_options.MinCarbonIntensity),
            Redondear(_options.MaxCarbonIntensity),
        },
    };

    // Escala propia, distinta de la de RF-03: el factor de emisión solo se mueve si cambia
    // el mix o la comercializadora, así que duplicarlo ya es señal fuerte.
    private Severity SeverityFor(double magnitud) =>
        magnitud >= _options.IntensityHighSeverityDeviation ? Severity.High
        : magnitud >= _options.IntensityMediumSeverityDeviation ? Severity.Medium
        : Severity.Low;

    private static double Redondear(double value) => Math.Round(value, 4);

    private static string Cifra(double value) => value.ToString("0.####", Formato);

    private static string Porcentaje(double magnitud) => (magnitud * 100).ToString("0.##", Formato);
}
