# API de FichajeApp. Se construye desde esta carpeta:  docker build -t fichaje-api .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/FichajeApp.Api/FichajeApp.Api.csproj src/FichajeApp.Api/
RUN dotnet restore src/FichajeApp.Api/FichajeApp.Api.csproj
COPY src/FichajeApp.Api/ src/FichajeApp.Api/
RUN dotnet publish src/FichajeApp.Api/FichajeApp.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Todo lo que tiene que sobrevivir a un reinicio (credencial cifrada, llaves, código de activación y fotos) va en /datos:
# montá ahí un volumen persistente. Sin volumen, cada despliegue lo borra.
RUN mkdir /datos && chown app /datos
VOLUME /datos

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Setup__Carpeta=/datos \
    Proxy__Confiar=true
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "FichajeApp.Api.dll"]