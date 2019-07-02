using System;

namespace WTFShared.Configuration
{
    [AttributeUsage(AttributeTargets.Field)]
    public class IniEntryAttribute : Attribute
    {
        public string Name { get; }
        public string Section { get; }
        public object DefaultValue { get; set; }

        public IniEntryAttribute(string name = "", object defaultValue = null, string section = "")
        {
            DefaultValue = defaultValue;
            Name = name;
            Section = section;
        }
    }
}