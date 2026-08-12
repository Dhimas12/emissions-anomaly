# 002 — Plan técnico y decisiones

**Fase SDD:** 2 de 4 — CÓMO · **Deriva de:** `001-especificacion.md`

---

## 1. Arquitectura

```
Emissions.Domain      modelos y vocabulario. Cero dependencias.
      ▲
Emissions.Analysis    motor: estadística, reglas, opciones. No conoce ASP.NET.
      ▲
Emissions.Api         Minimal API: transporte, validación de entrada, serialización.
```

La dirección de las dependencias es la decisión estructural: **el motor no sabe
que existe una API**. El enunciado admite consola, API o servicio; separando así,
el anfitrión es un detalle sustituible en una tarde. El mismo `Emissions.Analysis`
sirve a una Minimal API hoy y a un worker de Service Bus o a un job de Databricks
mañana sin tocar una línea.

### Flujo de una petición

```
lote de registros
   │
   ├─ 1. validación estructural (RF-01)  → separa fiables de corruptos
   ├─ 2. líneas base por sede, solo con fiables (RN-01)
   ├─ 3. evaluación regla a regla, base leave-one-out (RN-02)
   │       RF-02 duplicados · RF-03 consumo · RF-04 intensidad
   └─ 4. agregación por registro (RF-05)
```

El orden de fases es necesidad, no estilo: no se puede comparar un registro
contra la normalidad de su sede sin saber antes qué registros de esa sede son
fiables.

---

## 2. Decisiones de arquitectura

### ADR-01 · Estadística robusta (mediana + MAD) en lugar de media y σ

**Contexto.** Entre 2 y 4 observaciones por sede. Madrid contiene un valor 6,3
veces superior al resto.

**Decisión.** Línea base = mediana. Dispersión = *Median Absolute Deviation*.
Criterio = z-score modificado de Iglewicz–Hoaglin: `0,6745 · (x − mediana) / MAD`,
umbral 3,5.

**Por qué — con los números del propio dataset.** La media y σ tienen punto de
ruptura 1/n: un solo atípico las arrastra.

| Serie de Madrid | media/σ                | z clásico | mediana/MAD (LOO) | z modificado |
|-----------------|------------------------|-----------|-------------------|--------------|
| 12.000          | μ=29.075 · σ=28.826    | −0,59     | 12.800 / 300      | −1,80        |
| 12.500          |                        | −0,58     | 12.800 / 800      | −0,25        |
| 12.800          |                        | −0,56     | 12.500 / 500      | +0,41        |
| **79.000**      |                        | **+1,73** | 12.500 / 300      | **+149,5**   |

Con estadística clásica el valor anómalo queda en 1,73 σ: **por debajo del
umbral habitual de 3, no se detecta**. Es el efecto de enmascaramiento, y aquí no
es teórico: es la diferencia entre resolver el ejercicio y no resolverlo. La
mediana tiene punto de ruptura 50 %: haría falta que la mitad de las
observaciones fuesen anómalas para desplazarla.

El factor 0,6745 es el cuantil 0,75 de la normal estándar; hace que el
estadístico sea comparable a un z-score clásico bajo normalidad. El umbral 3,5 es
el valor convencional de la literatura para este estadístico.

**Coste asumido.** La mediana ignora información de la cola. Con n ≤ 4 esa
información no existe, así que el coste aquí es nominal.

### ADR-02 · Doble condición: significancia estadística **y** materialidad

**Contexto.** En una serie muy estable el MAD tiende a cero y cualquier variación
produce un z-score enorme. Si a Madrid le llegase un mes de 13.500 kWh (+8 %), el
z-score superaría 3,5 y saltaría la alerta.

**Decisión.** Anomalía solo si `|z| > 3,5` **y** desviación relativa > 25 %.

**Por qué.** El z-score responde a "¿es raro?" y la desviación relativa a
"¿importa?". Un sistema que dispara por un 8 % de variación mensual se desactiva
solo: el analista deja de mirarlo en dos semanas y vuelven los falsos negativos
por la puerta de atrás. La materialidad es un concepto de auditoría, no un parche;
incorporarla alinea la detección con cómo se juzgan realmente los informes ESG.

