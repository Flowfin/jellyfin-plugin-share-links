using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What <c>docs/operator-guide.md</c> says the screens look like, judged against
/// the screen (#83).
/// </summary>
/// <remarks>
/// <para>
/// The guide's first clause is that every step names the screen or the route it
/// happens on, and the failure that clause is written against is silent: the page
/// gains a column, nobody edits the guide, and an operator matching one against
/// the other finds a column the page they were told to read does not mention.
/// That is what happened. The share view's in-force column landed on 2026-08-23
/// for #64, the guide's column sentence was written the day before, and it went
/// on naming six of the seven for a fortnight while every route stayed green.
/// </para>
/// <para>
/// So the guide's list is compared with the table's own headings, in order and in
/// both directions, because both directions are defects: a column the guide does
/// not name is one an operator does not know to read, and a column the guide
/// names that the screen does not have is a page describing software that is not
/// there. Nothing here renders the page or reaches a browser, which
/// <c>docs/testing.md</c> refuses; what is compared is the text a server hands out
/// and the text a person reads.
/// </para>
/// </remarks>
public class OperatorGuideTests
{
    private static readonly Assembly PluginAssembly = typeof(Plugin).Assembly;

    /// <summary>
    /// The configuration page as it ships, read out of the assembly rather than
    /// off disk, because the embedded copy is the one an operator is handed.
    /// </summary>
    /// <returns>The page.</returns>
    private static string Page()
    {
        using var stream = PluginAssembly.GetManifestResourceStream(
            string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", typeof(Plugin).Namespace));
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        return Lines(reader.ReadToEnd());
    }

    /// <summary>
    /// The guide, copied next to the test assembly by the project file.
    /// </summary>
    /// <returns>The guide.</returns>
    private static string Guide()
    {
        var path = Path.Join(AppContext.BaseDirectory, "docs", "operator-guide.md");
        Assert.True(File.Exists(path), "docs/operator-guide.md was not copied next to the test assembly: " + path);
        return Lines(File.ReadAllText(path));
    }

    /// <summary>
    /// One line ending, so an anchored pattern reads the same on a checkout that
    /// took carriage returns and on one that did not. A comparison that quietly
    /// found nothing on Windows would pass by reading an empty section, which is
    /// the failure the emptiness assertions below are for.
    /// </summary>
    /// <param name="text">The text as it was read.</param>
    /// <returns>The same text with no carriage returns.</returns>
    private static string Lines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// The headings of the share view's own table, in the order the screen puts
    /// them. The credentials table above it carries headings too, so the table is
    /// selected by its class rather than the page being scanned whole.
    /// </summary>
    /// <returns>The headings, with the action column's empty one dropped.</returns>
    private static IReadOnlyList<string> ColumnsTheScreenHeads()
    {
        var table = Regex.Match(Page(), @"<table class=""tblShares[^""]*"">(.*?)</thead>", RegexOptions.Singleline);
        Assert.True(table.Success, "the configuration page carries no share table with a heading row, so this comparison has nothing to read");

        var headings = Regex.Matches(table.Groups[1].Value, @"<th scope=""col"">([^<]*)</th>")
            .Select(match => match.Groups[1].Value.Trim())
            .Where(heading => heading.Length > 0)
            .ToList();

        // A table whose headings were renamed out of this shape would agree with
        // every assertion below by having nothing to disagree with, which is how
        // this file would pass on the day the comparison stopped reading anything.
        Assert.NotEmpty(headings);
        return headings;
    }

    /// <summary>
    /// The columns the guide's share-view step lists, in the order it lists them.
    /// </summary>
    /// <returns>The names the guide gives.</returns>
    private static IReadOnlyList<string> ColumnsTheGuideNames()
    {
        var section = Regex.Match(Guide(), @"^## 6\. Read the list$(.*?)^## ", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(section.Success, "docs/operator-guide.md carries no share-view step to read a column list out of");

        var named = Regex.Matches(section.Groups[1].Value, @"^- \*\*([^*]+)\.\*\* ", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value.Trim())
            .ToList();

        Assert.NotEmpty(named);
        return named;
    }

    /// <summary>
    /// Every column the share view heads is named in the guide's step for it, in
    /// the same order, and the guide names no column the screen does not have.
    /// </summary>
    [Fact]
    public void TheGuideNamesEveryColumnTheShareViewHeads()
    {
        var screen = ColumnsTheScreenHeads();
        var guide = ColumnsTheGuideNames();

        var missing = screen.Except(guide, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            "the share view heads columns docs/operator-guide.md does not name, so an operator reading the guide meets a column it never mentions: " + string.Join(", ", missing));

        var invented = guide.Except(screen, StringComparer.Ordinal).ToList();
        Assert.True(
            invented.Count == 0,
            "docs/operator-guide.md names share-view columns the page does not head, so the guide describes a screen that is not there: " + string.Join(", ", invented));

        Assert.Equal(screen, guide);
    }
}
