using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the guard decided about one routine that makes a record out of a record
/// (#45, #47).
/// </summary>
public enum ExpiryVerdict
{
    /// <summary>
    /// The routine was driven and the record it produced carries the instant the
    /// record it was given had.
    /// </summary>
    CarriesTheInstantAcross,

    /// <summary>
    /// The routine was driven and the record it produced carries a different
    /// instant. This is the refusal the guard exists for.
    /// </summary>
    MovesTheInstant,

    /// <summary>
    /// The routine could not be driven, so nothing was learned about it. It is
    /// refused rather than skipped, because a routine the guard cannot read is
    /// indistinguishable from one it read and cleared.
    /// </summary>
    CouldNotBeDriven
}

/// <summary>
/// One routine and what the guard made of it.
/// </summary>
/// <param name="Type">The declaring type's name.</param>
/// <param name="Routine">The method's name.</param>
/// <param name="Verdict">What the guard decided.</param>
/// <param name="Detail">Why, in the words a reader of a failure message needs.</param>
public sealed record JudgedProducer(string Type, string Routine, ExpiryVerdict Verdict, string Detail)
{
    /// <summary>
    /// Gets a value indicating whether this routine is one the plugin may not
    /// ship.
    /// </summary>
    public bool IsRefused => Verdict is not ExpiryVerdict.CarriesTheInstantAcross;
}

