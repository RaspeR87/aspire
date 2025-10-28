using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var azurite = builder.AddContainer("azurite", "mcr.microsoft.com/azure-storage/azurite", "3.31.0")
    .WithEndpoint(10000, 10000, "blob")
    .WithEndpoint(10001, 10001, "queue")
    .WithEndpoint(10002, 10002, "table");

string BuildAzuriteConn() =>
    "DefaultEndpointsProtocol=http;" +
    "AccountName=devstoreaccount1;" +
    "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
    $"BlobEndpoint={azurite.GetEndpoint("blob").Url}devstoreaccount1;" +
    $"QueueEndpoint={azurite.GetEndpoint("queue").Url}devstoreaccount1;" +
    $"TableEndpoint={azurite.GetEndpoint("table").Url}devstoreaccount1;";

builder.AddProject<Projects.AzureFunction>("azure-func")
    .WithHttpEndpoint(port: 7071, name: "http")
    // pass a Func<string> so Aspire evaluates it after endpoints are allocated
    .WithEnvironment("AzureWebJobsStorage", BuildAzuriteConn)
    .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
    .WaitFor(azurite);

builder.Build().Run();
