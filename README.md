# Detector de anomalías en consumo energético y emisiones de CO₂

Analiza lecturas mensuales de energía y CO₂ por sede y aparta las sospechosas para revisión
humana, con una explicación auditable. **El sistema no corrige ni descarta datos: los
enruta.** La decisión final siempre es de una persona.

.NET 8 · Minimal API · sin ML y sin llamadas externas en el camino de decisión.

---

## Cómo ejecutarlo

```bash
dotnet build                                  # 0 avisos (TreatWarningsAsErrors)
dotnet test                                   # 156 tests
dotnet run --project src/Emissions.Api

curl -s localhost:5080/api/v1/analysis/sample | jq '.summary'
# { "totalRecords": 10, "recordsRequiringReview": 3, "highSeverity": 3, ... }
```

Swagger UI en `http://localhost:5080/`, sonda en `/health`, y `POST /api/v1/analysis`
acepta `?onlyFlagged=true`.

> Si el puerto 5080 devuelve `Empty reply from server`, el contenedor de `docker-compose`
> está levantado y lo tiene publicado: los `curl` llegan a su proxy, no a Kestrel. Bájalo o
> usa `--urls` con otro puerto.

`Dockerfile` y `docker-compose.yml` son **entorno de desarrollo**, no parte de la solución.

---

## El criterio de detección

Cinco comprobaciones independientes que devuelven `Passed`, `Anomaly` o `NotEvaluated`. El
registro se marca si alguna encuentra anomalía.

**RF-01 · Validación estructural.** Campos ausentes, consumo o emisiones negativos, periodo
mal formado, emitir sin consumir, valores no finitos. Siempre severidad alta: no hay grados
en "este dato no puede ser cierto". Además queda **excluido de las líneas base de su sede**:
un dato corrupto no puede definir la normalidad.

**RF-02 · Duplicidad de periodo.** Si una sede repite mes, se marcan **todos** los
implicados: desde fuera no se sabe cuál es el bueno, y elegir uno sería tomar la decisión
que le corresponde al analista. Un periodo repetido se suma dos veces al agregado anual.

**RF-03 · Desviación de consumo.** Contra el histórico **de su propia sede**, nunca contra
la media global: 12.000 kWh es normal en Madrid y altísimo en Valencia. Se marca solo si la
desviación es **estadísticamente extrema y materialmente relevante** — `|z| > 3,5` **y**
desviación > 25 %. El z-score responde a "¿es raro?" y la desviación relativa a "¿importa?".
Un sistema que alerta por un 20 % mensual se desactiva solo: el analista deja de mirarlo en
dos semanas y los falsos negativos vuelven por la puerta de atrás.

**RF-04a · Banda física de intensidad.** `co2Kg / energyKwh` entre 0,05 y 0,80 kg CO₂/kWh.
**No necesita histórico**, así que protege a las sedes recién dadas de alta, y es lo único
que detecta el id 8.

**RF-04b · Coherencia con el histórico de la sede**, con un 40 % de tolerancia. Sus umbrales
de severidad son más bajos que los de consumo porque las dos magnitudes tienen **fuentes de
variación legítima distintas**: el consumo se mueve con la actividad real —producción,
ocupación, temperatura, calendario—, la intensidad solo si cambia el mix de la red. La misma
desviación pesa más como evidencia en intensidad porque quedan menos explicaciones inocentes
que descartar.

Las líneas base usan **solo registros válidos**, y un registro **nunca participa en la base
contra la que se le compara**: con n pequeño, incluirse es auto-justificarse.

### Volumen e intensidad son señales separadas

Nunca se combinan en una puntuación única, y ahí está la utilidad para el negocio:

| Patrón | Interpretación |
|---|---|
| Volumen anómalo, intensidad normal | la sede consumió más de verdad |
| Volumen normal, intensidad anómala | el dato está mal o cambió el mix |
| Ambos anómalos | error de unidades o de origen |

Un score agregado colapsaría los tres casos en un número y destruiría lo que el analista
necesita para decidir.

---

## Por qué mediana y MAD, y no media y σ

Es la decisión que hace que el ejercicio se resuelva, y se demuestra con los datos del
propio enunciado. Serie de Madrid: `[12000, 12500, 12800, 79000]`.

| Serie | media / σ | z clásico | mediana / MAD (LOO) | z modificado |
|---|---|---|---|---|
| 12.000 | μ=29.075 · σ=28.826 | −0,59 | 12.800 / 300 | −1,80 |
| 12.500 | | −0,58 | 12.800 / 800 | −0,25 |
| 12.800 | | −0,56 | 12.500 / 500 | +0,41 |
| **79.000** | | **+1,73** | 12.500 / 300 | **+149,5142** |

Con estadística clásica el atípico queda en **1,73 σ, por debajo del umbral habitual de 3:
no se detecta.** Es el efecto de enmascaramiento, y con n = 4 no es un tecnicismo: es la
diferencia entre resolver el ejercicio y no resolverlo. La media y σ tienen punto de ruptura
1/n —un solo atípico las arrastra, y deja a los tres valores sanos a media σ de una media
que no representa a ninguno—; la mediana lo tiene del 50 %.

El estadístico es el z modificado de Iglewicz–Hoaglin, `0,6745 · (x − mediana) / MAD`, con
umbral 3,5. Está en el código como test ejecutable, no como comentario:
`AcceptanceTests.ADR01_MediaYDesviacionTipicaEnmascararianElAtipico` calcula ambas
estadísticas y comprueba las dos conclusiones.

