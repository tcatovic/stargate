using Microsoft.EntityFrameworkCore;
using StargateAPI.Business.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options =>
{
    // Binding errors on route/query values must not echo the submitted text.
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((_, _) => "The value is not valid.");
    messages.SetValueIsInvalidAccessor(_ => "The value is not valid.");
    messages.SetValueMustBeANumberAccessor(_ => "The value must be a number.");
    // Parameters (a route value such as personId) use the "non-property" variants, which are the ones that echo.
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "The value is not valid.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "The value must be a number.");
}).AddJsonOptions(options =>
{
    // Malformed-JSON 400s must not carry System.Text.Json's message (CLR type names, JSON path, line and byte position).
    options.AllowInputFormatterExceptionMessages = false;
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Resolved eagerly so a bad or relative path in a Release build fails at startup, not on the first request.
var connectionString = StarbaseDatabase.FromConfiguration(builder.Configuration);
builder.Services.AddDbContext<StargateContext>(options => options.UseSqlite(connectionString));

builder.Services.AddDevelopmentDatabaseInitializer();

builder.Services.AddMediatR(cfg =>
{
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
