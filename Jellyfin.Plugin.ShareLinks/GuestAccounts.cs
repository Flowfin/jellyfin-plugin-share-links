using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What the end of the last live share does to the account it was for (#58).
/// </summary>
/// <remarks>
/// <para>
/// Disabled rather than deleted. <c>docs/guest-accounts.md</c> takes that
/// decision and gives the reason: deletion cannot be undone, and an operator who
/// revoked the wrong share has nothing to put back. Disabling stops the account
/// working at that moment, which is the property the end of a share actually
/// needs.
/// </para>
/// <para>
/// Two rules bound which accounts are reached, and both narrow it. Only accounts
/// a record claims under <see cref="ShareRecord.WasCreatedByThisPlugin"/>, which
/// is <c>docs/account-restoration.md</c>'s gate: an account this plugin did not
/// make belongs to somebody who did, and switching it off is done to that person
/// rather than to a share. And only accounts that no live share invites any more,
/// because an account named by two shares stays live while either does, or
/// revoking one share would quietly break the other.
/// </para>
/// <para>
/// Nothing here is prompt. There is no scheduled task in this plugin, so a share
/// that ended by reaching its expiry instant disables its account the next time
/// this routine runs rather than at the instant itself. That is a real gap for an
/// operator: between the expiry and the next run the record refuses every
/// resolution while the account it was for can still sign in. What the account may
/// reach then is the server's answer rather than this plugin's, and what stops the
/// link is the record refusing.
/// </para>
/// <para>
/// What none of this shows is a server doing anything. Whether a server honours
/// <c>IsDisabled</c> is asserted by nothing in this repository, because no test
/// here may reach a server, and <c>docs/testing.md</c> is where that rule is
/// written.
/// </para>
/// </remarks>
public static class GuestAccounts
{
    /// <summary>
    /// The accounts this plugin made that no live share invites any more.
    /// </summary>
    /// <param name="records">Every record the store holds, as it stands after the share ended.</param>
    /// <param name="now">The instant the records are judged live at.</param>
    /// <returns>The accounts, in the order the records name them, each one once.</returns>
    /// <remarks>
    /// <para>
    /// Over the whole store rather than over the record that just ended, which is
    /// the difference between this and
    /// <see cref="GuestSessions.LeftWithNothingToWatch"/>. A session is ended
    /// because a particular share stopped and somebody is watching it now. An
    /// account is disabled because nothing live names it any more, and a share
    /// that reached its expiry instant last week left an account behind that no
    /// revocation of a different share would ever have looked at.
    /// </para>
    /// <para>
    /// A record that has ended still claims the accounts it created, so the
    /// candidates are read off every record and the live ones are what take an
    /// account back out again. Reading the claims off live records alone would
    /// find nothing, because the record holding the claim is the one that ended.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Guid> WithNoLiveShareLeft(IReadOnlyList<ShareRecord> records, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        var ending = new List<Guid>();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            for (var position = 0; position < record.PluginCreatedUserIds.Count; position++)
            {
                var account = record.PluginCreatedUserIds[position];

                // The claim is asked of the record rather than read off the list,
                // so an identifier a hand edit put among the created accounts
                // without inviting it is not claimed and is not switched off.
                if (record.WasCreatedByThisPlugin(account)
                    && !ending.Contains(account)
                    && !HoldsALiveShare(records, account, now))
                {
                    ending.Add(account);
                }
            }
        }

        return ending;
    }

    /// <summary>
    /// Disables the accounts named.
    /// </summary>
    /// <param name="users">The server's own account management.</param>
    /// <param name="accounts">The accounts to switch off, from <see cref="WithNoLiveShareLeft"/>.</param>
    /// <returns>A task that completes when the server has been asked about every one of them.</returns>
    /// <remarks>
    /// <para>
    /// The switch is the only thing this is for, and the rest of the policy
    /// written beside it is the guest policy the account already carries.
    /// <c>IUserManager</c> offers no way to read a policy back, so a write is a
    /// whole policy or it is nothing, and rebuilding it through
    /// <see cref="GuestPolicy"/> is what leaves the other switches where the
    /// create put them instead of at whatever a fresh policy defaults to.
    /// </para>
    /// <para>
    /// The session ceiling is the one value that routine does not fix, so it is
    /// taken off the account rather than out of the configuration. A number an
    /// operator raised after the share was created would otherwise be written onto
    /// the account here, and widening an account is the thing this issue is named
    /// after. Where the account carries the server's own zero, which is no ceiling
    /// at all, or a number outside the bounds an operator may set, what is written
    /// is inside those bounds and is therefore narrower than what was there.
    /// </para>
    /// <para>
    /// That the account's <c>MaxActiveSessions</c> is where its policy's ceiling
    /// is held is a claim rather than a measurement. The server's mapping between
    /// the two is not in this tree and was not read. The names are the same and
    /// the field is what this plugin last wrote through the policy.
    /// </para>
    /// <para>
    /// Asking twice is asking once. An account already disabled is written the
    /// same policy again, because nothing here can read the switch back to find
    /// out, and a repeated write is cheaper than a copy of the server's state kept
    /// in this plugin.
    /// </para>
    /// </remarks>
    public static async Task DisableAsync(IUserManager users, IReadOnlyList<Guid> accounts)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(accounts);

        for (var index = 0; index < accounts.Count; index++)
        {
            var account = accounts[index];

            // Over the account where there is one, because a policy is written
            // onto an account field by field and two of those fields are the
            // account's own providers, which refuse null in the database. An
            // account the server does not answer for is still written to, which
            // is this file's own decision and has a test: what the server does
            // with a policy for an account it does not hold is the server's, and
            // it does not reach those two fields to find them empty.
            var policy = users.GetUserById(account) is { } user
                ? GuestPolicy.For(user, TheCeilingToKeep(user.MaxActiveSessions))
                : GuestPolicy.Create(TheCeilingToKeep(0));
            policy.IsDisabled = true;

            await users.UpdatePolicyAsync(account, policy).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The accounts a sweep released, which is what the removal path acts on (#238).
    /// </summary>
    /// <param name="before">The records the store held before the write.</param>
    /// <param name="after">The records it holds after it, which is what the sweep left.</param>
    /// <returns>The accounts, in the order the records that claimed them named them, each one once.</returns>
    /// <remarks>
    /// <para>
    /// An account goes when the last record naming it is gone, which
    /// <c>docs/guest-accounts.md</c> decides and this is the reading of. So the
    /// candidates are the accounts claimed by the records that are no longer
    /// there, and a candidate any surviving record still invites is not released.
    /// </para>
    /// <para>
    /// Claimed rather than merely named, through
    /// <see cref="ShareRecord.WasCreatedByThisPlugin"/>. This is the gate that
    /// separates tidying up after a share from deleting somebody's own account,
    /// and it is the same gate the disabling uses, for the same reason: an account
    /// this plugin did not make belongs to whoever did.
    /// </para>
    /// <para>
    /// Which records went is never asked, only which accounts are still named, and
    /// that is one comparison rather than two on purpose. A record that survived
    /// invites every account it claims, so skipping it first refuses nothing the
    /// second comparison does not already refuse; the guard was written, could not
    /// be made to bite, and was removed rather than shipped unproven.
    /// </para>
    /// <para>
    /// A record another call swept between the two readings is in neither list, so
    /// its accounts are that call's rather than this one's.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Guid> ReleasedBy(
        IReadOnlyList<ShareRecord> before,
        IReadOnlyList<ShareRecord> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var released = new List<Guid>();
        for (var index = 0; index < before.Count; index++)
        {
            var record = before[index];
            for (var position = 0; position < record.PluginCreatedUserIds.Count; position++)
            {
                var account = record.PluginCreatedUserIds[position];
                if (record.WasCreatedByThisPlugin(account)
                    && !released.Contains(account)
                    && !IsNamedByAny(after, account))
                {
                    released.Add(account);
                }
            }
        }

        return released;
    }

    /// <summary>
    /// Removes the accounts named, and says which of them are still there.
    /// </summary>
    /// <param name="users">The server's own account management.</param>
    /// <param name="accounts">The accounts to remove, from <see cref="ReleasedBy"/>.</param>
    /// <param name="logger">Where the line about it goes (#27).</param>
    /// <returns>What went and what was left behind.</returns>
    /// <remarks>
    /// <para>
    /// This is the most irreversible call this plugin makes, and what bounds it is
    /// the list it is handed rather than anything it does itself. Hand it an
    /// identifier that reached a record some other way and it deletes a person's
    /// account on their own server, which is why the gate is in
    /// <see cref="ReleasedBy"/> and why nothing else in this plugin builds a list
    /// for it.
    /// </para>
    /// <para>
    /// A refusal on one account does not stop the others. The record that claimed
    /// them is already gone, so an account skipped here is one nothing will look at
    /// again, and stopping at the first failure would strand every account after
    /// it for a reason that has nothing to do with them.
    /// </para>
    /// <para>
    /// An account the server says it does not have is counted as removed rather
    /// than as left behind. The call asked for it to be gone and it is gone, and a
    /// second pass over the same identifier is the ordinary way that happens.
    /// </para>
    /// </remarks>
    public static async Task<GuestAccountRemoval> RemoveAsync(
        IUserManager users,
        IReadOnlyList<Guid> accounts,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(logger);

        var removed = new List<Guid>();
        var leftBehind = new List<Guid>();

        for (var index = 0; index < accounts.Count; index++)
        {
            var account = accounts[index];
            try
            {
                await users.DeleteUserAsync(account).ConfigureAwait(false);
                removed.Add(account);
            }
            catch (ArgumentException)
            {
                // The account is not there, which is the state this was after.
                removed.Add(account);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception)
            {
                // Whatever the server refused with, the account is still there and
                // the next one is still owed its attempt. What this cannot do is
                // tell a refusal apart from a fault, because the interface
                // declares neither.
                leftBehind.Add(account);
            }
#pragma warning restore CA1031
        }

        var outcome = new GuestAccountRemoval { Removed = removed, LeftBehind = leftBehind };
        ShareLog.GuestAccountsRemoved(logger, outcome);

        return outcome;
    }

    private static bool IsNamedByAny(IReadOnlyList<ShareRecord> records, Guid account)
    {
        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].InvitedUserIds.Contains(account))
            {
                return true;
            }
        }

        return false;
    }

    // Never above what is there. Below the lowest an operator may set is how the
    // server spells no ceiling, so there is no number to keep and the default is
    // written, which is narrower than none.
    private static int TheCeilingToKeep(int carried)
    {
        if (carried < GuestPolicy.MinimumMaxActiveSessions)
        {
            return GuestPolicy.DefaultMaxActiveSessions;
        }

        return carried > GuestPolicy.MaximumMaxActiveSessions
            ? GuestPolicy.MaximumMaxActiveSessions
            : carried;
    }

    // Live is ShareBounds' answer rather than a second comparison over the same
    // two fields, for the same reason GuestSessions takes it from there.
    private static bool HoldsALiveShare(IReadOnlyList<ShareRecord> records, Guid account, DateTimeOffset now)
    {
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (ShareBounds.IsLive(record, now) && record.InvitedUserIds.Contains(account))
            {
                return true;
            }
        }

        return false;
    }
}
