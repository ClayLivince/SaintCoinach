using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SaintCoinach.Graphics.Lgb {
    public class LgbGroup {
        #region Struct
        [StructLayout(LayoutKind.Sequential)]
        public struct HeaderData {
            public uint Unknown1;          // 0x00  Key
            public int GroupNameOffset;    // 0x04
            public int EntriesOffset;      // 0x08
            public int EntryCount;         // 0x0C
            public uint Unknown2;          // 0x10
            public uint Unknown3;          // 0x14  OffsetFilter
            public uint FestivalId;        // 0x18
            public uint Unknown5;          // 0x1C
            // Just a guess -
            // This number corresponds to the last digits of Map.Id.  In
            // territories with rotated subdivisions, it can be used to select
            // the appropriate map for coordinate calculation.
            // Possibly 3-4 bytes to match the first three columns in the Map
            // exd.
            public uint MapIndex;          // 0x20
            public uint Unknown7;
            public uint Unknown8;
            public uint Unknown9;
            public uint Unknown10;
            public uint Unknown11;
        }
        #endregion

        #region Properties
        public LgbFile Parent { get; private set; }
        public HeaderData Header { get; private set; }
        public string Name { get; private set; }
        public ILgbEntry[] Entries { get; private set; }
        #endregion

        #region Constructor
        public LgbGroup(LgbFile parent, byte[] buffer, int offset) {
            this.Parent = parent;
            this.Header = buffer.ToStructure<HeaderData>(offset);
            this.Name = buffer.ReadString(offset + Header.GroupNameOffset);

            //uint[] Unknown = new uint[100];
            //System.Buffer.BlockCopy(buffer, offset + System.Runtime.InteropServices.Marshal.SizeOf<HeaderData>(), Unknown, 0, 400);


            var entriesOffset = offset + Header.EntriesOffset;
            Entries = new ILgbEntry[Header.EntryCount];
            for (var i = 0; i < Header.EntryCount; ++i) {
                var entryOffset = entriesOffset + BitConverter.ToInt32(buffer, entriesOffset + i * 4);
                var type = (LgbEntryType)BitConverter.ToInt32(buffer, entryOffset);

                try {
                    switch (type) {
                        case LgbEntryType.Model:
                            Entries[i] = new LgbModelEntry(Parent.File.Pack.Collection, buffer, entryOffset);
                            break;
                        case LgbEntryType.Gimmick:
                        case LgbEntryType.SharedGroup15:
                            Entries[i] = new LgbGimmickEntry(Parent.File.Pack.Collection, buffer, entryOffset);
                            break;
                        case LgbEntryType.EventObject:
                            Entries[i] = new LgbEventObjectEntry(Parent.File.Pack.Collection, buffer, entryOffset);
                            break;
                        case LgbEntryType.Light:
                            Entries[i] = new LgbLightEntry(Parent.File.Pack.Collection, buffer, entryOffset);
                            break;
                        case LgbEntryType.EventNpc:
                            Entries[i] = new LgbENpcEntry(Parent.File.Pack.Collection, buffer, entryOffset);
                            break;
                        case LgbEntryType.Vfx:
                            Entries[i] = new LgbVfxEntry(Parent.File.Pack.Collection, buffer, entryOffset);
                            break;
                        case LgbEntryType.EventRange:
                        case LgbEntryType.FateRange:
                            Entries[i] = new LgbFateRangeEntry(Parent.File.Pack.Collection, buffer, entryOffset);
                            break;
                        default:
                            // TODO: Work out other parts.
                            //Debug.WriteLine($"{Parent.File.Path} {type} at 0x{entryOffset:X} in {Name}: Can't read type.");
                            break;
                    }
                }
                catch (Exception ex) {
                    Debug.WriteLine($"{Parent.File.Path} {type} at 0x{entryOffset:X} in {Name} failure: {ex.Message}");
                }
            }

        }
        #endregion

        public override string ToString() => Name ?? "(null)";
    }
}
