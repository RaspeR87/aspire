#pragma warning disable ASPIREPIPELINES001
#pragma warning disable ASPIREPIPELINES004

using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("env");

var cache = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects._01_PipelineStarterTest_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects._01_PipelineStarterTest_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Pipeline.AddStep("modify-env", async (context) =>
{
    // Create a reporting task
    var modifyEnvFileTask = await context.ReportingStep
        .CreateTaskAsync($"Modify .env file", context.CancellationToken)
        .ConfigureAwait(false);

    await using (modifyEnvFileTask.ConfigureAwait(false))
    {
        // Get output directory from pipeline output service
        var outputService = context.PipelineContext.Services.GetRequiredService<IPipelineOutputService>();
        var outputDirectory = outputService.GetOutputDirectory();
        context.Logger.LogInformation($"Output directory: {outputDirectory}");

        var dotEnvPath = Path.Combine(outputDirectory, ".env");
        List<string> lines = File.Exists(dotEnvPath) ? File.ReadAllLines(dotEnvPath).ToList() : new();

        void Upsert(string key, string val)
        {
            var p = key + "=";
            var nl = p + val;
            var i = lines.FindIndex(l => l.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) lines[i] = nl; else lines.Add(nl);
        }

        // Add custom environment variables
        context.Logger.LogInformation($"Add custom environment variable to .env file: SAMPLE_ENV_VAR=HelloWorld");
        Upsert("SAMPLE_ENV_VAR", "HelloWorld");

        if (lines.Count == 0 || lines[^1] != "") lines.Add("");
        File.WriteAllLines(dotEnvPath, lines);
    }
}, dependsOn: "publish-env", requiredBy: "publish");

builder.Build().Run();
