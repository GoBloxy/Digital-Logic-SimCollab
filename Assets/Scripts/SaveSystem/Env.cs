using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DLS.SaveSystem
{
    public static class Env
    {
        static readonly Dictionary<string, string> values;

        static Env()
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string envPath = Path.Combine(projectRoot, ".env");
                if (File.Exists(envPath))
                {
                    foreach (var raw in File.ReadAllLines(envPath))
                    {
                        var line = raw?.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        var key = line.Substring(0, eq).Trim();
                        var val = line.Substring(eq + 1).Trim();
                        if (val.Length >= 2 && ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'"))))
                            val = val.Substring(1, val.Length - 2);
                        values[key] = val;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Env: failed to load .env — " + e.Message);
            }
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (values.TryGetValue(key, out var v)) return v;
            return Environment.GetEnvironmentVariable(key);
        }
    }
}
