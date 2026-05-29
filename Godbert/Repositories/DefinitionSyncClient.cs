using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Godbert.Repositories {
    /// <summary>
    /// HTTP client for the definition/icon sharing server. Speaks plain HTTP — the server may be
    /// git-backed internally, but the client never needs git installed.
    ///
    /// INERT until <see cref="Godbert.Settings.DefinitionServerUrl"/> (or the baked-in default) is
    /// set: every method short-circuits to a "no server" result so the app behaves exactly as before.
    ///
    /// Files are addressed by repo-relative path, mirrored locally under the working directory:
    ///   GET  {base}/manifest.json          → { version, files: { "&lt;relPath&gt;": "&lt;sha256&gt;" } }
    ///   GET  {base}/files/&lt;relPath&gt;         → raw file (e.g. Definitions/Item.json, IconVersions/SquareEnix.json, IconRemarks.json)
    ///   POST {base}/submit  { user, pin, kind, key, json }
    ///        kind "definition" (key=sheet) | "remark" (key=IconRemarks) | "iconversion" (key=clientType)
    ///        → { ok, status: "accepted"|"pending", newVersion, message } | { ok:false, error }
    /// </summary>
    public class DefinitionSyncClient {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>UTF-8 without a byte-order mark — keeps written files byte-consistent with the server.</summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Baked-in default server root, used when the user hasn't set one explicitly. Leave blank
        /// to keep all sharing UI hidden until a URL is configured; fill this in after deployment so
        /// the trusted circle gets sharing without each person having to type the URL.
        /// </summary>
        public const string DefaultServerUrl = "https://definitions.garlandtools.org";

        /// <summary>The user's configured URL, falling back to <see cref="DefaultServerUrl"/>.</summary>
        public static string EffectiveUrl =>
            string.IsNullOrWhiteSpace(Settings.Default.DefinitionServerUrl)
                ? DefaultServerUrl
                : Settings.Default.DefinitionServerUrl;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(EffectiveUrl);

        private string BaseUrl => (EffectiveUrl ?? string.Empty).TrimEnd('/');

        public sealed class UpdateCheckResult {
            public bool ServerConfigured;
            public bool Reachable;
            public string RemoteVersion;
            /// <summary>Repo-relative paths whose content differs from the local copy (or is missing).</summary>
            public List<string> ChangedPaths = new();
            public string Error;
            public bool HasUpdates => ChangedPaths.Count > 0;
        }

        public sealed class SubmitResult {
            public bool Ok;
            /// <summary>"accepted" (live now) or "pending" (queued for review). Null on failure.</summary>
            public string Status;
            public string NewVersion;
            public string Message;
            public string Error;

            public bool IsPending => string.Equals(Status, "pending", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Fetch the remote manifest and diff per-file SHA-256 against the local copies. Returns the
        /// repo-relative paths whose content differs. Never throws — failures come back in
        /// <see cref="UpdateCheckResult.Error"/>.
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync() {
            var result = new UpdateCheckResult { ServerConfigured = IsConfigured };
            if (!IsConfigured)
                return result;

            try {
                var manifestJson = await Http.GetStringAsync($"{BaseUrl}/manifest.json").ConfigureAwait(false);
                var manifest = JObject.Parse(manifestJson);
                result.Reachable = true;
                result.RemoteVersion = (string)manifest["version"];

                if (manifest["files"] is JObject files) {
                    foreach (var entry in files) {
                        var relPath = entry.Key;
                        var remoteHash = (string)entry.Value;
                        if (!string.Equals(LocalHash(relPath), remoteHash, StringComparison.OrdinalIgnoreCase))
                            result.ChangedPaths.Add(relPath);
                    }
                }
            } catch (Exception ex) {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>Download the given files and overwrite them locally (creating sub-folders).</summary>
        public async Task<int> DownloadChangedAsync(IEnumerable<string> relPaths) {
            if (!IsConfigured) return 0;

            var written = 0;
            foreach (var relPath in relPaths) {
                try {
                    var url = $"{BaseUrl}/files/" + string.Join("/", relPath.Split('/').Select(Uri.EscapeDataString));
                    var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                    var local = LocalPath(relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(local));
                    File.WriteAllText(local, json, Utf8NoBom);
                    written++;
                } catch {
                    // Skip individual file failures; caller can re-check next launch.
                }
            }
            return written;
        }

        /// <summary>
        /// Submit a resource to the server (write-gated by user+PIN).
        /// kind = "definition" (key=sheet) | "remark" (key="IconRemarks") | "iconversion" (key=clientType).
        /// </summary>
        public async Task<SubmitResult> SubmitAsync(string kind, string key, string json) {
            if (!IsConfigured)
                return new SubmitResult { Ok = false, Error = "No definition server configured." };

            try {
                var body = new JObject {
                    ["user"] = Settings.Default.DefinitionServerUser ?? string.Empty,
                    ["pin"] = Settings.Default.DefinitionServerPin ?? string.Empty,
                    ["kind"] = kind,
                    ["key"] = key,
                    ["json"] = json
                };
                var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var resp = await Http.PostAsync($"{BaseUrl}/submit", content).ConfigureAwait(false);
                var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                JObject obj = null;
                try { obj = JObject.Parse(respText); } catch { /* non-JSON error body */ }

                if (obj != null && (bool?)obj["ok"] == true)
                    return new SubmitResult {
                        Ok = true,
                        Status = (string)obj["status"],
                        NewVersion = (string)obj["newVersion"],
                        Message = (string)obj["message"]
                    };

                var err = obj != null ? (string)obj["error"] : respText;
                return new SubmitResult { Ok = false, Error = err ?? $"HTTP {(int)resp.StatusCode}" };
            } catch (Exception ex) {
                return new SubmitResult { Ok = false, Error = ex.Message };
            }
        }

        /// <summary>Map a repo-relative path to a local file under the working directory.</summary>
        public static string LocalPath(string relPath) =>
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
        /// Normalized SHA-256: strip a leading BOM and collapse CRLF/CR to LF before hashing, so
        /// the hash is stable regardless of how the bytes were written. MUST match the server's
        /// DefinitionCanonicalizer.HashText so unchanged files don't show up as changed.
        /// </summary>
        public static string HashText(string text) {
            if (text.Length > 0 && text[0] == (char)0xFEFF)
                text = text.Substring(1);
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
}
