using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TDLib.Saves;

internal static class PlayerSaveRegistry
{
    private static readonly object Gate = new();
    private static readonly List<ISavedPlayerField> RegisteredFields = [];

    private static ISavedPlayerField[]? _frozenFields;

    internal static void Register(ISavedPlayerField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        lock (Gate)
        {
            if (_frozenFields is not null)
            {
                throw new InvalidOperationException(
                    $"TDLib player-save metadata is already frozen; "
                    + $"'{field.EntryKey}' registered too late.");
            }

            if (RegisteredFields.Any(existing =>
                    string.Equals(
                        existing.EntryKey,
                        field.EntryKey,
                        StringComparison.Ordinal)
                    || string.Equals(
                        existing.JsonPropertyName,
                        field.JsonPropertyName,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "TDLib player-save keys must be unique: "
                    + $"{field.JsonPropertyName} / {field.EntryKey}.");
            }

            RegisteredFields.Add(field);
        }
    }

    internal static IEnumerable<JsonPropertyInfo> CreateJsonProperties(
        JsonSerializerOptions options)
    {
        foreach (ISavedPlayerField field in Fields)
        {
            yield return field.CreateJsonProperty(options);
        }
    }

    internal static void AddJsonProperties(
        JsonTypeInfo typeInfo,
        JsonSerializerOptions options)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object
            || typeInfo.IsReadOnly)
        {
            return;
        }

        var existingNames = new HashSet<string>(
            typeInfo.Properties.Select(property => property.Name),
            StringComparer.Ordinal);
        foreach (JsonPropertyInfo property in CreateJsonProperties(options))
        {
            if (existingNames.Add(property.Name))
            {
                typeInfo.Properties.Add(property);
            }
        }
    }

    internal static void Capture(
        Player player,
        SerializablePlayer serializable)
    {
        foreach (ISavedPlayerField field in Fields)
        {
            field.Capture(player, serializable);
        }
    }

    internal static void Restore(
        SerializablePlayer serializable,
        Player player)
    {
        foreach (ISavedPlayerField field in Fields)
        {
            field.Restore(serializable, player);
        }
    }

    internal static void Write(
        SerializablePlayer serializable,
        PacketWriter writer)
    {
        foreach (ISavedPlayerField field in Fields)
        {
            field.Write(serializable, writer);
        }
    }

    internal static void Read(
        SerializablePlayer serializable,
        PacketReader reader)
    {
        foreach (ISavedPlayerField field in Fields)
        {
            field.Read(serializable, reader);
        }
    }

    private static IReadOnlyList<ISavedPlayerField> Fields
    {
        get
        {
            lock (Gate)
            {
                _frozenFields ??= RegisteredFields
                    .OrderBy(
                        field => field.EntryKey,
                        StringComparer.Ordinal)
                    .ThenBy(
                        field => field.ValueType.FullName,
                        StringComparer.Ordinal)
                    .ToArray();
                return _frozenFields;
            }
        }
    }
}
