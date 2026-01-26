var builder = DistributedApplication.CreateBuilder(args);

// Add the Octopus Server API with external HTTP endpoints
var server = builder.AddProject<Projects.Octopus_Server_App>("octopus-server")
    .WithExternalHttpEndpoints();

// Future: Add web app and database resources
// var web = builder.AddProject<Projects.Octopus_Web>("octopus-web")
//     .WithReference(server);

builder.Build().Run();
