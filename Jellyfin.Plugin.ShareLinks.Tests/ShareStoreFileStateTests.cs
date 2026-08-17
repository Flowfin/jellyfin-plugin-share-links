using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The file states the store could meet and had no defined answer for (#78),
/// and the property that keeps this whole group off a shared path.
/// </summary>
/// <remarks>
/// <para>
/// Six of the eight states #78 lists were already covered when this file was
/// written, in <see cref="ShareStoreTests"/> and
/// <see cref="ShareStoreVersionTests"/>. Repeating them here would give the
/// suite two tests to keep in step for one property, so what is here is the two
/// that were missing: a store file that cannot be read, and a folder the store
/// cannot be written into. Truncation is the one addition to a state that was
/// already covered, and the test says why it is not the same test.
/// </para>
/// <para>
/// Both used to leave the framework's own exception about a file handle. That is
/// not a defined outcome, which is what #78 asks for in those words: a caller
/// meeting one cannot tell a disk that refused from a bug in this plugin, and
/// nothing tells it whether the records survived. They now arrive as this
/// plugin's own two refusals, which say which of the two happened and, for the
/// write, that nothing changed.
/// </para>
/// <para>
/// Neither state is produced by an elevated right. `docs/testing.md` refuses
/// those, and a test that needed one would be skipped and disclosed rather than
/// worked around. What that costs is written against each case below, because
/// the way a file becomes unreadable is not the same operation on every platform
/// the suite runs on.
/// </para>
/// </remarks>
public sealed class ShareStoreFileStateTests : IDisposable
{
    private readonly string _directory;

    public ShareStoreFileStateTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-states-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover directory under the temporary directory is not worth
            // failing a green suite over.
        }
        catch (UnauthorizedAccessException)
        {
            // A test below takes the write mode off a directory it made. If it
            // failed before putting it back, this is where that shows, and it is
            // still not worth reddening a green suite.
        }
    }

    private string StorePath => Path.Combine(_directory, "shares.json");

    /// <summary>
    /// A store that is there and cannot be opened.
    /// </summary>
    /// <remarks>
    /// The refusal is the one a damaged store gets rather than the empty list a
    /// missing store gets, and that difference is the point. Present and
    /// unopenable arriving as no records is how a plugin comes to write a fresh
    /// empty store over one an operator could have restored.
    /// </remarks>
    [Fact]
    public async Task AStoreFileThatCannotBeReadIsRefusedRatherThanReadAsEmpty()
    {
        var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord("one") });

        using (MakeUnreadable(StorePath))
        {
            var refused = await Assert.ThrowsAsync<ShareStoreUnreadableException>(() => store.ReadAsync());

            Assert.Equal(StorePath, refused.Path);
            Assert.Contains("not being treated as an empty store", refused.Message, StringComparison.Ordinal);
        }

        // And the record is still there once the file can be opened again, so the
        // refusal cost nothing beyond the request it refused.
        Assert.Single(await store.ReadAsync());
    }

    /// <summary>
    /// The same state met through a change rather than a listing. The read is
    /// inside the write, so the change never runs.
    /// </summary>
    [Fact]
    public async Task AChangeAgainstAStoreThatCannotBeReadDoesNotRunTheChange()
    {
        var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord("one") });

        using (MakeUnreadable(StorePath))
        {
            var ran = false;

            await Assert.ThrowsAsync<ShareStoreUnreadableException>(() => store.MutateAsync(current =>
            {
                ran = true;
                return current;
            }));

            Assert.False(ran);
        }
    }

    /// <summary>
    /// A folder that will not take a new file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where the store lives is a path with something on it that is not a
    /// directory. That is produced identically on every platform the suite runs
    /// on, and it is a state an operator reaches by restoring a backup that had a
    /// file where the folder should be.
    /// </para>
    /// <para>
    /// The store the write refuses for is the store that was asked for, not the
    /// obstruction, so the refusal names the path a caller passed in.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AStoreWhoseFolderCannotBeMadeIsRefusedRatherThanThrowingAFileHandleError()
    {
        var obstruction = Path.Combine(_directory, "data");
        await File.WriteAllTextAsync(obstruction, "not a directory");
        var path = Path.Combine(obstruction, "shares.json");
        var store = new ShareStore(path);

        var refused = await Assert.ThrowsAsync<ShareStoreUnwritableException>(
            () => store.MutateAsync(_ => new[] { ARecord("one") }));

        Assert.Equal(path, refused.Path);
        Assert.Contains("Nothing was changed", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder whose permissions refuse a write, which is the state the issue
    /// names, on the platforms where a test can produce one without an elevated
    /// right.
    /// </summary>
    /// <remarks>
    /// On Windows a directory is made unwritable through its access control list
    /// rather than through a mode, and this test does not do that. The state is
    /// NOT COVERED on Windows and the assertion below is skipped rather than
    /// weakened, which is the disclosure #78 asks for in place of a test that
    /// degrades quietly. The case above reaches the same refusal on both
    /// platforms by a different route.
    /// </remarks>
    [Fact]
    public async Task AFolderThatRefusesAWriteIsRefusedRatherThanThrowingAFileHandleError()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var folder = Path.Combine(_directory, "read-only");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "shares.json");
        var store = new ShareStore(path);

        var mode = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var refused = await Assert.ThrowsAsync<ShareStoreUnwritableException>(
                () => store.MutateAsync(_ => new[] { ARecord("one") }));

            Assert.Equal(path, refused.Path);
            Assert.Contains("Nothing was changed", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(folder, mode);
            }
        }
    }

    /// <summary>
    /// A write that cannot land leaves the records that were there.
    /// </summary>
    /// <remarks>
    /// This is the sentence the refusal makes to a caller, asserted against the
    /// bytes on the disk rather than taken from the message.
    /// </remarks>
    [Fact]
    public async Task AWriteThatIsRefusedLeavesTheStoreHoldingWhatItHeld()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var folder = Path.Combine(_directory, "goes-read-only");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "shares.json");
        var store = new ShareStore(path);
        await store.MutateAsync(_ => new[] { ARecord("before") });
        var asWritten = await File.ReadAllTextAsync(path);

        var mode = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await Assert.ThrowsAsync<ShareStoreUnwritableException>(
                () => store.MutateAsync(_ => new[] { ARecord("after") }));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(folder, mode);
            }
        }

        Assert.Equal(asWritten, await File.ReadAllTextAsync(path));
        Assert.Equal(new[] { "before" }, NamesIn(await store.ReadAsync()));
    }

    /// <summary>
    /// A store file the operating system will not let this process replace,
    /// which is the last step of a write rather than the first.
    /// </summary>
    /// <remarks>
    /// The read-only attribute is the state a restore from an archive leaves, and
    /// on Windows it refuses the rename that puts the finished file in place. On
    /// a platform with POSIX modes the same attribute takes the write bits off
    /// the file and a rename in a writable directory is unaffected, so the state
    /// does not arise there and this is NOT COVERED on those platforms. The
    /// assertion is skipped rather than weakened.
    /// </remarks>
    [Fact]
    public async Task AStoreFileThatCannotBeReplacedIsRefusedRatherThanThrowingAFileHandleError()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord("before") });
        var asWritten = await File.ReadAllTextAsync(StorePath);
        File.SetAttributes(StorePath, FileAttributes.ReadOnly);

        try
        {
            var refused = await Assert.ThrowsAsync<ShareStoreUnwritableException>(
                () => store.MutateAsync(_ => new[] { ARecord("after") }));

            Assert.Equal(StorePath, refused.Path);
            Assert.Contains("Nothing was changed", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(StorePath, FileAttributes.Normal);
        }

        // The store still holds what it held, and the debris of the attempt is
        // not sitting beside it.
        Assert.Equal(asWritten, await File.ReadAllTextAsync(StorePath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    /// <summary>
    /// A store cut short, which is the shape a write that died leaves rather
    /// than the shape somebody types.
    /// </summary>
    /// <remarks>
    /// <see cref="ShareStoreTests.AStoreThatCannotBeParsedIsRefusedRatherThanReadAsEmpty"/>
    /// writes a prefix by hand, which is the same class of bytes arrived at from
    /// the other end. This one takes a store the plugin itself wrote and removes
    /// the second half of it, so what is parsed is a real record list ending
    /// mid-record, and the length it is cut to is derived from the file rather
    /// than guessed at.
    /// </remarks>
    [Fact]
    public async Task AStoreTruncatedPartwayThroughIsRefusedRatherThanReadAsWhatSurvived()
    {
        var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord("one"), ARecord("two") });
        var whole = await File.ReadAllBytesAsync(StorePath);
        await File.WriteAllBytesAsync(StorePath, whole.AsSpan(0, whole.Length / 2).ToArray());

        var refused = await Assert.ThrowsAsync<ShareStoreUnreadableException>(() => store.ReadAsync());

        Assert.Equal(StorePath, refused.Path);
        Assert.Contains("not being treated as an empty store", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The third clause of #78, which is about these tests rather than about the
    /// store: each one gets a directory of its own and removes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// xunit builds a fresh instance of a test class per test, so a constructor
    /// that mints a directory is what makes the property hold. This asserts that
    /// it does, by building the fixtures the way the runner does and comparing
    /// what they chose.
    /// </para>
    /// <para>
    /// It reaches every class the eight states are covered in rather than this
    /// one, because "no shared path between them" is a property of the group and
    /// two of the three classes are elsewhere. A class that stopped minting a
    /// directory per instance, or that named the field something else, fails here
    /// rather than passing quietly, which is why the field is looked for and its
    /// absence asserted on.
    /// </para>
    /// <para>
    /// The fixture is disposed in a finally, because the two lines that read it
    /// are reflection over a private field and a class is free to rename or
    /// retype that field. Where the read throws, the constructor has already made
    /// the directory, and a test whose name ends in "and none is left behind"
    /// would be the thing leaving one behind. A plain using does not do here: the
    /// absence of the directory is asserted after disposal, so the disposal has to
    /// end before the assertion rather than at the end of the block.
    /// </para>
    /// <para>
    /// What it cannot reach is a test that opens a path in the temporary
    /// directory without going through its fixture. Nothing here would see that.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoTwoFixturesOverTheStoreShareADirectoryAndNoneIsLeftBehind()
    {
        var classes = new[]
        {
            typeof(ShareStoreTests),
            typeof(ShareStoreVersionTests),
            typeof(ShareStoreFileStateTests),
        };

        var chosen = new List<string>();

        foreach (var fixtureClass in classes)
        {
            var field = fixtureClass.GetField("_directory", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(field is not null, fixtureClass.Name + " no longer holds a directory of its own");

            for (var run = 0; run < 3; run++)
            {
                var fixture = (IDisposable)Activator.CreateInstance(fixtureClass)!;
                string directory;

                try
                {
                    directory = (string)field!.GetValue(fixture)!;

                    Assert.True(Directory.Exists(directory));
                    chosen.Add(directory);
                }
                finally
                {
                    fixture.Dispose();
                }

                Assert.False(Directory.Exists(directory));
            }
        }

        Assert.Equal(chosen.Count, new HashSet<string>(chosen, StringComparer.Ordinal).Count);
    }

    private static ShareRecord ARecord(string tokenHash) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Guid.NewGuid() },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        TokenHash = tokenHash,
    };

    private static IReadOnlyList<string> NamesIn(IReadOnlyList<ShareRecord> records)
    {
        var names = new List<string>(records.Count);
        foreach (var record in records)
        {
            names.Add(record.TokenHash);
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Makes a file impossible for this process to open, without an elevated
    /// right and by the route an operator's machine actually produces.
    /// </summary>
    /// <remarks>
    /// The branch is explicit rather than one mechanism hoped to work on both,
    /// which is the shape <see cref="ShareKeyTests"/> already uses for the key
    /// file. On Windows a handle held with no sharing is what a reader meets.
    /// Elsewhere the file's mode is taken away.
    /// </remarks>
    /// <param name="path">The file to close off.</param>
    /// <returns>Something to dispose to put the file back.</returns>
    private static IDisposable MakeUnreadable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        return new RestoreTheMode(path, mode);
    }

    private sealed class RestoreTheMode : IDisposable
    {
        private readonly string _path;
        private readonly UnixFileMode _mode;

        public RestoreTheMode(string path, UnixFileMode mode)
        {
            _path = path;
            _mode = mode;
        }

        public void Dispose()
        {
            // The guard is repeated rather than assumed from the branch that
            // built this object, because nothing stops a later caller
            // constructing one on a platform with no modes.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_path, _mode);
            }
        }
    }
}
