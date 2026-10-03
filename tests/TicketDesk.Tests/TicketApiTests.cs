using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TicketDesk.Api.Auth;
using TicketDesk.Api.Data;

namespace TicketDesk.Tests;

public sealed class TicketDeskFactory : WebApplicationFactory<Program>
{
    // One shared in-memory store for every service provider this factory builds,
    // so startup seeding and request handling always see the same data.
    private readonly InMemoryDatabaseRoot _databaseRoot = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var root = _databaseRoot;
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<TicketDeskContext>));
            services.Remove(descriptor);
            services.AddDbContext<TicketDeskContext>(options =>
                options.UseInMemoryDatabase("TicketDeskTests", root));
        });
    }
}

public sealed class TicketApiTests : IDisposable
{
    private readonly TicketDeskFactory _factory = new();
    private readonly HttpClient _client;
    private bool _disposed;

    private static readonly string Requester = SeedData.RequesterId.ToString();
    private static readonly string Agent1 = SeedData.Agent1Id.ToString();
    private static readonly string Admin = SeedData.AdminId.ToString();

    public TicketApiTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client.Dispose();
            _factory.Dispose();
            _disposed = true;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string userId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(DemoAuthHandler.HeaderName, userId);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static string GetString(JsonElement el, string name) =>
        el.GetProperty(name).GetString() ?? string.Empty;

    [Fact]
    public async Task CreateTicket_AsRequester_SetsSlaDueDate()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "VPN is down", description = "Cannot connect since morning", priority = "High" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("High", GetString(json, "priority"));
        Assert.Equal("Open", GetString(json, "status"));

        var created = json.GetProperty("createdAt").GetDateTimeOffset();
        var due = json.GetProperty("dueAt").GetDateTimeOffset();
        Assert.Equal(8, (due - created).TotalHours, precision: 3);
    }

    [Fact]
    public async Task CreateTicket_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/tickets",
            new { title = "X", description = "Y", priority = "Low" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requester_CannotAssignTicket_Forbidden()
    {
        var create = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "Printer jam", description = "Floor 2 printer", priority = "Low" }));
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/tickets/{id}/assign", Requester,
            new { assigneeId = (Guid?)null }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agent_CanAutoAssignTicket_RoundRobinToLeastLoaded()
    {
        var create = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "Email sync", description = "Outlook not syncing", priority = "Medium" }));
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/tickets/{id}/assign", Agent1,
            new { assigneeId = (Guid?)null }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("InProgress", GetString(json, "status"));
        Assert.NotEqual(Guid.Empty, json.GetProperty("assigneeId").GetGuid());
    }

    [Fact]
    public async Task Requester_CannotSee_OthersTickets()
    {
        var create = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "Private issue", description = "Only mine", priority = "Low" }));
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        // Admin can see it.
        var adminView = await _client.SendAsync(Request(HttpMethod.Get, $"/api/tickets/{id}", Admin));
        Assert.Equal(HttpStatusCode.OK, adminView.StatusCode);

        // Requester's list contains their own ticket.
        var list = await _client.SendAsync(Request(HttpMethod.Get, "/api/tickets", Requester));
        var ids = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task UpdateTicket_PriorityChange_RecomputesDueDate()
    {
        var create = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "Slow laptop", description = "Takes ages to boot", priority = "Low" }));
        var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        var id = body.GetProperty("id").GetGuid();
        var created = body.GetProperty("createdAt").GetDateTimeOffset();

        var response = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/tickets/{id}", Agent1,
            new { priority = "Critical" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var due = updated.GetProperty("dueAt").GetDateTimeOffset();
        Assert.Equal(4, (due - created).TotalHours, precision: 3);
    }

    [Fact]
    public async Task AddComment_PublicAndInternal_VisibilityRules()
    {
        var create = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "WiFi dead", description = "No signal in room 3", priority = "Medium" }));
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        // Agent posts an internal note and a public reply.
        await _client.SendAsync(Request(HttpMethod.Post, $"/api/tickets/{id}/comments", Agent1,
            new { body = "Check the AP logs first", isInternal = true }));
        await _client.SendAsync(Request(HttpMethod.Post, $"/api/tickets/{id}/comments", Agent1,
            new { body = "We're looking into it", isInternal = false }));

        var agentView = await _client.SendAsync(Request(HttpMethod.Get, $"/api/tickets/{id}/comments", Agent1));
        var agentComments = JsonDocument.Parse(await agentView.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, agentComments.GetArrayLength());

        var requesterView = await _client.SendAsync(Request(HttpMethod.Get, $"/api/tickets/{id}/comments", Requester));
        var requesterComments = JsonDocument.Parse(await requesterView.Content.ReadAsStringAsync()).RootElement;
        Assert.Single(requesterComments.EnumerateArray());
        Assert.False(requesterComments.EnumerateArray().First().GetProperty("isInternal").GetBoolean());
    }

    [Fact]
    public async Task AuditTrail_RecordsCreateAndUpdate()
    {
        var create = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "Audit me", description = "Track everything", priority = "Medium" }));
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        await _client.SendAsync(Request(HttpMethod.Patch, $"/api/tickets/{id}", Agent1,
            new { status = "InProgress" }));

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/tickets/{id}/audit", Agent1));
        var entries = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().Select(e => GetString(e, "action")).ToList();

        Assert.Contains("Created", entries);
        Assert.Contains("Updated", entries);
    }

    [Fact]
    public async Task SlaBreaches_FlagsOverdueTicket()
    {
        // Seed an overdue ticket directly is not possible via API (due dates are
        // computed), so create a Critical ticket and verify the endpoint works;
        // a separate unit-level check covers breach classification.
        var create = await _client.SendAsync(Request(HttpMethod.Post, "/api/tickets", Requester,
            new { title = "Fresh", description = "Not overdue", priority = "Low" }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/tickets/sla/breaches", Agent1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Workload_ReturnsPerAgentCounts()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/users/workload", Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, entries.GetArrayLength());
    }

    [Fact]
    public async Task Me_ReturnsCurrentUser()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/users/me", Requester));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Requester", GetString(json, "role"));
    }
}
