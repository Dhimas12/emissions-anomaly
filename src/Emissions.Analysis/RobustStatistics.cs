namespace Emissions.Analysis;

// ADR-01. Mediana y MAD en lugar de media y desviación típica. La media tiene punto de
// ruptura 1/n —basta un atípico para arrastrarla— y con dos a cuatro observaciones por
// sede eso no es un matiz teórico: sobre la serie de Madrid, la media deja el valor
// anómalo en 1,73 σ, por debajo del umbral habitual de 3, y no se detecta. La mediana
// tiene punto de ruptura del 50 %.
public static class RobustStatistics
{
    // Cuantil 0,75 de la normal estándar. Hace que el estadístico sea comparable a un
    // z-score clásico bajo normalidad, que es lo que da sentido al umbral de 3,5.
    public const double MadScaleFactor = 0.6745;

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException(
                "La mediana no está definida sobre una muestra vacía.", nameof(values));
        }

        var ordered = values.ToArray();
        Array.Sort(ordered);

        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2;
    }

    public static double MedianAbsoluteDeviation(IReadOnlyList<double> values, double median)
    {
        var deviations = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            deviations[i] = Math.Abs(values[i] - median);
        }

        return Median(deviations);
    }

    // Null cuando el MAD es cero: una serie sin dispersión no ofrece unidad con la que
    // medir distancias, y dividir por cero daría un infinito que se leería como "anomalía
    // segura" en cuanto el valor se aparte un céntimo. En ese caso decide la materialidad
    // (RN-03), no la significancia estadística.
    public static double? ModifiedZScore(double value, double median, double mad) =>
        mad > 0 ? MadScaleFactor * (value - median) / mad : null;

    // Null cuando la mediana es cero: no hay base sobre la que expresar una proporción.
    public static double? RelativeDeviation(double value, double median) =>
        median != 0 ? (value - median) / median : null;
}
