using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A document that names a test is telling a reader where to go and check, and
/// the name rots on its own. A test renamed or deleted leaves the sentence
/// reading exactly as it did on the day it was true, and every route stays green,
/// because a name in a backtick is prose. M10 asks that every claim the
/// documentation makes about behaviour match a test that exists (#81), and this
/// is the half of that a machine can hold: where a document names the test, the
/// name resolves.
/// </summary>
/// <remarks>
/// <para>
/// <c>ThreatModelTests.EveryTestNamedAsAProofExistsInThisAssembly</c> already
/// does this for the proof column of one document. This reaches every document
/// under <c>docs/</c> and the readme, and it was written because a name in one
/// of them had already gone stale: <c>docs/account-restoration.md</c> named a
/// tripwire under the name it carried before <c>fd319d8</c> narrowed and renamed
/// it, so a reader following the name found nothing in the suite.
/// </para>
/// <para>
/// The subject is a backticked name ending in <c>Tests</c>, alone or followed by
/// one member: <c>`XTests`</c> and <c>`XTests.Method`</c>. That shape is a
/// pointer somebody is meant to follow. A bare identifier in a backtick is not
/// judged here and the bound is deliberate: a document writes bare identifiers
/// for types, members and states that are not tests at all, and the one table
/// that carries bare proof names is judged by the file named above.
/// </para>
/// <para>
/// What this cannot judge is whether a test it resolves still proves the sentence
/// beside it. A control that moves while its test goes on compiling leaves this
/// green, which is the same residual the threat model states about its own proof
/// column, and it is a judgement about meaning rather than a reading of the tree.
/// </para>
/// </remarks>
public class DocumentedProofsTests
{
    // `XTests` or `XTests.Method`, in backticks. The trailing Tests is what
    // separates a pointer into the suite from an ordinary identifier, and the
    // remarks above say what that leaves out.
    private const string NamedTest = "`(?<type>[A-Za-z][A-Za-z0-9_]*Tests)(?:\\.(?<member>[A-Za-z_][A-Za-z0-9_]*))?`";

    [Fact]
    public void EveryTestNamedInADocumentExistsInThisAssembly()
    {
        var classes = typeof(DocumentedProofsTests).Assembly
            .GetTypes()
            .Where(type => type.IsClass)
            .ToLookup(type => type.Name, StringComparer.Ordinal);

        var named = NamedTests();

        // A run that resolved no name at all would walk the loop below without
        // asserting anything and report success, which is the state this file
        // exists to refuse.
        Assert.NotEmpty(named);

        foreach (var (document, line, type, member) in named)
        {
            var candidates = classes[type].ToList();
            Assert.True(
                candidates.Count > 0,
                $"{document}:{line} names {type}, and no class in this assembly is called that");

            if (member is null)
            {
                continue;
            }

            var resolves = candidates.Any(candidate => candidate
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(method => string.Equals(method.Name, member, StringComparison.Ordinal)));

            Assert.True(
                resolves,
                $"{document}:{line} names {type}.{member}, and {type} in this assembly has no member called that");
        }
    }

    private static IReadOnlyList<(string Document, int Line, string Type, string? Member)> NamedTests()
    {
        var named = new List<(string Document, int Line, string Type, string? Member)>();

        foreach (var path in Documents())
        {
            var document = Path.GetFileName(path);
            var lines = File.ReadAllLines(path);

            for (var number = 0; number < lines.Length; number++)
            {
                foreach (Match match in Regex.Matches(lines[number], NamedTest))
                {
                    var member = match.Groups["member"];
                    named.Add((document, number + 1, match.Groups["type"].Value, member.Success ? member.Value : null));
                }
            }
        }

        return named;
    }

    // The whole directory rather than a list, so a document written tomorrow is
    // judged without anybody remembering to add it here. The readme is beside it
    // because it makes the same kind of claim to the same reader.
    private static IReadOnlyList<string> Documents()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "docs");
        Assert.True(Directory.Exists(directory), $"the docs directory was not copied next to the test assembly: {directory}");

        var readme = Path.Combine(AppContext.BaseDirectory, "README.md");
        Assert.True(File.Exists(readme), $"README.md was not copied next to the test assembly: {readme}");

        var documents = Directory.GetFiles(directory, "*.md")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Append(readme)
            .ToList();

        Assert.True(documents.Count > 1, $"{directory} holds no document to read");
        return documents;
    }
}
