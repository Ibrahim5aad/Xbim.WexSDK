var builder = DistributedApplication.CreateBuilder(args);

// Add SQL Server database
// Note: For development, we use non-persistent container for clean state on restart.
// Add .WithLifetime(ContainerLifetime.Persistent) to retain data between runs.
var sqlServer = builder.AddSqlServer("sql");

var database = sqlServer.AddDatabase("OctopusDb");

// Add the Octopus API with external HTTP endpoints
var server = builder.AddProject<Projects.Octopus_Server_App>("octopus-server")
    .WithExternalHttpEndpoints()
    .WithReference(database)
    .WaitFor(database);

// Add the Octopus Web app with reference to the server
// The web app uses service discovery to connect to the server via "http://octopus-server"
var web = builder.AddProject<Projects.Octopus_Web>("octopus-web")
    .WithExternalHttpEndpoints()
    .WithReference(server)
    .WaitFor(server);

builder.Build().Run();
