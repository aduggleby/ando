// =============================================================================
// PortableValueSerializerTests.cs
//
// Summary: Unit tests for the column value serializer of the export file.
//
// Design Decisions:
// - Each test writes a value into a one-property JSON object (with the export's
//   writer options) and reads it back, asserting both the textual form (the
//   documented file format) and the round trip
// =============================================================================

using System.Text;
using System.Text.Json;
using Ando.Server.DataPortability;
using Ando.Server.Models;

namespace Ando.Server.Tests.Unit.DataPortability;

/// <summary>
/// Tests for <see cref="PortableValueSerializer"/>.
/// </summary>
public class PortableValueSerializerTests
{
    private static string WriteJson(object? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, DataExportService.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("v");
            PortableValueSerializer.Write(writer, value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static object? ReadJson(string json, Type type)
    {
        using var doc = JsonDocument.Parse(json);
        return PortableValueSerializer.Read(doc.RootElement.GetProperty("v"), type);
    }

    [Fact]
    public void DateTime_Utc_IsWrittenWithZ_AndReadBackAsUtc()
    {
        var value = new DateTime(2026, 10, 13, 7, 14, 15, DateTimeKind.Utc).AddTicks(1234567);

        var json = WriteJson(value);
        json.ShouldBe("{\"v\":\"2026-10-13T07:14:15.1234567Z\"}");

        var read = (DateTime)ReadJson(json, typeof(DateTime))!;
        read.ShouldBe(value);
        read.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void DateTime_Unspecified_IsTaggedUtc_LocalIsConverted()
    {
        var unspecified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        WriteJson(unspecified).ShouldBe("{\"v\":\"2026-01-02T03:04:05.0000000Z\"}");

        var local = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var read = (DateTime)ReadJson(WriteJson(local), typeof(DateTime))!;
        read.ShouldBe(local.ToUniversalTime());
        read.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void NullableDateTime_RoundTripsNullAndValue()
    {
        WriteJson(null).ShouldBe("{\"v\":null}");
        ReadJson("{\"v\":null}", typeof(DateTime?)).ShouldBeNull();
        ((DateTime?)ReadJson("{\"v\":\"2026-01-02T03:04:05.0000000Z\"}", typeof(DateTime?)))!.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void DateTimeOffset_KeepsOffsetAndInstant()
    {
        var value = new DateTimeOffset(2031, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)).AddTicks(1234560);

        var json = WriteJson(value);
        json.ShouldBe("{\"v\":\"2031-05-06T07:08:09.1234560+02:00\"}");

        var read = (DateTimeOffset)ReadJson(json, typeof(DateTimeOffset?))!;
        read.ShouldBe(value);
        read.Offset.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void TimeSpan_UsesConstantFormat()
    {
        var value = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(5_000_000));

        var json = WriteJson(value);
        json.ShouldBe("{\"v\":\"1.02:03:04.5000000\"}");
        ReadJson(json, typeof(TimeSpan?)).ShouldBe(value);
    }

    [Fact]
    public void Enum_IsWrittenAsName_AndUnknownNamesOrNumbersFail()
    {
        WriteJson(BuildStatus.Running).ShouldBe("{\"v\":\"Running\"}");
        ReadJson("{\"v\":\"TimedOut\"}", typeof(BuildStatus)).ShouldBe(BuildStatus.TimedOut);

        Should.Throw<FormatException>(() => ReadJson("{\"v\":\"Exploded\"}", typeof(BuildStatus)));
        Should.Throw<FormatException>(() => ReadJson("{\"v\":\"2\"}", typeof(BuildStatus)));
        Should.Throw<FormatException>(() => ReadJson("{\"v\":\"running\"}", typeof(BuildStatus)));
    }

    [Fact]
    public void ByteArray_IsBase64()
    {
        var json = WriteJson(new byte[] { 0, 1, 2, 255 });
        json.ShouldBe("{\"v\":\"AAEC/w==\"}");
        ((byte[])ReadJson(json, typeof(byte[]))!).ShouldBe(new byte[] { 0, 1, 2, 255 });
    }

    [Theory]
    [InlineData("{\"v\":true}", typeof(bool), true)]
    [InlineData("{\"v\":2147483647}", typeof(int), int.MaxValue)]
    [InlineData("{\"v\":9223372036854775807}", typeof(long), long.MaxValue)]
    [InlineData("{\"v\":\"text \\\"quoted\\\"\\n\"}", typeof(string), "text \"quoted\"\n")]
    public void Primitives_RoundTrip(string json, Type type, object expected)
    {
        var read = ReadJson(json, type);
        read.ShouldBe(expected);
        WriteJson(read).ShouldBe(json);
    }

    [Fact]
    public void Strings_WithUnicodeAndControlCharacters_RoundTrip()
    {
        const string value = "ünïcode – 中文 \t tab \r\n line \u2028 sep";
        ReadJson(WriteJson(value), typeof(string)).ShouldBe(value);
        WriteJson(value).ShouldNotContain("\n");
    }

    [Fact]
    public void Null_ForNonNullableValueType_Fails()
    {
        Should.Throw<FormatException>(() => ReadJson("{\"v\":null}", typeof(int)));
        Should.Throw<FormatException>(() => ReadJson("{\"v\":null}", typeof(DateTime)));
        ReadJson("{\"v\":null}", typeof(string)).ShouldBeNull();
    }

    [Fact]
    public void WrongJsonKind_FailsWithFormatException()
    {
        Should.Throw<FormatException>(() => ReadJson("{\"v\":\"12\"}", typeof(int)));
        Should.Throw<FormatException>(() => ReadJson("{\"v\":12}", typeof(string)));
        Should.Throw<FormatException>(() => ReadJson("{\"v\":12}", typeof(DateTime)));
    }

    [Fact]
    public void UnsupportedType_Throws()
    {
        PortableValueSerializer.IsSupported(typeof(decimal)).ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => WriteJson(1.5m));
    }
}
