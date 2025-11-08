# Use official .NET 8 runtime as base
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Expose port
EXPOSE 8080

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["AppointmentBot/AppointmentBot.csproj", "AppointmentBot/"]
RUN dotnet restore "AppointmentBot/AppointmentBot.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/AppointmentBot"
RUN dotnet publish "AppointmentBot.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "AppointmentBot.dll"]
