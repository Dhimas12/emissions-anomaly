namespace Emissions.Domain;

// RF-05. Los campos son anulables a propósito: que falte uno es una de las condiciones a
// detectar (RF-01), no un error de parseo que deba reventar la deserialización.
public sealed record EmissionRecord(
    int Id,
    string? Site,
    string? Month,
    double? EnergyKwh,
    double? Co2Kg)
{
    // Factor de emisión implícito de la sede, en kg CO₂/kWh (RF-04). Null siempre que no
    // pueda producir un número finito y con significado, que es un invariante del tipo y
    // no de las reglas: NaN hace falsas todas las comparaciones, así que una intensidad
    // NaN atravesaría la banda física de RF-04a como si fuese correcta. Que RF-01 marque
    // el registro por NON_FINITE antes de llegar ahí no basta para sostenerlo.
    //
    // Un CO₂ de cero sí calcula y da 0: es una medición real, no una ausencia de dato.
    public double? CarbonIntensity =>
        EnergyKwh is { } energy && double.IsFinite(energy) && energy > 0
        && Co2Kg is { } co2 && double.IsFinite(co2)
            ? co2 / energy
            : null;
}
