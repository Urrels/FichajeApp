# API de FichajeApp. Se construye desde esta carpeta:  docker build -t fichaje-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/FichajeApp.Api/FichajeApp.Api.csproj src/FichajeApp.Api/
RUN dotnet restore src/FichajeApp.Api/FichajeApp.Api.csproj
COPY src/FichajeApp.Api/ src/FichajeApp.Api/
RUN dotnet publish src/FichajeApp.Api/FichajeApp.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Todo lo que tiene que sobrevivir a un reinicio (credencial cifrada, llaves, código de activación y fotos) va en /datos:
# montá ahí un volumen persistente. Sin volumen, cada despliegue lo borra.
RUN mkdir /datos && chown app /datos
VOLUME /datos

# La API toma la hora del servidor para fichajes, "Hoy" y liquidación: en el contenedor es UTC y los fichajes quedaban 3 h corridos
# (y los de la noche caían en el día siguiente). TZ lo corrige; se puede pisar con otra zona al desplegar.
ENV TZ=America/Argentina/Buenos_Aires \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Setup__Carpeta=/datos \
    Proxy__Confiar=true
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "FichajeApp.Api.dll"]