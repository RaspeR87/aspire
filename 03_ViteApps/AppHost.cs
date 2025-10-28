var builder = DistributedApplication.CreateBuilder(args);

/*
builder.AddNpmApp(
        name: "frontend",
        workingDirectory: "./my-vite-app-npm", // pot do mape s package.json
        scriptName: "dev")                            // npr. vite "dev"/"serve" ali "start"
    .WithNpmPackageInstallation()
    .WithExternalHttpEndpoints();   
*/

builder.AddViteApp("frontend", "./my-vite-app-npm")
    .WithNpmPackageInstallation()
    .WithExternalHttpEndpoints();

builder.Build().Run();
