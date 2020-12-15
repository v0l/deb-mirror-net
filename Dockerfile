FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /source

# copy csproj and restore as distinct layers
COPY . .
RUN dotnet restore

# copy and publish app and libraries
RUN dotnet publish -c release -o /app --no-restore

# final stage/image
FROM mcr.microsoft.com/dotnet/core/runtime:3.1
LABEL org.opencontainers.image.source https://github.com/v0l/deb-mirror-net
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "DebMirrorNet.dll"]
