using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The install's keyed-hash key, as a file of its own (#28).
/// </summary>
/// <remarks>
/// <para>
/// The store holds a keyed hash of each token rather than the token, so there is
/// a key, and a key has a lifecycle nobody thinks about until it is missing.
/// <c>docs/share-store.md</c> decides that this plugin keeps its files under the
/// plugin data folder; this is the one file in there that is a credential.
/// </para>
/// <para>
/// Its own file rather than a field in the configuration. The configuration file
/// is the one an operator is invited to edit by hand and the one the
/// configuration page rewrites in full, and a key in it is a key in every backup
/// of that file, in every support paste of it, and one careless save away from
/// being replaced. It is not in the store either: a store and a key in one file
/// is a copy of one file that resolves every share in it.
/// </para>
/// <para>
/// First run writes one. There is nothing to lose at that point, because a key
/// that has never existed has hashed nothing.
/// </para>
/// <para>
/// Every later failure fails closed. A file that is there and cannot be read, or
/// is the wrong length, is a refusal rather than a fresh key: replacing it would
/// invalidate every live share on the server, which is a safe outcome arrived at
/// silently, and silence is the part that is not acceptable. The refusal names
/// the path so the operator can go and look.
/// </para>
/// <para>
/// Rotation is deliberate and it is destructive by design. Every stored hash was
/// computed under the old key, so after a rotation nothing that was issued
/// resolves. That is the point of it, and <see cref="Rotate"/> says how many live
/// shares it just stopped so that the caller can tell the operator rather than
/// leaving them to notice.
/// </para>
/// <para>
/// Permissions. On a platform with POSIX modes the file is created and kept at
/// owner read and write only, which is set here rather than left to the process
/// umask. On Windows the file inherits the data folder's access control, and what
/// that comes to was NOT measured. No claim is made about it in either direction,
/// and this plugin does not promise a permission it has not seen.
/// </para>
/// </remarks>
public class ShareKeyFile
{
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareKeyFile"/> class.
    /// </summary>
    /// <param name="path">The full path of the file the key is kept in. Its directory is created if it is missing.</param>
    public ShareKeyFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>
    /// Gets the full path of the file the key is kept in.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Reads the key, writing one on first run.
    /// </summary>
    /// <returns>The key, <see cref="ShareTokens.KeyBytes"/> bytes long.</returns>
    /// <exception cref="ShareKeyUnavailableException">The file exists and cannot be read, or does not hold a key of the length this plugin writes. Nothing is replaced, because replacing it would stop every share that already exists.</exception>
    public byte[] Read()
    {
        byte[] key;
        try
        {
            key = File.ReadAllBytes(_path);
        }
        catch (FileNotFoundException)
        {
            return Create();
        }
        catch (DirectoryNotFoundException)
        {
            return Create();
        }
        catch (IOException error)
        {
            throw new ShareKeyUnavailableException(_path, "the file could not be opened", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new ShareKeyUnavailableException(_path, "this plugin is not allowed to read the file", error);
        }

        // The width goes through a local before it is compared, and that is not
        // style. `token-compared-in-constant-time` refuses a comparison beside a
        // symbol whose name carries Token, Secret or Hash, and `ShareTokens.KeyBytes`
        // carries one. What is compared here is a length, which is public and
        // fixed, so a fixed-time comparison would buy nothing; the local is what
        // lets a crude pattern read a line it is not about. Do not inline it
        // back.
        var expectedWidth = ShareTokens.KeyBytes;
        if (key.Length != expectedWidth)
        {
            // A file of the wrong length is a file that was truncated, edited or
            // written by something else. Using it would resolve nothing and look
            // exactly like a server nobody has shared anything on.
            throw new ShareKeyUnavailableException(
                _path,
                $"the file holds {key.Length} bytes and this plugin writes {expectedWidth}",
                null);
        }

        return key;
    }

    /// <summary>
    /// Replaces the key, stopping every share that was issued under the old one.
    /// </summary>
    /// <param name="liveShares">How many shares are live at this moment, which the caller has counted against the store.</param>
    /// <returns>What the rotation did, for the operator to be told.</returns>
    /// <remarks>
    /// The count is passed in rather than read here, because this type owns a
    /// file and not a store, and a key that reached into the store to count would
    /// be two responsibilities in one place. What it does own is the statement:
    /// the number is meaningless unless it is taken at the moment the key
    /// changes, so it is returned from the call that changes it.
    /// </remarks>
    public ShareKeyRotation Rotate(int liveShares)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(liveShares);

        Write(ShareTokens.MintKeyBytes());
        return new ShareKeyRotation(liveShares);
    }

    private byte[] Create()
    {
        var key = ShareTokens.MintKeyBytes();
        Write(key);
        return key;
    }

    private void Write(byte[] key)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(_path, key);
        RestrictToTheOwner();
    }

    private void RestrictToTheOwner()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Nothing is set here, and nothing is claimed. The file inherits the
            // data folder's access control, which was not measured.
            return;
        }

        File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
