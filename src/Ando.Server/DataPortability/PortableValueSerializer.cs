// =============================================================================
// PortableValueSerializer.cs
//
// Summary: Writes and reads single column values of the export file in a
// provider-neutral JSON form.
//
// The export file must be readable by either database provider, so every
// value type used by the exported entities has exactly one textual form here.
// The import side reads a value back to the property's CLR type.
//
// Design Decisions:
// - DateTime: ISO 8601 round-trip format with "Z" (all values are UTC after
//   UtcDateTimeConverter; an Unspecified value is tagged UTC, Local converted)
// - DateTimeOffset: ISO 8601 round-trip format with the stored offset
// - TimeSpan: invariant constant ("c") format, e.g. "1.02:03:04.5000000"
// - Enums: member name as a string (the database stores them as strings too)
// - byte[]: base64
// - null: written explicitly; reading null into a non-nullable type fails
// - Any other type throws NotSupportedException, so a new column type added to
//   the model fails the export (and the unit test) instead of being lossy
// =============================================================================

using System.Globalization;
using System.Text.Json;

namespace Ando.Server.DataPortability;

/// <summary>
/// Converts column values to and from their JSON representation in the export file.
/// </summary>
public static class PortableValueSerializer
{
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
    private const string TimeSpanFormat = "c";

    /// <summary>
    /// Returns whether values of <paramref name="type"/> (or its nullable form) can be serialized.
    /// </summary>
    /// <param name="type">CLR type of a property.</param>
    /// <returns>True when <see cref="Write"/> and <see cref="Read"/> support the type.</returns>
    public static bool IsSupported(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(string) || t == typeof(bool) || t == typeof(short) || t == typeof(int)
            || t == typeof(long) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
            || t == typeof(TimeSpan) || t == typeof(byte[]) || t == typeof(Guid) || t.IsEnum;
    }

    /// <summary>
    /// Writes <paramref name="value"/> as the current JSON property value.
    /// </summary>
    /// <param name="writer">The JSON writer positioned after a property name.</param>
    /// <param name="value">The value to write (may be null).</param>
    /// <exception cref="NotSupportedException">Thrown for an unsupported value type.</exception>
    public static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case short sh:
                writer.WriteNumberValue(sh);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case DateTime dt:
                writer.WriteStringValue(ToUtc(dt).ToString(DateTimeFormat, CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString(TimeSpanFormat, CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case Enum e:
                writer.WriteStringValue(e.ToString());
                break;
            default:
                throw new NotSupportedException($"Values of type '{value.GetType().FullName}' cannot be exported.");
        }
    }

    /// <summary>
    /// Reads a JSON value into <paramref name="type"/>.
    /// </summary>
    /// <param name="element">The JSON value.</param>
    /// <param name="type">Target CLR type (nullable types accept null).</param>
    /// <returns>The converted value, or null.</returns>
    /// <exception cref="FormatException">Thrown when the JSON value does not match the type.</exception>
    /// <exception cref="NotSupportedException">Thrown for an unsupported type.</exception>
    public static object? Read(JsonElement element, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var t = underlying ?? type;

        if (element.ValueKind == JsonValueKind.Null)
        {
            if (t.IsValueType && underlying == null)
            {
                throw new FormatException($"Null is not valid for non-nullable type '{t.Name}'.");
            }

            return null;
        }

        try
        {
            if (t == typeof(string)) return RequireKind(element, JsonValueKind.String).GetString();
            if (t == typeof(bool)) return element.GetBoolean();
            if (t == typeof(short)) return element.GetInt16();
            if (t == typeof(int)) return element.GetInt32();
            if (t == typeof(long)) return element.GetInt64();
            if (t == typeof(DateTime))
            {
                var parsed = DateTime.Parse(
                    RequireKind(element, JsonValueKind.String).GetString()!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
                return ToUtc(parsed);
            }

            if (t == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(
                    RequireKind(element, JsonValueKind.String).GetString()!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
            }

            if (t == typeof(TimeSpan))
            {
                return TimeSpan.ParseExact(
                    RequireKind(element, JsonValueKind.String).GetString()!, TimeSpanFormat, CultureInfo.InvariantCulture);
            }

            if (t == typeof(byte[])) return element.GetBytesFromBase64();
            if (t == typeof(Guid)) return element.GetGuid();
            if (t.IsEnum)
            {
                var name = RequireKind(element, JsonValueKind.String).GetString()!;
                if (!Enum.TryParse(t, name, ignoreCase: false, out var result)
                    || !Enum.IsDefined(t, result!)
                    || char.IsDigit(name[0]) || name[0] == '-')
                {
                    throw new FormatException($"'{name}' is not a member of {t.Name}.");
                }

                return result;
            }
        }
        catch (InvalidOperationException ex)
        {
            // JsonElement getters throw InvalidOperationException for a wrong value kind.
            throw new FormatException($"Expected a {t.Name} value but found {element.ValueKind}.", ex);
        }

        throw new NotSupportedException($"Values of type '{t.FullName}' cannot be imported.");
    }

    private static JsonElement RequireKind(JsonElement element, JsonValueKind kind)
    {
        if (element.ValueKind != kind)
        {
            throw new FormatException($"Expected a JSON {kind} but found {element.ValueKind}.");
        }

        return element;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
