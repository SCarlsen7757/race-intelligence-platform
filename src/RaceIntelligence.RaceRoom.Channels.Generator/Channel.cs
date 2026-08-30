using System;
using System.Collections.Generic;
using System.Linq;

namespace RaceIntelligence.RaceRoom.Channels.Generator;

/// <summary>One telemetry channel, after the manifest's corner macros have been expanded.</summary>
/// <remarks>
/// <see cref="Index"/> is the channel's position in the manifest, and it is the whole point of the
/// type: it is simultaneously the MessagePack key, the column's ordinal in the table, and the
/// position the bulk writer writes it at. Those three used to be three hand-kept lists.
/// </remarks>
internal sealed class Channel
{
    public Channel(int index, string group, string name, string column, string clrType, string storeType)
    {
        Index = index;
        Group = group;
        Name = name;
        Column = column;
        ClrType = clrType;
        StoreType = storeType;
    }

    public int Index { get; }

    public string Group { get; }

    /// <summary>The camelCase wire name, also the property name once capitalised.</summary>
    public string Name { get; }

    public string Column { get; }

    /// <summary>The C# type as written in the manifest, e.g. <c>float?</c>.</summary>
    public string ClrType { get; }

    /// <summary>The PostgreSQL type, e.g. <c>real</c>.</summary>
    public string StoreType { get; }

    public bool IsNullable => ClrType.EndsWith("?", StringComparison.Ordinal);

    /// <summary>The CLR type with any trailing <c>?</c> removed.</summary>
    public string BareClrType => IsNullable ? ClrType.Substring(0, ClrType.Length - 1) : ClrType;

    public string PropertyName => char.ToUpperInvariant(Name[0]) + Name.Substring(1);
}

/// <summary>Parses <c>channels/raceroom-telemetry.channels</c>.</summary>
/// <remarks>
/// Line-oriented and whitespace-separated rather than JSON, because a generator runs inside the
/// compiler and every dependency it takes is a dependency the compiler has to load. Splitting on
/// whitespace needs nothing at all.
/// </remarks>
internal static class ChannelManifestParser
{
    /// <summary>FL, FR, RL, RR — the platform's corner order, everywhere, without exception.</summary>
    private static readonly string[] Corners = { "Fl", "Fr", "Rl", "Rr" };

    /// <summary>
    /// The tread's three readings across the tyre, inboard first. These are the tyre's own edges
    /// resolved by which side of the car it is fitted to — see <c>MapTyreTemperature</c>, which is
    /// where the resolution happens and where getting it backwards was a real bug (#107).
    /// </summary>
    private static readonly string[] TreadPositions = { "Inner", "Middle", "Outer" };

    public static IReadOnlyList<Channel> Parse(string text, out IReadOnlyList<string> errors)
    {
        var channels = new List<Channel>();
        var problems = new List<string>();
        var group = "";
        var lineNumber = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "group")
            {
                if (parts.Length != 2)
                {
                    problems.Add("line " + lineNumber + ": 'group' takes exactly one name");
                    continue;
                }

                group = parts[1];
                continue;
            }

            // Five fields at minimum, and the store type takes whatever is left: PostgreSQL type
            // names contain spaces ("double precision", "timestamp with time zone") and writing
            // them as they appear in the schema is worth more than a tidier split.
            if (parts.Length < 5)
            {
                problems.Add("line " + lineNumber + ": expected '<kind> <name> <column> <clrType> <storeType>'");
                continue;
            }

            var kind = parts[0];
            var name = parts[1];
            var column = parts[2];
            var clrType = parts[3];
            var storeType = string.Join(" ", parts.Skip(4));

            if (group.Length == 0)
            {
                problems.Add("line " + lineNumber + ": channel '" + name + "' appears before any 'group' line");
                continue;
            }

            switch (kind)
            {
                case "one":
                    channels.Add(new Channel(channels.Count, group, name, column, clrType, storeType));
                    break;

                case "wheels":
                    foreach (var corner in Corners)
                    {
                        channels.Add(new Channel(
                            channels.Count, group,
                            name + corner,
                            column + "_" + corner.ToLowerInvariant(),
                            clrType, storeType));
                    }

                    break;

                case "tread":
                    foreach (var corner in Corners)
                    {
                        foreach (var position in TreadPositions)
                        {
                            channels.Add(new Channel(
                                channels.Count, group,
                                name + corner + position,
                                column + "_" + corner.ToLowerInvariant() + "_" + position.ToLowerInvariant(),
                                clrType, storeType));
                        }
                    }

                    break;

                default:
                    problems.Add("line " + lineNumber + ": unknown kind '" + kind + "' (expected one, wheels or tread)");
                    break;
            }
        }

        foreach (var duplicate in channels.GroupBy(c => c.Name).Where(g => g.Count() > 1))
        {
            problems.Add("channel name '" + duplicate.Key + "' is declared more than once");
        }

        foreach (var duplicate in channels.GroupBy(c => c.Column).Where(g => g.Count() > 1))
        {
            problems.Add("column '" + duplicate.Key + "' is declared more than once");
        }

        errors = problems;
        return channels;
    }
}
