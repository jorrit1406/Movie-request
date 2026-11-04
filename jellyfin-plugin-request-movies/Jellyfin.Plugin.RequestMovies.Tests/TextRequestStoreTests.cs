using System;
using System.IO;
using System.Reflection;
using Jellyfin.Plugin.RequestMovies.Services;
using MediaBrowser.Common.Configuration;
using Xunit;

namespace Jellyfin.Plugin.RequestMovies.Tests;

public class TextRequestStoreTests
{
    [Fact]
    public void ReadsTextRequestsSavedBeforeVotingWasAdded()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "RequestMoviesTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        try
        {
            var id = Guid.NewGuid();
            var json = $"[{{\"id\":\"{id}\",\"title\":\"Saved title\",\"notes\":\"Saved note\",\"requestedBy\":\"User\",\"createdAtUtc\":\"2026-11-04T00:00:00Z\"}}]";
            File.WriteAllText(Path.Combine(dataPath, "request-movies-text-requests.json"), json);

            var store = new TextRequestStore(CreateApplicationPaths(dataPath));
            var request = Assert.Single(store.GetAll(Guid.NewGuid()));

            Assert.Equal(id, request.Id);
            Assert.Equal("Saved title", request.Title);
            Assert.Equal(0, request.VoteCount);
            Assert.False(request.HasVoted);
            Assert.False(request.IsDone);
        }
        finally
        {
            Directory.Delete(dataPath, true);
        }
    }

    [Fact]
    public void MarkingRequestDonePersistsAndKeepsItInTheList()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "RequestMoviesTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        try
        {
            var store = new TextRequestStore(CreateApplicationPaths(dataPath));
            var request = store.Add("Finished title", string.Empty, "User");
            var userId = Guid.NewGuid();
            Assert.NotNull(store.ToggleVote(request.Id, userId));

            Assert.True(store.MarkDone(request.Id));
            Assert.True(store.MarkDone(request.Id));
            Assert.False(store.MarkDone(Guid.NewGuid()));

            var reloaded = new TextRequestStore(CreateApplicationPaths(dataPath));
            var savedRequest = Assert.Single(reloaded.GetAll(userId));
            Assert.Equal(request.Id, savedRequest.Id);
            Assert.Equal(1, savedRequest.VoteCount);
            Assert.True(savedRequest.HasVoted);
            Assert.True(savedRequest.IsDone);
        }
        finally
        {
            Directory.Delete(dataPath, true);
        }
    }

    [Fact]
    public void VotesAreUniqueToggleablePersistentAndSortTheList()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "RequestMoviesTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        try
        {
            var userOne = Guid.NewGuid();
            var userTwo = Guid.NewGuid();
            var store = new TextRequestStore(CreateApplicationPaths(dataPath));
            var first = store.Add("First title", string.Empty, "User one");
            var second = store.Add("Second title", string.Empty, "User two");

            Assert.NotNull(store.ToggleVote(first.Id, userOne));
            Assert.NotNull(store.ToggleVote(first.Id, userOne));
            Assert.NotNull(store.ToggleVote(first.Id, userOne));
            Assert.NotNull(store.ToggleVote(first.Id, userTwo));
            Assert.NotNull(store.ToggleVote(second.Id, userOne));

            var restartedStore = new TextRequestStore(CreateApplicationPaths(dataPath));
            var requests = restartedStore.GetAll(userOne);

            Assert.Equal("First title", requests[0].Title);
            Assert.Equal(2, requests[0].VoteCount);
            Assert.True(requests[0].HasVoted);
            Assert.Equal("Second title", requests[1].Title);
            Assert.Equal(1, requests[1].VoteCount);
            Assert.True(requests[1].HasVoted);
            Assert.False(restartedStore.GetAll(userTwo)[1].HasVoted);
        }
        finally
        {
            Directory.Delete(dataPath, true);
        }
    }

    private static IApplicationPaths CreateApplicationPaths(string dataPath)
    {
        var paths = DispatchProxy.Create<IApplicationPaths, ApplicationPathsProxy>();
        ((ApplicationPathsProxy)(object)paths).ConfiguredDataPath = dataPath;
        return paths;
    }

    public class ApplicationPathsProxy : DispatchProxy
    {
        public string ConfiguredDataPath { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_DataPath")
            {
                return ConfiguredDataPath;
            }

            throw new NotSupportedException($"Unexpected application paths member: {targetMethod?.Name}");
        }
    }
}
