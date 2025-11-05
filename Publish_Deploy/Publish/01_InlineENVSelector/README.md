dotnet new aspire-starter --use-redis-cache --output 01_InlineENVSelector

dotnet add package Aspire.Hosting.Docker --version 9.5.2-preview.1.25522.3

<PropertyGroup>
    <NoWarn>ASPIREINTERACTION001;ASPIRECOMPUTE001;ASPIREPUBLISHERS001;</NoWarn>
</PropertyGroup>