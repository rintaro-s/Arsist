using System;
using System.Collections.Generic;

namespace Arsist.Runtime.DataFlow
{
    public class ArsistDataStore
    {
        public static ArsistDataStore Instance { get; } = new ArsistDataStore();

        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public event Action<string, object> OnValueChanged;

        public void SetValue(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _values[key] = value;
            OnValueChanged?.Invoke(key, value);
        }

        public object GetValue(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            _values.TryGetValue(key, out var value);
            return value;
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (!_values.TryGetValue(key, out var raw)) return false;
            if (raw is T typed)
            {
                value = typed;
                return true;
            }
            return false;
        }

        public bool TryGetValueByPath(string path, out object value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            var parts = path.Split('.');
            object current = _values;
            for (var i = 0; i < parts.Length; i++)
            {
                if (current is Dictionary<string, object> dict)
                {
                    if (!dict.TryGetValue(parts[i], out current)) return false;
                    continue;
                }

                if (current is IDictionary<string, object> genericDict)
                {
                    if (!genericDict.TryGetValue(parts[i], out current)) return false;
                    continue;
                }

                return false;
            }

            value = current;
            return true;
        }

        public Dictionary<string, object> GetSnapshot()
        {
            return new Dictionary<string, object>(_values, StringComparer.OrdinalIgnoreCase);
        }
    }
}
