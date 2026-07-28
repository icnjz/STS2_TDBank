using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TDLib.Saves;

public static class JsonSaveTypeRegistry
{
    private static readonly object Gate = new();

    private static readonly Dictionary<
        Type,
        Func<IJsonTypeInfoResolver, JsonSerializerOptions, JsonTypeInfo>>
        RegisteredTypes = [];

    public static void RegisterObjectSaveType<T>(
        params Func<JsonSerializerOptions, JsonPropertyInfo>[] properties)
        where T : notnull, new()
    {
        ArgumentNullException.ThrowIfNull(properties);

        Register<T>((resolver, options) =>
        {
            var values = new JsonObjectInfoValues<T>
            {
                ObjectCreator = static () => new T(),
                ObjectWithParameterizedConstructorCreator = null,
                PropertyMetadataInitializer =
                    _ => properties.Select(create => create(options)).ToArray(),
                ConstructorParameterMetadataInitializer = null,
                ConstructorAttributeProviderFactory = static () =>
                    typeof(T).GetConstructor(
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic,
                        binder: null,
                        types: [],
                        modifiers: null)!,
                SerializeHandler = null,
            };
            JsonTypeInfo typeInfo =
                JsonMetadataServices.CreateObjectInfo(options, values);
            typeInfo.NumberHandling = null;
            typeInfo.OriginatingResolver = resolver;
            return typeInfo;
        });
    }

    public static Func<JsonSerializerOptions, JsonPropertyInfo>
        Property<TDeclaring, TProperty>(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        PropertyInfo property = typeof(TDeclaring).GetProperty(propertyName)
            ?? throw new ArgumentException(
                $"Unable to find public property '{propertyName}' on "
                + $"{typeof(TDeclaring).FullName}.",
                nameof(propertyName));

        return options =>
        {
            var values = new JsonPropertyInfoValues<TProperty>
            {
                IsProperty = true,
                IsPublic = true,
                IsVirtual = false,
                DeclaringType = typeof(TDeclaring),
                Converter = null,
                Getter = instance => (TProperty?)property.GetValue(instance),
                Setter = (instance, value) =>
                    property.SetValue(instance, value),
                IgnoreCondition = null,
                HasJsonInclude = false,
                IsExtensionData = false,
                NumberHandling = null,
                PropertyName = propertyName,
                JsonPropertyName = null,
                AttributeProviderFactory = () => property,
            };
            JsonPropertyInfo propertyInfo =
                JsonMetadataServices.CreatePropertyInfo(options, values);
            propertyInfo.IsGetNullable = false;
            propertyInfo.IsSetNullable = false;
            return propertyInfo;
        };
    }

    internal static void RegisterDictionarySaveType<TKey, TValue>()
        where TKey : notnull
    {
        Register<Dictionary<TKey, TValue>>((resolver, options) =>
        {
            var values =
                new JsonCollectionInfoValues<Dictionary<TKey, TValue>>
                {
                    ObjectCreator = static () => [],
                    SerializeHandler = null,
                };
            JsonTypeInfo typeInfo =
                JsonMetadataServices.CreateDictionaryInfo<
                    Dictionary<TKey, TValue>,
                    TKey,
                    TValue>(options, values);
            typeInfo.NumberHandling = null;
            typeInfo.OriginatingResolver = resolver;
            return typeInfo;
        });
    }

    internal static bool TryResolve(
        Type type,
        IJsonTypeInfoResolver resolver,
        JsonSerializerOptions options,
        out JsonTypeInfo? typeInfo)
    {
        Func<IJsonTypeInfoResolver, JsonSerializerOptions, JsonTypeInfo>?
            factory;
        lock (Gate)
        {
            RegisteredTypes.TryGetValue(type, out factory);
        }

        typeInfo = factory?.Invoke(resolver, options);
        return typeInfo is not null;
    }

    private static void Register<T>(
        Func<IJsonTypeInfoResolver, JsonSerializerOptions, JsonTypeInfo>
            factory)
    {
        lock (Gate)
        {
            RegisteredTypes.TryAdd(typeof(T), factory);
        }
    }
}
