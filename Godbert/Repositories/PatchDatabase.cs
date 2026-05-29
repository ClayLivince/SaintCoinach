using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Godbert.Repositories {
    internal class PatchDatabase {
        private static string FileName => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godbert", "patches.json");

        // Hierarchy: ClientType - Type - <ID, Patch>
        private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> patchDictByClientType = new();

        public static void Load() {
            try {
                if (File.Exists(FileName)) {
                    var text = File.ReadAllText(FileName);
                    patchDictByClientType = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(text);
                }
            }
            catch (Exception) {
                // Error reading settings.  Return default.
            }
        }

        public static void Save() {
            var path = Path.GetDirectoryName(FileName);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            var text = JsonConvert.SerializeObject(patchDictByClientType, Formatting.Indented);
            try {
                File.WriteAllText(FileName, text);
            }
            catch (IOException) {
                // Error saving settings.  Ignore.
            }
        }

        public static string Get(string type, string id, string clientType, string cur) {
            if (!patchDictByClientType.ContainsKey(clientType)) {
                var clientDictNew = new Dictionary<string, Dictionary<string, string>>();
                patchDictByClientType[clientType] = clientDictNew;
            }
            var clientDict = patchDictByClientType[clientType];

            if (!clientDict.ContainsKey(type)) {
                var typeDictNew = new Dictionary<string, string>();
                clientDict[type] = typeDictNew;
            }
            var typeDict = clientDict[type];

            if (typeDict.TryGetValue(id, out string patch)) {
                return patch;
            }

            // For Test Purpose
            //if (type == "icon" & int.Parse(id) < 20000) {
            //    cur = "2024.01.01.0000.0000";
            //}

            typeDict[id] = cur;
            return cur;

        }

        /// <summary>Snapshot of the icon-version map for a client type (for sharing/submission).</summary>
        public static Dictionary<string, string> GetIconVersionMap(string clientType) {
            if (patchDictByClientType.TryGetValue(clientType, out var byType) &&
                byType.TryGetValue("icon", out var map))
                return new Dictionary<string, string>(map);
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// Merge a shared icon-version map (from the server) into the local store, keeping the
        /// EARLIEST version per icon (ordinal min works for the sortable yyyy.mm.dd.bbbb.bbbb format).
        /// Returns the number of entries that became earlier/new locally.
        /// </summary>
        public static int MergeSharedIconVersions(string clientType, IDictionary<string, string> incoming) {
            if (!patchDictByClientType.TryGetValue(clientType, out var byType)) {
                byType = new Dictionary<string, Dictionary<string, string>>();
                patchDictByClientType[clientType] = byType;
            }
            if (!byType.TryGetValue("icon", out var map)) {
                map = new Dictionary<string, string>();
                byType["icon"] = map;
            }

            var changed = 0;
            foreach (var kv in incoming) {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (!map.TryGetValue(kv.Key, out var cur) || string.CompareOrdinal(kv.Value, cur) < 0) {
                    map[kv.Key] = kv.Value;
                    changed++;
                }
            }
            return changed;
        }
    }
}
