using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A document that pastes <c>path:line:text</c> under the command that produced
/// it is handing the reader evidence, and the line number in it rots on its own:
/// anybody inserting a line above the one quoted moves it, the page goes on
/// naming the old number, and every route stays green because a paste is prose.
/// M10 asks that every claim the documentation makes about behaviour match a
/// test that exists (#81), and a pasted reading is the claim a reader trusts
/// most.
/// </summary>
/// <remarks>
/// <para>
/// <c>DocumentedProofsTests</c> is the neighbour and a different subject. It
/// resolves a backticked test name against the compiled assembly; this re-reads
/// a pasted file reference against the file. Eleven of the forty-eight such
/// references in this tree did not reproduce on the day this landed, and none of
/// them was found by a check: they were found by running the commands.
/// </para>
/// <para>
/// The subject is a line inside an indented block that reads as
/// <c>path:number:text</c>, where the path ends in an extension this repository
/// tracks. That shape is what <c>grep -n</c> and <c>git grep -n</c> emit, and it
/// is the only form judged here. A paste written without <c>-n</c> carries no
/// number to go stale and is outside this subject rather than exempted by it,
/// which is also the cheapest repair for a reference that keeps drifting.
/// </para>
/// <para>
/// What this cannot judge. The comparison is between the trimmed pasted text and
/// the trimmed source line, so a change to leading whitespace alone passes here.
/// Whether the line quoted is the right line for the sentence above it is a
/// judgement about meaning that no reading of the tree makes. And a document
/// that describes a file without quoting one is invisible to this, exactly as a
/// document naming no test is invisible to the neighbour named above.
/// </para>
/// </remarks>
public class DocumentedPastesTests
{
    // What `grep -n` and `git grep -n` emit, inside a markdown indented block.
    // The extension list is what stops an ordinary sentence containing a colon
    // and a number from being read as a paste.
    private const string PastedReference =
        @"^\s+(?<path>[A-Za-z0-9_][A-Za-z0-9_./-]*\.(?:cs|md|yml|yaml|html|props|json|txt|sh|sln|csproj|ruleset)):(?<line>\d+):(?<text>.*)$";

    [Fact]
    public void EveryPastedFileReferenceInADocumentStillReadsThatWay()
    {
        var root = RepositoryRoot();
        var pasted = PastedReferences(root);

        // A run that matched nothing would walk the loop below without asserting
        // anything and report success, which is the state this file exists to
        // refuse.
        Assert.NotEmpty(pasted);

        foreach (var (document, at, path, line, text) in pasted)
        {
            var target = Path.Join(root, path.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(
                File.Exists(target),
                $"{document}:{at} quotes {path}:{line}, and this tree has no file at {path}");

            var source = File.ReadAllLines(target);

            Assert.True(
                line <= source.Length,
                $"{document}:{at} quotes {path}:{line}, and {path} has {source.Length} lines");

            Assert.True(
                string.Equals(source[line - 1].Trim(), text.Trim(), StringComparison.Ordinal),
                $"{document}:{at} quotes {path}:{line} as{Environment.NewLine}"
                + $"  {text.Trim()}{Environment.NewLine}"
                + $"and that line reads{Environment.NewLine}"
                + $"  {source[line - 1].Trim()}");
        }
    }

    private static IReadOnlyList<(string Document, int At, string Path, int Line, string Text)> PastedReferences(string root)
    {
        var pasted = new List<(string Document, int At, string Path, int Line, string Text)>();

        foreach (var document in Documents(root))
        {
            var name = Path.GetRelativePath(root, document).Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadAllLines(document);

            for (var number = 0; number < lines.Length; number++)
            {
                var match = Regex.Match(lines[number], PastedReference);
                if (!match.Success)
                {
                    continue;
                }

                pasted.Add((
                    name,
                    number + 1,
                    match.Groups["path"].Value,
                    int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    match.Groups["text"].Value));
            }
        }

        return pasted;
    }

    // The whole directory rather than a list, so a document written tomorrow is
    // judged without anybody remembering to add it here. The readme is beside it
    // because it pastes the same kind of evidence to the same reader.
    private static IReadOnlyList<string> Documents(string root)
    {
        var directory = Path.Join(root, "docs");
        Assert.True(Directory.Exists(directory), $"the docs directory is not under the repository root: {directory}");

        var documents = new List<string>(Directory.GetFiles(directory, "*.md"));
        documents.Sort(StringComparer.Ordinal);

        var readme = Path.Join(root, "README.md");
        Assert.True(File.Exists(readme), $"README.md is not at the repository root: {readme}");
        documents.Add(readme);

        return documents;
    }

    // The files quoted are the tree's own sources rather than anything copied
    // beside the test assembly, so this walks up to the checkout instead of
    // reading the output directory. The solution file is what marks the root, and
    // a run that cannot find it fails here rather than passing over no documents.
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "Jellyfin.Plugin.ShareLinks.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"no directory at or above {AppContext.BaseDirectory} carries Jellyfin.Plugin.ShareLinks.sln, so the sources the documents quote cannot be read.");
    }
}
