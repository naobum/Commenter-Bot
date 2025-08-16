FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Bot.Domain/Bot.Domain.csproj Bot.Domain/
COPY Bot.Application/Bot.Application.csproj Bot.Application/
COPY Bot.Infrastructure/Bot.Infrastructure.csproj Bot.Infrastructure/
COPY Bot.Presentation/Bot.Presentation.csproj Bot.Presentation/
COPY Directory.Build.props ./

RUN dotnet restore Bot.Presentation/Bot.Presentation.csproj

COPY . ./
RUN dotnet publish Bot.Presentation/Bot.Presentation.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bot.Presentation.dll"]