### ADR-03 · Línea base *leave-one-out*

**Decisión.** La línea base de un registro excluye ese registro (RN-02).

**Por qué.** Con n pequeño, incluirse es auto-justificarse: si el id 4 entra en su
propia mediana la desplaza y reduce su distancia aparente a la normalidad.
Recalcular la mediana por registro es irrelevante a esta escala y resoluble con
estadísticos precalculados a escala real (§4).

### ADR-04 · Volumen e intensidad como señales independientes

**Decisión.** RF-03 mira `energyKwh`. RF-04 mira `co2Kg / energyKwh`. **Nunca se
combinan en una puntuación única.**

**Por qué.** Es lo que separa los dos casos difíciles del dataset y, sobre todo,
lo que hace útil la salida para el negocio:

| Patrón                              | Interpretación                          | Caso   |
|-------------------------------------|-----------------------------------------|--------|
| volumen anómalo, intensidad normal  | la sede consumió más de verdad          | id 4   |
| volumen normal, intensidad anómala  | el dato está mal o cambió el mix        | id 8   |
| ambos anómalos                      | error de unidades o de origen           | —      |

Un score agregado colapsaría estos tres casos en un número y destruiría justo la
información que el analista necesita para decidir. Esta separación es también la
que sostiene la respuesta al Escenario A.

### ADR-05 · Reglas independientes tras una interfaz, con tres estados

**Decisión.** `IAnomalyRule` con implementaciones registradas en DI. Cada
evaluación devuelve `Anomaly`, `Passed` o `NotEvaluated`, con mensaje y evidencia.

**Por qué.** Añadir una regla no modifica ninguna existente ni el motor, y cada
regla se testea aislada. El tercer estado es deliberado: sin él, "no había
histórico" y "se comprobó y está bien" comparten el mismo silencio, y esa
confusión es exactamente lo que produce falsos negativos silenciosos (RF-06).

### ADR-06 · Umbrales en configuración, validados al arrancar

**Decisión.** `AnomalyDetectionOptions` enlazado desde `appsettings.json`, con
`ValidateDataAnnotations().ValidateOnStart()`.

**Por qué.** Los umbrales son criterio de negocio, no lógica: cambiarlos no debe
exigir recompilar. Y validarlos al arrancar convierte una configuración
incoherente en un fallo inmediato y ruidoso, en lugar de un análisis
silenciosamente equivocado que acaba en un informe publicado.

La banda de intensidad por defecto (0,05–0,80) asume red eléctrica europea. Una
red nórdica (~0,02) o una intensiva en carbón (~0,9) exigen recalibrarla: es
configuración por región, no una constante universal. Reconocerlo explícitamente
es parte del diseño, no una carencia.

### ADR-07 · Determinismo y ausencia de ML en el camino de decisión

**Decisión.** Todo el criterio es aritmética explicable. Sin modelo entrenado, sin
dependencias externas al decidir.

**Por qué.** La salida alimenta reporting ESG, que es material auditable. Un
auditor puede exigir que se reproduzca a mano por qué el mes de abril de Madrid
quedó fuera del informe, y con este diseño se reproduce con una calculadora a
partir de la evidencia adjunta (RN-05). Un modelo entrenado con 10 observaciones
no aportaría precisión y sí destruiría esa propiedad. Este mismo razonamiento es
la respuesta al Escenario B.

---

## 3. Parámetros por defecto

