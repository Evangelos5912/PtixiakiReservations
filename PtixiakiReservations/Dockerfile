FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

ENV NUGET_PACKAGES=/src/.nuget/packages
ENV TEMP=/src/tmp
ENV TMP=/src/tmp

COPY ["PtixiakiReservations.csproj", "./"]
RUN dotnet restore "PtixiakiReservations.csproj"

COPY . .
RUN dotnet publish "PtixiakiReservations.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:PublishSingleFile=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PtixiakiReservations.dll"]