---

## Resultado sobre el dataset del enunciado

| Id | Sede | Resultado | Regla | Severidad | Motivo |
|----|------|-----------|-------|-----------|--------|
| 1–3 | Madrid | no revisar | — | — | dentro de su normalidad |
| 4 | Madrid | **revisar** | RF-03 | High | consumo 6,3× su línea base |
| 5–6 | Barcelona | no revisar | — | — | valores e intensidad correctos |
| 7 | Barcelona | **revisar** | RF-01 | High | energía y CO₂ negativos |
| 8 | Barcelona | **revisar** | RF-04a | High | intensidad 0,9551 kg/kWh, fuera de banda |
| 9–10 | Valencia | no revisar | — | — | histórico insuficiente, declarado |

**Los dos casos que definen el diseño.** El id 4 escala consumo y emisiones a la vez: su
intensidad (0,2304) sigue siendo la de Madrid (0,2320), así que dispara RF-03 y **no**
RF-04. El id 8 se detecta por la banda física, porque Barcelona tiene tres registros válidos
y su base de dos no llega al mínimo: sin RF-04a pasaría sin que nadie lo mirase.

**Los ids 9 y 10 no se dan por buenos:** se declara que su histórico es insuficiente. La
diferencia entre "comprobado y correcto" y "no comprobable" es media especificación. Sin
ella ambos casos comparten el mismo silencio, y ese silencio es la fuente habitual de falsos
negativos en producción.

---

## Estrategia de pruebas

156 tests: unitarios por regla, de aceptación registro a registro contra la tabla anterior, y
de regresión sobre las decisiones que hay que poder defender. Los valores esperados están
**calculados a mano en las especificaciones** y no se ajustan nunca para que un test pase.

### Verificación por mutación

Un test verde no demuestra que proteja algo. Antes de dar por buena una garantía se rompe el
código a propósito y se comprueba que el test correspondiente cae. Encontró dos cosas que la
suite entera en verde no mostraba:

**Un mecanismo redundante.** El desempate del motivo por prioridad de regla tenía dos
implementaciones. Quitando una, **los 132 tests seguían pasando**: ninguna prueba podía
distinguir cuál estaba funcionando. Se dejó un solo mecanismo y se añadió el test que
faltaba, que construye el motor con las reglas registradas al revés.

**El alcance real de una garantía.** El test que sostiene la separación entre volumen e
intensidad protege esa separación, pero **no** la base *leave-one-out*: metiendo el id 4 en
su propia base, la mediana apenas se mueve y el test sigue verde. Saberlo evita presentarlo
como algo que no cubre.

También midió la sensibilidad del criterio: subiendo `MinimumBaselineSize` de 3 a 4 caen 35
tests y **el id 4 deja de marcarse**, porque la base de Madrid se queda corta; las otras
nueve filas sobreviven, que es lo correcto. Y detectó un error aritmético en el ejemplo que
justificaba la doble condición, que habría llegado a la entrega como un test en verde
afirmando algo falso.

### Integración continua

Un PR por tarea, con compilación y tests en cada uno. Un segundo job levanta la API y
verifica que los registros marcados son **exactamente** los ids 4, 7 y 8: comprobar solo los
totales dejaría pasar un sistema que marcase tres registros equivocados.

---

## Escalado a millones de registros

El diseño carga el lote en memoria: correcto a esta escala, insuficiente en producción. Por
orden de rentabilidad:

1. **Particionar por sede.** Las reglas solo miran registros de la misma sede: el problema es
   *embarrassingly parallel* por clave `site`. Es la palanca grande y no toca el motor.
2. **Estadísticos precalculados e incrementales** por sede, actualizados por ventana; t-digest
   o P² para cuantiles en streaming. Deja el análisis en O(1) por registro.
3. **Streaming en vez de materialización:** `IAsyncEnumerable` y `System.Text.Json` en modo
   streaming, memoria constante.
4. **Separar ingesta de detección** con una cola entre ambas y el detector como worker
   escalable e idempotente por `id`.
5. **Empujar RF-01 y RF-02 al almacén**, que son SQL puro, para no mover lo que se descarta.
6. **Medir antes de optimizar.**

El motor no sabe que existe una API, así que cambiar el anfitrión por un worker o un job por
lotes no toca una línea de `Emissions.Analysis`.

---

## Limitaciones conocidas

**Sin estacionalidad, tendencia ni detección multivariante.** Con cuatro puntos por sede no
hay información para estimarlas, y una técnica que no se justifica con los datos disponibles
es ruido disfrazado de rigor.

**La banda de intensidad asume red europea.** Una red nórdica (~0,02) o intensiva en carbón
(~0,9) exigen recalibrarla: es configuración por región, no una constante universal.

**Falta observabilidad de la tasa de `NotEvaluated`**, y es la limitación más importante. Un
`MinimumBaselineSize` mal calibrado —500 con doce meses por sede— dejaría las reglas
estadísticas sin evaluar para siempre: el detector no marcaría nada, en silencio y con el CI
en verde. Ningún rango de validación atrapa eso, porque 99 pasaría igual con un techo de 100;
lo atrapa alertar cuando esa proporción supera un umbral en producción.

**No distingue un pico de un escalón.** Tres meses seguidos en el nuevo nivel son una realidad
nueva que el sistema debería adoptar en lugar de alertar cada mes. Requiere detección de punto
de cambio y un registro de eventos de sede.

Fuera de alcance por decisión: persistencia, autenticación y frontend.
