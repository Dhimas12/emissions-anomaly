# Cómo usar este paquete con Claude Code

## Preparación

```bash
mkdir emissions-anomaly && cd emissions-anomaly
git init
# copia aquí CLAUDE.md y la carpeta specs/
claude
```

## Prompt de arranque

Pégalo tal cual en la primera sesión:

> Lee `CLAUDE.md` y los cuatro documentos de `specs/` completos antes de escribir
> nada. Cuando los tengas, resume en diez líneas qué vas a construir y señala
> cualquier ambigüedad o contradicción que encuentres — no empieces a implementar
> hasta que yo te confirme.
>
> Después ejecuta `specs/004-tareas.md` en orden, una tarea por vez. Al terminar
> cada una: compila, pasa los tests, marca la casilla en el fichero de tareas y
> párate a que yo revise antes de seguir con la siguiente.
>
> Los valores esperados de los tests están calculados a mano en `specs/003-contratos.md`
> §6. Si un test falla, el código está mal o la spec está mal: dímelo. No ajustes
> un valor esperado para que pase.

## Ritmo recomendado

Un commit por tarea, con el identificador del requisito en el mensaje:

```
git commit -m "T8: ConsumptionDeviationRule (RF-03, RN-02, RN-03)"
```

Sirve para dos cosas: si algo se tuerce, revertir es barato; y en el vídeo puedes
enseñar el historial como prueba de que hubo un método detrás, no una sesión de
generación a ciegas.

## Puntos donde conviene que revises con calma

Estos son los que te van a preguntar en la entrevista. Léelos línea a línea:

- `RobustStatistics.cs` — tienes que saber explicar qué es el MAD, de dónde sale
  el 0,6745 y por qué la media no vale aquí. Los números están en `002` §ADR-01.
- `ConsumptionDeviationRule.cs` — la doble condición (ADR-02). Es la decisión
  técnica más defendible de la entrega y buen candidato para el minuto del vídeo
  que piden sobre "una decisión técnica que hayas tomado".
- `AnomalyDetectionEngine.cs` — por qué la validación va antes que las líneas base.
- El test `RF04b_IdCuatroEscalaConsumoYEmisiones_NoSeMarca` — es el puente entre el
  código y la respuesta del Escenario A.

## Antes de entregar

- [ ] `dotnet build` sin avisos y `dotnet test` en verde.
- [ ] `GET /api/v1/analysis/sample` reproduce la tabla de `001` §6.
- [ ] `README.md` escrito y breve.
- [ ] `ESCENARIOS.md` reescrito **con tus palabras** a partir de `005`.
- [ ] Las specs se entregan dentro del ZIP: son parte del trabajo, no andamiaje.
      Que se vea que hubo especificación antes que código es una señal de seniority
      que casi nadie va a enseñar.
- [ ] Vídeo de 5 minutos. Guion sugerido: 45 s de planteamiento y arquitectura ·
      90 s de criterio de detección (con la tabla media/σ frente a mediana/MAD en
      pantalla, es lo que más impresiona) · 45 s de la decisión técnica de la doble
      condición · 60 s de escalado a millones · 45 s de dónde sí y dónde no usarías
      IA · cierre.
- [ ] Todo en un único ZIP.

## Aviso

El enunciado dice que puedes usar asistentes de IA, pero que debes poder
**explicar, modificar y defender** todo el código, y que en la entrevista pueden
pedirte cambios en vivo sobre tu propia solución. Si hay una línea que no
entiendes, pídele a Claude Code que te la explique o reescríbela hasta que la
entiendas. Ese es el filtro real de la prueba.
