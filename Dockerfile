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
