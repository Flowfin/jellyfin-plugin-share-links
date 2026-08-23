using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.ShareLinks.Fuzz;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The committed seed corpus is the derivation's output, and the fuzz target's
/// property holds over it (#19).
/// </summary>
/// <remarks>
/// <para>
/// The corpus is committed because the weekly job has to start somewhere, and a
/// committed corpus is a set of bytes nobody re-reads. Two failures follow from
/// that and both are refused here: a seed edited by hand so the tree no longer
/// says what the derivation says, and a derivation edited so the committed files
/// are the old encoding's.
/// </para>
/// <para>
/// The second half runs the target over every seed. That is the only thing in
/// this repository that executes the fuzz harness on a pull request, and what it
/// buys is the case the whole issue is written against: a scheduled job pointed
/// at a routine that has been renamed, or at an oracle that has stopped meaning
/// anything, reports exactly what a clean run reports.
/// </para>
/// </remarks>
public sealed class FuzzCorpusTests
{
    private static string CorpusDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "corpus");
        Assert.True(Directory.Exists(path), $"the seed corpus was not copied next to the test assembly: {path}");
        return path;
    }

    private static IReadOnlyDictionary<string, byte[]> Committed()
    {
        var committed = Directory.GetFiles(CorpusDirectory())
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes, StringComparer.Ordinal);

        // A run that read no file would pass every comparison below it.
        Assert.NotEmpty(committed);
        return committed;
    }

    /// <summary>
    /// Every committed seed is a byte-for-byte match for what the derivation
    /// produces, and there are no others.
    /// </summary>
    [Fact]
    public void TheCommittedCorpusIsWhatTheDerivationProduces()
    {
        var derived = ShareTokenCorpus.Seeds();
        var committed = Committed();

        Assert.Equal(
            derived.Keys.OrderBy(name => name, StringComparer.Ordinal),
            committed.Keys.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var (name, expected) in derived)
        {
            Assert.True(
                expected.SequenceEqual(committed[name]),
                $"the committed seed '{name}' is not what the derivation produces. Regenerate with: dotnet run --project Jellyfin.Plugin.ShareLinks.Fuzz -- emit Jellyfin.Plugin.ShareLinks.Fuzz/corpus");
        }
    }

    /// <summary>
    /// The corpus is derived from the two constants that fix a token's shape, so
    /// it moves when they move.
    /// </summary>
    /// <remarks>
    /// Read off the committed bytes rather than off the derivation, because a
    /// derivation compared against itself agrees whatever it says. The lengths
    /// below are the shape statements the issue's seed list is made of.
    /// </remarks>
    [Fact]
    public void TheCorpusIsShapedByTheEncodingRatherThanByAHand()
    {
        var committed = Committed();

        Assert.Equal(ShareTokens.EncodedLength, committed["a-token-shaped-string"].Length);
        Assert.Equal(ShareTokens.EncodedLength - 1, committed["one-character-short"].Length);
        Assert.Equal(ShareTokens.EncodedLength + 1, committed["one-character-over"].Length);
        Assert.Equal(ShareTokens.Alphabet.Length * ShareTokens.EncodedLength, committed["oversized"].Length);
        Assert.Empty(committed["nothing-at-all"]);

        var valid = Encoding.UTF8.GetString(committed["a-token-shaped-string"]);
        Assert.All(valid, character => Assert.True(ShareTokens.Alphabet.Contains(character, StringComparison.Ordinal)));

        var outside = Encoding.UTF8.GetString(committed["outside-the-alphabet"]);
        Assert.Contains(outside, character => !ShareTokens.Alphabet.Contains(character, StringComparison.Ordinal));

        Assert.Contains((byte)0, committed["embedded-null"]);
        Assert.Contains((byte)'=', committed["base64-padding"]);
    }

    /// <summary>
    /// The target refuses every seed, and throws on none of them.
    /// </summary>
    [Fact]
    public void TheTargetRefusesEverySeed()
    {
        foreach (var (name, bytes) in Committed())
        {
            var thrown = Record.Exception(() => ShareTokenFuzzTarget.Run(bytes));
            Assert.True(thrown is null, $"the seed '{name}' broke the fuzz target: {thrown?.Message}");
        }
    }
}
