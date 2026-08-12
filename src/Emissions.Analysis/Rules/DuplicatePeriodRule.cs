using Emissions.Domain;

namespace Emissions.Analysis.Rules;

// RF-02. Si una sede tiene más de un registro para el mismo periodo, se marcan **todos**,
// no solo el segundo: desde fuera no hay forma de saber cuál es el bueno, y elegir uno
// sería tomar la decisión que RN-04 reserva al humano.
//
// No dispara sobre el dataset del enunciado. Se implementa igualmente porque es el fallo
// de ingesta más común en producción y su efecto es doble conteo en el agregado anual,
// que es justo el número que se publica.
public sealed class DuplicatePeriodRule : IAnomalyRule
{
    private const string Id = "DUPLICATE_PERIOD";
    private const string Requirement = "RF-02";

    public string RuleId => Id;

    public string RequirementId => Requirement;

    public int Priority => 20;

    // Un periodo duplicado lo sigue siendo aunque sus cifras estén mal: el doble conteo se
    // produce igual, así que la regla tiene que ver también los registros que fallan RF-01.
    public bool AppliesToInvalidRecords => true;

    public IReadOnlyList<RuleEvaluation> Evaluate(EmissionRecord record, SiteHistory history)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(history);

        if (string.IsNullOrWhiteSpace(record.Month))
        {
            return
            [
                RuleEvaluation.NotEvaluated(Id, Requirement,
                    "No se comprueba la duplicidad de periodo: el registro no indica cuál es. "
                    + "El dato falta, que no es lo mismo que estar comprobado y correcto."),
            ];
        }

        // Comparación insensible a mayúsculas porque esta regla también ve los registros
        // que fallan RF-01, y en esos el periodo es texto libre: "2026-ENE" y "2026-ene"
        // describen el mismo mes duplicado aunque ninguno de los dos sea válido.
        var duplicados = history.AllRecords
            .Where(otro => otro.Id != record.Id
                && string.Equals(otro.Month, record.Month, StringComparison.OrdinalIgnoreCase))
            .Select(otro => otro.Id)
            .Order()
            .ToList();

        if (duplicados.Count == 0)
        {
            return
            [
                RuleEvaluation.Passed(Id, Requirement,
                    $"No hay ningún otro registro de {history.Site} para el periodo {record.Month}."),
            ];
        }

        return
        [
            RuleEvaluation.Anomaly(Id, Requirement,
                $"El periodo {record.Month} de {history.Site} aparece por duplicado, junto a "
                + $"{Enumerar(duplicados)}. Un periodo repetido se suma dos veces al agregado "
                + "anual, que es el número que acaba publicado en el informe.",
                Severity.High,
                new Dictionary<string, object?>
                {
                    ["site"] = history.Site,
                    ["month"] = record.Month,
                    ["duplicateRecordIds"] = duplicados,
                }),
        ];
    }

    private static string Enumerar(IReadOnlyList<int> ids) => ids.Count switch
    {
        1 => $"el registro {ids[0]}",
        _ => $"los registros {string.Join(", ", ids.Take(ids.Count - 1))} y {ids[^1]}",
    };
}
