# 005 — Escenarios A y B (borrador)

Base para `ESCENARIOS.md`. **Léelo, discútelo y reescríbelo con tus palabras**:
en la entrevista te van a preguntar por esto y tiene que sonar tuyo.

---

## Escenario A · La fábrica de Madrid se amplió en mayo

> Entra `{ "site": "Madrid", "month": "2026-05", "energyKwh": 25000, "co2Kg": 5900 }`,
> el sistema lo marca, y después el cliente explica que ampliaron la fábrica.

### Lo primero: el sistema no se equivocó

25.000 kWh son el doble de la línea base de Madrid. Estadísticamente **es** una
anomalía, y marcarla fue el comportamiento correcto. Lo que falló no es la
detección, es que no había forma de que el sistema supiera algo que solo existía
en la cabeza del cliente. Bajar el umbral para que este caso no salte sería
resolver el síntoma rompiendo la herramienta: el mismo umbral que deja pasar un
+100 % legítimo deja pasar un error de unidades.

**Así que no, no cambiaría el algoritmo.** Cambiaría lo que el algoritmo sabe.

### Y hay una pista en los propios datos

La intensidad de carbono de ese registro es 5.900/25.000 = **0,236 kg/kWh**, casi
idéntica a la histórica de Madrid (0,232). Es decir: **salta RF-03 pero no salta
RF-04**. Ese patrón es precisamente la firma del crecimiento real —consumir más
manteniendo el mismo factor de emisión— frente a la del dato corrupto, que rompe
la relación. Por eso volumen e intensidad son señales separadas (ADR-04): la
combinación de reglas que disparan ya es información de negocio, y permite bajar
la severidad de este caso de "probable error" a "cambio a confirmar".

### Cómo evitar falsos positivos como este

1. **Registro de eventos de sede.** Una tabla de cambios estructurales (ampliación,
   nueva línea, cierre, cambio de comercializadora) con fecha de efecto. Un evento
   activo hace que el motor **rebaseline**: la normalidad se recalcula desde la
   fecha del evento en lugar de arrastrar el histórico anterior. Es la solución de
   fondo, y encaja sin tocar las reglas porque solo cambia qué entra en la línea
   base.
2. **Realimentación del analista.** Cuando alguien resuelve una alerta como
   "justificada: ampliación de capacidad", eso se guarda. Sirve para dos cosas:
   suprimir alertas repetidas del mismo evento y, con el tiempo, tener datos reales
   sobre la tasa de falsos positivos por regla, que es lo que permite calibrar
   umbrales con evidencia en vez de con intuición.
3. **Normalizar por actividad.** El indicador que de verdad debería vigilarse no es
   kWh absolutos sino **kWh por unidad producida** (o por m², o corregido por
   grados-día). Una línea nueva sube el consumo total y **no** debería mover el
   consumo por unidad. Es la mejora más valiosa a medio plazo y la que exige
   coordinación con el cliente, porque hay que ingerir el dato de producción.
4. **Distinguir pico de escalón.** Un mes alto y vuelta a la normalidad es un
   incidente; tres meses seguidos en el nuevo nivel es una realidad nueva.
   Detección de punto de cambio: si el nivel se sostiene, el sistema debe adoptar
   la nueva normalidad solo y dejar de alertar, no seguir gritando cada mes.
5. **Enrutar, no bloquear.** El registro nunca se descartó (RN-04): quedó marcado a
   revisión. El coste real de este falso positivo fueron cinco minutos de un
   analista, y ese es el diseño funcionando como debe.

### El matiz de negocio

En ESG, un salto del 100 % en emisiones **debería** pasar por revisión humana
aunque sea legítimo, porque es exactamente el tipo de variación que un auditor va
a preguntar. El objetivo no es que este caso deje de aparecer, sino que aparezca
**una vez**, con la explicación adjunta, y no los doce meses siguientes.

---

## Escenario B · Preguntarle a un LLM si cada registro es correcto

> Propuesta del equipo: mandar cada registro a un LLM y preguntarle si es anómalo.

### Por qué esa propuesta concreta no

Tal cual está planteada tiene cuatro problemas, y el cuarto es el grave:

- **No determinista.** La misma entrada puede dar veredictos distintos. Un informe
  ESG que no se puede reproducir no se puede auditar (RN-06).
