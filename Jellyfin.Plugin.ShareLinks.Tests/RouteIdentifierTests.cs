using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// No identifier a route of this plugin hands out counts (#26).
/// </summary>
/// <remarks>
/// <para>
/// The failure this is written against is the one #26's first paragraph names: an
/// identifier that counts upwards tells whoever receives it how many of the thing
/// exist, and a second request tells them how fast it is growing. It costs nothing
/// to get right and it cannot be repaired afterwards, because the numbers are in
/// whatever read them.
/// </para>
/// <para>
/// The guest route's half is held where that route's own answer is pinned:
/// <c>GuestRouteTests.ACallerTheShareNamesIsSentToTheItem</c> compares the whole
/// address rather than a part of it, so nothing of this plugin's could join it
/// without reddening that test. What is left is the administrator surface, which
/// arrived with #67, and it is the half that matters more: it hands out an
/// identifier per share rather than one per request.
/// </para>
/// <para>
/// What this does not reach. It reads the type a route answers with and the
/// routine that fills it in, so it says nothing about a value the server itself
/// composes, and nothing about an identifier a share is given when one is created,
/// because no route of this plugin's creates one.
/// </para>
/// </remarks>
public class RouteIdentifierTests
{
    /// <summary>
    /// The members of the administrator surface that carry an identifier, found by
    /// their name rather than listed here, so a member added later is judged by
    /// this without anybody remembering to add it.
    /// </summary>
    /// <returns>The identifier members.</returns>
    private static IReadOnlyList<PropertyInfo> IdentifierMembers() =>
        typeof(ShareSummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(member => member.Name.EndsWith("Id", StringComparison.Ordinal)
                || member.Name.EndsWith("Ids", StringComparison.Ordinal))
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every identifier the administrator surface hands out is the server's own
    /// kind of identifier and not a number this plugin could have counted.
    /// </summary>
    /// <remarks>
    /// A hundred and twenty eight bits drawn as an identifier says nothing about
    /// how many there are or when this one was made, which is the whole property.
    /// An integer in any of these positions says both, whatever it is called.
    /// </remarks>
    [Fact]
    public void EveryIdentifierTheAdministratorSurfaceHandsOutIsTheServersOwnKind()
    {
        var members = IdentifierMembers();

        // A surface carrying none would satisfy every assertion below by having
        // nothing to fail, which is the reading #26 refuses by name.
        Assert.NotEmpty(members);

        foreach (var member in members)
        {
            var carried = member.PropertyType == typeof(IReadOnlyList<Guid>)
                ? typeof(Guid)
                : Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;

            Assert.Equal(typeof(Guid), carried);
        }
    }

    /// <summary>
    /// Nothing about where a record sits in the store can reach a row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the same property one step behind the type. A row that carried no
    /// counting identifier of its own would still be counted if the routine that
    /// built it were handed the position it was read at, and the shape that
    /// arrives is a loop index passed along because it was already in hand.
    /// </para>
    /// <para>
    /// So the routine is required to be a function of the record and the instant
    /// alone. The instant is there because a state is a comparison against a
    /// clock; it is the same value for every row of one listing and orders
    /// nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingAboutWhereARecordSitsInTheStoreCanReachARow()
        => Assert.Equal(
            new[] { typeof(ShareRecord), typeof(DateTimeOffset) },
            typeof(ShareSummary)
                .GetMethod(nameof(ShareSummary.Of), BindingFlags.Public | BindingFlags.Static)!
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray());

    /// <summary>
    /// The record behind the surface holds no counting identifier either, so there
    /// is nothing for a later row to be widened with.
    /// </summary>
    /// <remarks>
    /// The record is read here rather than only the summary because the summary is
    /// chosen from it. A counter on the record is a counter one argued-for member
    /// away from a route, and the argument that would add it is that the value was
    /// already there.
    /// </remarks>
    [Fact]
    public void TheRecordBehindItCarriesNoCountingIdentifierEither()
    {
        var members = typeof(ShareRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(member => member.Name.EndsWith("Id", StringComparison.Ordinal)
                || member.Name.EndsWith("Ids", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(members);

        foreach (var member in members)
        {
            var carried = member.PropertyType == typeof(IReadOnlyList<Guid>)
                ? typeof(Guid)
                : Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;

            Assert.Equal(typeof(Guid), carried);
        }
    }

    /// <summary>
    /// This plugin draws no identifier of its own for anything it hands out. The
    /// one draw in the sources names a temporary file, which never leaves the
    /// machine the store is on.
    /// </summary>
    /// <remarks>
    /// Read off the record rather than off the source: every identifier on it is
    /// required and arrives from the caller, so there is no routine here that could
    /// mint the next one in a sequence. When a route that creates a share arrives,
    /// it is the thing that mints one, and this is the assertion it will meet.
    /// </remarks>
    [Fact]
    public void TheIdentifierOfAShareIsGivenToTheRecordRatherThanWrittenIntoIt()
    {
        var identifier = typeof(ShareRecord).GetProperty(nameof(ShareRecord.Id), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(identifier);
        Assert.Equal(typeof(Guid), identifier.PropertyType);

        // Init-only, so nothing renumbers a record that is already written down and
        // no routine here can hand out the next one in a run.
        Assert.Contains(
            typeof(System.Runtime.CompilerServices.IsExternalInit),
            identifier.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
    }
}
