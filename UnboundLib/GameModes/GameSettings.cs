﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;

namespace UnboundLib.GameModes
{
    /// <summary>
    ///     A Photon serializable wrapper for arbitrary game settings.
    /// </summary>
    public class GameSettings : IReadOnlyDictionary<string, object>
    {
        public static byte[] Serialize(object settings) {
            using (MemoryStream ms = new MemoryStream()) {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, ((GameSettings) settings).values);
                return ms.ToArray();
            }
        }

        public static GameSettings Deserialize(byte[] data) {
            var result = new GameSettings();
            using (MemoryStream ms = new MemoryStream(data)) {
                var formatter = new BinaryFormatter();
                result.values = (Dictionary<string, object>) formatter.Deserialize(ms);
            }
            return result;
        }

        private Dictionary<string, object> values;

        public GameSettings()
        { 
            this.values = new Dictionary<string, object>();
        }

        public GameSettings(GameSettings settingsToCopy)
        {
            this.values = new Dictionary<string, object>();

            foreach (var entry in settingsToCopy)
            {
                this.values.Add(entry.Key, entry.Value);
            }
        }

        public void Add(string name, object initialValue = default) {
            if (!initialValue.GetType().IsSerializable) {
                throw new ArgumentException($"Setting \"{name}\" must be serializable");
            }

            if (this.values.ContainsKey(name)) {
                throw new ArgumentException($"Setting \"{name}\" already exists");
            }

            this.values.Add(name, initialValue);
        }

        public bool ContainsKey(string name) {
            return this.values.ContainsKey(name);
        }

        public bool TryGetValue(string name, out object value)
        {
            return this.values.TryGetValue(name, out value);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return this.values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.values.GetEnumerator();
        }

        public IEnumerable<string> Keys {
            get {
                return this.values.Keys;
            }
        }

        public IEnumerable<object> Values {
            get {
                return this.values.Values;
            }
        }

        public int Count {
            get {
                return this.values.Count;
            }
        }

        public object this[string setting] {
            get { return this.values[setting]; }
        }
    }
}
