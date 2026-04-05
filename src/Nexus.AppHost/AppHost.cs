var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Nexus_OrchestratorApi>("orchestrator-api")
    .WithHttpHealthCheck("/api/health", 200, "http");

builder.AddProject<Projects.Nexus_Mcp_Toolbelt>("mcp-toolbelt")
    .WithHttpHealthCheck("/api/health", 200, "http");

builder.Build().Run();
