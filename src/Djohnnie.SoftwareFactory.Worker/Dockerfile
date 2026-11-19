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
    AgentWorker__WorkDirectory=/tmp/dghsf-agent-worker \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    NODE_PATH=/usr/local/lib/node_modules:/usr/lib/node_modules
# Node.js, Playwright and a headless Chromium (with its system libraries), so the agent can start the app it changed and
# take screenshots of its user interface for the pull request. The browsers go to a shared folder the app user can read.
RUN apt-get update \
    && apt-get install -y --no-install-recommends nodejs npm \
    && npm install -g playwright@1.55.0 \
    && playwright install --with-deps chromium \
    && chmod -R a+rX "$PLAYWRIGHT_BROWSERS_PATH" \
    && rm -rf /var/lib/apt/lists/* /root/.npm
WORKDIR /app
COPY --from=build /app .
# The SDK image's non-root user.
USER app
ENTRYPOINT ["dotnet", "Djohnnie.SoftwareFactory.Worker.dll"]
