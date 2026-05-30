using System;
using System.IO;

namespace SaintCoinach.Sound {
    /// <summary>
    /// Reads an HCA-encoded (SquareEnix Sscf wave format 0x1A) entry from an SCD file
    /// and decodes it to WAV-formatted bytes via the DereTore HCA library.
    ///
    /// Subheader layout (24 bytes, starting at <c>dataOffset</c>):
    ///   0x00  Unk1            (2 bytes)
    ///   0x02  HeaderSize      (int16, little-endian)
    ///   0x04  BlockSize       (int16, little-endian)
    ///   0x06  Unk2            (7 bytes)
    ///   0x0D  PlainText       (1 byte; 0 = XOR-encrypted audio data, non-zero = plain)
    ///   0x0E  Unk3            (10 bytes)
    ///   0x18  HCA header      (HeaderSize bytes)
    ///   0x18+HeaderSize  HCA audio data (Header.DataSize bytes)
    ///
    /// When PlainText is false the audio data is XOR-decoded against a fixed table
    /// (see <c>XorDecodeFromTable</c>) using the HCA header size as the seed; the
    /// HCA header itself is always plain. Both header and audio data are then handed
    /// to <c>DereTore.Exchange.Audio.HCA.HcaAudioStream</c> which decodes to WAV.
    ///
    /// Ported from VFXEditor's ScdHca / ScdUtils.XorDecodeFromTableHca.
    /// </summary>
    public class ScdHcaEntry : ScdEntry {
        #region XorTable
        // Identical to the ScdOggEntry XorTable. Kept local so this file is self-contained.
        private static readonly byte[] XorTable = new byte[256] {
            0x3A, 0x32, 0x32, 0x32, 0x03, 0x7E, 0x12, 0xF7,
            0xB2, 0xE2, 0xA2, 0x67, 0x32, 0x32, 0x22, 0x32,
            0x32, 0x52, 0x16, 0x1B, 0x3C, 0xA1, 0x54, 0x7B,
            0x1B, 0x97, 0xA6, 0x93, 0x1A, 0x4B, 0xAA, 0xA6,
            0x7A, 0x7B, 0x1B, 0x97, 0xA6, 0xF7, 0x02, 0xBB,
            0xAA, 0xA6, 0xBB, 0xF7, 0x2A, 0x51, 0xBE, 0x03,
            0xF4, 0x2A, 0x51, 0xBE, 0x03, 0xF4, 0x2A, 0x51,
            0xBE, 0x12, 0x06, 0x56, 0x27, 0x32, 0x32, 0x36,
            0x32, 0xB2, 0x1A, 0x3B, 0xBC, 0x91, 0xD4, 0x7B,
            0x58, 0xFC, 0x0B, 0x55, 0x2A, 0x15, 0xBC, 0x40,
            0x92, 0x0B, 0x5B, 0x7C, 0x0A, 0x95, 0x12, 0x35,
            0xB8, 0x63, 0xD2, 0x0B, 0x3B, 0xF0, 0xC7, 0x14,
            0x51, 0x5C, 0x94, 0x86, 0x94, 0x59, 0x5C, 0xFC,
            0x1B, 0x17, 0x3A, 0x3F, 0x6B, 0x37, 0x32, 0x32,
            0x30, 0x32, 0x72, 0x7A, 0x13, 0xB7, 0x26, 0x60,
            0x7A, 0x13, 0xB7, 0x26, 0x50, 0xBA, 0x13, 0xB4,
            0x2A, 0x50, 0xBA, 0x13, 0xB5, 0x2E, 0x40, 0xFA,
            0x13, 0x95, 0xAE, 0x40, 0x38, 0x18, 0x9A, 0x92,
            0xB0, 0x38, 0x00, 0xFA, 0x12, 0xB1, 0x7E, 0x00,
            0xDB, 0x96, 0xA1, 0x7C, 0x08, 0xDB, 0x9A, 0x91,
            0xBC, 0x08, 0xD8, 0x1A, 0x86, 0xE2, 0x70, 0x39,
            0x1F, 0x86, 0xE0, 0x78, 0x7E, 0x03, 0xE7, 0x64,
            0x51, 0x9C, 0x8F, 0x34, 0x6F, 0x4E, 0x41, 0xFC,
            0x0B, 0xD5, 0xAE, 0x41, 0xFC, 0x0B, 0xD5, 0xAE,
            0x41, 0xFC, 0x3B, 0x70, 0x71, 0x64, 0x33, 0x32,
            0x12, 0x32, 0x32, 0x36, 0x70, 0x34, 0x2B, 0x56,
            0x22, 0x70, 0x3A, 0x13, 0xB7, 0x26, 0x60, 0xBA,
            0x1B, 0x94, 0xAA, 0x40, 0x38, 0x00, 0xFA, 0xB2,
            0xE2, 0xA2, 0x67, 0x32, 0x32, 0x12, 0x32, 0xB2,
            0x32, 0x32, 0x32, 0x32, 0x75, 0xA3, 0x26, 0x7B,
            0x83, 0x26, 0xF9, 0x83, 0x2E, 0xFF, 0xE3, 0x16,
            0x7D, 0xC0, 0x1E, 0x63, 0x21, 0x07, 0xE3, 0x01,
        };
        #endregion

