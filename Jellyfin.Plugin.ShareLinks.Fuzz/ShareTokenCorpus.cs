using System;
using System.Collections.Generic;
using System.Text;

namespace Jellyfin.Plugin.ShareLinks.Fuzz;

/// <summary>
/// The seed corpus, derived from the two constants that fix a token's shape (#19).
/// </summary>
/// <remarks>
/// <para>
/// Every seed below is computed from <see cref="ShareTokens.Alphabet"/> and
/// <see cref="ShareTokens.EncodedLength"/> rather than typed out. That is the
/// point of the clause it answers: a corpus written by hand records what the
/// encoding was on the day somebody wrote it, and the day the encoding changes it
/// goes on seeding the fuzzer with the old shape while looking maintained.
/// </para>
/// <para>
/// Seeds are bytes, not strings, because that is what libFuzzer hands the target
/// and what a request carries. The harness decodes them as UTF-8, which is what
/// the route does with a path segment, so a seed that is not valid UTF-8 is a
/// legitimate case rather than a broken file.
/// </para>
/// </remarks>
public static class ShareTokenCorpus
{
    /// <summary>
    /// The offset the shape-breaking seeds put their bad byte at.
    /// </summary>
    /// <remarks>
    /// The middle rather than either end. A bad byte at the front is refused by
    /// anything that looks at the first character and a bad byte at the back by
    /// anything that looks at the last, so both are found by a check that is
    /// nowhere near right.
    /// </remarks>
    private const int Midpoint = ShareTokens.EncodedLength / 2;

    /// <summary>
    /// Gets the seeds, keyed by the file name each is committed under.
    /// </summary>
    /// <remarks>
    /// The names are what a reader sees in a crash report and in the corpus
    /// directory, so they say what the seed is rather than numbering it.
    /// </remarks>
    /// <returns>Every seed, in a fixed order.</returns>
    public static IReadOnlyDictionary<string, byte[]> Seeds()
    {
        var seeds = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            // The shape a real token has. Nothing in the tree accepts it, because it
            // names no record, and that is the case a fuzzer spends most of its time
            // near.
            ["a-token-shaped-string"] = Utf8(ShapedLikeAToken(ShareTokens.EncodedLength)),

            // Nothing at all. `Resolve` answers this before it looks at a record.
            ["nothing-at-all"] = Array.Empty<byte>(),

            // One character. The shortest thing that is not the case above, and the
            // one that finds an index into the presented token.
            ["one-character"] = Utf8(ShapedLikeAToken(1)),

            // One short and one long. Either side of the only length this encoding
            // produces, which is where an off-by-one lives.
            ["one-character-short"] = Utf8(ShapedLikeAToken(ShareTokens.EncodedLength - 1)),
            ["one-character-over"] = Utf8(ShapedLikeAToken(ShareTokens.EncodedLength + 1)),

            // Oversized, and the size is derived rather than picked: one character
            // per alphabet member per token position.
            ["oversized"] = Utf8(ShapedLikeAToken(ShareTokens.Alphabet.Length * ShareTokens.EncodedLength)),

            // A character the alphabet does not hold, at the midpoint. Derived by
            // asking the alphabet rather than by naming a character, so it stays
            // outside the alphabet on the day the alphabet changes.
            ["outside-the-alphabet"] = Utf8(WithCharacterAt(Midpoint, FirstPrintableCharacterOutsideTheAlphabet())),

            // Base64 padding. A token-shaped string is unpadded, so a decoder that
            // accepts padding accepts something this encoding never mints.
            ["base64-padding"] = Utf8(ShapedLikeAToken(ShareTokens.EncodedLength) + "="),

            // A null byte in the middle. This is the seed that separates a string
            // from a C string, and it is the one the issue names by hand.
            ["embedded-null"] = Utf8(WithCharacterAt(Midpoint, '\0')),

            // Bytes that are not valid UTF-8 at all. The decode in the harness is
            // the same one a request goes through, so what this seeds is what
            // happens to a path segment nobody could have typed.
            ["not-valid-utf8"] = NotValidUtf8(),
        };

        return seeds;
    }

    /// <summary>
    /// A string of the given length drawn from the alphabet in order.
    /// </summary>
    /// <remarks>
    /// In order rather than at random. A corpus has to be the same bytes on every
    /// machine and in every run, or the committed files and the derivation stop
    /// agreeing for a reason nobody can read out of the diff.
    /// </remarks>
    /// <param name="length">How many characters.</param>
    /// <returns>The string.</returns>
    private static string ShapedLikeAToken(int length)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(ShareTokens.Alphabet[i % ShareTokens.Alphabet.Length]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A token-shaped string with one character replaced.
    /// </summary>
    /// <param name="offset">Where the replacement goes.</param>
    /// <param name="replacement">What goes there.</param>
    /// <returns>The string.</returns>
    private static string WithCharacterAt(int offset, char replacement)
    {
        var characters = ShapedLikeAToken(ShareTokens.EncodedLength).ToCharArray();
        characters[offset] = replacement;
        return new string(characters);
    }

    /// <summary>
    /// The first printable ASCII character the alphabet does not hold.
    /// </summary>
    /// <returns>The character.</returns>
    private static char FirstPrintableCharacterOutsideTheAlphabet()
    {
        for (var candidate = '!'; candidate <= '~'; candidate++)
        {
            if (!ShareTokens.Alphabet.Contains(candidate, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "the alphabet holds every printable ASCII character, so there is no seed outside it");
    }

    /// <summary>
    /// A token-length run of bytes that no UTF-8 decoder accepts.
    /// </summary>
    /// <remarks>
    /// <c>0xFF</c> is not a legal byte anywhere in UTF-8, so a run of them is
    /// invalid however it is split.
    /// </remarks>
    /// <returns>The bytes.</returns>
    private static byte[] NotValidUtf8()
    {
        var bytes = new byte[ShareTokens.EncodedLength];
        Array.Fill(bytes, (byte)0xFF);
        return bytes;
    }

    /// <summary>
    /// The UTF-8 bytes of a seed string.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
