# The DGHSF agent worker: an event-driven Azure Container Apps job runs one execution of this image per agent run.
# Build from the repository root:
#   docker build -f src/Djohnnie.SoftwareFactory.Worker/Dockerfile -t dghsf-agent-worker .
#   az acr build -r <registry> -t dghsf-agent-worker:<tag> -f src/Djohnnie.SoftwareFactory.Worker/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/ src/
# The RuntimeIdentifier makes the GitHub Copilot SDK package download the Linux build of the Copilot runtime.
RUN dotnet publish src/Djohnnie.SoftwareFactory.Worker/Djohnnie.SoftwareFactory.Worker.csproj \
    --configuration Release --runtime linux-x64 --self-contained false --output /app

# The agent builds and tests the repositories it changes, so the worker runs on the SDK image (which has git), not on
# the slim runtime image. Add the tool chains your repositories need (Node.js, Python, ...) here.
FROM mcr.microsoft.com/dotnet/sdk:11.0
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    AgentWorker__WorkDirectory=/tmp/dghsf-agent-worker
WORKDIR /app
COPY --from=build /app .
# The SDK image's non-root user.
USER app
ENTRYPOINT ["dotnet", "Djohnnie.SoftwareFactory.Worker.dll"]
