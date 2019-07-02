using System;

namespace WTFShared.Configuration
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SourceFileAttribute : Attribute
    {
        public string Path { get; }

        public SourceFileAttribute(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new Exception($"{nameof(SourceFileAttribute)} attribute can't have empty name");
            Path = path;
        }
    }
}