namespace Emissions.Domain;

// ADR-05, RF-06. El tercer estado es deliberado: sin él, "no había histórico con el que
// comparar" y "se comprobó y está bien" comparten el mismo silencio, y esa confusión es
// la fuente habitual de falsos negativos en producción.
public enum RuleOutcome
{
    Passed,
    Anomaly,
    NotEvaluated,
}
