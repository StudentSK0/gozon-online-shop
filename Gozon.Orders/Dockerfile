FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG HTTP_PROXY
ARG HTTPS_PROXY
ARG NO_PROXY
ENV HTTP_PROXY=${HTTP_PROXY}
ENV HTTPS_PROXY=${HTTPS_PROXY}
ENV NO_PROXY=${NO_PROXY}
WORKDIR /src
COPY ./src ./src
COPY ./Gozon.Orders.sln ./
RUN dotnet restore ./Gozon.Orders.sln
RUN dotnet publish ./src/Gozon.Orders.Api/Gozon.Orders.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Gozon.Orders.Api.dll"]
