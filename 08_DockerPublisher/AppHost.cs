var builder = DistributedApplication.CreateBuilder(args);

// Enables Docker publisher
builder.AddDockerComposeEnvironment("aspire-docker-demo");

var postgres = builder.AddPostgres("database")
    .WithDataVolume();

var database = postgres.AddDatabase("demo-db");

var redis = builder.AddRedis("cache");

builder.Build().Run();
