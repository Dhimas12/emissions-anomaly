# Escenarios

Respuestas a los dos escenarios del enunciado, apoyadas en el sistema que hay en este
repositorio.

---

## Escenario A · La fábrica de Madrid se amplió en mayo

> Entra `{ "site": "Madrid", "month": "2026-05", "energyKwh": 25000, "co2Kg": 5900 }`, el
> sistema lo marca, y después el cliente explica que ampliaron la fábrica.

### Lo primero: el sistema no se equivocó

25.000 kWh son el doble de la línea base de Madrid. Estadísticamente **es** una anomalía y
marcarla fue correcto. Lo que falló no es la detección, sino que no había forma de que el
sistema supiera algo que solo existía en la cabeza del cliente.

Bajar el umbral para que este caso no salte sería resolver el síntoma rompiendo la
herramienta: el mismo umbral que deja pasar un +100 % legítimo deja pasar un error de
unidades. **No cambiaría el algoritmo. Cambiaría lo que el algoritmo sabe.**

### Y los datos ya traen la pista

Pasando ese registro por la API, junto a los tres meses sanos de Madrid:

| Regla | Resultado | Evidencia |
|---|---|---|
| RF-03 · consumo | **Anomaly** | mediana 12.500 · MAD 300 · z = 28,10 · desviación **+100 %** |
| RF-04a · banda física | Passed | intensidad 0,236, dentro de 0,05–0,80 |
| RF-04b · histórico de la sede | **Passed** | 0,236 frente a una mediana de 0,232 · desviación **+1,7 %** |

La intensidad de carbono es `5.900 / 25.000 = 0,236 kg CO₂/kWh`, frente a los 0,232
históricos de Madrid: un 1,7 % de diferencia contra una tolerancia del 40 %.

**Salta RF-03 y no salta RF-04, y esa combinación es la firma del crecimiento real.** Una
sede que produce más consume más manteniendo su factor de emisión; un dato corrupto rompe la
relación. Por eso volumen e intensidad son señales separadas y nunca se combinan en una
puntuación única (ADR-04): la combinación de reglas que disparan **ya es información de
negocio**. El test `CarbonIntensityRuleTests.RF04b_IdCuatroEscalaConsumoYEmisiones_NoSeMarca`
es exactamente este razonamiento aplicado al id 4 del dataset.

Consecuencia práctica inmediata: el sistema no clasifica este caso como grave. Le asigna
severidad **Medium**, no `High`, porque la desviación es del 100 % y no del 200 %. En la
bandeja del analista no aparece como "probable error" sino como "cambio a confirmar", que es
lo que es.

### Cómo evitar falsos positivos como este

1. **Registro de eventos de sede.** Una tabla de cambios estructurales —ampliación, nueva
   línea, cierre, cambio de comercializadora— con fecha de efecto. Un evento activo hace que
   el motor **rebaseline**: la normalidad se recalcula desde esa fecha en lugar de arrastrar
   el histórico anterior. Es la solución de fondo y encaja sin tocar ninguna regla, porque
   solo cambia qué entra en la línea base.
2. **Realimentación del analista.** Cuando alguien resuelve una alerta como "justificada:
   ampliación de capacidad", eso se guarda. Sirve para suprimir alertas repetidas del mismo
   evento y, con el tiempo, para tener tasas reales de falso positivo por regla con las que
   calibrar umbrales con evidencia en vez de con intuición.
3. **Normalizar por actividad.** El indicador que de verdad debería vigilarse no son kWh
   absolutos sino **kWh por unidad producida** —o por m², o corregidos por grados-día—. Una
   línea nueva sube el consumo total y no debería mover el consumo por unidad. Es la mejora
   más valiosa a medio plazo y la que exige coordinación con el cliente, porque hay que
   ingerir el dato de producción.
4. **Distinguir pico de escalón.** Un mes alto y vuelta a la normalidad es un incidente;
   tres meses seguidos en el nuevo nivel es una realidad nueva. Con detección de punto de
   cambio, el sistema adopta la nueva normalidad solo y deja de alertar.

### El matiz de negocio

En reporting ESG, un salto del 100 % **debería** pasar por revisión humana aunque sea
legítimo: es exactamente el tipo de variación por la que pregunta un auditor. El objetivo no
es que este caso deje de aparecer, sino que aparezca **una vez**, con la explicación
adjunta, y no los doce meses siguientes. El registro nunca se descartó: quedó marcado a
revisión (RN-04), y el coste real de este falso positivo fueron cinco minutos de un analista.

