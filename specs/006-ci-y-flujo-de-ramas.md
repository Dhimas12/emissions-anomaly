# 006 — Integración continua y flujo de ramas

**Fase SDD:** anexo · **Deriva de:** `001`, `003`, `004`

Define cómo se valida y se integra el trabajo. Mismo régimen que el resto de
specs: identificadores estables, criterios de aceptación y contratos exactos.

---

## 1. Por qué

Con las tareas de `004-tareas.md` ejecutándose una a una, el riesgo no es que el
código no funcione: es que una tarea rompa en silencio algo que ya funcionaba y
nadie lo note hasta el final. El CI convierte la tabla de aceptación de
`001-especificacion.md` §6 en una barrera automática, y el flujo de PR convierte
cada tarea en una unidad revisable y reversible.

Efecto secundario que importa en una prueba técnica: el historial del repositorio
queda como prueba de método. Un `main` con un PR por tarea, cada uno en verde, es
una señal difícil de fingir.

---

## 2. Requisitos

### RF-CI-01 · Validación en pull request

Todo PR contra `main` dispara compilación y tests. Si cualquiera de los dos
falla, el PR no se mezcla.

### RF-CI-02 · Validación al mezclar

El mismo pipeline se ejecuta sobre `main` después de cada mezcla. La razón no es
redundancia: dos PRs abiertos en paralelo pueden pasar por separado y romperse al
combinarse (*semantic merge conflict*). Validar `main` es lo único que detecta eso.

### RF-CI-03 · Gate de criterios de aceptación

Además de los tests unitarios, el pipeline levanta la API y verifica contra
`GET /api/v1/analysis/sample` que la tabla de `001` §6 se cumple: 10 registros,
3 a revisión, 3 de severidad alta, y que los marcados son exactamente los ids
4, 7 y 8.

**Activación diferida.** Este gate solo se ejecuta cuando el endpoint existe. Si
se activara desde la primera tarea, los PRs de T1 a T11 estarían en rojo por
diseño durante todo el desarrollo, y un CI que está rojo de forma esperada deja
de leerse: la próxima vez que se ponga rojo de verdad, nadie lo mirará. La
detección es automática (`003` §5 de este documento), no manual.

### RF-CI-04 · Una rama y un pull request por tarea

Cada tarea de `004-tareas.md` se desarrolla en su propia rama y se integra por
PR. Nada se empuja directamente a `main`.

### RF-CI-05 · Historial lineal

Las mezclas son *squash*: un commit en `main` por tarea, con el identificador de
tarea y los requisitos en el título. La rama se borra al mezclar.

### RN-CI-01 · Nada rojo se mezcla

Sin excepciones, tampoco "es solo un aviso". `TreatWarningsAsErrors` está activo
precisamente para que no exista la categoría "solo un aviso".

### RN-CI-02 · La desviación se documenta en el mismo PR

Si la implementación se aparta de la especificación, el PR debe incluir la
actualización de la spec. Un PR que cambia el comportamiento sin tocar la spec
rompe la premisa de que la spec es la fuente de verdad, y se rechaza.

### RN-CI-03 · Sin fallos intermitentes

Ningún paso del pipeline depende de una espera fija. Los arranques se comprueban
por sondeo. Un CI intermitente es peor que no tener CI: entrena al equipo a
reintentar en lugar de a investigar.

---

## 3. Contratos

### 3.1 Ficheros a crear

```
.github/
├── workflows/
│   └── ci.yml
└── pull_request_template.md
```

### 3.2 Disparadores

| Evento             | Ramas   |
|--------------------|---------|
| `pull_request`     | `main`  |
| `push`             | `main`  |
| `workflow_dispatch`| —       |

`concurrency` agrupado por `github.ref` con `cancel-in-progress: true`:
dos empujes seguidos a la misma rama cancelan la ejecución anterior en lugar de
consumir minutos en un resultado que ya no interesa.

`permissions: contents: read` a nivel de workflow. El pipeline solo lee el
repositorio; no hay motivo para concederle escritura.

### 3.3 Job `build-and-test`

`ubuntu-latest`. Pasos en orden:

1. `actions/checkout@v4`
2. `actions/setup-dotnet@v4` con `dotnet-version: '8.0.x'`
3. `actions/cache@v4` sobre `~/.nuget/packages`, clave
   `${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}` y `restore-keys`
   `${{ runner.os }}-nuget-`
4. `dotnet restore`
5. `dotnet build --configuration Release --no-restore`
6. `dotnet test --configuration Release --no-build --logger "trx;LogFileName=test-results.trx" --results-directory ./TestResults`
7. `actions/upload-artifact@v4` con `if: always()`, nombre `test-results`,
   ruta `./TestResults`, `retention-days: 7`

El artefacto se sube con `if: always()` a propósito: los resultados de test
interesan sobre todo **cuando el paso anterior ha fallado**.

### 3.4 Job `acceptance`

`ubuntu-latest`, con `needs: build-and-test`. No tiene sentido levantar la API si
los tests unitarios ya fallaron.

Pasos: checkout, setup-dotnet y cache idénticos, y después:

