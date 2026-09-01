using System;
using System.IO;
using System.Linq;
using SharpFuzz;

namespace Jellyfin.Plugin.ShareLinks.Fuzz;

/// <summary>
/// The entry point libFuzzer drives, and the one that writes the seed corpus (#19).
/// </summary>
/// <remarks>
/// Two modes in one executable so the corpus a run is seeded from and the corpus
/// committed to the tree come out of the same derivation. Two programs would be
/// two derivations, and they would agree until one of them was edited.
/// </remarks>
public static class Program
{
    /// <summary>
    /// Runs the harness.
    /// </summary>
    /// <param name="args">
    /// <c>emit &lt;directory&gt;</c> writes the seed corpus and exits. Anything
    /// else hands the target to libFuzzer, which is what the workflow does.
    /// </param>
    /// <returns>Zero, or one when the arguments were not understood.</returns>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            Fuzzer.LibFuzzer.Run(ShareTokenFuzzTarget.Run);
            return 0;
        }

        if (args.Length == 2 && string.Equals(args[0], "emit", StringComparison.Ordinal))
        {
            return Emit(args[1]);
        }

        Console.Error.WriteLine("usage: Jellyfin.Plugin.ShareLinks.Fuzz [emit <directory>]");
        return 1;
    }

    /// <summary>
    /// Writes the seed corpus into a directory, and removes what does not belong.
    /// </summary>
    /// <remarks>
    /// Removing is the half that matters. A seed left behind from an older
    /// derivation is a file the fuzzer goes on reading and the tree goes on
    /// carrying, and it is invisible in a diff that only ever adds.
    /// </remarks>
    /// <param name="directory">Where the seeds go.</param>
    /// <returns>Zero.</returns>
    private static int Emit(string directory)
    {
        Directory.CreateDirectory(directory);
        var seeds = ShareTokenCorpus.Seeds();

        var stale = Directory
            .GetFiles(directory)
            .Where(file => !seeds.ContainsKey(Path.GetFileName(file)));

        foreach (var file in stale)
        {
            File.Delete(file);
            Console.WriteLine(
                FormattableString.Invariant(
                    $"removed {Path.GetFileName(file)}, which the derivation no longer produces"));
        }

        foreach (var (name, bytes) in seeds)
        {
            File.WriteAllBytes(Path.Join(directory, name), bytes);
            Console.WriteLine(FormattableString.Invariant($"{name}: {bytes.Length} byte(s)"));
        }

        return 0;
    }
}
