using System;

namespace WTFShared.Configuration
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public class SectionAttribute : Attribute
    {
        public const string DefaultSection = "Default";
        public string Name { get; }

        public SectionAttribute(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new Exception($"{nameof(SectionAttribute)} attribute can't have empty name");
            Name = name;
        }
    }
}