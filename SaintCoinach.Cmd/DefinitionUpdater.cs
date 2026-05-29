using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json.Linq;

namespace SaintCoinach.Cmd {
    /// <summary>
    /// Pulls the latest shared definition files from the definition server on startup.
    ///
    /// Reads are public, so this needs no credentials — it only ever downloads. It is deliberately
    /// self-contained (NOT shared with Godbert's <c>DefinitionSyncClient</c>) so the Cmd tool stays
    /// dependency-light; only <c>Definitions/*.json</c> are fetched (icon resources are ignored).
    ///
    /// Call this BEFORE constructing <see cref="ARealmReversed"/> so the refreshed files are picked
    /// up when the realm reads its definitions. Any failure (offline, server down, bad response) is
    /// swallowed with a log line and never blocks startup.
    /// </summary>
    internal static class DefinitionUpdater {
        private const string DefaultServerUrl = "https://definitions.garlandtools.org";
        private const string DefinitionsPrefix = "Definitions/";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>Fetch the manifest, diff local definition hashes, and download what changed.</summary>
        public static void UpdateOnStartup(string serverUrl = null) {
            var baseUrl = (string.IsNullOrWhiteSpace(serverUrl) ? DefaultServerUrl : serverUrl).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return;

            try {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

                var manifest = JObject.Parse(http.GetStringAsync($"{baseUrl}/manifest.json").GetAwaiter().GetResult());
                var version = (string)manifest["version"];
                if (!(manifest["files"] is JObject files))
                    return;

                var changed = new List<string>();
                foreach (var entry in files) {
                    var relPath = entry.Key;
                    if (!relPath.StartsWith(DefinitionsPrefix, StringComparison.OrdinalIgnoreCase))
                        continue; // definitions only
                    if (!string.Equals(LocalHash(relPath), (string)entry.Value, StringComparison.OrdinalIgnoreCase))
                        changed.Add(relPath);
                }

                if (changed.Count == 0) {
                    Console.WriteLine($"Definitions up to date (server v{version}).");
                    return;
                }

                Console.WriteLine($"Definition server v{version}: downloading {changed.Count} updated file(s)...");
                var written = 0;
                foreach (var relPath in changed) {
                    try {
                        var url = $"{baseUrl}/files/" + string.Join("/", relPath.Split('/').Select(Uri.EscapeDataString));
                        var content = http.GetStringAsync(url).GetAwaiter().GetResult();
                        var local = LocalPath(relPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(local));
                        File.WriteAllText(local, content, Utf8NoBom);
                        written++;
                    } catch (Exception ex) {
                        Console.WriteLine($"  failed {relPath}: {ex.Message}");
                    }
                }
                Console.WriteLine($"Downloaded {written} definition file(s) from server v{version}.");
            } catch (Exception ex) {
                Console.WriteLine($"Definition update skipped: {ex.Message}");
            }
        }

        /// <summary>Map a repo-relative manifest path to a local file under the working directory.</summary>
        private static string LocalPath(string relPath) =>
            Path.GetFullPath(relPath.Replace('/', Path.DirectorySeparatorChar));

        private static string LocalHash(string relPath) {
            var path = LocalPath(relPath);
            if (!File.Exists(path))
                return null;
            try {
                return HashText(File.ReadAllText(path));
            } catch {
                return null;
            }
        }

        /// <summary>
        /// Normalized SHA-256: strip a leading BOM and collapse CRLF/CR to LF before hashing, so the
        /// hash is stable regardless of how the bytes were written. MUST match the server's
        /// DefinitionCanonicalizer.HashText so unchanged files don't show up as changed.
        /// </summary>
        private static string HashText(string text) {
            if (text.Length > 0 && text[0] == (char)0xFEFF)
                text = text.Substring(1);
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
}
