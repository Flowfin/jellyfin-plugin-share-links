using System.Buffers.Text;
using System.Collections.Generic;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The token is the whole of what a link carries, so its shape is asserted
/// exactly rather than approximately. A test that accepts "about the right
/// length, mostly the right characters" accepts a token half of which is a
/// constant.
/// </summary>
public class ShareTokenTests
{
    [Fact]
    public void TheDeclaredLengthIsWhatTheEncodingActuallyProduces()
    {
        // The two constants are one decision written twice, and the second copy is
        // the one that goes stale. Derive the length from the entropy here, so
        // changing the entropy without changing the length reds this rather than
        // shipping a declared length no token has.
        var encoded = Base64Url.EncodeToString(new byte[ShareTokens.EntropyBytes]);

        Assert.Equal(ShareTokens.EncodedLength, encoded.Length);
    }

    [Fact]
    public void AMintedTokenIsExactlyTheDeclaredLength()
    {
        Assert.Equal(ShareTokens.EncodedLength, ShareTokens.Mint().Length);
    }

    [Fact]
    public void AMintedTokenUsesOnlyTheDeclaredAlphabet()
    {
        // Every character has to be unreserved in a URI. One that is not is a
        // token that a link rewriter may percent-encode and hand back different.
        var token = ShareTokens.Mint();

        Assert.All(token, character => Assert.Contains(character, ShareTokens.Alphabet));
    }

    [Fact]
    public void TheDeclaredAlphabetIsTheUrlSafeOneWithNoPadding()
    {
        // Asserting the token's characters are in the alphabet says nothing about
        // the alphabet itself; a declared alphabet containing '+' and '=' would
        // pass that. This pins the alphabet to RFC 4648 section 5.
        Assert.Equal(64, ShareTokens.Alphabet.Length);
        Assert.DoesNotContain('+', ShareTokens.Alphabet);
        Assert.DoesNotContain('/', ShareTokens.Alphabet);
        Assert.DoesNotContain('=', ShareTokens.Alphabet);
        Assert.Contains('-', ShareTokens.Alphabet);
        Assert.Contains('_', ShareTokens.Alphabet);
    }

    [Fact]
    public void AMintedTokenCarriesTheDeclaredEntropy()
    {
        // A token of the right length and alphabet can still be short: encoding
        // sixteen bytes and padding the string out reaches the same length. Decode
        // it and count the bytes that were actually drawn.
        var bytes = Base64Url.DecodeFromChars(ShareTokens.Mint());

        Assert.Equal(ShareTokens.EntropyBytes, bytes.Length);
    }

    [Fact]
    public void ALargeBatchOfTokensContainsNoDuplicate()
    {
        // A duplicate in a batch this size is not the birthday bound arriving. At
        // 256 bits that bound is unreachable, so a duplicate here means the source
        // is not what it claims: a fixed seed, a short cycle, or a constant in
        // place of a draw. This is the test that reds when the generator is
        // swapped for something predictable and the two shape tests do not.
        const int Batch = 100_000;

        var seen = new HashSet<string>(Batch);
        for (var i = 0; i < Batch; i++)
        {
            seen.Add(ShareTokens.Mint());
        }

        Assert.Equal(Batch, seen.Count);
    }
}
