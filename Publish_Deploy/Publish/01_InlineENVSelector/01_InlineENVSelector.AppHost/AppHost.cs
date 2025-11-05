using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// PRE step that runs BEFORE compose publisher
if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddResource(new PrePublishTask("pre-publish"))
        .WithAnnotation(new PublishingCallbackAnnotation(async ctx =>
        {
            if (!ctx.ExecutionContext.IsPublishMode) return;

            var ct = ctx.CancellationToken;

            EnterpriseEnvironment? selected = null;
            var fromVar = Environment.GetEnvironmentVariable(EnterpriseEnvironmentHelpers.MsBuildOrEnvName);
            if (EnterpriseEnvironmentHelpers.TryParse(fromVar, out var envFromVar))
                selected = envFromVar;

            if (selected is null)
            {
                var interactionService = ctx.Services.GetRequiredService<IInteractionService>();
                var envResult = await interactionService.PromptInputAsync(
                    "Environment Configuration",
                    "Please enter the target environment name:",
                    new InteractionInput
                    {
                        Name = "environmentName",
                        Label = "Environment Name",
                        InputType = InputType.Text,
                        Required = true,
                        Placeholder = "dev, staging, prod"
                    },
                    cancellationToken: ct);

                ct.ThrowIfCancellationRequested();

                if (!EnterpriseEnvironmentHelpers.TryParse(envResult.Data?.Value, out var envFromPrompt))
                    throw new InvalidOperationException($"Invalid environment selection: '{envResult.Data?.Value}'.");

                selected = envFromPrompt;
            }

            var valueRaw = selected!.Value.ToString();   // "Dev"
            var tag = valueRaw.ToLowerInvariant();  // "dev"

            // 1) Make tag available to the .NET container build:
            //    This sets MSBuild property ContainerImageTags for the build.
            Environment.SetEnvironmentVariable("ContainerImageTags", tag, EnvironmentVariableTarget.Process);

            // (optional) also keep ENTERPRISE_ENV around
            Environment.SetEnvironmentVariable(EnterpriseEnvironmentHelpers.MsBuildOrEnvName, valueRaw, EnvironmentVariableTarget.Process);

            // 2) (optional) If you still want to pre-create/patch .env now, you can;
            //    or move this to a post step. It doesn't affect image tags.
            Directory.CreateDirectory(ctx.OutputPath);
            var dotEnvPath = Path.Combine(ctx.OutputPath, ".env");
            List<string> lines = File.Exists(dotEnvPath) ? File.ReadAllLines(dotEnvPath).ToList() : new();

            void Upsert(string key, string val)
            {
                var p = key + "=";
                var nl = p + val;
                var i = lines.FindIndex(l => l.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) lines[i] = nl; else lines.Add(nl);
            }

            Upsert("ENTERPRISE_ENV", valueRaw);
            Upsert("APISERVICE_IMAGE", $"apiservice:{tag}");
            Upsert("WEBFRONTEND_IMAGE", $"webfrontend:{tag}");
            if (lines.Count == 0 || lines[^1] != "") lines.Add("");
            File.WriteAllLines(dotEnvPath, lines);
        }));
}

builder.AddDockerComposeEnvironment("docker-env");

var cache = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects._01_InlineENVSelector_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects._01_InlineENVSelector_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
