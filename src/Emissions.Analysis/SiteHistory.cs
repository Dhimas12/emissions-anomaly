using Emissions.Domain;

namespace Emissions.Analysis;

// RN-01, RN-02. Las dos listas están separadas porque las reglas necesitan poblaciones
// distintas: RF-02 tiene que ver los registros inválidos —un duplicado sigue siendo un
// duplicado aunque el consumo sea negativo—, mientras que las líneas base solo pueden
// construirse con los que superan RF-01, o un dato corrupto acabaría definiendo la
// normalidad de la sede.
public sealed class SiteHistory
{
    public SiteHistory(
        string site,
        IReadOnlyList<EmissionRecord> allRecords,
        IReadOnlyList<EmissionRecord> validRecords)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(allRecords);
        ArgumentNullException.ThrowIfNull(validRecords);

        Site = site;
        AllRecords = allRecords;
        ValidRecords = validRecords;
    }

    public string Site { get; }

    public IReadOnlyList<EmissionRecord> AllRecords { get; }

    public IReadOnlyList<EmissionRecord> ValidRecords { get; }

    // RN-02. Un registro nunca participa en la línea base contra la que se le compara: si
    // se incluyese, se auto-justificaría. Con n pequeño el efecto no es sutil — el id 4
    // del dataset del enunciado desplaza su propia mediana y reduce su distancia aparente
    // a la normalidad hasta enmascararse.
    //
    // La exclusión va por `Id`, la identidad de dominio, y no por igualdad de valor:
    // `EmissionRecord` es un record y dos lecturas distintas con las mismas cifras son
    // iguales para el compilador. Comparar por valor encogería la base en silencio justo
    // en el caso que RF-02 existe para detectar.
    public IReadOnlyList<double> BaselineExcluding(
        EmissionRecord record,
        Func<EmissionRecord, double?> selector)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(selector);

        var baseline = new List<double>(ValidRecords.Count);

        foreach (var candidate in ValidRecords)
        {
            if (candidate.Id == record.Id)
            {
                continue;
            }

            // Un nulo o un no finito no es un cero: incluirlo desplazaría la mediana con
            // un valor que nadie midió.
            if (selector(candidate) is { } value && double.IsFinite(value))
            {
                baseline.Add(value);
            }
        }

        return baseline;
    }
}
