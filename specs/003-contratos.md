# 003 — Estructura, contratos y valores dorados

**Fase SDD:** 3 de 4 — CONTRATOS · **Deriva de:** `001`, `002`

Este documento existe para que la implementación sea determinista: fija la
estructura de archivos, las firmas públicas y los valores numéricos esperados.
**Las firmas son vinculantes. Los cuerpos de método son libres.**

---

## 1. Estructura de archivos a crear

```
emissions-anomaly/
├── CLAUDE.md
├── README.md                                   ← T13
├── ESCENARIOS.md                               ← T14
├── EmissionsAnomaly.sln
├── global.json                                 ← T0
├── .gitignore
├── .github/                                    ← T-CI-1, detallado en 006 §3.1
├── specs/
│   ├── 001-especificacion.md
│   ├── 002-plan-tecnico.md
│   ├── 003-contratos.md
│   ├── 004-tareas.md
│   ├── 005-escenarios-borrador.md
│   └── 006-ci-y-flujo-de-ramas.md
├── src/
│   ├── Emissions.Domain/
│   │   ├── Emissions.Domain.csproj
│   │   ├── EmissionRecord.cs
│   │   ├── Severity.cs
│   │   ├── RuleOutcome.cs
│   │   ├── RuleEvaluation.cs
│   │   └── AnalysisResult.cs           (RecordAnalysis, AnalysisSummary, AnalysisResult)
│   ├── Emissions.Analysis/
│   │   ├── Emissions.Analysis.csproj
│   │   ├── RobustStatistics.cs
│   │   ├── AnomalyDetectionOptions.cs
│   │   ├── SiteHistory.cs
│   │   ├── IAnomalyRule.cs
│   │   ├── AnomalyDetectionEngine.cs   (+ IAnomalyDetectionEngine)
│   │   ├── ServiceCollectionExtensions.cs
│   │   └── Rules/
│   │       ├── StructuralValidationRule.cs
│   │       ├── DuplicatePeriodRule.cs
│   │       ├── ConsumptionDeviationRule.cs
│   │       └── CarbonIntensityRule.cs
│   └── Emissions.Api/
│       ├── Emissions.Api.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Properties/launchSettings.json
│       └── Data/sample-records.json    (copiado al output)
└── tests/
    └── Emissions.Analysis.Tests/
        ├── Emissions.Analysis.Tests.csproj
        ├── EmissionRecordTests.cs
        ├── RuleEvaluationTests.cs
        ├── RobustStatisticsTests.cs
        ├── AnomalyDetectionOptionsTests.cs
        ├── SiteHistoryTests.cs
        ├── StructuralValidationRuleTests.cs
        ├── DuplicatePeriodRuleTests.cs
        ├── ConsumptionDeviationRuleTests.cs
        ├── CarbonIntensityRuleTests.cs
        ├── AnomalyDetectionEngineTests.cs
        ├── AcceptanceTests.cs
        ├── SampleDataset.cs            (dataset del enunciado como fixture)
        └── SampleDatasetTests.cs       (humo de T0, exigido por 006 T-CI-2)
```

### `global.json`

Fija la banda del SDK 8 con `rollForward: latestFeature`. No es adorno: en una máquina
con varios SDK instalados, compilar con uno posterior activa analizadores nuevos cuyos
avisos, con `TreatWarningsAsErrors`, se convierten en errores. Sin fijarlo, "compila
sin avisos" deja de ser una propiedad del repositorio y pasa a depender de quién lo
compila.

### Propiedades comunes de los `.csproj` de `src/`

```xml
<TargetFramework>net8.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

### Dependencias permitidas (ninguna más)

| Proyecto              | Paquetes |
|-----------------------|----------|
| `Emissions.Domain`    | ninguno |
| `Emissions.Analysis`  | `Microsoft.Extensions.Options` 8.0.2, `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.0, `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.2 |
| `Emissions.Api`       | `Swashbuckle.AspNetCore` 6.6.2 |
| tests                 | `Microsoft.NET.Test.Sdk` 17.10.0, `xunit` 2.8.1, `xunit.runner.visualstudio` 2.8.1 |

En el proyecto de tests, `TreatWarningsAsErrors` va **desactivado** (los
analizadores de xUnit generan avisos que no aportan aquí).

Referencias entre proyectos: `Analysis` → `Domain`, `Api` → `Analysis`,
`Tests` → `Analysis`. El proyecto de tests **no** referencia a `Api`; para llegar al
dataset enlaza el fichero sin crear dependencia:

```xml
<Content Include="..\..\src\Emissions.Api\Data\sample-records.json"
         Link="Data\sample-records.json"
         CopyToOutputDirectory="PreserveNewest" />
