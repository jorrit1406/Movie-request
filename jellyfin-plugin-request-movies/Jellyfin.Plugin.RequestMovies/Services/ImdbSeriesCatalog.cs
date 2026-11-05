using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RequestMovies.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestMovies.Services;

public sealed class ImdbSeriesCatalog
{
    private const string DatasetUrl = "https://datasets.imdbws.com/title.basics.tsv.gz";
    private const long MaximumDatasetSize = 1024L * 1024 * 1024;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    private readonly object _updateSync = new();
    private readonly IHttpClientFactory _http;
    private readonly ILogger<ImdbSeriesCatalog> _logger;
    private readonly string _indexPath;
    private Task? _updateTask;
    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private string? _lastError;

    public ImdbSeriesCatalog(IApplicationPaths applicationPaths, IHttpClientFactory http, ILogger<ImdbSeriesCatalog> logger)
    {
        Directory.CreateDirectory(applicationPaths.DataPath);
        _indexPath = Path.Combine(applicationPaths.DataPath, "request-movies-imdb-series.tsv");
        _http = http;
        _logger = logger;
    }

    public async Task<ImdbSeriesSearchResponse> SearchAsync(string query, CancellationToken cancellationToken)
    {
        EnsureUpdateStarted();
        if (!File.Exists(_indexPath))
        {
            return new ImdbSeriesSearchResponse
            {
                IsUpdating = IsUpdating(),
                Error = GetLastError()
            };
        }

        var results = await SearchIndexAsync(query, cancellationToken).ConfigureAwait(false);
        return new ImdbSeriesSearchResponse
        {
            IsReady = true,
            IsUpdating = IsUpdating(),
            LastUpdatedUtc = File.GetLastWriteTimeUtc(_indexPath),
            Error = GetLastError(),
            Results = results
        };
    }

    private void EnsureUpdateStarted()
    {
        var indexExists = File.Exists(_indexPath);
        if (indexExists && DateTime.UtcNow - File.GetLastWriteTimeUtc(_indexPath) < RefreshInterval)
        {
            return;
        }

        lock (_updateSync)
        {
            if (_updateTask is { IsCompleted: false })
            {
                return;
            }

            if (DateTime.UtcNow - _lastAttemptUtc < RetryInterval)
            {
                return;
            }

            _lastAttemptUtc = DateTime.UtcNow;
            _lastError = null;
            _updateTask = Task.Run(UpdateIndexAsync);
        }
    }

    private async Task UpdateIndexAsync()
    {
        var temporaryPath = _indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using var client = _http.CreateClient("RequestMovies.Imdb");
            client.Timeout = Timeout.InfiniteTimeSpan;
            using var response = await client.GetAsync(
                DatasetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumDatasetSize)
            {
                throw new InvalidDataException("IMDb title dataset exceeds the 1 GiB download limit.");
            }

            await using var compressedStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var datasetStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var reader = new StreamReader(datasetStream, Encoding.UTF8, true, 65536, leaveOpen: true);
            var header = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (header is null || !header.StartsWith("tconst\ttitleType\tprimaryTitle\t", StringComparison.Ordinal))
            {
                throw new InvalidDataException("IMDb title dataset has an unexpected format.");
            }

            var indexedCount = 0;
            await using (var outputStream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                65536,
                useAsync: true))
            {
                await using (var writer = new StreamWriter(outputStream, new UTF8Encoding(false), 65536, leaveOpen: true))
                {
                    while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
                    {
                        var columns = line.Split('\t');
                        if (columns.Length < 6
                            || columns[1] != "tvSeries"
                            || columns[4] == "1"
                            || !columns[0].StartsWith("tt", StringComparison.Ordinal)
                            || columns[2].Length == 0
                            || columns[2] == "\\N")
                        {
                            continue;
                        }

                        var year = columns[5] == "\\N" ? string.Empty : columns[5];
                        await writer.WriteLineAsync(
                            string.Concat(columns[0], "\t", year, "\t", columns[2].Replace('\t', ' ')).AsMemory(),
                            timeout.Token).ConfigureAwait(false);
                        indexedCount++;
                    }

                    await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
                }
            }

            if (indexedCount == 0)
            {
                throw new InvalidDataException("IMDb title dataset contained no series.");
            }

            File.Move(temporaryPath, _indexPath, true);
            _logger.LogInformation("RequestMovies: refreshed the compact IMDb series catalog with {Count} titles.", indexedCount);
        }
        catch (Exception ex)
        {
            lock (_updateSync)
            {
                _lastError = "IMDb catalog update failed. You can still add the title manually.";
            }

            _logger.LogError(ex, "RequestMovies: failed to download or build the IMDb series catalog.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "RequestMovies: could not remove temporary IMDb catalog file {Path}.", temporaryPath);
                }
            }
        }
    }

    private async Task<List<ImdbSeriesSearchResult>> SearchIndexAsync(string query, CancellationToken cancellationToken)
    {
        var matches = new List<(int Score, ImdbSeriesSearchResult Result)>(10);
        using var stream = new FileStream(
            _indexPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            65536,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var firstTab = line.IndexOf('\t');
            var secondTab = firstTab < 0 ? -1 : line.IndexOf('\t', firstTab + 1);
            if (firstTab < 0 || secondTab < 0)
            {
                continue;
            }

            var title = line[(secondTab + 1)..];
            var matchIndex = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                continue;
            }

            var score = matchIndex == 0
                ? (title.Length == query.Length ? 0 : 1)
                : 2;
            var result = new ImdbSeriesSearchResult
            {
                ImdbId = line[..firstTab],
                Year = line[(firstTab + 1)..secondTab] is { Length: > 0 } year ? year : null,
                Title = title
            };
            if (matches.Count < 10)
            {
                matches.Add((score, result));
                continue;
            }

            var worstIndex = 0;
            for (var i = 1; i < matches.Count; i++)
            {
                if (Compare(matches[i], matches[worstIndex]) > 0)
                {
                    worstIndex = i;
                }
            }

            if (Compare((score, result), matches[worstIndex]) < 0)
            {
                matches[worstIndex] = (score, result);
            }
        }

        return matches
            .OrderBy(match => match.Score)
            .ThenBy(match => match.Result.Title.Length)
            .ThenBy(match => match.Result.Title, StringComparer.OrdinalIgnoreCase)
            .Select(match => match.Result)
            .ToList();
    }

    private static int Compare(
        (int Score, ImdbSeriesSearchResult Result) left,
        (int Score, ImdbSeriesSearchResult Result) right)
    {
        var scoreComparison = left.Score.CompareTo(right.Score);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        var lengthComparison = left.Result.Title.Length.CompareTo(right.Result.Title.Length);
        return lengthComparison != 0
            ? lengthComparison
            : StringComparer.OrdinalIgnoreCase.Compare(left.Result.Title, right.Result.Title);
    }

    private bool IsUpdating()
    {
        lock (_updateSync)
        {
            return _updateTask is { IsCompleted: false };
        }
    }

    private string? GetLastError()
    {
        lock (_updateSync)
        {
            return _lastError;
        }
    }
}