---

## Escenario B · Preguntarle a un LLM si cada registro es correcto

> Propuesta del equipo: mandar cada registro a un LLM y preguntarle si es anómalo.

### La respuesta corta

**El LLM no puede escribir en `requiresReview` ni en `severity`.** Esos dos campos los
produce el motor determinista, y ningún modelo tiene acceso a ellos. La salida del LLM viaja
en un campo aparte, etiquetado como sugerencia.

Esa es la barrera, y es **arquitectónica, no de prompt**. Un modelo que solo puede escribir
en un campo de texto no puede corromper un informe, por mal que responda y por mucho que
cambie de versión. Todo lo demás —el contexto que se le da, la validación de esquema, el
humano en el bucle— es consecuencia de esa decisión, no sustituto de ella.

### Por qué la propuesta tal cual está planteada no

- **No determinista.** La misma entrada puede dar veredictos distintos. Un informe ESG que
  no se puede reproducir no se puede auditar (RN-06).
- **Ciego al contexto que importa.** El LLM no ve el histórico de la sede, y sin él no hay
  forma de saber si 25.000 kWh es mucho: depende de si la sede es Madrid o Valencia.
- **Caro y lento** por registro, cuando el criterio es aritmética que se resuelve en
  microsegundos.
- **Malo justo en lo que se le pide.** Comparar magnitudes contra una distribución es el
  tipo de tarea donde un modelo de lenguaje es menos fiable, y donde el error es silencioso:
  devuelve una respuesta segura y plausible que resulta ser falsa.

### Qué resuelvo con código y reglas

**Toda la decisión.** Validación, estadística, umbrales y severidad. Es determinista,
gratuito y reproducible con una calculadora a partir de la evidencia que acompaña a cada
marca (ADR-07). Un auditor puede exigir que se justifique por qué un mes concreto salió del
informe, y esa justificación no puede ser "lo dijo el modelo".

### Dónde sí usaría un LLM

Siempre **después** de la detección y nunca decidiendo:

1. **Explicar el hallazgo al analista.** Convertir evidencia numérica en una narrativa breve:
   qué pasó, contra qué se comparó, qué hipótesis mirar primero. Reduce el tiempo de revisión
   sin tocar el veredicto.
2. **Cruzar la alerta con contexto no estructurado** — correos, partes de incidencia, órdenes
   de trabajo. Es el uso de más valor: el Escenario A es exactamente esto, porque la
   información que faltaba ("ampliamos la fábrica") existía en texto libre. Una sugerencia
   del tipo *"posible causa: ampliación comunicada el 3 de mayo"* ataca la raíz del falso
   positivo.
3. **Extraer datos de facturas y PDF** hacia el esquema de registros. Ahí el LLM hace lo que
   sabe hacer —leer documentos desordenados— y su salida entra por la puerta de la validación
   como cualquier otro dato.
4. **Clasificar las resoluciones de los analistas** para tener estadísticas de falso positivo
   por regla y calibrar umbrales con datos.

### Cómo impido que una respuesta incorrecta afecte al reporting

Además de la barrera principal:

- **Sin escritura sobre el dato.** El LLM nunca corrige ni imputa valores; el registro que
  llega al reporting es el original.
- **Salida estructurada y validada** contra esquema, con temperatura baja. Lo que no valida
  se descarta: se prefiere no dar sugerencia a dar una mala.
- **Toda cifra que mencione, recalculada en código** antes de mostrarla. Si el texto no
  cuadra con la evidencia, se descarta el texto.
- **Trazabilidad completa:** prompt, respuesta, versión de modelo y timestamp. Si mañana hay
  que auditar una revisión, tiene que poder reconstruirse qué se le enseñó al modelo.
- **Degradación limpia.** Si el proveedor falla o tarda, el sistema sigue funcionando sin
  sugerencias. La detección nunca depende de una llamada externa.
- **Conjunto de evaluación fijo** que se pasa en cada cambio de modelo o de prompt, tratado
  como cualquier otro test de regresión.

### En una frase

**El LLM explica y contextualiza; nunca decide.** La decisión que acaba en un informe
auditable tiene que poder reproducirse con una calculadora.