| Parámetro                        | Valor | Justificación                                                        |
|----------------------------------|-------|----------------------------------------------------------------------|
| `RobustZScoreThreshold`          | 3,5   | umbral convencional del z-score modificado                           |
| `MinimumRelativeDeviation`       | 0,25  | materialidad: bajo ±25 % mensual no se molesta a un analista         |
| `MinimumBaselineSize`            | 3     | con menos de 3 puntos válidos la mediana y el MAD no son informativos |
| `MinCarbonIntensity`             | 0,05  | por debajo, o falta CO₂ imputado o la energía no es de red           |
| `MaxCarbonIntensity`             | 0,80  | por encima del factor de emisión de casi cualquier red eléctrica      |
| `IntensityRelativeTolerance`     | 0,40  | holgura para cambios reales de mix; por encima, algo cambió          |
| `HighSeverityRelativeDeviation`  | 2,0   | desviación de **consumo** ≥200 % ⇒ severidad alta                     |
| `MediumSeverityRelativeDeviation`| 1,0   | desviación de **consumo** ≥100 % ⇒ severidad media                    |
| `IntensityHighSeverityDeviation` | 1,0   | desviación de **intensidad** ≥100 % ⇒ severidad alta                  |
| `IntensityMediumSeverityDeviation`| 0,7  | desviación de **intensidad** ≥70 % ⇒ severidad media                  |

Las dos últimas existen porque consumo e intensidad tienen **fuentes de variación legítima
distintas**. No es que una desviación sea más grave en una magnitud que en otra.

El consumo se mueve con la actividad real: producción, ocupación, temperatura, calendario
laboral. Duplicarlo puede ser perfectamente normal —el Escenario A es exactamente eso—, así
que hace falta una desviación grande antes de sospechar del dato. La intensidad solo se
mueve si cambia el mix de la red o la comercializadora, y eso ocurre pocas veces y de forma
gradual.

De ahí que la misma desviación relativa pese más como evidencia en intensidad que en
consumo: en intensidad quedan muchas menos explicaciones inocentes que descartar antes de
llegar a "el dato está mal". Reutilizar los umbrales de RF-03 en RF-04b mezclaría dos
magnitudes que ADR-04 separa a propósito.

**Consecuencia importante y buscada:** `MinimumBaselineSize = 3` con base
*leave-one-out* implica que Barcelona (3 válidos ⇒ base de 2) y Valencia (2
registros ⇒ base de 1) **no reciben análisis estadístico**. Es el resultado
correcto, no una limitación: el id 8 se detecta igualmente por la banda física de
RF-04a, que no necesita histórico, y el resto se declara explícitamente como no
evaluado (RF-06) en lugar de darse por bueno.

---

## 4. Escalado a millones de registros

El diseño actual carga el lote en memoria: correcto a la escala del enunciado,
insuficiente en producción. Por orden de rentabilidad:

1. **Particionar por sede.** Las reglas solo miran registros de la misma sede: el
   problema es *embarrassingly parallel* por clave `site`. Es la palanca grande y
   no obliga a cambiar el motor.
2. **Estadísticos precalculados e incrementales.** Guardar mediana y MAD por sede
   y actualizarlos por ventana en lugar de recalcularlos por registro. Para
   cuantiles aproximados en streaming, t-digest o P². Convierte el análisis en
   O(1) por registro.
3. **Streaming en lugar de materialización.** `IAsyncEnumerable<EmissionRecord>` y
   `System.Text.Json` en modo streaming: memoria constante.
4. **Separar ingesta de detección.** Cola (Service Bus / Kafka) entre ambas, con el
   detector como worker escalable horizontalmente e idempotente por `id`.
5. **Empujar el filtro al almacén.** RF-01 y RF-02 son SQL puro; ejecutarlos en el
   motor de datos evita mover registros que se van a descartar.
6. **Medir antes de optimizar.** BenchmarkDotNet sobre el motor antes de reescribir
   nada: la intuición sobre dónde está el coste suele fallar.

---

## 5. Estrategia de pruebas

- **Unitarias** por regla y sobre `RobustStatistics`, con valores calculados a mano
  (tabla de valores dorados en `003-contratos.md`).
- **De aceptación** sobre el dataset del enunciado, registro a registro contra la
  tabla de `001-especificacion.md` §6.
- **De regresión de negocio**, los que protegen el criterio:
  - el id 4 dispara RF-03 y **no** RF-04;
  - los ids 1–3 no se disparan pese a convivir con el id 4 — este test falla si
    alguien sustituye la mediana por la media, que es precisamente el error que
    este diseño existe para evitar;
  - los ids 9–10 producen nota de histórico insuficiente y no marca.
- Cada test nombra su requisito, de modo que la trazabilidad spec → test se lee
  directamente en la salida del runner.
