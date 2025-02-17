using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SaintCoinach.Graphics.Lgb {
    public class LgbFateRangeEntry : ILgbEntry {
        #region Struct
        [StructLayout(LayoutKind.Sequential)]
        public struct HeaderData {
            public LgbEntryType Type;
            public uint UnknownId;
            // 24 bytes of unknowns
        }
        #endregion

        #region Properties
        public LgbEntryType Type => Header.Type;
        public HeaderData Header { get; private set; }
        public string Name { get; private set; }
        #endregion

        #region Constructor
        public LgbFateRangeEntry(IO.PackCollection packs, byte[] buffer, int offset) {
            this.Header = buffer.ToStructure<HeaderData>(offset);
        }
        #endregion

    }
}
