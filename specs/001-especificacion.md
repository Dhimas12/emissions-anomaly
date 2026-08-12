# 001 — Especificación funcional

**Fase SDD:** 1 de 4 — QUÉ y POR QUÉ · **Estado:** aprobada

Define el comportamiento esperado sin decidir la implementación. Cada requisito
tiene identificador estable, referenciado después en el plan, en el código y en
el nombre de cada test. **Si el código y este documento discrepan, gana este
documento.**

---

## 1. Problema

Una plataforma SaaS de sostenibilidad ingiere lecturas mensuales de consumo
energético y emisiones de CO₂ por sede. Antes de que entren en el reporting ESG
hay que apartar automáticamente las sospechosas para revisión humana.

El coste de los dos errores posibles **no es simétrico**:

- **Falso negativo** (dejar pasar un dato corrupto) → contamina un informe ESG
  auditable o publicable. Coste alto, potencialmente regulatorio.
- **Falso positivo** (marcar un dato correcto) → consume tiempo de un analista.
  Coste bajo, pero si es sistemático el equipo deja de mirar las alertas y el
  sistema se vuelve decorativo.

De ahí la decisión de producto que atraviesa toda la especificación: **el sistema
no corrige ni descarta datos, solo los enruta a revisión** con una explicación
auditable. La decisión final siempre es humana (RN-04).

## 2. Alcance

**Dentro:** análisis de un lote de registros, detección, severidad, motivo legible
y evidencia numérica; exposición vía Minimal API.

**Fuera:** persistencia, autenticación, frontend, corrección automática, ingesta
desde fuentes externas, modelos de ML entrenados.

## 3. Datos de entrada

| Campo       | Tipo   | Notas                                     |
|-------------|--------|-------------------------------------------|
| `id`        | entero | identificador del registro                |
| `site`      | texto  | sede                                      |
| `month`     | texto  | periodo, formato `yyyy-MM`                |
| `energyKwh` | número | consumo eléctrico del mes, kWh            |
| `co2Kg`     | número | emisiones asociadas del mes, kg de CO₂    |

Cualquier campo puede llegar ausente o nulo: eso es precisamente una de las
condiciones a detectar (RF-01), no un error de parseo que deba reventar.

---

## 4. Requisitos funcionales

### RF-01 · Validación estructural (valores imposibles o inválidos)

Severidad **siempre `High`**. Se marca si se cumple cualquiera de:

| Código                     | Condición                                             | Justificación                                    |
|----------------------------|-------------------------------------------------------|--------------------------------------------------|
| `MISSING_FIELD`            | `site`, `month`, `energyKwh` o `co2Kg` nulo o vacío   | sin el dato no hay análisis posible              |
| `NEGATIVE_ENERGY`          | `energyKwh < 0`                                       | un consumo negativo no existe físicamente        |
| `NEGATIVE_CO2`             | `co2Kg < 0`                                           | una emisión negativa no existe físicamente       |
| `INVALID_PERIOD`           | `month` no cumple `yyyy-MM` con mes entre 01 y 12     | rompe orden temporal y agregación                |
| `EMISSIONS_WITHOUT_ENERGY` | `energyKwh == 0` y `co2Kg > 0`                        | emitir sin consumir es contradictorio            |
| `NON_FINITE`               | valor `NaN` o infinito                                | corrupción en origen o división previa por cero  |

Un registro que falla RF-01 queda **excluido de las líneas base de su sede**
(RN-01): un dato corrupto no puede definir la normalidad.

### RF-02 · Duplicidad de periodo

Si existe más de un registro para la misma pareja (`site`, `month`), **todos**
ellos se marcan con severidad `High`. Duplicar un periodo implica doble conteo en
el agregado anual, que es el número que se publica.

No dispara en el dataset de la prueba. Se implementa igualmente: es el fallo de
ingesta más común en la vida real y su ausencia sería una laguna evidente.

### RF-03 · Desviación anómala del consumo de una sede

Se compara `energyKwh` contra el histórico **de su propia sede**, nunca contra la
media global: 12.000 kWh es normal en Madrid y altísimo en Valencia.

Se marca si la desviación es **estadísticamente extrema Y materialmente
relevante** — las dos condiciones a la vez (RN-03). Estadística robusta y base
*leave-one-out* (RN-01, RN-02).

### RF-04 · Relación sospechosa entre energía y CO₂

Magnitud analizada: **intensidad de carbono** = `co2Kg / energyKwh`, en kg CO₂/kWh.
Es el factor de emisión implícito de la sede: comparable entre sedes de distinto
tamaño y estable mientras no cambie el mix energético.

- **RF-04a · Banda física.** La intensidad debe caer en un rango plausible para
  consumo eléctrico de red. Fuera → `High`. **No necesita histórico**, por lo que
  protege también a sedes nuevas (RF-06).
- **RF-04b · Coherencia con la propia sede.** La intensidad debe parecerse a la
  intensidad histórica de esa sede.

### RF-05 · Salida por registro

Contrato mínimo exigido por el enunciado:

```json
{ "id": 4, "requiresReview": true, "reason": "...", "severity": "High" }
```

