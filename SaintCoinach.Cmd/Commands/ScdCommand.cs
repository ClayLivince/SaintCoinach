using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tharga.Console.Commands.Base;

namespace SaintCoinach.Cmd.Commands {
    public class ScdCommand : AsyncActionCommandBase {
        private ARealmReversed _Realm;

        public ScdCommand(ARealmReversed realm)
            : base("scd", "Export specific scd file by inputed path.") {
            _Realm = realm;
        }

        public override async Task InvokeAsync(string[] paramList) {
            string[] searchStrings;

            if (paramList.Length == 0) {
                OutputError("Please input paths you want to export. For bgm please use bgm command.");
            }
            

            var successCount = 0;
            var failCount = 0;
            foreach (string filePath in paramList) {
                
                try {
                    if (string.IsNullOrWhiteSpace(filePath))
                        continue;

                    if (ExportFile(filePath, null)) {
                        ++successCount;
                    }
                    else {
                        OutputError($"File {filePath} not found.");
                        ++failCount;
                    }
                }
                catch (Exception e) {
                    OutputError($"Export of {filePath} failed!");
                    OutputError(e, true);
                    ++failCount;
                }
            }

            OutputInformation($"{successCount} files exported, {failCount} failed");
        }

        private bool ExportFile(string filePath, string suffix) {
            if (!_Realm.Packs.TryGetFile(filePath, out var file))
                return false;

            var scdFile = new Sound.ScdFile(file);
            var count = 0;
            for (var i = 0; i < scdFile.ScdHeader.EntryCount; ++i) {
                var e = scdFile.Entries[i];
                if (e == null)
                    continue;

                var fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (suffix != null)
                    fileNameWithoutExtension += "-" + suffix;
                if (++count > 1)
                    fileNameWithoutExtension += "-" + count;

                foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
                    fileNameWithoutExtension = fileNameWithoutExtension.Replace(invalidChar.ToString(), "");

                var targetPath = System.IO.Path.Combine(_Realm.GameVersion, System.IO.Path.GetDirectoryName(filePath), fileNameWithoutExtension);

                switch (e.Header.Codec) {
                    case Sound.ScdCodec.MSADPCM:
                        targetPath += ".wav";
                        break;
                    case Sound.ScdCodec.OGG:
                        targetPath += ".ogg";
                        break;
                    default:
                        throw new NotSupportedException();
                }

                var fInfo = new System.IO.FileInfo(targetPath);

                if (!fInfo.Directory.Exists)
                    fInfo.Directory.Create();
                System.IO.File.WriteAllBytes(fInfo.FullName, e.GetDecoded());
            }

            return true;
        }
    }
}
