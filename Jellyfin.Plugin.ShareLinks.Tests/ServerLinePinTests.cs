using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The server-line pins in <c>Directory.Build.props</c> are the ones
/// <c>docs/versioning.md</c> declares (#340).
/// </summary>
/// <remarks>
/// <para>
/// Neither pin follows the newest thing published, and both of them look wrong to
/// somebody reading only the value. The 10.11 pin is the floor <c>build.yaml</c>
/// names in <c>targetAbi</c>, because an assembly compiled against anything newer
/// binds types that floor does not have (#136). The 12.0 pin is the fifth of seven
/// candidates, and it stays there until a <c>12.0.0</c> exists, because every
/// candidate on that line stamps the same assembly version - so a pin that follows
/// candidates moves the compiled surface without moving anything a manifest or a
/// floor check can see (#340).
/// </para>
/// <para>
/// What this holds is that the two files agree. A pin moved without the document
/// moving with it reds here, which is the whole point of writing the rule down: the
/// next reader meets a decision rather than a value, and a bump that was dragged
/// along by a dependency update cannot land quietly.
/// </para>
/// <para>
/// The bound is that agreement and nothing more. This cannot tell a considered move
/// from a careless one, and it does not ask a package registry what exists - no test
/// in this repository reaches the network, which <c>docs/testing.md</c> fixes. The
/// reason a pin moved is what the commit message and the pull-request body are for.
/// </para>
/// <para>
/// NOTHING HERE NOTICES THE DAY <c>12.0.0</c> IS RELEASED, and that is the half of
/// the rule this cannot carry. A test asserting the pin is still a candidate was
/// written and taken out again: the fixture that would prove it bites has to pin a
/// release, no release on that line exists, and a restore against a version nuget
/// does not hold fails before any assertion runs. So the end of the rule is a thing
/// somebody reads, and what this holds is that whoever moves the pin moves the
/// sentence with it.
/// </para>
/// </remarks>
public sealed class ServerLinePinTests
{
    // The framework condition and the version it selects, off one line of the
    // property file. Kept on one line in that file on purpose, which the comment
    // above the version properties there says.
    private static readonly Regex Pin = new(
        @"<JellyfinVersion Condition=""'\$\(TargetFramework\)' == '(?<framework>[^']+)'"">(?<version>[^<]+)</JellyfinVersion>",
        RegexOptions.Compiled);

    // The sentence in the document that names both, so the two values a reader is
    // told to expect are the two values the property file holds.
    private static readonly Regex Declared = new(
        @"pins this repository holds while that rule is in force are `(?<net9>[^`]+)` for\s+`net9\.0` and `(?<net10>[^`]+)` for `net10\.0`",
        RegexOptions.Compiled);

    private static string ReadBesideTheAssembly(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(File.Exists(path), $"{name} was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    private static IReadOnlyDictionary<string, string> PinsInThePropertyFile()
    {
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Pin.Matches(ReadBesideTheAssembly("Directory.Build.props")))
        {
            pins[match.Groups["framework"].Value] = match.Groups["version"].Value.Trim();
        }

        // A reading that matched nothing would agree with any document, which is the
        // silence this repository refuses elsewhere.
        Assert.Equal(2, pins.Count);
        return pins;
    }

    [Fact]
    public void TheDocumentNamesThePinsThePropertyFileHolds()
    {
        var document = ReadBesideTheAssembly(Path.Combine("docs", "versioning.md"));
        var declared = Declared.Match(document);
        Assert.True(declared.Success, "docs/versioning.md names no server-line pins for this to compare against");

        var pins = PinsInThePropertyFile();

        Assert.Equal(declared.Groups["net9"].Value, pins["net9.0"]);
        Assert.Equal(declared.Groups["net10"].Value, pins["net10.0"]);
    }
}
