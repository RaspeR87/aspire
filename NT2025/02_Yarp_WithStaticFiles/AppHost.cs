var builder = DistributedApplication.CreateBuilder(args);

builder.AddYarp("frontend")
    .WithStaticFiles("./web");

builder.Build().Run();
