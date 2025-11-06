https://github.com/dotnet/aspire/blob/ba4b4ec960c3dcda439995c21b40a15b7ddfb0ed/docs/using-latest-daily.md?plain=1#L76

aspire update
    - daily

aspire new
    - Blazor & Minimal API starter
    
<PackageReference Include="Aspire.Hosting.Docker" Version="13.1.0-preview.1.25555.14" />

builder.AddDockerComposeEnvironment("env");

aspire publish --output-path publish-output