FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY /bndradio-backend/bndradio-backend.csproj .
RUN dotnet restore
COPY ./bndradio-backend .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "bndradio-backend.dll"]
