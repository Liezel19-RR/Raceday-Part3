using RaceDay.Api.Services;

namespace RaceDay.Tests;

public sealed class FakeBlobStorageService : IBlobStorageService
{
    public bool IsConfigured => true;

    public Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult($"https://images.example.test/{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
}
