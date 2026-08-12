using System.Globalization;
using Emissions.Domain;
using Microsoft.Extensions.Options;

namespace Emissions.Analysis.Rules;

// RF-03, RN-02, RN-03. Compara el consumo contra el histórico de su propia sede, nunca
// contra la media global: 12.000 kWh es normal en Madrid y altísimo en Valencia.
//
// La doble condición de ADR-02 es la decisión de diseño de esta clase. El z-score responde
// a "¿es raro?" y la desviación relativa a "¿importa?", y hacen falta las dos. En una serie
// muy estable el MAD tiende a cero y cualquier variación produce un z enorme; un sistema
// que alerta por una fluctuación del 8 % se desactiva solo, porque el analista deja de
// mirarlo en dos semanas y los falsos negativos vuelven por la puerta de atrás.
public sealed class ConsumptionDeviationRule : IAnomalyRule
{
    private const string Id = "CONSUMPTION_DEVIATION";
    private const string Requirement = "RF-03";

    // Separador decimal fijado a mano en lugar de una cultura con nombre: el mensaje va a
    // un informe auditable y no puede cambiar según la máquina que ejecute el análisis
    // (RN-06), ni depender de que el contenedor traiga datos de globalización.
    private static readonly NumberFormatInfo Formato = new() { NumberDecimalSeparator = "," };

    private readonly AnomalyDetectionOptions _options;

    public ConsumptionDeviationRule(IOptions<AnomalyDetectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public string RuleId => Id;

    public string RequirementId => Requirement;

    public int Priority => 30;

    public IReadOnlyList<RuleEvaluation> Evaluate(EmissionRecord record, SiteHistory history)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(history);

        if (record.EnergyKwh is not { } energy)
        {
            return
            [
                RuleEvaluation.NotEvaluated(Id, Requirement,
                    "No se compara el consumo con el histórico de la sede: el registro no indica "
                    + "consumo."),
            ];
        }

        var baseline = history.BaselineExcluding(record, r => r.EnergyKwh);

        if (baseline.Count < _options.MinimumBaselineSize)
        {
            return
            [
                RuleEvaluation.NotEvaluated(Id, Requirement,
                    $"Histórico insuficiente en {history.Site}: hay {baseline.Count} registro(s) "
                    + $"con los que comparar y hacen falta {_options.MinimumBaselineSize}. El "
                    + "consumo no se da por correcto, simplemente no se ha podido comprobar."),
            ];
        }

        var median = RobustStatistics.Median(baseline);
        var mad = RobustStatistics.MedianAbsoluteDeviation(baseline, median);
        var z = RobustStatistics.ModifiedZScore(energy, median, mad);

        if (RobustStatistics.RelativeDeviation(energy, median) is not { } relative)
        {
            return
            [
                RuleEvaluation.NotEvaluated(Id, Requirement,
                    $"Histórico no utilizable en {history.Site}: la mediana de consumo es cero, "
                    + "así que no hay base sobre la que expresar una desviación."),
            ];
        }

        // Con MAD cero no hay z-score que calcular y el criterio recae solo en la
        // materialidad. No es una excepción al diseño: es ADR-02 funcionando en el caso
        // límite, porque una serie sin dispersión no puede decir si algo es "raro".
        var extremo = z is not { } zScore || Math.Abs(zScore) > _options.RobustZScoreThreshold;
        var material = Math.Abs(relative) > _options.MinimumRelativeDeviation;

        var magnitud = Math.Abs(relative);

        if (!(extremo && material))
        {
            return
            [
                RuleEvaluation.Passed(Id, Requirement,
                    $"El consumo de {history.Site} está dentro de su comportamiento histórico "
                    + $"({Cifra(energy)} kWh frente a una mediana de {Cifra(median)} kWh, "
                    + $"desviación {Porcentaje(magnitud)} %)."),
            ];
        }

        var direccion = relative > 0
            ? $"El consumo supera de forma significativa el comportamiento histórico de {history.Site}"
            : $"El consumo queda muy por debajo del comportamiento histórico de {history.Site}";

        return
        [
            RuleEvaluation.Anomaly(Id, Requirement,
                $"{direccion} ({Cifra(energy)} kWh frente a una mediana de {Cifra(median)} kWh, "
                + $"{Veces(energy, median)}x, desviación {Porcentaje(magnitud)} %).",
                SeverityFor(magnitud),
                new Dictionary<string, object?>
                {
                    ["energyKwh"] = Redondear(energy),
                    ["baselineMedianKwh"] = Redondear(median),
                    ["baselineMad"] = Redondear(mad),
                    ["baselineSize"] = baseline.Count,

                    // La clave se emite siempre, con null cuando el MAD es cero. Omitirla
                    // dejaría a un auditor sin poder distinguir "no se calculó" de "se
                    // olvidó incluirlo", y esa duda invalida la evidencia (RN-05).
                    ["modifiedZScore"] = z is { } valor ? Redondear(valor) : null,
                    ["relativeDeviation"] = Redondear(relative),
                }),
        ];
    }

    private Severity SeverityFor(double magnitud) =>
        magnitud >= _options.HighSeverityRelativeDeviation ? Severity.High
        : magnitud >= _options.MediumSeverityRelativeDeviation ? Severity.Medium
        : Severity.Low;

    private static double Redondear(double value) => Math.Round(value, 4);

    private static string Cifra(double value) => value.ToString("0.####", Formato);

    private static string Porcentaje(double magnitud) => (magnitud * 100).ToString("0", Formato);

    private static string Veces(double value, double median) =>
        (value / median).ToString("0.0", Formato);
}
