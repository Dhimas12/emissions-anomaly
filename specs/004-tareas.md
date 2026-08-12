# 004 — Tareas

**Fase SDD:** 4 de 4 — EJECUCIÓN · **Deriva de:** `001`, `002`, `003`

Se ejecutan **en orden**. Cada tarea se cierra completa (código + test verde)
antes de empezar la siguiente. Marcar `[x]` al terminar.

---

- [x] **T0 · Andamiaje**
  Solución `EmissionsAnomaly.sln`, los cuatro proyectos con las propiedades y
  paquetes de `003` §1, referencias entre proyectos, `.gitignore` de .NET.
  *Hecho cuando:* `dotnet build` compila sin avisos.

- [ ] **T1 · Modelos de dominio** — RF-05
  `EmissionRecord`, `Severity`, `RuleOutcome`, `RuleEvaluation`, `RecordAnalysis`,
  `AnalysisSummary`, `AnalysisResult` según `003` §2.
  *Hecho cuando:* `CarbonIntensity` devuelve null para energía nula, cero o negativa,
  y valor correcto en el resto.

- [ ] **T2 · `RobustStatistics`** — ADR-01
  Mediana, MAD, z-score modificado, desviación relativa.
  *Test:* `RobustStatisticsTests`, contra la tabla de `003` §6 "valores unitarios".
  Incluir el caso de muestra vacía (lanza) y el de MAD cero (devuelve null).

- [ ] **T3 · `AnomalyDetectionOptions`** — RN-07, ADR-06
  Propiedades y valores por defecto de `002` §3, con `DataAnnotations` y el
  método `Validate()` para las coherencias cruzadas.
  *Test:* configuración con `MinCarbonIntensity >= MaxCarbonIntensity` es rechazada.

- [ ] **T4 · `SiteHistory`** — RN-01, RN-02
  Separación entre `AllRecords` y `ValidRecords`, y `BaselineExcluding`.
  *Test:* `SiteHistoryTests.RN02_*` — la base de un registro nunca lo contiene;
  los valores nulos o no finitos quedan fuera.

- [ ] **T5 · `IAnomalyRule`** — ADR-05
  Interfaz de `003` §3, con `AppliesToInvalidRecords` por defecto en `false`.

- [ ] **T6 · `StructuralValidationRule`** — RF-01
  Los seis códigos de `001` §4, más `Validate` e `IsStructurallyValid` estáticos.
  *Test:* un caso por código; el id 7 del dataset produce dos anomalías
  (`NEGATIVE_ENERGY` y `NEGATIVE_CO2`); un registro sano devuelve `Passed`.

- [ ] **T7 · `DuplicatePeriodRule`** — RF-02
  *Test:* dos registros de la misma sede y mes ⇒ ambos marcados `High`, y cada uno
  referencia al otro en `duplicateRecordIds`. Sin duplicados ⇒ `Passed`.

- [ ] **T8 · `ConsumptionDeviationRule`** — RF-03, RN-02, RN-03
  Algoritmo literal de `003` §4.
  *Tests obligatorios:*
  - `RF03_ConsumoMuySuperiorALaBase_SeMarcaAlto` — id 4, z ≈ 149,5145, rel = 5,32.
  - `RF03_ConsumoNormal_NoSeMarca` — ids 1–3.
  - `RN03_DesviacionExtremaPeroInmaterial_NoSeMarca` — base `[12000, 12500, 12800]`
    con valor 13.500: el z supera 3,5 pero la desviación relativa es ~8 %, así que
    **no** se marca. Es el test que documenta ADR-02.
  - `RF06_HistoricoInsuficiente_NoSeEvalua` — base de 2 puntos.
  - `RF03_SerieConstante_MadCero_DecideLaMaterialidad` — base `[5000, 5000, 5000]`:
    5.200 (+4 %) no se marca; 9.000 (+80 %) sí.

- [ ] **T9 · `CarbonIntensityRule`** — RF-04
  Las dos evaluaciones de `003` §4, con sus `RuleId` distintos.
  *Tests obligatorios:*
  - `RF04a_IntensidadFueraDeBanda_SeMarcaAlto` — id 8, intensidad 0,9551.
  - `RF04a_IntensidadDemasiadoBaja_SeMarcaAlto`.
  - `RF04b_IdCuatroEscalaConsumoYEmisiones_NoSeMarca` — **el test clave**: id 4
    tiene intensidad 0,2304 frente a mediana 0,2320 de Madrid ⇒ `Passed`.
  - `RF06_HistoricoInsuficiente_HistoricoNoSeEvaluaPeroLaBandaSi` — Valencia.

