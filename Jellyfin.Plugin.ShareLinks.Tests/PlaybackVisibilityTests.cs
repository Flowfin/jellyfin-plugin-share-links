using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The members <c>docs/playback-visibility.md</c> names still exist on the server
/// this plugin compiles against, with the types it states, and the absence that
/// page rests on is still an absence (#59).
/// </summary>
/// <remarks>
/// <para>
/// The page says what a guest's viewing leaves behind and who can read it, and
/// every one of those statements is a claim about another artefact. A server line
/// that renames <c>PlaybackPositionTicks</c> or drops <c>RemoteEndPoint</c> turns
/// the page into a description of somewhere else, and nothing else in this
/// repository would notice.
/// </para>
/// <para>
/// So the page itself is the input. The rows are read out of the document rather
/// than copied into this file, because a copy is a second list that drifts the
/// first time somebody adds a row and stops before the test, which is the defect
/// <see cref="ConfigurationReferenceTests"/> exists for one document further along.
/// </para>
/// <para>
/// The second test is the one worth reading twice. The page rests on an absence,
/// that the user configuration offers no switch which suppresses playback progress,
/// and an absence rots by something arriving rather than by something going.
/// Pinning the whole set is what makes the arrival visible. It reds on any change
/// to that type, which is deliberate: the answer is cheap, and the page being
/// re-read is the point.
/// </para>
/// <para>
/// What is not here is a test of behaviour. There is no route for a guest to
/// travel and no guest account to travel as it, so nothing drives playback and
/// asserts what was written. The page says so in its own words rather than leaving
/// a reader to take these names for a run.
/// </para>
/// </remarks>
public class PlaybackVisibilityTests
{
    // The types the page is allowed to be about. A carrier the page names and this
    // map does not hold is a failure rather than a row quietly skipped, because a
    // skipped row is an unchecked claim wearing a green suite.
    private static readonly IReadOnlyDictionary<string, Type> Carriers = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["UserItemData"] = typeof(UserItemData),
        ["UserItemDataDto"] = typeof(UserItemDataDto),
        ["SessionInfo"] = typeof(SessionInfo),
        ["ActivityLogEntry"] = typeof(ActivityLogEntry),
    };

    // The user configuration as it stands on the server line this plugin compiles
    // against. This is not a list of things the page approves of; it is the surface
    // the page's negative claim was read off, held so that a member arriving on it
    // is a red suite rather than a sentence that quietly stopped being true.
    private static readonly string[] UserConfigurationMembers =
    [
        "AudioLanguagePreference",
        "CastReceiverId",
        "DisplayCollectionsView",
        "DisplayMissingEpisodes",
        "EnableLocalPassword",
        "EnableNextEpisodeAutoPlay",
        "GroupedFolders",
        "HidePlayedInLatest",
        "LatestItemsExcludes",
        "MyMediaExcludes",
        "OrderedViews",
        "PlayDefaultAudioTrack",
        "RememberAudioSelections",
        "RememberSubtitleSelections",
        "SubtitleLanguagePreference",
        "SubtitleMode",
    ];

    /// <summary>
    /// Gets one case per row of the tables in the document: the carrier as the page
    /// spells it, the member, and the type the page states.
    /// </summary>
    public static TheoryData<string, string, string> DocumentedMembers
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var row in Rows())
            {
                data.Add(row.Carrier, row.Member, row.StatedType);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DocumentedMembers))]
    public void TheMemberTheDocumentNamesExistsWithTheTypeItStates(string carrier, string member, string statedType)
    {
        Assert.True(
            Carriers.TryGetValue(carrier, out var owner),
            $"docs/playback-visibility.md names {carrier}.{member} and this test knows no type called {carrier}. "
            + "Add it to Carriers, because a row nothing resolves is a claim nothing checks.");

        var property = owner!.GetProperty(member);

        Assert.True(
            property is not null,
            $"docs/playback-visibility.md names {carrier}.{member} and no such member exists. "
            + "The server line moved under the document: "
            + string.Join(", ", owner.GetProperties().Select(candidate => candidate.Name).OrderBy(candidate => candidate, StringComparer.Ordinal)));

        Assert.Equal(statedType, Spell(property!.PropertyType));
    }

    [Fact]
    public void TheDocumentStillHasRowsToCheck()
    {
        // Without this every case above passes on a document whose tables were
        // deleted, reworded or reformatted out of the shape this file parses, and
        // an empty theory is a green run that judged nothing.
        Assert.True(Rows().Count >= 20, $"the tables in docs/playback-visibility.md parsed to {Rows().Count} rows");
    }

    [Fact]
    public void TheUserConfigurationStillOffersNoSwitchThatSuppressesPlaybackProgress()
    {
        var actual = typeof(UserConfiguration)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(UserConfigurationMembers, actual);
    }

    private static string Document()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "playback-visibility.md");
        Assert.True(File.Exists(path), $"playback-visibility.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    // A row is a table line whose second cell is a backticked `Type.Member`. The
    // heading and the separator are not, so neither has to be skipped by position,
    // and a paragraph or another table between them changes nothing.
    private static IReadOnlyList<(string Carrier, string Member, string StatedType)> Rows() =>
        Document()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length >= 3)
            .Select(cells => (Cells: cells, Match: Regex.Match(cells[1], @"^`([A-Za-z][A-Za-z0-9]*)\.([A-Za-z][A-Za-z0-9]*)`$")))
            .Where(row => row.Match.Success)
            .Select(row => (
                row.Match.Groups[1].Value,
                row.Match.Groups[2].Value,
                row.Cells[2].Trim('`')))
            .ToList();

    // The type as C# spells it, so the document holds something a reader can compare
    // against source rather than a runtime name nobody writes. `Nullable`1` was what
    // stood here before, and it made `DateTime?`, `double?` and `bool?` one answer,
    // which is three claims collapsed into a check none of them fails.
    private static string Spell(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return Spell(underlying) + "?";
        }

        return type.Name switch
        {
            "Int64" => "long",
            "Int32" => "int",
            "Boolean" => "bool",
            "Double" => "double",
            "String" => "string",
            _ => type.Name,
        };
    }
}
