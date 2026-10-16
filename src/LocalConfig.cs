using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ScamOllama
{
    internal interface IAcceptable { object Clamp(object value); }
    internal sealed class ConfigDescription
    {
        internal readonly IAcceptable Acceptable;
        internal ConfigDescription(string help, IAcceptable acceptable) { Acceptable = acceptable; }
    }
    internal sealed class AcceptableValueRange<T> : IAcceptable where T : IComparable<T>
    {
        private readonly T min, max;
        internal AcceptableValueRange(T minimum, T maximum) { min = minimum; max = maximum; }
        public object Clamp(object input) { T value = (T)input; return value.CompareTo(min) < 0 ? min : value.CompareTo(max) > 0 ? max : value; }
    }
    internal sealed class AcceptableValueList<T> : IAcceptable
    {
        private readonly T[] values;
        internal AcceptableValueList(params T[] allowed) { values = allowed; }
        public object Clamp(object value) { return Array.IndexOf(values, (T)value) >= 0 ? value : values[0]; }
    }
    internal sealed class ConfigEntry<T>
    {
        private T value;
        private readonly Action<T> save;
        private readonly IAcceptable acceptable;
        internal T DefaultValue { get; private set; }
        internal T Value
        {
            get { return value; }
            set { this.value = acceptable == null ? value : (T)acceptable.Clamp(value); save(this.value); }
        }
        internal ConfigEntry(T initial, T defaultValue, Action<T> save, IAcceptable acceptable)
        { DefaultValue = defaultValue; value = acceptable == null ? initial : (T)acceptable.Clamp(initial); this.save = save; this.acceptable = acceptable; }
    }
    internal sealed class LocalConfig
    {
        private readonly string path = Path.Combine(Application.persistentDataPath, "ScamOllama.json");
        private readonly JObject document;
        internal LocalConfig()
        {
            try { document = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject(); }
            catch (Exception e) { Debug.LogWarning("[ScamOllama] Could not read settings; using defaults: " + e.Message); document = new JObject(); }
        }
        internal ConfigEntry<T> Bind<T>(string section, string name, T fallback, object description)
        {
            string key = section + "." + name;
            T initial = fallback;
            try { if (document[key] != null) initial = document[key].ToObject<T>(); } catch (Exception) { }
            var desc = description as ConfigDescription;
            return new ConfigEntry<T>(initial, fallback, value =>
            {
                document[key] = JToken.FromObject(value);
                try { File.WriteAllText(path, document.ToString(Formatting.Indented)); }
                catch (Exception e) { Debug.LogWarning("[ScamOllama] Could not save settings: " + e.Message); }
            }, desc == null ? null : desc.Acceptable);
        }
    }
}
