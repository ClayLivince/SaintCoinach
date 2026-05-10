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
    }
}
