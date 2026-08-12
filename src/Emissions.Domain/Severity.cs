namespace Emissions.Domain;

// RF-05. El motor agrega quedándose con la severidad máxima de las reglas que
// dispararon, así que el orden numérico forma parte del contrato y no es decorativo.
public enum Severity
{
    Low = 1,
    Medium = 2,
    High = 3,
}
