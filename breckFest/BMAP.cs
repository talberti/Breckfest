﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace breckFest
{
    public class BMAP
    {
        int mode;
        string path;
        DDS dds;
        Bitmap raw;

        public string Path
        {
            get { return path; }
            set { path = value; }
        }

        public int Mode
        {
            get { return mode; }
            set { mode = value; }
        }

        public DDS DDS
        {
            get { return dds; }
            set { dds = value; }
        }

        public Bitmap Raw
        {
            get { return raw; }
            set { raw = value; }
        }

        public static BMAP Load(string path, bool bDump = false)
        {
            FileInfo fi = new FileInfo(path);
            BMAP bmap = new BMAP();

            byte[] buff = new byte[134217728];
            int size = 0;

            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
            using (BinaryReader br = new BinaryReader(ms))
            {
                if (!IsBMAP(br)) { return null; }

                while (br.BaseStream.Position < br.BaseStream.Length)
                {
                    int length = (int)br.ReadUInt32();

                    using (var lzs = new MemoryStream(br.ReadBytes(length)))
                    using (var lz4 = new LZ4Decompress(lzs))
                    {
                        size += lz4.Read(buff, size, buff.Length);
                    }
                }
            }

            if (bDump)
            {
                using (BinaryWriter bw = new BinaryWriter(new FileStream(path.Replace(".bmap", ".raw"), FileMode.Create)))
                {
                    bw.Write(buff, 0, size);
                }
            }

            using (MemoryStream ms = new MemoryStream(buff, 0, size))
            using (BinaryReader br = new BinaryReader(ms))
            {
                bmap.mode = (int)br.ReadUInt32();
                bmap.path = br.ReadString((int)br.ReadUInt32());
                int dataSize = (int)br.ReadUInt32();

                switch (bmap.mode)
                {
                    case 0:
                        bmap.dds = DDS.Load(br.ReadBytes(dataSize));
                        break;

                    case 1:
                        br.ReadUInt16();
                        br.ReadUInt16();
                        br.ReadUInt32();
                        br.ReadUInt32();
                        bmap.raw = new Bitmap(br.ReadUInt16(), br.ReadUInt16(), PixelFormat.Format32bppArgb);
                        br.ReadNullTerminatedString();

                        BitmapData bmpdata = bmap.raw.LockBits(new Rectangle(0, 0, bmap.raw.Width, bmap.raw.Height), ImageLockMode.ReadWrite, bmap.raw.PixelFormat);
                        dataSize = (int)(br.BaseStream.Length - br.BaseStream.Position);
                        Marshal.Copy(br.ReadBytes(dataSize), 0, bmpdata.Scan0, dataSize);
                        break;
                }
            }

            return bmap;
        }

        public void Save(string path, bool bCompress = true)
        {
            // Wreckfest doesn't seem to support uncompressed files.
            // bCompress is just for eyeballing things

            using (var fw = new BinaryWriter(new FileStream(path, FileMode.Create)))
            {
                fw.Write(4);
                fw.Write((byte)0x70);
                fw.Write((byte)0x61);
                fw.Write((byte)0x6D);
                fw.Write((byte)0x62);
                fw.Write(3);

                var input = this.getAllBytes();

                if (bCompress)
                {
                    var hashTable = new int[1 << (14 - 2)];
                    var output = new byte[LZ4Compress.CalculateChunkSize(input.Length)];
                    int i = 0;

                    while (i < input.Length)
                    {
                        byte[] chunk = new byte[Math.Min(input.Length - i, output.Length)];

                        Array.Copy(input, i, chunk, 0, chunk.Length);
                        Array.Clear(hashTable, 0, hashTable.Length);

                        int size = LZ4Compress.Compress(hashTable, chunk, output, chunk.Length, chunk.Length + 4);

                        fw.Write(size);
                        fw.Write(output, 0, size);

                        i += chunk.Length;
                    }
                }
                else
                {
                    fw.Write(input);
                }
            }
        }

        public void SaveAsPNG(string path)
        {
            if (mode == 0)
            {
                dds.Decompress().Save(path, ImageFormat.Png);
            }
            else
            {
                raw.Save(path, ImageFormat.Png);
            }
        }

        public static bool IsBMAP(string path)
        {
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
            using (BinaryReader br = new BinaryReader(ms))
            {
                return IsBMAP(br);
            }
        }

        public static bool IsBMAP(BinaryReader br)
        {
            return (br.ReadUInt32() == 4 &&
                    br.ReadByte() == 0x70 && // p
                    br.ReadByte() == 0x61 && // a
                    br.ReadByte() == 0x6D && // m
                    br.ReadByte() == 0x62 && // b
                    br.ReadUInt32() == 3);
        }

        private byte[] getAllBytes()
        {
            byte[] b;

            if (mode == 0)
            {
                int ddsSize = 128;

                for (int i = 0; i < dds.MipMaps.Count; i++)
                {
                    ddsSize += dds.MipMaps[i].Data.Length;
                }

                b = new byte[4 + 4 + path.Length + 4 + ddsSize];

                using (MemoryStream ms = new MemoryStream(b))
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(mode);                 // 4
                    bw.Write(path.Length);          // 4
                    bw.Write(path.ToCharArray());   // path.length
                    bw.Write(ddsSize);              // 4
                    DDS.Save(bw, this.dds);         // ddsSize
                }
            }
            else
            {
                int rawSize = 46 + (raw.Width * raw.Height * 4);
                int offset = 0;
                b = new byte[4 + 4 + path.Length + 4 + rawSize];

                using (MemoryStream ms = new MemoryStream(b))
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(mode);                 // 4
                    bw.Write(path.Length);          // 4
                    bw.Write(path.ToCharArray());   // path.length
                    bw.Write(rawSize);              // 4
                    bw.Write((short)0x1c);
                    bw.Write((short)0x02);
                    bw.Write(0);
                    bw.Write(0);
                    bw.Write((short)raw.Width);
                    bw.Write((short)raw.Height);
                    bw.Write(" (Bugbear Entertainment Ltd. ".ToCharArray());
                    bw.Write((byte)0);

                    offset = (int)bw.BaseStream.Position;
                }

                BitmapData bmpdata = raw.LockBits(new Rectangle(0, 0, raw.Width, raw.Height), ImageLockMode.ReadOnly, raw.PixelFormat);
                Marshal.Copy(bmpdata.Scan0, b, offset, bmpdata.Stride * bmpdata.Height);
                raw.UnlockBits(bmpdata);
            }

            return b;
        }
    }
}