Se añaden `site`, `month`, el detalle de **todas** las reglas evaluadas con su
evidencia numérica (RN-05) y las notas de reglas no evaluadas (RF-06).

- `requiresReview` = verdadero si al menos una regla detectó anomalía.
- `severity` = máxima entre las reglas que dispararon.
- `reason` = mensaje de la regla de mayor severidad; a igualdad gana la de menor
  prioridad numérica, de modo que la validación se explica antes que la
  estadística.

### RF-06 · Histórico insuficiente (arranque en frío)

Cuando una sede no tiene suficientes registros válidos para inferir su
normalidad, las reglas estadísticas **no se evalúan** y así se declara
explícitamente en la salida.

El registro no se marca como anómalo por esa vía, pero **tampoco se afirma que
sea correcto**: hay que distinguir "comprobado y correcto" de "no comprobable".
Sin esa distinción, ambos casos comparten el mismo silencio, y ese silencio es la
fuente habitual de falsos negativos en producción.

Las reglas que no dependen de histórico (RF-01, RF-02, RF-04a) siguen aplicando
siempre.

### RF-07 · API

| Método | Ruta                        | Descripción                                    |
|--------|-----------------------------|------------------------------------------------|
| POST   | `/api/v1/analysis`          | analiza el lote del cuerpo                     |
| GET    | `/api/v1/analysis/sample`   | analiza el dataset del enunciado               |
| GET    | `/health`                   | sonda de vida                                  |
| GET    | `/`                         | Swagger UI                                     |

`POST` admite `?onlyFlagged=true` para devolver solo los registros marcados.

---

## 5. Reglas de negocio transversales

| Id     | Regla |
|--------|-------|
| RN-01  | **Línea base limpia.** Las líneas base se calculan solo con registros que superan RF-01. |
| RN-02  | **Leave-one-out.** Un registro nunca participa en la línea base contra la que se le compara. En su ausencia, un valor extremo se auto-justifica y se enmascara. |
| RN-03  | **Doble condición.** Una desviación estadísticamente extrema pero materialmente pequeña no se marca. |
| RN-04  | **El sistema no decide, enruta.** Nunca corrige, borra ni bloquea un dato; solo lo etiqueta. |
| RN-05  | **Auditabilidad.** Toda marca lleva la evidencia numérica que la produjo. Un auditor ESG debe poder reproducir la decisión a mano. |
| RN-06  | **Determinismo.** Misma entrada y misma configuración ⇒ misma salida. Sin aleatoriedad, sin servicios externos, sin dependencia del reloj en el resultado. |
| RN-07  | **Umbrales configurables.** Ningún umbral incrustado en código; todos son configuración versionable y auditable. |

---

## 6. Criterios de aceptación sobre el dataset del enunciado

Definición operativa de "funciona". `GET /api/v1/analysis/sample` debe producir
exactamente esto:

| Id | Sede      | Resultado   | Regla   | Severidad | Motivo                                                        |
|----|-----------|-------------|---------|-----------|---------------------------------------------------------------|
| 1  | Madrid    | no revisar  | —       | —         | dentro de su normalidad                                       |
| 2  | Madrid    | no revisar  | —       | —         | ídem                                                          |
| 3  | Madrid    | no revisar  | —       | —         | ídem                                                          |
| 4  | Madrid    | **revisar** | RF-03   | High      | consumo 6,3× su línea base                                    |
| 5  | Barcelona | no revisar  | —       | —         | valores e intensidad correctos                                |
| 6  | Barcelona | no revisar  | —       | —         | ídem                                                          |
| 7  | Barcelona | **revisar** | RF-01   | High      | energía y CO₂ negativos                                       |
| 8  | Barcelona | **revisar** | RF-04a  | High      | intensidad 0,955 kg/kWh, fuera de banda física                |
| 9  | Valencia  | no revisar  | —       | —         | histórico insuficiente, declarado explícitamente              |
| 10 | Valencia  | no revisar  | —       | —         | ídem                                                          |

Resumen esperado: 10 registros, 3 a revisión, 3 de severidad alta.

### Los dos casos que definen el diseño

**El id 4 escala consumo y emisiones a la vez.** Su intensidad (18.200/79.000 =
0,2304) coincide con la histórica de Madrid (0,2320). Debe disparar **solo RF-03,
nunca RF-04**. Volumen e intensidad tienen que ser señales independientes, porque
son exactamente lo que distingue "creció de verdad" de "el dato está mal".

**Los ids 1, 2 y 3 conviven con el valor extremo del id 4 en la misma sede.** Con
media y desviación típica sobre `[12000, 12500, 12800, 79000]`: media 29.075, σ
28.826, y el valor extremo queda en **z = +1,73** — por debajo del umbral
habitual de 3, es decir **no se detecta**, mientras los tres valores sanos quedan
a media σ de una media que no representa a ninguno. Con n = 4, la estadística
clásica no sobrevive a un solo atípico. Este caso es la justificación empírica de
ADR-01 y debe existir como test de regresión.

## 7. Fuera de alcance consciente

No se implementa estacionalidad, tendencia ni detección multivariante. Con cuatro
puntos por sede no hay información para estimarlas, y una técnica que no se puede
justificar con los datos disponibles es ruido disfrazado de rigor. Se documenta
como evolución en el README.
