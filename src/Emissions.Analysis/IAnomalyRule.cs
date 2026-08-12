using Emissions.Domain;

namespace Emissions.Analysis;

// ADR-05. Cada regla vive detrás de esta interfaz para que añadir una no obligue a
// modificar ninguna existente ni el motor, y para que cada una se pueda probar aislada.
public interface IAnomalyRule
{
    // Identificador de la regla. Una implementación puede emitir evaluaciones con otros
    // `RuleId`: `CarbonIntensityRule` devuelve dos, banda física e histórico, y por eso
    // `RuleEvaluation` lleva el suyo propio en vez de heredarlo de aquí.
    string RuleId { get; }

    string RequirementId { get; }

    // Desempate del `reason` cuando dos reglas disparan con la misma severidad (RF-05).
    // Gana la de menor número, de modo que la validación estructural se explica antes que
    // la estadística: a un analista le sirve más "el consumo es negativo" que "el consumo
    // se desvía de su histórico" cuando ambas cosas son ciertas a la vez.
    int Priority { get; }

    // Casi ninguna regla debe opinar sobre un registro que no supera RF-01: comparar
    // contra un histórico un dato que se sabe corrupto produce ruido con apariencia de
    // hallazgo. Las excepciones son RF-01, que es quien lo detecta, y RF-02, porque un
    // periodo duplicado lo sigue siendo aunque sus cifras estén mal.
    bool AppliesToInvalidRecords => false;

    // Devuelve una lista y no una sola evaluación porque una regla puede pronunciarse
    // sobre varias condiciones en una pasada.
    IReadOnlyList<RuleEvaluation> Evaluate(EmissionRecord record, SiteHistory history);
}
