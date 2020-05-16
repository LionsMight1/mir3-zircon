﻿using ManagedSquish;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryEditor
{
    public sealed class MLibrary
    {
        public const int LibVersion = 1;
        public static bool Load = true;
        public string FileName;
        
        public List<MImage> Images = new List<MImage>();
        public List<int> IndexList = new List<int>();
        public int Count;
        private bool _initialized;

        private BinaryReader _reader;
        private FileStream _stream;

        public MLibrary(string filename)
        {
            FileName = filename;
            Initialize();
            Close();
        }

        public void Initialize()
        {
            int CurrentVersion;
            _initialized = true;

            if (!File.Exists(FileName))
                return;

            _stream = new FileStream(FileName, FileMode.Open, FileAccess.ReadWrite);
            _reader = new BinaryReader(_stream);
            CurrentVersion = _reader.ReadInt32();
            if (CurrentVersion != LibVersion)
            {
                MessageBox.Show("Wrong version, expecting lib version: " + LibVersion.ToString() + " found version: " + CurrentVersion.ToString() + ".", "Failed to open", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }
            Count = _reader.ReadInt32();
            Images = new List<MImage>();
            IndexList = new List<int>();

            for (int i = 0; i < Count; i++)
                IndexList.Add(_reader.ReadInt32());

            for (int i = 0; i < Count; i++)
                Images.Add(null);

            for (int i = 0; i < Count; i++)
                CheckImage(i);
        }

        public void Close()
        {
            if (_stream != null)
                _stream.Dispose();
            // if (_reader != null)
            //     _reader.Dispose();
        }

        public void Save()
        {
            Close();

            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            Count = Images.Count;
            IndexList.Clear();

            int offSet = 8 + Count * 4;
            for (int i = 0; i < Count; i++)
            {
                IndexList.Add((int)stream.Length + offSet);
                Images[i].Save(writer);
            }

            writer.Flush();
            byte[] fBytes = stream.ToArray();
            //  writer.Dispose();

            _stream = File.Create(FileName);
            writer = new BinaryWriter(_stream);
            writer.Write(LibVersion);
            writer.Write(Count);
            for (int i = 0; i < Count; i++)
                writer.Write(IndexList[i]);

            writer.Write(fBytes);
            writer.Flush();
            writer.Close();
            writer.Dispose();
            Close();
        }

        private void CheckImage(int index)
        {
            if (!_initialized)
                Initialize();

            if (Images == null || index < 0 || index >= Images.Count)
                return;

            if (Images[index] == null)
            {
                _stream.Position = IndexList[index];
                Images[index] = new MImage(_reader);
            }

            if (!Load) return;

            MImage mi = Images[index];
            if (!mi.TextureValid)
            {
                _stream.Seek(IndexList[index] + 12, SeekOrigin.Begin);
                mi.CreateTexture(_reader);
            }
        }

        public void ToMLibrary()
        {
            string fileName = Path.ChangeExtension(FileName, ".Zl");

            if (File.Exists(fileName))
                File.Delete(fileName);

            Mir3Library library = new Mir3Library(fileName);
            library.Images.AddRange(Enumerable.Repeat(new Mir3Library.Mir3Image(), Images.Count));
            //library.Save();

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

            try
            {
                Parallel.For(0, Images.Count, options, i =>
                {
                    MImage image = Images[i];
                    if (image.HasMask)
                        library.Images[i] = new Mir3Library.Mir3Image(image.Image, null, image.MaskImage) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
                    else
                    library.Images[i] = new Mir3Library.Mir3Image(image.Image) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
                });
            }
            catch (System.Exception)
            {
                throw;
            }
            finally
            {
                library.Save(fileName);
            }

            // Operation finished.
            // System.Windows.Forms.MessageBox.Show("Converted " + fileName + " successfully.",
            //    "Wemade Information",
            //        System.Windows.Forms.MessageBoxButtons.OK,
            //            System.Windows.Forms.MessageBoxIcon.Information,
            //                System.Windows.Forms.MessageBoxDefaultButton.Button1);
        }
        public void MergeToMLibrary(Mir3Library lib)
        {            
            int offset = lib.Images.Count;
            lib.Images.AddRange(Enumerable.Repeat(new Mir3Library.Mir3Image(), Images.Count));
            //library.Save();

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

            try
            {
                Parallel.For(0, Images.Count, options, i =>
                {
                    MImage image = Images[i];
                    if (image.HasMask)
                        lib.Images[i+offset] = new Mir3Library.Mir3Image(image.Image, null, image.MaskImage) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
                    else
                        lib.Images[i+ offset] = new Mir3Library.Mir3Image(image.Image) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
                });
                lib.addTo1k();
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        public sealed class MImage
        {
            public short Width, Height, X, Y, ShadowX, ShadowY;
            public byte Shadow;
            public int Length;
            public byte[] FBytes;
            public bool TextureValid;
            public Bitmap Image, Preview;

            //layer 2:
            public short MaskWidth, MaskHeight, MaskX, MaskY;

            public int MaskLength;
            public byte[] MaskFBytes;
            public Bitmap MaskImage;
            public Boolean HasMask;

            public MImage(BinaryReader reader)
            {
                //read layer 1
                Width = reader.ReadInt16();
                Height = reader.ReadInt16();
                X = reader.ReadInt16();
                Y = reader.ReadInt16();
                ShadowX = reader.ReadInt16();
                ShadowY = reader.ReadInt16();
                Shadow = reader.ReadByte();
                Length = reader.ReadInt32();
                FBytes = reader.ReadBytes(Length);
                //check if there's a second layer and read it
                HasMask = ((Shadow >> 7) == 1) ? true : false;
                if (HasMask)
                {
                    MaskWidth = reader.ReadInt16();
                    MaskHeight = reader.ReadInt16();
                    MaskX = reader.ReadInt16();
                    MaskY = reader.ReadInt16();
                    MaskLength = reader.ReadInt32();
                    MaskFBytes = reader.ReadBytes(MaskLength);
                }
            }

            private Bitmap FixImageSize(Bitmap input)
            {
                int w = input.Width + (4 - input.Width % 4) % 4;
                int h = input.Height + (4 - input.Height % 4) % 4;

                if (input.Width != w || input.Height != h)
                {
                    Bitmap temp = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(temp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.DrawImage(input, 0, 0);
                        g.Save();
                    }
                    input.Dispose();
                    input = temp;
                }

                return input;
            }

            private unsafe byte[] ConvertBitmapToArray(Bitmap input)
            {
                byte[] output;

                BitmapData data = input.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadOnly,
                                                 PixelFormat.Format32bppArgb);

                byte[] pixels = new byte[input.Width * input.Height * 4];

                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                input.UnlockBits(data);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    //Reverse Red/Blue
                    byte b = pixels[i];
                    pixels[i] = pixels[i + 2];
                    pixels[i + 2] = b;

                    if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
                        pixels[i + 3] = 0; //Make Transparent
                }

                int count = Squish.GetStorageRequirements(input.Width, input.Height, SquishFlags.Dxt1);

                output = new byte[count];
                fixed (byte* dest = output)
                fixed (byte* source = pixels)
                {
                    Squish.CompressImage((IntPtr)source, input.Width, input.Height, (IntPtr)dest, SquishFlags.Dxt1);
                }
                return output;
            }

            public void Save(BinaryWriter writer)
            {
                writer.Write(Width);
                writer.Write(Height);
                writer.Write(X);
                writer.Write(Y);
                writer.Write(ShadowX);
                writer.Write(ShadowY);
                writer.Write(HasMask ? (byte)(Shadow | 0x80) : (byte)Shadow);
                writer.Write(FBytes.Length);
                writer.Write(FBytes);
                if (HasMask)
                {
                    writer.Write(MaskWidth);
                    writer.Write(MaskHeight);
                    writer.Write(MaskX);
                    writer.Write(MaskY);
                    writer.Write(MaskFBytes.Length);
                    writer.Write(MaskFBytes);
                }
            }

            public unsafe void CreateTexture(BinaryReader reader)
            {
                int w = Width + (4 - Width % 4) % 4;
                int h = Height + (4 - Height % 4) % 4;

                if (w == 0 || h == 0)
                {
                    return;
                }

                Image = new Bitmap(w, h);

                BitmapData data = Image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
                                                 PixelFormat.Format32bppArgb);

                fixed (byte* source = FBytes)
                    Squish.DecompressImage(data.Scan0, w, h, (IntPtr)source, SquishFlags.Dxt1);

                byte* dest = (byte*)data.Scan0;

                for (int i = 0; i < h * w * 4; i += 4)
                {
                    //Reverse Red/Blue
                    byte b = dest[i];
                    dest[i] = dest[i + 2];
                    dest[i + 2] = b;
                }

                Image.UnlockBits(data);

                if (HasMask)
                {
                    w = MaskWidth + (4 - MaskWidth % 4) % 4;
                    h = MaskHeight + (4 - MaskHeight % 4) % 4;

                    if (w == 0 || h == 0)
                    {
                        return;
                    }
                    MaskImage = new Bitmap(w, h);

                    data = MaskImage.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
                                                     PixelFormat.Format32bppArgb);

                    fixed (byte* source = MaskFBytes)
                        Squish.DecompressImage(data.Scan0, w, h, (IntPtr)source, SquishFlags.Dxt1);

                    dest = (byte*)data.Scan0;

                    for (int i = 0; i < h * w * 4; i += 4)
                    {
                        //Reverse Red/Blue
                        byte b = dest[i];
                        dest[i] = dest[i + 2];
                        dest[i + 2] = b;
                    }

                    MaskImage.UnlockBits(data);
                }
            }
        }
    }
}