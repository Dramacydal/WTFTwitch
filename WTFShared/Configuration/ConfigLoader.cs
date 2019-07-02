using System;
using System.IO;
using System.Linq;
using System.Reflection;
using IniParser;
using IniParser.Model;

namespace WTFShared.Configuration
{
    public class ConfigLoader<T> where T : new()
    {
        private string _section;

        public const string FileExtension = "ini";

        private Type _entityType;
        private IniData _iniData;
        private FileIniDataParser _iniParser;
        private readonly string _path;

        public static T Load(string path = "")
        {
            var loader = new ConfigLoader<T>(path);
            loader._Load();

            return loader.Entity;
        }

        public T Entity { get; private set; }

        private static object DefaultValueOfType(FieldType type)
        {
            object defaultValue;
            switch (type)
            {
                case FieldType.Int:
                case FieldType.UInt:
                case FieldType.Double:
                case FieldType.Float:
                    defaultValue = 0;
                    break;
                case FieldType.String:
                    defaultValue = "";
                    break;
                default:
                    throw new Exception($"Unsupported value type {type.ToString()}");
            }

            return defaultValue;
        }

        private void CheckDefaultValue(FieldType type, object defaultValue)
        {
            switch (type)
            {
                case FieldType.Int:
                    if (!(defaultValue is int))
                        throw new Exception($"Default value is not of type {type}");
                    break;
                case FieldType.UInt:
                    if (!(defaultValue is uint))
                        throw new Exception($"Default value is not of type {type}");
                    break;
                case FieldType.Double:
                    if (!(defaultValue is double))
                        throw new Exception($"Default value is not of type {type}");
                    break;
                case FieldType.Float:
                    if (!(defaultValue is float))
                        throw new Exception($"Default value is not of type {type}");
                    break;
                case FieldType.String:
                    if (!(defaultValue is string))
                        throw new Exception($"Default value is not of type {type}");
                    break;
                default:
                    throw new Exception($"Unsupported value type {type.ToString()}");
            }
        }

        private static FieldType FromGeneric(Type genericType)
        {
            if (genericType == typeof(string))
                return FieldType.String;
            else if (genericType == typeof(int))
                return FieldType.Int;
            else if (genericType == typeof(uint))
                return FieldType.UInt;
            else if (genericType == typeof(float))
                return FieldType.Float;
            else if (genericType == typeof(double))
                return FieldType.Double;
            else
                return FieldType.Object;
        }

        public ConfigLoader(string path)
        {
            _entityType = typeof(T);

            var sourceFileAttribute = _entityType.GetAttributeValue((SourceFileAttribute _) => _.Path);
            if (sourceFileAttribute != null)
                path = Path.ChangeExtension(sourceFileAttribute, FileExtension);
            if (string.IsNullOrEmpty(path))
                path = Path.ChangeExtension(_entityType.Name, FileExtension);

            this._path = path;
        }

        public ConfigLoader(IniData iniData, string section)
        {
            _entityType = typeof(T);
            _section = section;
            _iniData = iniData;
        }

        private IniData GetData()
        {
            if (_iniData != null)
                return _iniData;

            if (_iniParser == null)
                _iniParser = new FileIniDataParser();

            _iniData = _iniParser.ReadFile(_path);

            return _iniData;
        }

        private string MakePath(string key)
        {
            return string.Concat(_section, GetData().SectionKeySeparator, key);
        }

        private void _Load()
        {
            if (string.IsNullOrEmpty(_section))
            {
                var section = SectionAttribute.DefaultSection;
                var sectionAttribute = _entityType.GetAttributeValue((SectionAttribute _) => _.Name);
                if (sectionAttribute != null)
                    section = sectionAttribute;
                this._section = section;
            }

            Entity = new T();

            var members = _entityType.GetFields();

            foreach (var member in members)
            {
                string sourceName = null;
                string section = null;
                object defaultValue = null;

                var type = FromGeneric(member.FieldType);
                if (type == FieldType.Object)
                {
                    section = member.GetCustomAttributes<SectionAttribute>().FirstOrDefault()?.Name;
                    if (!string.IsNullOrEmpty(section))
                        LoadNestedObject(member, section);

                    continue;
                }

                var attr = member.GetCustomAttributes<IniEntryAttribute>().FirstOrDefault();
                if (attr != null)
                {
                    sourceName = attr.Name;
                    section = attr.Section;
                    defaultValue = attr.DefaultValue;
                }

                if (string.IsNullOrEmpty(sourceName))
                    sourceName = member.Name;
                if (string.IsNullOrEmpty(section))
                    section = SectionAttribute.DefaultSection;
                if (defaultValue == null)
                    defaultValue = DefaultValueOfType(type);

                if (defaultValue != null && member.FieldType != defaultValue.GetType())
                    throw new Exception($"Type of default value for field {member.Name} must match field type");

                member.SetValue(Entity, GetValueFromIni(sourceName, type, defaultValue));
            }
        }

        private object GetValueFromIni(string field, FieldType fieldType, object defaultValue)
        {
            var fieldPath = MakePath(field);
            var ok = GetData().TryGetKey(fieldPath, out string value);
            if (!ok)
                return defaultValue;

            if (string.IsNullOrEmpty(value) && fieldType != FieldType.String)
                return defaultValue;

            try
            {
                object ret = null;
                switch (fieldType)
                {
                    case FieldType.Int:
                        ret = int.Parse(value);
                        break;
                    case FieldType.UInt:
                        ret = uint.Parse(value);
                        break;
                    case FieldType.Float:
                        ret = float.Parse(value);
                        break;
                    case FieldType.Double:
                        ret = double.Parse(value);
                        break;
                    case FieldType.String:
                        ret = value;
                        break;
                    case FieldType.Object:
                        throw new Exception($"Unexpected object field type for key [{fieldPath}]");
                }

                return ret;
            }
            catch (Exception e) when (e is ArgumentException || e is OverflowException)
            {
                throw new Exception($"Failed to parse config value for key [{fieldPath}]");
            }
        }

        private void LoadNestedObject(FieldInfo member, string section)
        {
            var genericClass = typeof(ConfigLoader<>);

            try
            {
                var templateClass = genericClass.MakeGenericType(member.FieldType);

                var instance = Activator.CreateInstance(templateClass, new object[] {GetData(), section});

                var loadMethod = templateClass.GetMethod("_Load",
                    BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.Instance);

                loadMethod.Invoke(instance, new object[] { });

                var entityProperty = templateClass.GetProperty("Entity");

                member.SetValue(Entity, entityProperty.GetValue(instance, null));
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? new Exception($"Failed to load member {member.Name} from config");
            }
        }
    }
}