1. **Comprobación de activación** (RF-CI-03). Un paso con `id: gate` que define
   la salida `ready`:

   ```bash
   if [ -f src/Emissions.Api/Program.cs ] && \
      grep -q 'analysis/sample' src/Emissions.Api/Program.cs; then
     echo "ready=true" >> $GITHUB_OUTPUT
   else
     echo "ready=false" >> $GITHUB_OUTPUT
     echo "El endpoint de aceptacion aun no existe (tarea T12). Gate omitido." \
       >> $GITHUB_STEP_SUMMARY
   fi
   ```

   Todos los pasos siguientes llevan `if: steps.gate.outputs.ready == 'true'`.

2. **Levantar la API** en segundo plano, guardando el PID y redirigiendo la salida
   a `api.log`:
   `dotnet run --project src/Emissions.Api --configuration Release --urls http://localhost:5080 > api.log 2>&1 &`

3. **Sondear `/health`** hasta 30 intentos de 1 segundo (RN-CI-03). Si agota los
   intentos: volcar `api.log` y fallar.

4. **Verificar la tabla de aceptación** con `jq -e`, cuatro comprobaciones
   independientes y con mensaje propio cada una:

   ```
   .summary.totalRecords            == 10
   .summary.recordsRequiringReview  == 3
   .summary.highSeverity            == 3
   [.results[] | select(.requiresReview) | .id] == [4,7,8]
   ```

   La cuarta es la importante: las tres primeras pasarían igual si el sistema
   marcase tres registros equivocados.

5. **Parar la API** con `if: always()`.

### 3.5 Plantilla de pull request

`.github/pull_request_template.md` con: identificador de tarea, requisitos que
cubre, resumen de dos o tres líneas, checklist (build sin avisos, tests en verde,
valores dorados sin ajustar, casilla marcada en `004-tareas.md`) y un apartado
obligatorio **"Desviaciones respecto a la especificación"** cuyo valor por defecto
es `Ninguna` (RN-CI-02).

---

## 4. Flujo operativo por tarea

```bash
git switch main && git pull
git switch -c task/T08-consumption-deviation-rule

# ... Claude Code ejecuta la tarea T8 ...

git add -A
git commit -m "T8: ConsumptionDeviationRule (RF-03, RN-02, RN-03)"
git push -u origin task/T08-consumption-deviation-rule

gh pr create --fill --base main
gh pr checks --watch          # espera al CI
gh pr merge --squash --delete-branch
git switch main && git pull
```

**Nomenclatura de ramas:** `task/T<nn>-<descripcion-en-kebab-case>`, con el
número a dos dígitos para que ordenen bien.

**Título del commit de squash:** `T<n>: <qué> (<requisitos>)`.

---

## 5. Protección de rama

Para que RN-CI-01 sea una barrera real y no una intención, en GitHub:

Settings → Branches → Add branch protection rule sobre `main`:

- Require a pull request before merging
- Require status checks to pass before merging → seleccionar **`Build y tests`** y
  **`Criterios de aceptacion (spec 001 §6)`**
- Require branches to be up to date before merging

> **Aviso de plan.** Las reglas de protección de rama están disponibles en repos
> **públicos** con GitHub Free, pero en repos **privados** requieren GitHub Pro,
> Team o Enterprise. Si el repositorio es privado y estás en plan gratuito, no
> podrás activarlas: el CI se ejecutará igualmente en cada PR y solo pierdes el
> bloqueo automático de la mezcla. En ese caso, la disciplina la pones tú —
> `gh pr checks --watch` antes de cada `merge`.

Los nombres de los checks requeridos deben coincidir **exactamente** con el campo
`name:` de cada job del workflow. Si se renombra un job, la protección queda
esperando un check que ya no existe y bloquea todos los PRs.

---

## 6. Tareas

- [ ] **T-CI-1 · Workflow y plantilla de PR**
  Crear `.github/workflows/ci.yml` y `.github/pull_request_template.md` según §3.
  *Hecho cuando:* el workflow aparece en la pestaña Actions y el job
  `build-and-test` termina en verde con el gate de aceptación omitido.

- [ ] **T-CI-2 · Test de humo en T0**
  El PR de la tarea T0 debe incluir al menos un test real, o `dotnet test` falla
  con "No test is available" y el CI arranca en rojo. Test mínimo aceptable, en
  `SampleDatasetTests`: deserializar `Data/sample-records.json` y comprobar que
  contiene 10 registros y que el id 7 tiene energía negativa. No es relleno:
  verifica que el dataset se copia al output, que es un fallo real y silencioso.

- [ ] **T-CI-3 · Protección de rama**
  Configurar `main` según §5, o dejar constancia en el README de por qué no se
  pudo (plan de GitHub).

---

## 7. Criterios de aceptación de este anexo

1. Un PR con un test que falla **no** se puede mezclar.
2. Un PR con un aviso de compilación **no** se puede mezclar
   (`TreatWarningsAsErrors`).
3. Antes de T12, el job `acceptance` pasa en verde indicando que el gate está
   omitido.
4. Desde T12, si se altera un umbral de `appsettings.json` de forma que el id 4
   deje de marcarse, el job `acceptance` falla.
5. Tras mezclar en `main`, el pipeline se ejecuta de nuevo sobre `main` y termina
   en verde.

El punto 4 es la prueba de que el gate sirve para algo: si se puede romper el
criterio de negocio sin que el CI se entere, el CI está decorando.
