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
    // Factor de emisión implícito de la sede, en kg CO₂/kWh (RF-04). Null cuando no es
    // calculable: sin energía positiva no hay divisor que produzca un cociente con
    // sentido físico, y un CO₂ ausente no es lo mismo que un CO₂ de cero.
    public double? CarbonIntensity =>
        EnergyKwh is { } energy && energy > 0 && Co2Kg is { } co2
            ? co2 / energy
            : null;
}
