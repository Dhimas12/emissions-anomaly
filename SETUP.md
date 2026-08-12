# SETUP — Contenedor, repositorio y arranque

---

## 1. Contenedor de desarrollo

`.NET 8` fijado por imagen y Claude Code dentro. Tu máquina no se ensucia y la
versión del SDK es exactamente la que pide el enunciado.

### `Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0

# git y curl para el flujo; ripgrep y less los usa Claude Code para buscar y paginar.
RUN apt-get update && apt-get install -y --no-install-recommends \
        git curl ca-certificates ripgrep less zip nano \
    && rm -rf /var/lib/apt/lists/*

# Usuario no root: el instalador nativo escribe en ~/.local/bin y no debe correr como root.
ARG USERNAME=dev
ARG UID=1000
ARG GID=1000
RUN groupadd -g ${GID} ${USERNAME} \
    && useradd -m -u ${UID} -g ${GID} -s /bin/bash ${USERNAME}

USER ${USERNAME}
ENV PATH="/home/${USERNAME}/.local/bin:${PATH}" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    ASPNETCORE_URLS=http://+:5080

# Instalador nativo: no requiere Node.js y se auto-actualiza en segundo plano.
RUN curl -fsSL https://claude.ai/install.sh | bash

WORKDIR /workspace
CMD ["/bin/bash"]
```

### `docker-compose.yml`

```yaml
services:
  dev:
    build: .
    container_name: emissions-dev
    stdin_open: true          # imprescindible: Claude Code es interactivo
    tty: true
    working_dir: /workspace
    ports:
      - "5080:5080"           # la Minimal API
    volumes:
      - .:/workspace          # tu código, editable desde el host
      - dev-home:/home/dev    # persiste el login de Claude Code y la caché NuGet
volumes:
  dev-home:
```

### Arranque

```bash
mkdir emissions-anomaly && cd emissions-anomaly
# copia aquí Dockerfile, docker-compose.yml, CLAUDE.md y specs/

docker compose build
docker compose run --rm --service-ports dev
```

Ya dentro del contenedor:

```bash
claude --version     # confirma la instalación
claude doctor        # diagnóstico si algo falla
claude               # primera vez: pide autenticarse
```

### Sobre la autenticación

En un contenedor sin navegador, `claude` imprime una URL: la abres en el
navegador del host, te autenticas y pegas el código de vuelta en la terminal. El
volumen `dev-home` guarda la sesión, así que esto se hace **una sola vez**
mientras no borres el volumen.

Claude Code necesita cuenta Pro, Max, Team, Enterprise o Console; el plan
gratuito no lo incluye. Alternativa para entornos sin navegador: exportar
`ANTHROPIC_API_KEY` (facturación por token en lugar de por plan).

Si ya usas Claude Code en el host y prefieres reaprovechar esa sesión, sustituye
el volumen `dev-home` por dos *bind mounts*:

```yaml
      - ${HOME}/.claude:/home/dev/.claude
      - ${HOME}/.claude.json:/home/dev/.claude.json
```

### Detalle que cuesta media hora si se pasa por alto

`ASPNETCORE_URLS=http://+:5080` está en el `Dockerfile` a propósito. Por defecto
Kestrel escucha solo en `localhost`, que dentro del contenedor **no es tu
máquina**: publicarías el puerto y aun así `curl` desde el host daría conexión
rechazada.

---

## 2. Repositorio git

Desde `/workspace`, dentro del contenedor:

```bash
git config --global user.name  "Tu Nombre"
git config --global user.email "tu@email.com"
git config --global init.defaultBranch main

git init
dotnet new gitignore          # .gitignore oficial de .NET, mejor que uno a mano

# Primer commit: SOLO las specs. Esto es lo que demuestra que hubo SDD.
git add CLAUDE.md specs/ Dockerfile docker-compose.yml .gitignore
git commit -m "docs: especificación, plan técnico y contratos (SDD fase 1-3)"
```

Añade al `.gitignore` lo que el de .NET no cubre:

```bash
printf '\n# Claude Code\n.claude/\n' >> .gitignore
```

Después, **un commit por tarea**, con el requisito en el mensaje:

```bash
git commit -m "T2: RobustStatistics — mediana, MAD y z-score modificado (ADR-01)"
git commit -m "T8: ConsumptionDeviationRule (RF-03, RN-02, RN-03)"
```

Dos motivos. Si una tarea sale mal, `git revert` es barato y no arrastra otras. Y
en el vídeo puedes enseñar `git log --oneline`: un historial que empieza por las
specs y avanza tarea a tarea es la prueba de que hubo método, no una sesión de
generación a ciegas. La mayoría de candidatos entregará un único commit inicial.

Si lo quieres en remoto (repo **privado**, es una prueba técnica):

```bash
gh repo create emissions-anomaly --private --source=. --push
# o, sin gh:
git remote add origin git@github.com:usuario/emissions-anomaly.git
git push -u origin main
```

---

## 3. Prompt de arranque para Claude Code (tarea T0)

Pégalo tal cual en la primera sesión dentro de `/workspace`:

> Antes de escribir nada, lee estos ficheros completos: `CLAUDE.md`,
> `specs/001-especificacion.md`, `specs/002-plan-tecnico.md`,
> `specs/003-contratos.md` y `specs/004-tareas.md`.
>
> Cuando los tengas, hazme un resumen de diez líneas de lo que vas a construir y
> dime si has encontrado alguna ambigüedad o contradicción entre documentos.
> **No implementes nada todavía**: espera mi confirmación.
>
> Después ejecuta únicamente la tarea **T0 (Andamiaje)** de `specs/004-tareas.md`:
>
> - Crea el árbol de carpetas y ficheros exacto de `specs/003-contratos.md` §1.
> - Crea la solución `EmissionsAnomaly.sln` y los cuatro proyectos con las
>   propiedades y versiones de paquete de `003` §1, ni una dependencia más.
> - Referencias: `Analysis` → `Domain`, `Api` → `Analysis`, `Tests` → `Analysis`.
> - `TreatWarningsAsErrors` activado en los tres proyectos de `src/` y
>   **desactivado** en el de tests.
> - Los ficheros `.cs` créalos vacíos o con solo el `namespace`: el contenido
>   corresponde a T1 y siguientes.
> - Copia el dataset del enunciado a `src/Emissions.Api/Data/sample-records.json`
>   y márcalo como `Content` con `CopyToOutputDirectory=PreserveNewest`.
>
> Al terminar T0: ejecuta `dotnet build`, confirma que compila sin avisos,
> enséñame el árbol resultante con `tree -I 'bin|obj'`, marca `[x]` la tarea T0 en
> `specs/004-tareas.md` y **párate**. No empieces T1 hasta que yo lo diga.
>
> Reglas para todo lo que venga después: una tarea por vez, siempre en orden, y
> me paras a revisar entre tarea y tarea. Los valores esperados de los tests están
> calculados a mano en `specs/003-contratos.md` §6; si un test falla, o el código
> está mal o la spec está mal, y en el segundo caso me avisas. **Nunca ajustes un
> valor esperado para que un test pase.**

### Para las tareas siguientes

```
Ejecuta la tarea T<n> de specs/004-tareas.md. Solo esa.
Al terminar: dotnet build && dotnet test, marca la casilla, y párate.
```

---

## 4. Comprobaciones al terminar

```bash
dotnet build                          # sin avisos
dotnet test                           # todo verde
dotnet run --project src/Emissions.Api
# en otra terminal del host:
curl -s localhost:5080/api/v1/analysis/sample | jq '.summary'
# esperado: totalRecords 10, recordsRequiringReview 3, highSeverity 3
```

Empaquetar la entrega, desde el host:

```bash
zip -r entrega-emissions-anomaly.zip emissions-anomaly \
    -x '*/bin/*' '*/obj/*' '*/.git/*'
```

---

## Nota sobre Docker y el alcance

`CLAUDE.md` prohíbe añadir Docker **a la entrega**. Esto no lo contradice: el
`Dockerfile` y el `docker-compose.yml` de aquí son tu entorno de desarrollo, no
parte de la solución.

Si decides incluirlos en el ZIP —es defendible, facilita que el evaluador lo
ejecute— hazlo consciente y menciónalo en una línea del README. Lo que no
conviene es que aparezcan sin explicación: en una prueba técnica, todo lo que
entregas es algo sobre lo que te pueden preguntar.
