FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SwapKino.Api/SwapKino.Api.csproj SwapKino.Api/
COPY SwapKino.Worker/SwapKino.Worker.csproj SwapKino.Worker/
RUN dotnet restore SwapKino.Api/SwapKino.Api.csproj && dotnet restore SwapKino.Worker/SwapKino.Worker.csproj
COPY . .
RUN dotnet publish SwapKino.Api/SwapKino.Api.csproj -c Release -o /out/api --no-restore && dotnet publish SwapKino.Worker/SwapKino.Worker.csproj -c Release -o /out/worker --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
ARG SERVICE=api
WORKDIR /app
COPY --from=build /out/api ./api
COPY --from=build /out/worker ./worker
EXPOSE 8000
ENTRYPOINT ["sh", "-c", "if [ \"$SERVICE\" = \"worker\" ]; then exec dotnet /app/worker/SwapKino.Worker.dll; else exec dotnet /app/api/SwapKino.Api.dll; fi"]
