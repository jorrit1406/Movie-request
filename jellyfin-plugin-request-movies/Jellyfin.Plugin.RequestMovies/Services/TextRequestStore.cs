using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.RequestMovies.Models;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.RequestMovies.Services;

public sealed class TextRequestStore
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly List<StoredTextRequest> _requests;

    public TextRequestStore(IApplicationPaths applicationPaths)
    {
        Directory.CreateDirectory(applicationPaths.DataPath);
        _filePath = Path.Combine(applicationPaths.DataPath, "request-movies-text-requests.json");
        _requests = File.Exists(_filePath)
            ? JsonSerializer.Deserialize<List<StoredTextRequest>>(File.ReadAllText(_filePath))
                ?? throw new InvalidDataException("The text request list could not be read.")
            : new List<StoredTextRequest>();
    }

    public IReadOnlyList<TextRequestDto> GetAll(Guid userId)
    {
        lock (_sync)
        {
            return _requests
                .OrderByDescending(request => request.VoterIds.Count)
                .ThenByDescending(request => request.CreatedAtUtc)
                .Select(request => ToDto(request, userId))
                .ToArray();
        }
    }

    public TextRequestDto Add(string title, string notes, string requestedBy)
    {
        lock (_sync)
        {
            var request = new StoredTextRequest
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
            return ToDto(request, Guid.Empty);
        }
    }

    public TextRequestDto? ToggleVote(Guid id, Guid userId)
    {
        lock (_sync)
        {
            var request = _requests.FirstOrDefault(item => item.Id == id);
            if (request is null)
            {
                return null;
            }

            var updated = _requests.Select(item => item.Id != id
                ? item
                : CopyWithVote(item, userId)).ToList();
            Save(updated);
            request.VoterIds = updated.First(item => item.Id == id).VoterIds;
            return ToDto(request, userId);
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

    private static StoredTextRequest CopyWithVote(StoredTextRequest request, Guid userId)
    {
        var voterIds = new List<Guid>(request.VoterIds);
        if (!voterIds.Remove(userId))
        {
            voterIds.Add(userId);
        }

        return new StoredTextRequest
        {
            Id = request.Id,
            Title = request.Title,
            Notes = request.Notes,
            RequestedBy = request.RequestedBy,
            CreatedAtUtc = request.CreatedAtUtc,
            VoterIds = voterIds
        };
    }

    private static TextRequestDto ToDto(StoredTextRequest request, Guid userId)
        => new()
        {
            Id = request.Id,
            Title = request.Title,
            Notes = request.Notes,
            RequestedBy = request.RequestedBy,
            CreatedAtUtc = request.CreatedAtUtc,
            VoteCount = request.VoterIds.Count,
            HasVoted = request.VoterIds.Contains(userId)
        };

    private void Save(IReadOnlyCollection<StoredTextRequest> requests)
    {
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(requests));
        File.Move(temporaryPath, _filePath, true);
    }

    private sealed class StoredTextRequest
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("requestedBy")]
        public string RequestedBy { get; set; } = string.Empty;

        [JsonPropertyName("createdAtUtc")]
        public DateTime CreatedAtUtc { get; set; }

        [JsonPropertyName("voterIds")]
        public List<Guid> VoterIds { get; set; } = new();
    }
}
