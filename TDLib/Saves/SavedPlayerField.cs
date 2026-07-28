using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TDLib.Saves;

internal interface ISavedPlayerField
{
    string EntryKey { get; }

    string JsonPropertyName { get; }

    Type ValueType { get; }

    void Capture(Player player, SerializablePlayer serializable);

    void Restore(SerializablePlayer serializable, Player player);

    JsonPropertyInfo CreateJsonProperty(JsonSerializerOptions options);

    void Write(SerializablePlayer serializable, PacketWriter writer);

    void Read(SerializablePlayer serializable, PacketReader reader);
}

public sealed class SavedPlayerField<T> : ISavedPlayerField
   where T : class, IPacketSerializable, new()
{
    private sealed class PlayerValue
    {
        public PlayerValue(T value)
        {
            Value = value;
        }

        public T Value { get; set; }
    }

    private sealed class SerializableValue
    {
        public bool HasValue { get; set; }

        public T? Value { get; set; }
    }

    private readonly ConditionalWeakTable<Player, PlayerValue> _playerValues =
        new();

    private readonly ConditionalWeakTable<
        SerializablePlayer,
        SerializableValue> _serializableValues = new();

    private readonly Func<Player, T> _defaultValue;

    public SavedPlayerField(
        Func<Player, T> defaultValue,
        string name,
        string? legacyJsonPropertyName = null,
        string? legacyEntryKey = null)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _defaultValue = defaultValue;
        Name = $"{nameof(Player)}_{name}";
        JsonPropertyName = legacyJsonPropertyName
            ?? $"save_dict_{typeof(T).FullName ?? typeof(T).Name}";
        EntryKey = legacyEntryKey ?? $"spirefield_{Name}";

        ArgumentException.ThrowIfNullOrWhiteSpace(JsonPropertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(EntryKey);

        JsonSaveTypeRegistry.RegisterDictionarySaveType<string, T>();
        PlayerSaveRegistry.Register(this);
    }

    public string Name { get; }

    public string JsonPropertyName { get; }

    public string EntryKey { get; }

    Type ISavedPlayerField.ValueType => typeof(T);

    public T Get(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return _playerValues.GetValue(
            player,
            key => new PlayerValue(_defaultValue(key))).Value;
    }

    public void Set(Player player, T value)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(value);
        _playerValues.GetValue(
            player,
            key => new PlayerValue(_defaultValue(key))).Value = value;
    }

    void ISavedPlayerField.Capture(
        Player player,
        SerializablePlayer serializable)
    {
        SerializableValue value = _serializableValues.GetOrCreateValue(
            serializable);
        value.Value = Get(player);
        value.HasValue = true;
    }

    void ISavedPlayerField.Restore(
        SerializablePlayer serializable,
        Player player)
    {
        if (_serializableValues.TryGetValue(
                serializable,
                out SerializableValue? value)
            && value.HasValue
            && value.Value is not null)
        {
            Set(player, value.Value);
        }
    }

    JsonPropertyInfo ISavedPlayerField.CreateJsonProperty(
        JsonSerializerOptions options)
    {
        var values =
            new JsonPropertyInfoValues<Dictionary<string, T>>
            {
                IsProperty = true,
                IsPublic = true,
                IsVirtual = false,
                DeclaringType = typeof(SerializablePlayer),
                Converter = null,
                Getter = instance => ExportJson((SerializablePlayer)instance),
                Setter = (instance, value) =>
                    ImportJson(
                        (SerializablePlayer)instance,
                        value),
                IgnoreCondition = null,
                HasJsonInclude = false,
                IsExtensionData = false,
                NumberHandling = null,
                PropertyName = JsonPropertyName,
                JsonPropertyName = JsonPropertyName,
            };
        return JsonMetadataServices.CreatePropertyInfo(options, values);
    }

    void ISavedPlayerField.Write(
        SerializablePlayer serializable,
        PacketWriter writer)
    {
        SerializableValue value = _serializableValues.GetOrCreateValue(
            serializable);
        bool present = value.HasValue && value.Value is not null;
        writer.WriteBool(present);
        if (present)
        {
            value.Value!.Serialize(writer);
        }
    }

    void ISavedPlayerField.Read(
        SerializablePlayer serializable,
        PacketReader reader)
    {
        SerializableValue value = _serializableValues.GetOrCreateValue(
            serializable);
        if (!reader.ReadBool())
        {
            value.HasValue = false;
            value.Value = null;
            return;
        }

        var restored = new T();
        restored.Deserialize(reader);
        value.Value = restored;
        value.HasValue = true;
    }

    private Dictionary<string, T> ExportJson(
        SerializablePlayer serializable)
    {
        SerializableValue value = _serializableValues.GetOrCreateValue(
            serializable);
        return value.HasValue && value.Value is not null
            ? new Dictionary<string, T>(StringComparer.Ordinal)
            {
                [EntryKey] = value.Value,
            }
            : new Dictionary<string, T>(StringComparer.Ordinal);
    }

    private void ImportJson(
        SerializablePlayer serializable,
        Dictionary<string, T>? values)
    {
        SerializableValue value = _serializableValues.GetOrCreateValue(
            serializable);
        if (values is not null
            && values.TryGetValue(EntryKey, out T? restored)
            && restored is not null)
        {
            value.Value = restored;
            value.HasValue = true;
            return;
        }

        value.Value = null;
        value.HasValue = false;
    }
}
