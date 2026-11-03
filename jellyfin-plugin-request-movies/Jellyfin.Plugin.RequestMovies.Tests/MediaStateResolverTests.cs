using Jellyfin.Plugin.RequestMovies.Services;
using Xunit;

namespace Jellyfin.Plugin.RequestMovies.Tests;

public class MediaStateResolverTests
{
    [Fact]
    public void Movie_InJellyfin_IsAvailable()
        => Assert.Equal(MediaState.Available, MediaStateResolver.ResolveMovie(true, null, null));

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Movie_SeerrAvailableButNotInJellyfin_IsImportPending_NeverAvailable(int seerrStatus)
        => Assert.Equal(MediaState.ImportPending, MediaStateResolver.ResolveMovie(false, seerrStatus, 2));

    [Fact]
    public void Movie_Processing() => Assert.Equal(MediaState.Processing, MediaStateResolver.ResolveMovie(false, 3, 2));

    [Fact]
    public void Movie_PendingApproval() => Assert.Equal(MediaState.Pending, MediaStateResolver.ResolveMovie(false, 2, 1));

    [Fact]
    public void Movie_ApprovedNotYetProcessing() => Assert.Equal(MediaState.Approved, MediaStateResolver.ResolveMovie(false, 2, 2));

    [Fact]
    public void Movie_Failed() => Assert.Equal(MediaState.Failed, MediaStateResolver.ResolveMovie(false, null, 4));

    [Fact]
    public void Movie_Unknown_IsNone() => Assert.Equal(MediaState.None, MediaStateResolver.ResolveMovie(false, null, null));

    [Theory]
    [InlineData(MediaState.None, true)]
    [InlineData(MediaState.Failed, true)]
    [InlineData(MediaState.Declined, true)]
    [InlineData(MediaState.Partial, true)]
    [InlineData(MediaState.Pending, false)]
    [InlineData(MediaState.Approved, false)]
    [InlineData(MediaState.Processing, false)]
    [InlineData(MediaState.ImportPending, false)]
    [InlineData(MediaState.Available, false)]
    public void CanRequest_AvoidsRedundantRequests(MediaState state, bool expected)
        => Assert.Equal(expected, MediaStateResolver.CanRequest(state));

    [Fact]
    public void Season_AllEpisodesPresent_IsAvailable()
        => Assert.Equal(MediaState.Available, MediaStateResolver.ResolveSeason(10, 10, null));

    [Fact]
    public void Season_SomeEpisodesMissing_IsPartial()
        => Assert.Equal(MediaState.Partial, MediaStateResolver.ResolveSeason(6, 10, 5));

    [Fact]
    public void Season_NothingInJellyfinButSeerrAvailable_IsImportPending()
        => Assert.Equal(MediaState.ImportPending, MediaStateResolver.ResolveSeason(0, 10, 5));

    [Fact]
    public void Series_AllSeasonsAvailable_IsAvailable()
        => Assert.Equal(
            MediaState.Available,
            MediaStateResolver.ResolveSeries(new[] { MediaState.Available, MediaState.Available }, 5, 2));

    [Fact]
    public void Series_OneSeasonAvailable_IsPartial_NotAvailable()
        => Assert.Equal(
            MediaState.Partial,
            MediaStateResolver.ResolveSeries(new[] { MediaState.Available, MediaState.None }, 4, 2));

    [Fact]
    public void Series_AddedToSonarrOnly_IsNotAvailable()
        => Assert.NotEqual(
            MediaState.Available,
            MediaStateResolver.ResolveSeries(new[] { MediaState.Processing, MediaState.Processing }, 3, 2));

    [Fact]
    public void Labels_AreNeverInventedPercentages()
    {
        foreach (var s in System.Enum.GetValues<MediaState>())
        {
            Assert.DoesNotContain("%", MediaStateResolver.Label(s));
        }
    }
}
