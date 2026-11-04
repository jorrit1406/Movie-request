using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.RequestMovies.Models;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.RequestMovies.Services;

public sealed class TextRequestStore
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly List<TextRequestDto> _requests;

    public TextRequestStore(IApplicationPaths applicationPaths)
    {
        Directory.CreateDirectory(applicationPaths.DataPath);
        _filePath = Path.Combine(applicationPaths.DataPath, "request-movies-text-requests.json");
        _requests = File.Exists(_filePath)
            ? JsonSerializer.Deserialize<List<TextRequestDto>>(File.ReadAllText(_filePath))
                ?? throw new InvalidDataException("The text request list could not be read.")
            : new List<TextRequestDto>();
    }

    public IReadOnlyList<TextRequestDto> GetAll()
    {
        lock (_sync)
        {
            return _requests.OrderByDescending(request => request.CreatedAtUtc).ToArray();
        }
    }

    public TextRequestDto Add(string title, string notes, string requestedBy)
    {
        lock (_sync)
        {
            var request = new TextRequestDto
            {
                Id = Guid.NewGuid(),
                Title = title,
                Notes = notes,
                RequestedBy = requestedBy,
                CreatedAtUtc = DateTime.UtcNow
            };
            var updated = _requests.Append(request).ToList();
            Save(updated);
            _requests.Add(request);
            return request;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_sync)
        {
            var index = _requests.FindIndex(request => request.Id == id);
            if (index < 0)
            {
                return false;
            }

            var updated = _requests.Where(request => request.Id != id).ToList();
            Save(updated);
            _requests.RemoveAt(index);
            return true;
        }
    }

    private void Save(IReadOnlyCollection<TextRequestDto> requests)
    {
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(requests));
        File.Move(temporaryPath, _filePath, true);
    }
}
