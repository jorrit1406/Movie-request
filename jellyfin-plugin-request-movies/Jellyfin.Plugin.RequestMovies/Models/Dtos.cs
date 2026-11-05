using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RequestMovies.Models;

public sealed class QualityInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;

    [JsonPropertyName("is4k")] public bool Is4K { get; set; }
}

public sealed class SeasonStateDto
{
    [JsonPropertyName("number")] public int Number { get; set; }

    [JsonPropertyName("state")] public string State { get; set; } = "none";

    [JsonPropertyName("stateLabel")] public string StateLabel { get; set; } = string.Empty;

    [JsonPropertyName("canRequest")] public bool CanRequest { get; set; }

    [JsonPropertyName("episodes")] public int Episodes { get; set; }

    [JsonPropertyName("episodeCount")] public int EpisodeCount { get; set; }
}

public sealed class QualityStateDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;

    [JsonPropertyName("is4k")] public bool Is4K { get; set; }

    [JsonPropertyName("state")] public string State { get; set; } = "none";

    [JsonPropertyName("stateLabel")] public string StateLabel { get; set; } = string.Empty;

    [JsonPropertyName("canRequest")] public bool CanRequest { get; set; }

    [JsonPropertyName("seasons")] public List<SeasonStateDto>? Seasons { get; set; }
}

public sealed class MediaStateResponse
{
    [JsonPropertyName("mediaType")] public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")] public int TmdbId { get; set; }

    [JsonPropertyName("seerrAvailable")] public bool SeerrAvailable { get; set; } = true;

    [JsonPropertyName("requestsAllowed")] public bool RequestsAllowed { get; set; }

    [JsonPropertyName("blockedReason")] public string? BlockedReason { get; set; }

    [JsonPropertyName("state")] public string State { get; set; } = "none";

    [JsonPropertyName("stateLabel")] public string StateLabel { get; set; } = string.Empty;

    [JsonPropertyName("qualities")] public List<QualityStateDto> Qualities { get; set; } = new();
}

public sealed class SearchItemDto
{
    [JsonPropertyName("mediaType")] public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")] public int TmdbId { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("year")] public string? Year { get; set; }

    [JsonPropertyName("overview")] public string? Overview { get; set; }

    [JsonPropertyName("poster")] public string? Poster { get; set; }

    [JsonPropertyName("state")] public string State { get; set; } = "none";

    [JsonPropertyName("stateLabel")] public string StateLabel { get; set; } = string.Empty;

    [JsonPropertyName("canRequest")] public bool CanRequest { get; set; }
}

public sealed class SearchResponse
{
    [JsonPropertyName("seerrAvailable")] public bool SeerrAvailable { get; set; } = true;

    [JsonPropertyName("requestsAllowed")] public bool RequestsAllowed { get; set; }

    [JsonPropertyName("blockedReason")] public string? BlockedReason { get; set; }

    [JsonPropertyName("qualities")] public List<QualityInfo> Qualities { get; set; } = new();

    [JsonPropertyName("results")] public List<SearchItemDto> Results { get; set; } = new();

    [JsonPropertyName("page")] public int Page { get; set; }

    [JsonPropertyName("totalPages")] public int TotalPages { get; set; }
}

public sealed class RequestBody
{
    [JsonPropertyName("mediaType")] public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")] public int TmdbId { get; set; }

    /// <summary>Identifiant de qualité ("hd" ou "4k").</summary>
    [JsonPropertyName("quality")] public string Quality { get; set; } = "hd";

    [JsonPropertyName("seasons")] public List<int>? Seasons { get; set; }
}

public sealed class ReportBody
{
    [JsonPropertyName("mediaType")] public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")] public int TmdbId { get; set; }

    [JsonPropertyName("category")] public string Category { get; set; } = "other";

    [JsonPropertyName("season")] public int? Season { get; set; }

    [JsonPropertyName("episode")] public int? Episode { get; set; }

    [JsonPropertyName("comment")] public string? Comment { get; set; }
}

public sealed class ActionOutcome
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }

    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;

    [JsonIgnore] public int HttpStatus { get; set; } = 200;
}

public sealed class StuckItemDto
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

    [JsonPropertyName("attempts")] public int Attempts { get; set; }

    [JsonPropertyName("firstSeenUtc")] public string FirstSeenUtc { get; set; } = string.Empty;
}

public sealed class TextRequestDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("notes")] public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("requestedBy")] public string RequestedBy { get; set; } = string.Empty;

    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("voteCount")] public int VoteCount { get; set; }

    [JsonPropertyName("hasVoted")] public bool HasVoted { get; set; }

    [JsonPropertyName("isDone")] public bool IsDone { get; set; }

    [JsonPropertyName("imdbId")] public string? ImdbId { get; set; }
}

public sealed class CreateTextRequestBody
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("notes")] public string? Notes { get; set; }

    [JsonPropertyName("imdbUrl")] public string? ImdbUrl { get; set; }
}
