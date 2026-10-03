using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Auth;
using TicketDesk.Api.Data;
using TicketDesk.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TicketDeskContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<SlaService>();
builder.Services.AddScoped<AssignmentService>();

builder.Services
    .AddAuthentication("Demo")
    .AddScheme<AuthenticationSchemeOptions, DemoAuthHandler>("Demo", _ => { });
builder.Services.AddAuthorization();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TicketDeskContext>();
    db.Database.EnsureCreated();
    SeedData.Seed(db);
}

app.Run();

// Exposed for WebApplicationFactory integration tests.
public partial class Program
{
}
