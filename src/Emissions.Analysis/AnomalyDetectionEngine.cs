using Emissions.Analysis.Rules;
using Emissions.Domain;

namespace Emissions.Analysis;

public interface IAnomalyDetectionEngine
{
    AnalysisResult Analyze(IReadOnlyList<EmissionRecord> records);
}

// RF-05, RF-06, RN-01. Las cuatro fases de 002 §1, y su orden es necesidad y no estilo: no
// se puede comparar un registro contra la normalidad de su sede sin saber antes qué
// registros de esa sede son fiables. Invertir las fases 1 y 2 dejaría que un dato corrupto
// definiese la normalidad y se auto-justificara.
public sealed class AnomalyDetectionEngine : IAnomalyDetectionEngine
{
    // Texto fijado por 003 §3. Lo emite el motor y no la regla, porque es el motor quien
    // sabe que el registro falló RF-01: la regla ni siquiera llega a mirarlo.
    private const string NoSeEvalua = "No se evalúa: el registro no supera la validación estructural.";

    private readonly IReadOnlyList<IAnomalyRule> _rules;

    // Las reglas se ordenan por prioridad aquí y en ningún sitio más. De esa ordenación
    // salen dos propiedades: `Findings` sale siempre en el mismo orden sea cual sea el
    // orden de registro en el contenedor de DI (RN-06), y el desempate del `reason` de
    // RF-05 se reduce a tomar el primero de la severidad máxima.
    public AnomalyDetectionEngine(IEnumerable<IAnomalyRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.OrderBy(rule => rule.Priority).ToList();
    }

    public AnalysisResult Analyze(IReadOnlyList<EmissionRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        // Fase 1: separar fiables de corruptos, antes de tocar ninguna línea base (RN-01).
        var fiables = records
            .Where(StructuralValidationRule.IsStructurallyValid)
            .Select(record => record.Id)
            .ToHashSet();

        // Fase 2: líneas base por sede, construidas solo con los fiables.
        var historias = ConstruirHistorias(records, fiables);

        // Fases 3 y 4: regla a regla y agregación por registro.
        var resultados = records
            .Select(record => Analizar(record, historias[ClaveDeSede(record)], fiables.Contains(record.Id)))
            .ToList();

        return new AnalysisResult(Resumir(resultados), resultados);
    }

    // La clave ignora mayúsculas y espacios sobrantes (003 §3). Cuando un lote se compone
    // de dos fuentes, la misma sede llega escrita de dos formas: agrupar por igualdad
    // ordinal partiría su histórico en dos mitades que caerían por debajo de
    // MinimumBaselineSize, y las tres consecuencias serían silenciosas.
    private static string ClaveDeSede(EmissionRecord record) => record.Site?.Trim() ?? string.Empty;

    private static Dictionary<string, SiteHistory> ConstruirHistorias(
        IReadOnlyList<EmissionRecord> records, HashSet<int> fiables)
    {
        var grupos = new Dictionary<string, List<EmissionRecord>>(StringComparer.OrdinalIgnoreCase);
        var grafias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var clave = ClaveDeSede(record);

            if (!grupos.TryGetValue(clave, out var grupo))
            {
                grupos[clave] = grupo = [];

                // La primera grafía que llegó, que es la que el analista reconoce.
                grafias[clave] = clave;
            }

            grupo.Add(record);
        }

        return grupos.ToDictionary(
            par => par.Key,
            par => new SiteHistory(
                grafias[par.Key],
                par.Value,
                par.Value.Where(record => fiables.Contains(record.Id)).ToList()),
            StringComparer.OrdinalIgnoreCase);
    }

    private RecordAnalysis Analizar(EmissionRecord record, SiteHistory historia, bool esFiable)
    {
        // La prioridad viaja junto a cada evaluación, tomada de la regla que la produjo. No
        // se deduce del RuleId: CARBON_INTENSITY_BAND y CARBON_INTENSITY_HISTORY comparten
        // prefijo, y resolver la prioridad por comparación de cadenas funcionaría hoy por
        // accidente y se rompería en cuanto alguien añadiese una regla con un nombre
        // parecido.
        var evaluadas = new List<(int Prioridad, RuleEvaluation Evaluacion)>();

        foreach (var regla in _rules)
        {
            if (!esFiable && !regla.AppliesToInvalidRecords)
            {
                evaluadas.Add((regla.Priority,
                    RuleEvaluation.NotEvaluated(regla.RuleId, regla.RequirementId, NoSeEvalua)));
                continue;
            }

            foreach (var evaluacion in regla.Evaluate(record, historia))
            {
                evaluadas.Add((regla.Priority, evaluacion));
            }
        }

        var anomalias = evaluadas.Where(e => e.Evaluacion.IsAnomaly).ToList();

        // RF-05: severidad máxima entre las reglas que dispararon.
        Severity? severidad = anomalias.Count > 0
            ? anomalias.Max(a => a.Evaluacion.Severity!.Value)
            : null;

        // RF-05: el motivo es el de la regla de mayor severidad y, a igualdad, el de la de
        // menor prioridad numérica, de modo que la validación se explica antes que la
        // estadística. No hace falta reordenar aquí: `_rules` ya viene ordenado por
        // prioridad del constructor, así que `anomalias` conserva ese orden y basta con el
        // primero. Un `OrderBy` extra en esta línea sería un segundo mecanismo para lo
        // mismo, y ninguna prueba podría distinguir cuál de los dos está funcionando.
        var motivo = anomalias
            .Where(a => a.Evaluacion.Severity == severidad)
            .Select(a => a.Evaluacion.Message)
            .FirstOrDefault();

        // RF-06: lo que no se pudo comprobar se declara, en lugar de darse por bueno.
        var notas = evaluadas
            .Where(e => e.Evaluacion.Outcome == RuleOutcome.NotEvaluated)
            .Select(e => e.Evaluacion.Message)
            .ToList();

        return new RecordAnalysis(
            record.Id,
            anomalias.Count > 0,
            motivo,
            severidad,
            record.Site,
            record.Month,
            evaluadas.Select(e => e.Evaluacion).ToList(),
            notas);
    }

    // Las severidades cuentan registros, no hallazgos: un registro con tres anomalías altas
    // sigue siendo un registro que revisar.
    private static AnalysisSummary Resumir(IReadOnlyList<RecordAnalysis> resultados) => new(
        resultados.Count,
        resultados.Count(r => r.RequiresReview),
        resultados.Count(r => r.Severity == Severity.High),
        resultados.Count(r => r.Severity == Severity.Medium),
        resultados.Count(r => r.Severity == Severity.Low));
}
