# CLAUDE.md — Constitución del proyecto

Contexto permanente para Claude Code. Se lee en cada sesión y prevalece sobre
cualquier costumbre general de estilo.

## Qué es esto

Detector de anomalías en registros de consumo energético y emisiones de CO₂ para
una plataforma SaaS de sostenibilidad. Prueba técnica de perfil Senior .NET.
Stack: **.NET 8 / C# 12**, Minimal API. Sin frontend.

## Método: Spec Driven Development

**La especificación es la fuente de verdad. El código es su consecuencia.**

- Los documentos de `specs/` se leen **antes** de escribir cualquier código.
- Si al implementar aparece una ambigüedad o una contradicción en las specs,
  **no se improvisa**: se para, se señala el punto exacto y se pregunta. Una
  decisión de criterio tomada en silencio dentro del código es un defecto,
  aunque el código funcione.
- Si una decisión de implementación se aparta de lo especificado, se actualiza
  primero la spec y luego el código. Nunca al revés.
- Todo requisito (`RF-xx`, `RN-xx`, `ADR-xx`) es trazable: aparece en un comentario
  de la clase que lo implementa y en el nombre del test que lo cubre.

## Orden de trabajo obligatorio

Las tareas de `specs/004-tareas.md` se ejecutan **en orden**, y cada una se
cierra completa (código + test verde) antes de empezar la siguiente. No se
adelanta trabajo de tareas posteriores.

Cada tarea vive en su propia rama y se integra por pull request, según
`specs/006-ci-y-flujo-de-ramas.md`. **Nunca se empuja directamente a `main`.**

## Reglas de código

- `Nullable` y `TreatWarningsAsErrors` activados en todos los proyectos de `src/`.
- Sin dependencias externas más allá de las declaradas en `specs/003-contratos.md`.
  En particular: **sin librerías de ML, sin llamadas a LLM en el camino de
  decisión**. El motivo está en ADR-07 y es la respuesta al Escenario B.
- Sin números mágicos. Todo umbral vive en `AnomalyDetectionOptions` (RN-07).
- `record` para modelos inmutables; clases selladas por defecto.
- Comentarios: solo donde explican **por qué**, nunca **qué**. Un comentario que
  parafrasea la línea siguiente se borra.
- Los mensajes de anomalía van dirigidos a un analista de sostenibilidad, no a un
  desarrollador: deben leerse sin conocer el código.
- Nombres de dominio en inglés (`EmissionRecord`, `RequiresReview`) por coherencia
  con el JSON de entrada; documentación y mensajes de usuario en español.

## Reglas de test

- xUnit. Sin FluentAssertions (cambio de licencia en v8): `Assert` a secas.
- Cada test nombra el requisito que cubre: `RF03_ConsumoMuySuperiorALaBase_SeMarca`.
- Los valores esperados de los tests están **calculados a mano en las specs**
  (`specs/003-contratos.md`, tabla de valores dorados). Se usan esos valores.
  **Prohibido ajustar un valor esperado para que un test pase**: si un test falla,
  o el código está mal o la spec está mal, y en el segundo caso se avisa.

## Qué NO hacer

- No añadir persistencia, autenticación ni frontend a la solución. Están fuera de
  alcance y engordan la entrega sin sumar puntos.
- La integración continua **sí** está en alcance y tiene su propia especificación:
  `specs/006-ci-y-flujo-de-ramas.md`. No inventes workflows fuera de lo que ese
  documento define.
- El `Dockerfile` y el `docker-compose.yml` de la raíz son **entorno de desarrollo**,
  no parte de la solución: no los modifiques ni generes artefactos que dependan de
  ellos (healthchecks, perfiles de publicación, etc.).
- No introducir estacionalidad, tendencia ni detección multivariante. Con 4 puntos
  por sede no hay datos para sostenerlas; se documentan como evolución futura.
- No “mejorar” el criterio de detección por iniciativa propia. El criterio es
  producto, está especificado y hay que poder defenderlo en una entrevista.

## Criterio de terminado

`dotnet build` sin avisos, `dotnet test` todo verde, y la tabla de aceptación de
`specs/001-especificacion.md` §6 reproducida exactamente por
`GET /api/v1/analysis/sample`.