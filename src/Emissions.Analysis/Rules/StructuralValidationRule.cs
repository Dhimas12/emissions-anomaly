using System.Globalization;
using Emissions.Domain;

namespace Emissions.Analysis.Rules;

// RF-01. Validación estructural: valores imposibles o inválidos. Siempre severidad High,
// porque no hay grados en "este dato no puede ser cierto".
//
// `Validate` e `IsStructurallyValid` son **estáticos a propósito**. El motor necesita
// saber qué registros son fiables *antes* de construir las líneas base, en la fase 1 de
// `002` §1, cuando todavía no existe ninguna `SiteHistory` que pasarle a `Evaluate`. Esa
// dependencia de orden es la que sostiene RN-01: si un registro corrupto entrase en la
// base, definiría la normalidad de su sede y se auto-justificaría. Convertir estos dos
// métodos en miembros de instancia obligaría al motor a construir las bases antes de
// saber con qué construirlas, que es exactamente el ciclo que la fase 1 rompe.
public sealed class StructuralValidationRule : IAnomalyRule
{
    private const string Id = "STRUCTURAL_VALIDATION";
    private const string Requirement = "RF-01";
    private const string PeriodFormat = "yyyy-MM";

    private const string MissingField = "MISSING_FIELD";
    private const string NegativeEnergy = "NEGATIVE_ENERGY";
    private const string NegativeCo2 = "NEGATIVE_CO2";
    private const string InvalidPeriod = "INVALID_PERIOD";
    private const string EmissionsWithoutEnergy = "EMISSIONS_WITHOUT_ENERGY";
    private const string NonFinite = "NON_FINITE";

    public string RuleId => Id;

    public string RequirementId => Requirement;

    public int Priority => 10;

    // Es la regla que decide qué es inválido, así que tiene que ver a todos los registros.
    public bool AppliesToInvalidRecords => true;

    public IReadOnlyList<RuleEvaluation> Evaluate(EmissionRecord record, SiteHistory history) =>
        Validate(record);

    public static bool IsStructurallyValid(EmissionRecord record) =>
        !Validate(record).Any(evaluation => evaluation.IsAnomaly);

    public static IReadOnlyList<RuleEvaluation> Validate(EmissionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var findings = new List<RuleEvaluation>();

        if (string.IsNullOrWhiteSpace(record.Site))
        {
            findings.Add(Anomaly(MissingField, "site", null,
                "El registro no indica sede. Sin ella no hay histórico de referencia con el que "
                + "comparar el consumo."));
        }

        if (string.IsNullOrWhiteSpace(record.Month))
        {
            findings.Add(Anomaly(MissingField, "month", null,
                "El registro no indica periodo. Sin él no se puede ordenar en el tiempo ni sumar "
                + "al total anual."));
        }
        else if (!DateTime.TryParseExact(
                     record.Month, PeriodFormat, CultureInfo.InvariantCulture,
                     DateTimeStyles.None, out _))
        {
            findings.Add(Anomaly(InvalidPeriod, "month", record.Month,
                $"El periodo «{record.Month}» no sigue el formato yyyy-MM con un mes entre 01 y 12, "
                + "así que el registro no se puede situar en el tiempo."));
        }

        // Cascada por campo: si falta, no tiene sentido preguntar por su signo; si no es
        // finito, tampoco, y además -infinito dispararía el código de negativo y daría dos
        // hallazgos que describen el mismo defecto.
        if (record.EnergyKwh is not { } energy)
        {
            findings.Add(Anomaly(MissingField, "energyKwh", null,
                "El registro no indica consumo eléctrico, que es el dato sobre el que se apoya "
                + "todo el análisis."));
        }
        else if (!double.IsFinite(energy))
        {
            findings.Add(Anomaly(NonFinite, "energyKwh", Describe(energy),
                "El consumo eléctrico no es un número utilizable. Suele venir de corrupción en el "
                + "origen o de una división por cero en un cálculo anterior."));
        }
        else if (energy < 0)
        {
            findings.Add(Anomaly(NegativeEnergy, "energyKwh", Round(energy),
                $"El consumo registrado es negativo ({Describe(energy)} kWh). Un consumo negativo "
                + "no existe físicamente, así que el dato llega mal desde el origen."));
        }

        if (record.Co2Kg is not { } co2)
        {
            findings.Add(Anomaly(MissingField, "co2Kg", null,
                "El registro no indica emisiones de CO₂, que es el dato que acaba en el informe."));
        }
        else if (!double.IsFinite(co2))
        {
            findings.Add(Anomaly(NonFinite, "co2Kg", Describe(co2),
                "Las emisiones de CO₂ no son un número utilizable. Suele venir de corrupción en el "
                + "origen o de una división por cero en un cálculo anterior."));
        }
        else if (co2 < 0)
        {
            findings.Add(Anomaly(NegativeCo2, "co2Kg", Round(co2),
                $"Las emisiones registradas son negativas ({Describe(co2)} kg). Una emisión "
                + "negativa no existe físicamente, así que el dato llega mal desde el origen."));
        }

        if (record.EnergyKwh == 0 && record.Co2Kg > 0)
        {
            findings.Add(Anomaly(EmissionsWithoutEnergy, "co2Kg", Round(record.Co2Kg.Value),
                $"El registro declara {Describe(record.Co2Kg.Value)} kg de CO₂ con un consumo de "
                + "cero kWh. Emitir sin consumir es contradictorio: o falta el consumo o sobran "
                + "las emisiones."));
        }

        return findings.Count > 0
            ? findings
            : [RuleEvaluation.Passed(Id, Requirement,
                "El registro está completo y sus valores son físicamente posibles.")];
    }

    private static RuleEvaluation Anomaly(string code, string field, object? value, string message) =>
        RuleEvaluation.Anomaly(Id, Requirement, message, Severity.High,
            new Dictionary<string, object?>
            {
                ["code"] = code,
                ["field"] = field,
                ["value"] = value,
            });

    private static double Round(double value) => Math.Round(value, 4);

    // Cultura invariante para que la salida no dependa de la máquina que ejecuta el
    // análisis (RN-06). Los no finitos se describen como texto porque `NaN` e `Infinity`
    // no son números JSON y reventarían la serialización de la evidencia.
    private static string Describe(double value) => value.ToString(CultureInfo.InvariantCulture);
}
