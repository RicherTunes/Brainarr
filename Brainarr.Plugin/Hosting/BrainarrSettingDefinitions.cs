using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lidarr.Plugin.Abstractions.Contracts;
using NzbDrone.Core.Annotations;

namespace NzbDrone.Core.ImportLists.Brainarr.Hosting;

internal static class BrainarrSettingDefinitions
{
    public static IReadOnlyCollection<SettingDefinition> Describe()
    {
        var defaults = new BrainarrSettings();
        var items = new List<(int Order, SettingDefinition Definition)>();

        foreach (var property in typeof(BrainarrSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var attribute = property.GetCustomAttribute<FieldDefinitionAttribute>();
            if (attribute == null)
            {
                continue;
            }

            var definition = new SettingDefinition
            {
                Key = property.Name,
                DisplayName = attribute.Label ?? property.Name,
                Description = attribute.HelpText ?? string.Empty,
                DataType = MapDataType(attribute.Type, attribute.Privacy),
                IsRequired = attribute.Hidden == HiddenType.Visible && attribute.Privacy != PrivacyLevel.Password,
                AllowedValues = BuildAllowedValues(attribute.SelectOptions),
                DefaultValue = SafeGetDefault(property, defaults)
            };

            items.Add((attribute.Order, definition));
        }

        return items
            .OrderBy(pair => pair.Order)
            .Select(pair => pair.Definition)
            .ToArray();
    }

    private static object? SafeGetDefault(PropertyInfo property, BrainarrSettings settings)
    {
        try
        {
            return property.GetValue(settings);
        }
        catch
        {
            return null;
        }
    }

    private static SettingDataType MapDataType(FieldType type, PrivacyLevel privacy)
    {
        if (privacy == PrivacyLevel.Password || type == FieldType.Password)
        {
            return SettingDataType.Password;
        }

        return type switch
        {
            FieldType.Checkbox => SettingDataType.Boolean,
            FieldType.Number => SettingDataType.Integer,
            FieldType.Select => SettingDataType.Enum,
            _ => SettingDataType.String
        };
    }

    private static IReadOnlyList<string>? BuildAllowedValues(Type selectOptions)
    {
        if (selectOptions == null)
        {
            return null;
        }

        if (selectOptions.IsEnum)
        {
            return Enum.GetNames(selectOptions);
        }

        return null;
    }
}
