var builder = DistributedApplication.CreateBuilder(args);

var uvicorn = builder.AddUvicornApp(
        name: "uvicornapp",
        projectDirectory: "./uvicornapp-api",
        appName: "main:app"
    )
    .WithHttpEndpoint(targetPort: 8000)   // app listens on 8000
    .WithExternalHttpEndpoints();         // optional, expose to host

builder.Build().Run();