```

En `Emissions.Api.csproj` el mismo fichero se marca con `Content **Update**`, no
`Include`: el SDK Web ya incluye `**/*.json` como `Content` por defecto y un `Include`
duplicaría el item (NETSDK1022).

---

## 2. Contratos de dominio

```csharp
namespace Emissions.Domain;

// Anulables a propósito: un campo ausente es una condición a detectar (RF-01),
// no un error de parseo que deba reventar la deserialización.
public sealed record EmissionRecord(
    int Id, string? Site, string? Month, double? EnergyKwh, double? Co2Kg)
{
    // Invariante del tipo: null siempre que no pueda producir un número finito y con
    // significado. Cuatro casos: `Co2Kg` ausente · `EnergyKwh` ausente · cualquiera de
    // los dos no finito · `EnergyKwh <= 0`.
    //
    // `Co2Kg == 0` sí calcula y devuelve 0: es una medición real —"no emitió"— y que
    // RF-04a la marque por debajo de la banda es el comportamiento correcto. Un
    // numerador ausente, en cambio, no es un numerador cero.
    //
    // El caso no finito pertenece al tipo y no a la regla: `NaN` hace falsas todas las
    // comparaciones, así que RF-04a devolvería `Passed` sobre un dato corrupto. Que
    // RF-01 lo intercepte antes con `NON_FINITE` no basta para sostener el invariante.
    public double? CarbonIntensity { get; }
}

// El orden numérico importa: el motor agrega por máximo (RF-05).
public enum Severity { Low = 1, Medium = 2, High = 3 }

public enum RuleOutcome { Passed, Anomaly, NotEvaluated }

public sealed record RuleEvaluation(
    string RuleId,
    string RequirementId,                                  // trazabilidad: "RF-03"
    RuleOutcome Outcome,
    string Message,
    Severity? Severity = null,
    IReadOnlyDictionary<string, object?>? Evidence = null)
{
    public bool IsAnomaly { get; }
    public static RuleEvaluation Passed(string ruleId, string requirementId, string message);
    public static RuleEvaluation NotEvaluated(string ruleId, string requirementId, string message);
    public static RuleEvaluation Anomaly(string ruleId, string requirementId, string message,
        Severity severity, IReadOnlyDictionary<string, object?> evidence);
}

public sealed record RecordAnalysis(
    int Id, bool RequiresReview, string? Reason, Severity? Severity,
    string? Site, string? Month,
    IReadOnlyList<RuleEvaluation> Findings,
    IReadOnlyList<string> Notes);

public sealed record AnalysisSummary(
    int TotalRecords, int RecordsRequiringReview,
    int HighSeverity, int MediumSeverity, int LowSeverity);

public sealed record AnalysisResult(
    AnalysisSummary Summary, IReadOnlyList<RecordAnalysis> Results);
```

## 3. Contratos del motor

```csharp
namespace Emissions.Analysis;

public static class RobustStatistics
{
    public const double MadScaleFactor = 0.6745;   // cuantil 0,75 de la normal estándar

    public static double Median(IReadOnlyList<double> values);            // lanza si vacía
    public static double MedianAbsoluteDeviation(IReadOnlyList<double> values, double median);
    public static double? ModifiedZScore(double value, double median, double mad);  // null si mad <= 0
    public static double? RelativeDeviation(double value, double median);           // null si median == 0
}

public sealed class SiteHistory
{
    public SiteHistory(string site, IReadOnlyList<EmissionRecord> allRecords,
                       IReadOnlyList<EmissionRecord> validRecords);
    public string Site { get; }
    public IReadOnlyList<EmissionRecord> AllRecords { get; }    // incluye inválidos: RF-02 los necesita
    public IReadOnlyList<EmissionRecord> ValidRecords { get; }  // solo los que superan RF-01 (RN-01)

    // RN-02: excluye el registro evaluado y descarta valores nulos o no finitos.
    public IReadOnlyList<double> BaselineExcluding(
        EmissionRecord record, Func<EmissionRecord, double?> selector);
}

public interface IAnomalyRule
{
    string RuleId { get; }
    string RequirementId { get; }
    int Priority { get; }                                   // desempate del reason (RF-05)
    bool AppliesToInvalidRecords => false;
    IReadOnlyList<RuleEvaluation> Evaluate(EmissionRecord record, SiteHistory history);
}

public interface IAnomalyDetectionEngine
{
    AnalysisResult Analyze(IReadOnlyList<EmissionRecord> records);
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAnomalyDetection(this IServiceCollection services,
                                                         IConfiguration configuration);
    // Motor con umbrales por defecto, para tests y uso embebido.
    public static IAnomalyDetectionEngine CreateDefaultEngine(AnomalyDetectionOptions? options = null);
}
```

### Identificadores y prioridades de reglas

| Clase                       | `RuleId` emitidos                                          | `RequirementId` | Priority | `AppliesToInvalidRecords` |
|-----------------------------|------------------------------------------------------------|-----------------|----------|---------------------------|
| `StructuralValidationRule`  | `STRUCTURAL_VALIDATION`                                    | RF-01           | 10       | **true**                  |
| `DuplicatePeriodRule`       | `DUPLICATE_PERIOD`                                         | RF-02           | 20       | **true**                  |
| `ConsumptionDeviationRule`  | `CONSUMPTION_DEVIATION`                                    | RF-03           | 30       | false                     |
| `CarbonIntensityRule`       | `CARBON_INTENSITY_BAND` y `CARBON_INTENSITY_HISTORY`       | RF-04a / RF-04b | 40       | false                     |

Las cuatro reglas viven en `Rules/` y declaran el namespace
`Emissions.Analysis.Rules`; el resto del motor va en `Emissions.Analysis`.

`CarbonIntensityRule` devuelve **dos** evaluaciones (banda e histórico) en una
sola llamada; por eso `RuleEvaluation` lleva su propio `RuleId` en vez de
heredarlo de la regla.

`StructuralValidationRule` expone además, porque el motor necesita saber qué
registros son fiables **antes** de construir las líneas base:

```csharp
public static IReadOnlyList<RuleEvaluation> Validate(EmissionRecord record);
public static bool IsStructurallyValid(EmissionRecord record);
```

Cuando una regla con `AppliesToInvalidRecords = false` recibe un registro que
falló RF-01, el **motor** (no la regla) emite `NotEvaluated` con el mensaje:
`"No se evalúa: el registro no supera la validación estructural."`

### Claves de evidencia (RN-05)

| Regla                     | Claves                                                                                                            |
|---------------------------|-------------------------------------------------------------------------------------------------------------------|
| `STRUCTURAL_VALIDATION`   | `code`, `field`, `value`                                                                                          |
| `DUPLICATE_PERIOD`        | `site`, `month`, `duplicateRecordIds`                                                                             |
| `CONSUMPTION_DEVIATION`   | `energyKwh`, `baselineMedianKwh`, `baselineMad`, `baselineSize`, `modifiedZScore`, `relativeDeviation`            |
| `CARBON_INTENSITY_BAND`   | `carbonIntensityKgPerKwh`, `plausibleRange`                                                                       |
| `CARBON_INTENSITY_HISTORY`| `carbonIntensityKgPerKwh`, `baselineMedianIntensity`, `baselineSize`, `relativeDeviation`                          |

Los valores numéricos de evidencia se redondean a 4 decimales.

## 4. Lógica exacta de las reglas estadísticas

### RF-03 · `ConsumptionDeviationRule`

```
si energyKwh es null                     → NotEvaluated
base ← BaselineExcluding(record, r => r.EnergyKwh)
si base.Count < MinimumBaselineSize      → NotEvaluated ("Histórico insuficiente en {sede}: {n} …")
mediana, mad ← RobustStatistics
z    ← ModifiedZScore(energyKwh, mediana, mad)     // null si mad == 0
rel  ← RelativeDeviation(energyKwh, mediana)
si rel es null                           → NotEvaluated (mediana cero)

extremo  ← (z es null) O (|z| > RobustZScoreThreshold)   // mad==0 ⇒ decide solo la materialidad
material ← |rel| > MinimumRelativeDeviation
si NO (extremo Y material)               → Passed
si no                                    → Anomaly, severidad = SeverityFor(|rel|)
```

`SeverityFor(x)`: `x ≥ HighSeverityRelativeDeviation` ⇒ High; `x ≥ Medium…` ⇒ Medium; resto ⇒ Low.

### RF-04 · `CarbonIntensityRule`

```
si CarbonIntensity es null → una sola evaluación NotEvaluated sobre CARBON_INTENSITY_BAND

(a) BANDA — no depende de histórico:
    intensidad > MaxCarbonIntensity → Anomaly High
    intensidad < MinCarbonIntensity → Anomaly High ("podrían faltar emisiones imputadas")
    si no                           → Passed

(b) HISTÓRICO:
    base ← BaselineExcluding(record, r => r.CarbonIntensity)
    si base.Count < MinimumBaselineSize → NotEvaluated
    rel ← RelativeDeviation(intensidad, mediana(base))
    si |rel| ≤ IntensityRelativeTolerance → Passed
    si no → Anomaly, severidad: |rel| ≥ 1,0 ⇒ High · ≥ 0,7 ⇒ Medium · resto ⇒ Low
```

## 5. Contrato JSON de la API

**Entrada** de `POST /api/v1/analysis`: array de registros con el esquema de
`001-especificacion.md` §3. Deserialización insensible a mayúsculas.

**Salida** (200):

```json
{
  "summary": { "totalRecords": 10, "recordsRequiringReview": 3,
               "highSeverity": 3, "mediumSeverity": 0, "lowSeverity": 0 },
  "results": [
    {
      "id": 4,
      "requiresReview": true,
      "reason": "El consumo supera de forma significativa el comportamiento histórico de Madrid (79000 kWh frente a una mediana de 12500 kWh, 6,3x, desviación 532 %).",
      "severity": "High",
      "site": "Madrid",
      "month": "2026-04",
      "findings": [
        { "ruleId": "CONSUMPTION_DEVIATION", "requirementId": "RF-03",
          "outcome": "Anomaly", "message": "…", "severity": "High",
          "evidence": { "energyKwh": 79000, "baselineMedianKwh": 12500,
                        "baselineMad": 300, "baselineSize": 3,
                        "modifiedZScore": 149.5145, "relativeDeviation": 5.32 } }
      ],
      "notes": []
    }
  ]
}
```

Serialización: `camelCase`, enums como cadena (`JsonStringEnumConverter`),
`WriteIndented = true` en Development.

**Errores:** cuerpo nulo o array vacío ⇒ `400` con `ValidationProblem`. Excepción
no controlada ⇒ `500` vía `UseExceptionHandler`, sin filtrar la traza.

`GET /health` ⇒ `200 { "status": "healthy" }`.

---

## 6. Valores dorados (calculados a mano — NO recalcular ni ajustar)

Base *leave-one-out* sobre registros válidos. Barcelona excluye el id 7 de sus
bases por fallar RF-01.

### RF-03 — consumo

| Id | Sede      | n base | Mediana | MAD | z modificado | Desv. rel. | Veredicto            |
|----|-----------|--------|---------|-----|--------------|------------|----------------------|
| 1  | Madrid    | 3      | 12.800  | 300 | −1,799       | −6,25 %    | Passed               |
| 2  | Madrid    | 3      | 12.800  | 800 | −0,253       | −2,34 %    | Passed               |
| 3  | Madrid    | 3      | 12.500  | 500 | +0,405       | +2,40 %    | Passed               |
| 4  | Madrid    | 3      | 12.500  | 300 | **+149,514** | **+532 %** | **Anomaly / High**   |
| 5,6,8 | Barcelona | 2   | —       | —   | —            | —          | NotEvaluated (RF-06) |
| 7  | Barcelona | —      | —       | —   | —            | —          | NotEvaluated (inválido) |
| 9,10 | Valencia | 1     | —       | —   | —            | —          | NotEvaluated (RF-06) |

### RF-04 — intensidad de carbono (kg CO₂/kWh)

| Id | Intensidad | Banda (RF-04a)         | n base | Mediana base | Desv. rel. | Histórico (RF-04b)   |
|----|------------|------------------------|--------|--------------|------------|----------------------|
| 1  | 0,2333     | Passed                 | 3      | 0,2305       | +1,24 %    | Passed               |
| 2  | 0,2320     | Passed                 | 3      | 0,2305       | +0,66 %    | Passed               |
| 3  | 0,2305     | Passed                 | 3      | 0,2320       | −0,66 %    | Passed               |
| 4  | 0,2304     | Passed                 | 3      | 0,2320       | −0,70 %    | **Passed** ← clave   |
| 5  | 0,2294     | Passed                 | 2      | —            | —          | NotEvaluated         |
| 6  | 0,2299     | Passed                 | 2      | —            | —          | NotEvaluated         |
| 7  | n/a        | NotEvaluated           | —      | —            | —          | —                    |
| 8  | **0,9551** | **Anomaly / High**     | 2      | —            | —          | NotEvaluated         |
| 9  | 0,2339     | Passed                 | 1      | —            | —          | NotEvaluated         |
| 10 | 0,2336     | Passed                 | 1      | —            | —          | NotEvaluated         |

**El id 4 en RF-04b debe dar `Passed`.** Si diera anomalía, el sistema estaría
confundiendo crecimiento real con dato corrupto y la respuesta al Escenario A se
caería. Es el test de regresión más importante de la entrega.

### Resultado agregado

`totalRecords = 10`, `recordsRequiringReview = 3` (ids 4, 7, 8),
`highSeverity = 3`, `mediumSeverity = 0`, `lowSeverity = 0`.

### Valores unitarios de `RobustStatistics`

| Entrada                         | Median | MAD  | ModifiedZScore(x)              |
|---------------------------------|--------|------|--------------------------------|
| `[1, 2, 3, 4]`                  | 2,5    | 1,0  | z(10) = 5,0588                 |
| `[12000, 12500, 12800]`         | 12500  | 300  | z(79000) = 149,5145            |
| `[5, 5, 5]`                     | 5      | 0    | null (MAD cero)                |
| `[]`                            | lanza `ArgumentException` | — | —              |

`RelativeDeviation(79000, 12500) = 5,32` · `RelativeDeviation(10, 0) = null`.