- [ ] **T10 · `AnomalyDetectionEngine` + DI** — RF-05, RF-06, RN-01
  Las cuatro fases de `002` §1. Agregación: `requiresReview`, severidad máxima,
  `reason` por severidad y desempate por prioridad, `notes` con los mensajes
  `NotEvaluated`. `AddAnomalyDetection` y `CreateDefaultEngine`.
  *Tests:* `AnomalyDetectionEngineTests` — el registro inválido no contamina la
  base de su sede (RN-01); con dos anomalías de distinta severidad el `reason`
  corresponde a la mayor; el desempate por prioridad funciona.

- [ ] **T11 · Tests de aceptación** — todos
  `AcceptanceTests` sobre el dataset del enunciado, comprobando **registro a
  registro** la tabla de `001` §6 y el resumen agregado.
  Incluir `ADR01_MediaYDesviacionTipicaEnmascararianElAtipico` como test de
  regresión documental: verifica que los ids 1–3 no se marcan pese a convivir con
  el id 4, que es exactamente lo que fallaría con media y σ.

- [ ] **T12 · Minimal API** — RF-07
  `Program.cs` con los cuatro endpoints de `003` §5, Swagger en la raíz,
  `JsonStringEnumConverter`, `camelCase`, manejo de errores.
  El dataset de ejemplo se lee de `Data/sample-records.json` (copiado al output),
  no incrustado en código.
  *Hecho cuando:* `curl localhost:5080/api/v1/analysis/sample` reproduce la tabla
  de aceptación.

- [ ] **T13 · README.md**
  Breve. Debe contener: qué hace, cómo ejecutarlo (build, test, run, un `curl` de
  ejemplo), el criterio de detección resumido en un párrafo por regla con su
  justificación, la tabla de resultados sobre el dataset, la sección de escalado a
  millones de registros (`002` §4) y las limitaciones conocidas.
  Sin capturas, sin badges, sin secciones de relleno.

- [ ] **T14 · ESCENARIOS.md**
  Respuesta a los escenarios A y B partiendo de `005-escenarios-borrador.md`.
  Breve y con criterio; el enunciado pide explícitamente que no sea teoría extensa.

---

## Matriz de trazabilidad

| Requisito | Implementación                       | Test                                                    |
|-----------|--------------------------------------|---------------------------------------------------------|
| RF-01     | `Rules/StructuralValidationRule.cs`  | `StructuralValidationRuleTests`                         |
| RF-02     | `Rules/DuplicatePeriodRule.cs`       | `DuplicatePeriodRuleTests`                              |
| RF-03     | `Rules/ConsumptionDeviationRule.cs`  | `ConsumptionDeviationRuleTests`                         |
| RF-04a    | `Rules/CarbonIntensityRule.cs`       | `CarbonIntensityRuleTests.RF04a_*`                      |
| RF-04b    | `Rules/CarbonIntensityRule.cs`       | `CarbonIntensityRuleTests.RF04b_*`                      |
| RF-05     | `AnomalyDetectionEngine.cs`          | `AnomalyDetectionEngineTests`                           |
| RF-06     | `SiteHistory.cs` + motor             | `AcceptanceTests.RF06_*`                                |
| RF-07     | `Emissions.Api/Program.cs`           | verificación manual documentada en README               |
| RN-01     | `AnomalyDetectionEngine.Analyze`     | `AnomalyDetectionEngineTests.RN01_*`                    |
| RN-02     | `SiteHistory.BaselineExcluding`      | `SiteHistoryTests.RN02_*`                               |
| RN-03     | `ConsumptionDeviationRule`           | `ConsumptionDeviationRuleTests.RN03_*`                  |
| RN-07     | `AnomalyDetectionOptions`            | `AnomalyDetectionOptionsTests`                          |
| ADR-01    | `RobustStatistics.cs`                | `RobustStatisticsTests`, `AcceptanceTests.ADR01_*`      |
