using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RaceDay.Api.Data;
using RaceDay.Api.Services;
using Xunit;

namespace RaceDay.Tests;

public class Part3ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private Harness CreateHarness()
    {
        var databaseName = $"RaceDay_{Guid.NewGuid():N}";
        var testFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<RaceDayDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            services.RemoveAll<IDbContextOptionsConfiguration<RaceDayDbContext>>();
            services.AddDbContext<RaceDayDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<IBlobStorageService>();
            services.AddSingleton<IBlobStorageService, FakeBlobStorageService>();
        }));
        return new Harness(testFactory, testFactory.CreateClient());
    }

    [Theory]
    [InlineData("?search=Trail", 1)]
    [InlineData("?type=Run", 1)]
    [InlineData("?location=Riverton", 1)]
    [InlineData("?from=2030-01-01&to=2030-12-31", 1)]
    [InlineData("?upcomingOnly=true", 1)]
    public async Task Event_filters_apply(string query, int expectedCount)
    {
        using var h = CreateHarness();
        await h.CreateEvent(name: "Trail Race", type: "Run", location: "Riverton", date: new DateTime(2030, 6, 1));
        var response = await h.Client.GetFromJsonAsync<List<JsonElement>>($"/api/events{query}");
        Assert.Equal(expectedCount, response!.Count);
    }

    [Fact]
    public async Task Banner_upload_returns_fake_blob_url()
    {
        using var h = CreateHarness();
        var (organiser, eventId, _) = await h.CreateEvent();
        using var form = ImageForm("banner.jpg");
        var response = await organiser.PostAsync($"/api/events/{eventId}/banner", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("images.example.test", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Profile_picture_upload_appears_in_profile()
    {
        using var h = CreateHarness();
        var user = await h.CreateUser("Participant");
        using var form = ImageForm("profile.png");
        var upload = await user.PostAsync("/api/users/me/profile-picture", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var profile = await user.GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Contains("images.example.test", profile.GetProperty("profilePictureUrl").GetString());
    }

    [Theory]
    [InlineData("image.svg", 4)]
    [InlineData("large.png", 5 * 1024 * 1024 + 1)]
    public async Task Image_upload_rejects_unsupported_or_oversized_file(string fileName, int size)
    {
        using var h = CreateHarness();
        var (organiser, eventId, _) = await h.CreateEvent();
        using var form = ImageForm(fileName, size);
        var response = await organiser.PostAsync($"/api/events/{eventId}/banner", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Participant_role_is_required_to_enrol()
    {
        using var h = CreateHarness();
        var (organiser, eventId, categoryId) = await h.CreateEvent();
        var response = await organiser.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Past_event_enrolment_is_rejected()
    {
        using var h = CreateHarness();
        var (_, eventId, categoryId) = await h.CreateEvent(date: DateTime.UtcNow.Date.AddDays(-1));
        var participant = await h.CreateUser("Participant");
        var response = await participant.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancelled_entries_release_category_capacity()
    {
        using var h = CreateHarness();
        var (organiser, eventId, categoryId) = await h.CreateEvent(capacity: 1);
        var first = await h.CreateUser("Participant");
        var second = await h.CreateUser("Participant");
        var enrolment = await first.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        Assert.Equal(HttpStatusCode.Created, enrolment.StatusCode);
        var full = await second.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        var id = (await enrolment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var cancelled = await first.PutAsJsonAsync($"/api/enrolments/{id}/status", new { status = "Cancelled" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await second.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId })).StatusCode);
        await AssertOwnerCanStillRead(organiser, eventId);
    }

    [Fact]
    public async Task Enrolment_with_result_cannot_be_cancelled()
    {
        using var h = CreateHarness();
        var (organiser, eventId, categoryId) = await h.CreateEvent();
        var participant = await h.CreateUser("Participant");
        var created = await participant.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var result = await organiser.PostAsJsonAsync("/api/results", new { enrolmentId = id, finishTime = "00:30:00", finishingPosition = 1, status = "Finished" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await participant.PutAsJsonAsync($"/api/enrolments/{id}/status", new { status = "Cancelled" })).StatusCode);
    }

    [Fact]
    public async Task Result_status_must_be_supported()
    {
        using var h = CreateHarness();
        var (organiser, eventId, categoryId) = await h.CreateEvent();
        var participant = await h.CreateUser("Participant");
        var enrolment = await participant.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        var id = (await enrolment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var response = await organiser.PostAsJsonAsync("/api/results", new { enrolmentId = id, finishTime = "00:30:00", finishingPosition = 1, status = "Started" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Participant_results_and_event_leaderboard_include_finisher_data()
    {
        using var h = CreateHarness();
        var (organiser, eventId, categoryId) = await h.CreateEvent();
        var participant = await h.CreateUser("Participant");
        var enrolment = await participant.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        var id = (await enrolment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Created, (await organiser.PostAsJsonAsync("/api/results",
            new { enrolmentId = id, finishTime = "00:30:00", finishingPosition = 1, status = "Finished" })).StatusCode);
        var mine = await participant.GetFromJsonAsync<JsonElement[]>("/api/results/me");
        Assert.Equal(1, mine![0].GetProperty("finisherCount").GetInt32());
        Assert.Equal("1 of 1 finishers", mine[0].GetProperty("positionDisplay").GetString());
        var leaderboard = await participant.GetFromJsonAsync<JsonElement[]>($"/api/results/event/{eventId}");
        Assert.Single(leaderboard!);
    }

    [Fact]
    public async Task Event_with_enrolments_cannot_be_deleted()
    {
        using var h = CreateHarness();
        var (organiser, eventId, categoryId) = await h.CreateEvent();
        var participant = await h.CreateUser("Participant");
        await participant.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        Assert.Equal(HttpStatusCode.Conflict, (await organiser.DeleteAsync($"/api/events/{eventId}")).StatusCode);
    }

    [Fact]
    public async Task Category_with_enrolments_cannot_be_deleted()
    {
        using var h = CreateHarness();
        var (organiser, eventId, categoryId) = await h.CreateEvent();
        var participant = await h.CreateUser("Participant");
        await participant.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        Assert.Equal(HttpStatusCode.Conflict,
            (await organiser.DeleteAsync($"/api/events/{eventId}/categories/{categoryId}")).StatusCode);
    }

    [Fact]
    public async Task Category_response_includes_active_enrolment_count()
    {
        using var h = CreateHarness();
        var (_, eventId, categoryId) = await h.CreateEvent();
        var participant = await h.CreateUser("Participant");
        await participant.PostAsJsonAsync("/api/enrolments", new { eventId, categoryId });
        var categories = await participant.GetFromJsonAsync<JsonElement[]>($"/api/events/{eventId}/categories");
        Assert.Equal(1, categories![0].GetProperty("enrolledCount").GetInt32());
    }

    [Fact]
    public async Task Updating_profile_to_another_users_email_returns_conflict()
    {
        using var h = CreateHarness();
        var first = await h.CreateUser("Participant");
        await h.CreateUser("Participant", "other@example.test");
        var response = await first.PutAsJsonAsync("/api/users/me",
            new { fullName = "Changed Name", email = "other@example.test", phone = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_mine_endpoint_is_role_protected()
    {
        using var h = CreateHarness();
        var participant = await h.CreateUser("Participant");
        Assert.Equal(HttpStatusCode.Forbidden, (await participant.GetAsync("/api/events/mine")).StatusCode);
    }

    [Fact]
    public async Task Event_deletion_requires_the_owner()
    {
        using var h = CreateHarness();
        var (_, eventId, _) = await h.CreateEvent();
        var otherOrganiser = await h.CreateUser("Organiser");
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOrganiser.DeleteAsync($"/api/events/{eventId}")).StatusCode);
    }

    [Fact]
    public async Task Login_response_includes_profile_picture_url()
    {
        using var h = CreateHarness();
        var email = $"{Guid.NewGuid():N}@example.test";
        var client = await h.CreateUser("Participant", email);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginUser = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user");
        Assert.True(loginUser.TryGetProperty("profilePictureUrl", out _));
        using var form = ImageForm("face.gif");
        var upload = await client.PostAsync("/api/users/me/profile-picture", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Contains("images.example.test", profile.GetProperty("profilePictureUrl").GetString());
    }

    private static async Task AssertOwnerCanStillRead(HttpClient organiser, int eventId)
        => Assert.Equal(HttpStatusCode.OK, (await organiser.GetAsync($"/api/events/{eventId}")).StatusCode);

    private static MultipartFormDataContent ImageForm(string fileName, int size = 8)
    {
        var form = new MultipartFormDataContent();
        var bytes = new byte[size];
        if (fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF;
        }
        else if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            Array.Copy(signature, bytes, signature.Length);
        }
        else if (fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            Array.Copy("GIF89a"u8.ToArray(), bytes, 6);
        else if (fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            Array.Copy("RIFF"u8.ToArray(), bytes, 4);
            Array.Copy("WEBP"u8.ToArray(), 0, bytes, 8, 4);
        }
        var content = new ByteArrayContent(bytes);
        var mediaType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/svg+xml"
        };
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        form.Add(content, "file", fileName);
        return form;
    }

    private sealed class Harness(WebApplicationFactory<Program> factory, HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;

        private readonly List<HttpClient> _clients = [client];

        public async Task<HttpClient> CreateUser(string role, string? email = null)
        {
            var user = factory.CreateClient();
            _clients.Add(user);
            email ??= $"{Guid.NewGuid():N}@example.test";
            var register = await user.PostAsJsonAsync("/api/auth/register",
                new { fullName = "Race Tester", email, password = "Password123!", role, phone = (string?)null });
            register.EnsureSuccessStatusCode();
            var login = await user.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
            login.EnsureSuccessStatusCode();
            return user;
        }

        public async Task<(HttpClient Client, int EventId, int CategoryId)> CreateEvent(
            string name = "Trail Race", string type = "Run", string location = "Riverton", DateTime? date = null, int? capacity = null)
        {
            var organiser = await CreateUser("Organiser");
            var response = await organiser.PostAsJsonAsync("/api/events",
                new { name, description = "Test event", date = date ?? DateTime.UtcNow.Date.AddDays(30),
                    location, distanceKm = 5m, eventType = type });
            response.EnsureSuccessStatusCode();
            var eventId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var category = await organiser.PostAsJsonAsync($"/api/events/{eventId}/categories",
                new { name = "Open", description = "Open category", distanceKm = 5m, price = 10m, capacity });
            category.EnsureSuccessStatusCode();
            var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            return (organiser, eventId, categoryId);
        }

        public void Dispose()
        {
            foreach (var httpClient in _clients) httpClient.Dispose();
            factory.Dispose();
        }
    }
}
