var builder = DistributedApplication.CreateBuilder(args);

var inspector = builder.AddMcpInspector("mcp-inspector");

var tunnel = builder.AddDevTunnel("dev-tunnel");

var pyMcpServer = builder.AddPythonApp(
    name: "pymcp",
    appDirectory: "./pymcp",
    scriptPath: "main.py"
).WithHttpEndpoint(env: "PORT");

tunnel.WithReference(pyMcpServer, allowAnonymous: true);

inspector.WithMcpServer(pyMcpServer);

builder.Build().Run();
