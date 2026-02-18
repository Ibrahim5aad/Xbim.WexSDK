var builder = DistributedApplication.CreateBuilder(args);

// Add SQL Server database
var sqlServer = builder.AddSqlServer("sql");

var database = sqlServer.AddDatabase("XbimDb");

// Add the Xbim API with external HTTP endpoints
var server = builder.AddProject<Projects.Xbim_Server_App>("Xbim-server")
    .WithExternalHttpEndpoints()
    .WithReference(database)
    .WaitFor(database);

// Add the Xbim Web app with reference to the server
// The web app uses service discovery to connect to the server via "http://Xbim-server"
var web = builder.AddProject<Projects.Xbim_Web>("Xbim-web")
    .WithExternalHttpEndpoints()
    .WithReference(server)
    .WaitFor(server);

builder.Build().Run();
