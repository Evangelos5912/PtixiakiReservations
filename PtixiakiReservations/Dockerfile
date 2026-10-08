FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

RUN curl -fsSL https://nodejs.org/dist/v20.11.0/node-v20.11.0-linux-x64.tar.gz -o node.tar.gz && \
    tar -xzf node.tar.gz && \
    cp -r node-v20.11.0-linux-x64/* /usr/local/ && \
    rm -rf node.tar.xz node-v20.11.0-linux-x64

ENV NUGET_PACKAGES=/src/.nuget/packages
ENV TEMP=/src/tmp
ENV TMP=/src/tmp

COPY ["PtixiakiReservations.csproj", "./"]
RUN dotnet restore "PtixiakiReservations.csproj"

COPY . .

RUN npm install

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