        #region Fields
        private byte[] _Decoded;
        #endregion

        #region Constructor
        internal ScdHcaEntry(ScdFile file, ScdEntryHeader header, int dataOffset)
            : base(file, header) {
            Decode(dataOffset);
        }
        #endregion

        public override byte[] GetDecoded() {
            return _Decoded;
        }

        #region Decode
        private void Decode(int dataOffset) {
            const int HeaderSizeOffset = 0x02;
            const int BlockSizeOffset = 0x04;
            const int PlainTextOffset = 0x0D;
            const int HcaHeaderOffset = 0x18;

            short headerSize = File.ReadInt16(dataOffset + HeaderSizeOffset);
            short blockSize = File.ReadInt16(dataOffset + BlockSizeOffset);
            bool plainText = File._InputBuffer[dataOffset + PlainTextOffset] != 0;

            var hcaHeader = new byte[headerSize];
            Array.Copy(File._InputBuffer, dataOffset + HcaHeaderOffset, hcaHeader, 0, headerSize);

            var audioData = new byte[Header.DataSize];
            Array.Copy(File._InputBuffer, dataOffset + HcaHeaderOffset + headerSize, audioData, 0, Header.DataSize);

            if (!plainText)
                audioData = XorDecodeFromTable(audioData, blockSize, Header.DataSize, headerSize);

            var streamData = new byte[headerSize + Header.DataSize];
            Array.Copy(hcaHeader, 0, streamData, 0, headerSize);
            Array.Copy(audioData, 0, streamData, headerSize, Header.DataSize);

            using (var inMs = new MemoryStream(streamData))
            using (var hcaStream = new DereTore.Exchange.Audio.HCA.HcaAudioStream(inMs, DereTore.Exchange.Audio.HCA.DecodeParams.CreateDefault()))
            using (var outMs = new MemoryStream()) {
                hcaStream.CopyTo(outMs);
                _Decoded = outMs.ToArray();
            }
        }

        private static byte[] XorDecodeFromTable(byte[] data, int blockSize, int dataLength, int seed) {
            if (blockSize <= 0) return data;

            var v47 = dataLength & 0x3F;
            var v48 = (byte)(dataLength & 0x7F);

            var result = new byte[data.Length];
            var pos = 0;
            var currentBlockStart = 0;

            while (pos + blockSize <= data.Length) {
                for (var i = 0; i < blockSize; i++) {
                    var tableIdx = (seed + i + currentBlockStart + v47) & 0xFF;
                    result[pos + i] = (byte)(data[pos + i] ^ XorTable[tableIdx] ^ v48);
                }
                pos += blockSize;
                currentBlockStart = (currentBlockStart + blockSize) & 0xFF;
            }

            // Copy any trailing partial block as-is to avoid out-of-range writes.
            if (pos < data.Length)
                Array.Copy(data, pos, result, pos, data.Length - pos);

            return result;
        }
        #endregion
    }
}
