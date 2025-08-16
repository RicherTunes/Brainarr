using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Annotations
{
    public enum FieldType
    {
        Textbox = 0,
        Number = 1,
        Password = 2,
        Checkbox = 3,
        Select = 4,
        Path = 5,
        Hidden = 6,
        Url = 7,
        Action = 8,
        Tag = 9,
        TagSelect = 10,
        Captcha = 11,
        OAuth = 12,
        Device = 13,
        Hint = 14
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class FieldDefinitionAttribute : Attribute
    {
        public int Order { get; set; }
        public string Label { get; set; }
        public string Unit { get; set; }
        public string HelpText { get; set; }
        public string HelpLink { get; set; }
        public FieldType Type { get; set; }
        public bool Advanced { get; set; }
        public string SelectOptions { get; set; }
        public string SelectOptionsProviderAction { get; set; }
        public string Section { get; set; }
        public string Hidden { get; set; }
        public Privacy Privacy { get; set; }
    }

    public enum Privacy
    {
        Normal = 0,
        Hidden = 1,
        ApiKey = 2,
        Password = 3,
        UserName = 4
    }

    public class FieldDefinition
    {
        public int Order { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string Unit { get; set; }
        public string HelpText { get; set; }
        public string HelpLink { get; set; }
        public object Value { get; set; }
        public FieldType Type { get; set; }
        public bool Advanced { get; set; }
        public List<SelectOption> SelectOptions { get; set; }
        public string SelectOptionsProviderAction { get; set; }
        public string Section { get; set; }
        public string Hidden { get; set; }
        public Privacy Privacy { get; set; }

        public FieldDefinition()
        {
            SelectOptions = new List<SelectOption>();
        }
    }

    public class SelectOption
    {
        public object Value { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
        public string Hint { get; set; }
        public int DividerAfter { get; set; }
    }
}