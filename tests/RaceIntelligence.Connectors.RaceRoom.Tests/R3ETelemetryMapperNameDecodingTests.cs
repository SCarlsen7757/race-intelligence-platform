using System.Text;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Verifies <see cref="R3ETelemetryMapper.DecodeUtf8Name"/>, which decodes RaceRoom's fixed
/// 64-byte, UTF-8, NUL-terminated name buffers (<c>track_name</c>, <c>layout_name</c>,
/// <c>player_name</c>, ...).
/// </summary>
public class R3ETelemetryMapperNameDecodingTests
{
    /// <summary>Builds a <see cref="Utf8Name64"/> from raw bytes, left-aligned, zero-padding any remainder.</summary>
    private static Utf8Name64 CreateName(ReadOnlySpan<byte> sourceBytes)
    {
        Utf8Name64 name = default;
        Span<byte> destination = name;
        int count = Math.Min(sourceBytes.Length, destination.Length);
        sourceBytes[..count].CopyTo(destination);
        return name;
    }

    [Fact]
    public void NormalAsciiName_DecodesExactly()
    {
        Utf8Name64 name = CreateName(Encoding.UTF8.GetBytes("John Doe"));

        R3ETelemetryMapper.DecodeUtf8Name(name).ShouldBe("John Doe");
    }

    [Fact]
    public void EmptyName_AllZeroBytes_DecodesToEmptyString()
    {
        Utf8Name64 name = default; // every byte zero

        R3ETelemetryMapper.DecodeUtf8Name(name).ShouldBe(string.Empty);
    }

    [Fact]
    public void NameFillingAll64Bytes_WithNoNulTerminator_DecodesEntireBufferVerbatim()
    {
        string sixtyFourAsciiChars = new('A', 64);
        Utf8Name64 name = CreateName(Encoding.UTF8.GetBytes(sixtyFourAsciiChars));

        string decoded = R3ETelemetryMapper.DecodeUtf8Name(name);

        decoded.ShouldBe(sixtyFourAsciiChars);
        decoded.Length.ShouldBe(64);
    }

    [Fact]
    public void MultiByteUtf8Name_DecodesExactly()
    {
        const string Expected = "Kimi Räikkönen";
        Utf8Name64 name = CreateName(Encoding.UTF8.GetBytes(Expected));

        R3ETelemetryMapper.DecodeUtf8Name(name).ShouldBe(Expected);
    }

    [Fact]
    public void MultiByteSequenceTruncatedAtBufferBoundary_DoesNotThrow_AndDecodesCleanly()
    {
        // "äx" is 3 bytes (0xC3 0xA4 for 'ä', 0x78 for 'x') and contains no zero byte, so 21 whole
        // repetitions (63 bytes) fill the buffer to one byte short of 64, and the 64th byte is the
        // lead byte of the 22nd 'ä' with its continuation byte pushed past the end of the 64-byte
        // buffer -- an incomplete multi-byte sequence with no NUL anywhere in the 64 bytes, forcing
        // DecodeUtf8Name down its "no NUL at all, decode the whole buffer verbatim" branch.
        const string Unit = "äx";
        byte[] unitBytes = Encoding.UTF8.GetBytes(Unit);
        unitBytes.Length.ShouldBe(3);

        string longSource = string.Concat(Enumerable.Repeat(Unit, 30));
        byte[] longBytes = Encoding.UTF8.GetBytes(longSource);
        longBytes.Length.ShouldBeGreaterThanOrEqualTo(64);

        byte[] truncated = longBytes[..64];
        truncated.ShouldNotContain((byte)0);

        int completeUnitCount = 64 / unitBytes.Length; // 21
        int leftoverByteCount = 64 % unitBytes.Length; // 1 (a lone lead byte of the next 'ä')
        leftoverByteCount.ShouldBeGreaterThan(0);

        Utf8Name64 name = CreateName(truncated);

        string decoded = R3ETelemetryMapper.DecodeUtf8Name(name);

        // Must not throw (verified implicitly by reaching this line), and the valid prefix must
        // decode cleanly with exactly one U+FFFD replacement character for the truncated tail --
        // not a cascade of mangled/garbage characters.
        string expectedValidPrefix = string.Concat(Enumerable.Repeat(Unit, completeUnitCount));
        decoded.ShouldBe(expectedValidPrefix + "�");
        decoded.ShouldStartWith(expectedValidPrefix);
        decoded[^1].ShouldBe('�');
    }
}
