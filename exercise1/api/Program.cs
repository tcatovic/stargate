using Microsoft.EntityFrameworkCore;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Resolved eagerly so a bad or relative path in a Release build fails at startup, not on the first request.
var connectionString = StarbaseDatabase.FromConfiguration(builder.Configuration);
builder.Services.AddDbContext<StargateContext>(options => options.UseSqlite(connectionString));

builder.Services.AddDevelopmentDatabaseInitializer();

builder.Services.AddMediatR(cfg =>
{
    cfg.AddRequestPreProcessor<CreateAstronautDutyPreProcessor>();
    cfg.RegisterServicesFromAssemblies(typeof(Program).Assembly);
});

var app = builder.Build();

// Development only (the initializer checks the environment itself). Runs after Build() so design-time
// `dotnet ef` commands, which stop at Build(), never trigger it, and before app.Run() so no request is
// served against an unmigrated database. A failure is deliberately not caught: the app must not start.
await DevelopmentDatabaseInitializer.RunAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();



// Makes the top-level-statements entry point visible to WebApplicationFactory<Program> in api.Tests.
public partial class Program;