/// <summary>
/// Reads an assembly for routines that make a <see cref="ShareRecord"/> out of a
/// <see cref="ShareRecord"/> and says whether each one carries the expiry
/// instant across (#45, #47).
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/expiry.md</c> decides that nothing extends a link, and
/// <c>docs/negative-capabilities.md</c> carries that as the line "no route can
/// move the expiry of an existing record". Until this guard the line was held by
/// the shape of the source rather than by a refusal: <see cref="ShareRecord.ExpiresAt"/>
/// is init-only, so moving it means writing a new record, and every routine that
/// writes one happened to copy the old instant. Nothing refused the next routine
/// that did not.
/// </para>
/// <para>
/// The near-miss this is written for is not a rewrite of the rule. It is one
/// line assigned from the wrong neighbour: the routine that writes a revocation
/// takes a revocation instant, sets <c>RevokedAt</c> from it, and sits directly
/// under the line that copies <c>ExpiresAt</c>. Both are
/// <see cref="DateTimeOffset"/>, so the mistake compiles, and what it produces is
/// a share whose expiry silently became the moment somebody revoked it.
/// </para>
/// <para>
/// Every routine is driven twice, once with a neighbouring instant before the
/// record's own and once after. One run would clear a routine that moves the
/// instant only in one direction, which is what an "extend, never shorten" rule
/// written by hand would look like.
/// </para>
/// <para>
/// What this does not reach. The subject is a routine that takes a record and
/// returns one; a routine that rebuilds a record out of something other than a
/// record - a listing, a request body, the store's own file - is outside it and
/// is not judged here. Constructors and object initialisers are outside it for
/// the same reason: the record is built at every call site that makes a first
/// one, and a first record has no earlier instant to carry. It also says nothing
/// about a routine's behaviour on inputs it was not driven with, which is the
/// bound every driven guard has.
/// </para>
/// </remarks>
public static class ExpiryPolicy
{
    /// <summary>
    /// The instant the driven record claims to expire at. Every other instant
    /// the guard hands a routine differs from it, so a routine that assigns the
    /// wrong one is refused rather than accidentally right.
    /// </summary>
    public static readonly DateTimeOffset TheRecordsOwnInstant = new DateTimeOffset(2026, 7, 4, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The two neighbouring instants a routine is driven with, one on each side
    /// of <see cref="TheRecordsOwnInstant"/>.
    /// </summary>
    private static readonly DateTimeOffset[] NeighbouringInstants =
    [
        TheRecordsOwnInstant.AddDays(-3),
        TheRecordsOwnInstant.AddDays(3)
    ];

    /// <summary>
    /// Judges every such routine in an assembly.
    /// </summary>
    /// <param name="assembly">The assembly to read.</param>
    /// <returns>One entry per routine, ordered so two runs read the same.</returns>
    public static IReadOnlyList<JudgedProducer> Judge(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly.GetTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(Judge)
            .ToList();
    }

    /// <summary>
    /// Judges every such routine on one type.
    /// </summary>
    /// <param name="type">The type to read.</param>
    /// <returns>One entry per routine, ordered so two runs read the same.</returns>
    public static IReadOnlyList<JudgedProducer> Judge(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return ProducersOf(type)
            .OrderBy(routine => routine.Name, StringComparer.Ordinal)
            .Select(routine => JudgeRoutine(type, routine))
            .ToList();
    }

    /// <summary>
    /// Writes the judged routines out as the table a failure message carries, so
    /// whoever reads the red sees every routine and not only the refused one.
    /// </summary>
    /// <param name="judged">What <see cref="Judge(Assembly)"/> returned.</param>
    /// <returns>The table, or a sentence saying the set was empty.</returns>
    public static string Describe(IReadOnlyList<JudgedProducer> judged)
    {
        ArgumentNullException.ThrowIfNull(judged);

        if (judged.Count == 0)
        {
            // An empty set is not a clean run and must not read like one.
            return "no routine making a record out of a record was found, so nothing was judged.";
        }

        var text = new StringBuilder();
        foreach (var routine in judged)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}.{2}  {3}: {4}",
                routine.IsRefused ? "REFUSED" : "ok     ",
                routine.Type,
                routine.Routine,
                routine.Verdict,
                routine.Detail));
        }

        return text.ToString();
    }

    /// <summary>
    /// A record for the guard to hand a routine, expiring at
    /// <see cref="TheRecordsOwnInstant"/> and with every other member set, so a
    /// routine that copies field by field has something to copy.
    /// </summary>
    /// <returns>The record the guard drives with.</returns>
    public static ShareRecord ARecord() => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        ItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        InvitedUserIds = [Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")],
        PluginCreatedUserIds = [Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")],
        CreatedByUserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        CreatedAt = TheRecordsOwnInstant.AddDays(-30),
        ExpiresAt = TheRecordsOwnInstant,
        RevokedAt = null,
        RevocationReason = null,
        RevokedByUserId = null,
        MaxBitrateBitsPerSecond = 4_000_000,
        TokenHash = "iCE4x1kEBP2rH1dg1V-mIfjnvHsEHhOZAcTRlJvcT_4",
    };

    /// <summary>
    /// The routines the guard is about: declared on this type, taking a record
    /// and answering with one. The walk includes non-public methods, because the
    /// routine that writes a revocation is private and is exactly the one the
    /// near-miss lives in.
    /// </summary>
    /// <param name="type">The type to read.</param>
    /// <returns>The routines to judge.</returns>
    private static IEnumerable<MethodInfo> ProducersOf(Type type)
    {
        const BindingFlags Everything = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        foreach (var method in type.GetMethods(Everything))
        {
            if (method.IsSpecialName || method.IsAbstract)
            {
                continue;
            }

            if (Produces(method) && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ShareRecord)))
            {
                yield return method;
            }
        }
    }

    /// <summary>
    /// Whether a routine answers with a record, through a task or directly. The
    /// nullable annotation is not read, because it is not in the type a running
    /// method returns.
    /// </summary>
    /// <param name="method">The routine to read.</param>
    /// <returns><c>true</c> where the routine answers with a record.</returns>
    private static bool Produces(MethodInfo method)
        => Unwrapped(method.ReturnType) == typeof(ShareRecord);

    private static Type Unwrapped(Type returned)
        => returned.IsGenericType
            && (returned.GetGenericTypeDefinition() == typeof(Task<>)
                || returned.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? returned.GetGenericArguments()[0]
            : returned;

    private static JudgedProducer JudgeRoutine(Type type, MethodInfo routine)
    {
        object? instance = null;
        if (!routine.IsStatic)
        {
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (Exception error) when (error is MissingMethodException or TargetInvocationException or NotSupportedException)
            {
                return new JudgedProducer(
                    type.Name,
                    routine.Name,
                    ExpiryVerdict.CouldNotBeDriven,
                    "the routine is an instance method and its type could not be made without arguments: " + error.Message);
            }
        }

        var source = ARecord();

        foreach (var neighbour in NeighbouringInstants)
        {
            var arguments = new object?[routine.GetParameters().Length];
            var parameters = routine.GetParameters();
            for (var index = 0; index < parameters.Length; index++)
            {
                if (!TryArgumentFor(parameters[index].ParameterType, source, neighbour, out arguments[index]))
                {
                    return new JudgedProducer(
                        type.Name,
                        routine.Name,
                        ExpiryVerdict.CouldNotBeDriven,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "the guard has no value to hand the parameter '{0}' of type {1}; teach it one rather than exempting the routine",
                            parameters[index].Name,
                            parameters[index].ParameterType.Name));
                }
            }

            ShareRecord? produced;
            try
            {
                produced = Awaited(routine.Invoke(instance, arguments));
            }
            catch (Exception error) when (error is TargetInvocationException or ArgumentException or NotSupportedException)
            {
                return new JudgedProducer(
                    type.Name,
                    routine.Name,
                    ExpiryVerdict.CouldNotBeDriven,
                    "driving the routine threw: " + (error.InnerException ?? error).Message);
            }

            if (produced is null)
            {
                return new JudgedProducer(
                    type.Name,
                    routine.Name,
                    ExpiryVerdict.CouldNotBeDriven,
                    "the routine answered with nothing, so no instant could be compared");
            }

            if (produced.ExpiresAt != source.ExpiresAt)
            {
                return new JudgedProducer(
                    type.Name,
                    routine.Name,
                    ExpiryVerdict.MovesTheInstant,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "given a record expiring at {0:O} and a neighbouring instant of {1:O}, it answered with a record expiring at {2:O}",
                        source.ExpiresAt,
                        neighbour,
                        produced.ExpiresAt));
            }
        }

        return new JudgedProducer(
            type.Name,
            routine.Name,
            ExpiryVerdict.CarriesTheInstantAcross,
            string.Format(
                CultureInfo.InvariantCulture,
                "driven with a neighbouring instant on each side of {0:O} and answered with that instant both times",
                source.ExpiresAt));
    }

    /// <summary>
    /// A value for one parameter. Every instant handed in is the neighbouring
    /// one rather than the record's, so a routine assigning the wrong instant is
    /// caught rather than accidentally right, and that is the whole reason this
    /// table exists instead of <c>default</c>.
    /// </summary>
    /// <param name="wanted">The parameter's type.</param>
    /// <param name="source">The record being handed to the routine.</param>
    /// <param name="neighbour">The instant standing in for every other instant.</param>
    /// <param name="argument">The value to pass.</param>
    /// <returns><c>false</c> where the guard has nothing to hand this type.</returns>
    private static bool TryArgumentFor(Type wanted, ShareRecord source, DateTimeOffset neighbour, out object? argument)
    {
        argument = null;

        if (wanted == typeof(ShareRecord))
        {
            argument = source;
            return true;
        }

        if (wanted == typeof(DateTimeOffset) || wanted == typeof(DateTimeOffset?))
        {
            argument = neighbour;
            return true;
        }

        if (wanted == typeof(Guid) || wanted == typeof(Guid?))
        {
            argument = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
            return true;
        }

        if (wanted == typeof(string))
        {
            argument = "a reason";
            return true;
        }

        if (wanted == typeof(int) || wanted == typeof(int?))
        {
            argument = ShareRecord.CurrentSchemaVersion;
            return true;
        }

        if (wanted == typeof(long) || wanted == typeof(long?))
        {
            argument = 4_000_000L;
            return true;
        }

        if (wanted == typeof(bool) || wanted == typeof(bool?))
        {
            argument = false;
            return true;
        }

        return false;
    }

    private static ShareRecord? Awaited(object? answered)
    {
        if (answered is not null
            && answered.GetType() is { IsGenericType: true } answeredType
            && answeredType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            answered = answeredType.GetMethod(nameof(ValueTask<int>.AsTask))?.Invoke(answered, null);
        }

        if (answered is Task task)
        {
            task.GetAwaiter().GetResult();
            return (ShareRecord?)task.GetType().GetProperty(nameof(Task<int>.Result))?.GetValue(task);
        }

        return (ShareRecord?)answered;
    }
}
