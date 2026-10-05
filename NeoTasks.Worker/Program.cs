using Microsoft.EntityFrameworkCore;
using NeoTasks;
using NeoTasks.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<TasksDb>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("Database connection is required."),
    postgres => postgres.MigrationsAssembly("NeoTasks.Data")));
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<TaskAssignmentConsumer>();

await builder.Build().RunAsync();
