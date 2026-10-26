using AF.Blood.Infrastructure;
using AF.Blood.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDatabase(connectionString);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHostedService<MatchingRunJob>();
builder.Services.AddHostedService<OutboxDeliveryJob>();
builder.Services.AddHostedService<ExpiryAndHousekeepingJob>();

var host = builder.Build();
host.Run();
