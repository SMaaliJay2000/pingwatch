using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PingWatch.Infrastructure.Persistence;

internal sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => SnakeCaseEnum.ToText(value),
    text => SnakeCaseEnum.Parse<TEnum>(text))
    where TEnum : struct, Enum;

internal static class SnakeCaseEnum
{
    public static string ToText<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static TEnum Parse<TEnum>(string text) where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(text.Replace("_", ""), ignoreCase: true);
}