- **Ciego al contexto que importa.** El LLM no ve el histórico de la sede, y sin él
  no hay forma de saber si 25.000 kWh es mucho: depende de si la sede es Madrid o
  Valencia.
- **Caro y lento** por registro, cuando el criterio es aritmética que se resuelve
  en microsegundos.
- **Malo en lo que se le pide.** Comparar magnitudes numéricas contra una
  distribución es justo el tipo de tarea donde un modelo de lenguaje es menos
  fiable, y donde el error es silencioso: devuelve una respuesta segura y
  plausible que resulta ser falsa.

### Qué resolvería con código y reglas

**Toda la decisión.** Validación, estadística, umbrales, severidad. Es
determinista, auditable, gratis y reproducible con una calculadora a partir de la
evidencia que acompaña a cada marca (ADR-07). Un auditor puede exigir que se
justifique por qué un mes concreto salió del informe, y esa justificación no
puede ser "lo dijo el modelo".

### Dónde sí usaría un LLM

En la capa de **interpretación**, siempre *después* de la detección y nunca
decidiendo:

1. **Explicación para el analista.** Convertir hallazgos y evidencia numérica en
   una narrativa breve: qué pasó, contra qué se comparó, qué hipótesis mirar
   primero. Reduce el tiempo de revisión sin tocar el veredicto.
2. **Enriquecimiento con contexto no estructurado** — el uso de más valor. Correos
   del cliente, partes de incidencia, órdenes de trabajo, notas de mantenimiento.
   El Escenario A es exactamente esto: la información que faltaba ("ampliamos la
   fábrica") existía en texto libre. Un LLM que cruce la alerta con esas fuentes y
   proponga *"posible causa: ampliación comunicada el 3 de mayo"* ataca la raíz del
   falso positivo.
3. **Extracción estructurada** de facturas y PDF de suministro hacia el esquema de
   registros. Aquí el LLM hace lo que sabe hacer —leer documentos desordenados— y
   su salida entra por la puerta de la validación como cualquier otro dato.
4. **Clasificar las resoluciones** de los analistas en categorías, para tener
   estadísticas de falsos positivos por regla y calibrar umbrales con datos.
5. **Sugerir ajustes de umbral o reglas nuevas** a partir de patrones históricos,
   siempre como propuesta que un humano aprueba.

**Contexto que le daría:** el registro, el histórico de su sede con los
estadísticos ya calculados, las reglas que dispararon con su evidencia, el
catálogo de eventos de la sede, casos similares resueltos y su desenlace, y las
unidades y definiciones. Nunca la pregunta cruda "¿esto es anómalo?" sobre un
registro suelto.

### Cómo impido que una respuesta incorrecta afecte al reporting ESG

La barrera es **arquitectónica, no de prompt**:

1. **El LLM no tiene voz en `requiresReview` ni en `severity`.** Esos campos los
   produce el motor determinista. La salida del LLM viaja en un campo aparte,
   claramente etiquetado como sugerencia. Un modelo que solo puede escribir en un
   campo de texto no puede corromper un informe.
2. **Sin escritura sobre el dato.** El LLM nunca corrige ni imputa valores. El
   registro que llega al reporting es el original.
3. **Salida estructurada y validada** contra esquema, con `temperature` baja. Lo que
   no valida, se descarta: se prefiere no dar sugerencia a dar una mala.
4. **Toda cifra que mencione, recalculada en código** antes de mostrarla. Si el
   texto no cuadra con la evidencia, se descarta el texto.
5. **Humano en el bucle** para cualquier cosa que altere el informe. La sugerencia
   informa la decisión; no la sustituye (RN-04).
6. **Trazabilidad completa:** prompt, respuesta, versión de modelo y timestamp
   guardados. Si mañana hay que auditar una revisión, tiene que poder reconstruirse
   qué se le enseñó al modelo y qué contestó.
7. **Degradación limpia.** Si el proveedor falla o tarda, el sistema sigue
   funcionando sin sugerencias. La detección nunca depende de una llamada externa.
8. **Evaluación con conjunto fijo.** Un set de casos etiquetados que se pasa en cada
   cambio de modelo o de prompt, tratado como cualquier otro test de regresión.

### En una frase

**El LLM explica y contextualiza; nunca decide.** La decisión que acaba en un
informe auditable tiene que poder reproducirse con una calculadora.
