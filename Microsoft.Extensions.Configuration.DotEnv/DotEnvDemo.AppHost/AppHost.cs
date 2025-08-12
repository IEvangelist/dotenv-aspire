var builder = DistributedApplication.CreateBuilder(args);

var dotEnvPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../dot.env"));

builder.AddProject<Projects.DotEnvDemo_Api>("api")
    .WithEnvironment("TEST_ENV_VAR", "TestValue")
    .WithEnvironment(dotEnvPath);

builder.Build().Run();