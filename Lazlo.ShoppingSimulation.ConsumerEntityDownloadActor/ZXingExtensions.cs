using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ZXing.Common;

namespace Lazlo.ShoppingSimulation.ConsumerEntityDownloadActor
{
    public static class ZXingExtensions
    {
        public static byte[] ToPng(this BitMatrix matrix)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                AppendHeader(bw);

                AppendIHDR(bw, matrix.Width, matrix.Height);

                AppendIDATChunks(bw, matrix);

                AppendIEND(bw);

                bw.Flush();

                return ms.ToArray();
            }
        }

        private static void AppendIDATChunks(BinaryWriter bw, BitMatrix matrix)
        {
            var compressedImageData = CreateCompressedImage(matrix);

            var chunkSize = 31500;

            var chunkCount = compressedImageData.Length / chunkSize;

            if (compressedImageData.Length % chunkSize != 0)
            {
                chunkCount++;
            }

            for (int i = 0; i < chunkCount; i++)
            {
                int count = Math.Min(compressedImageData.Length - i * chunkSize, chunkSize);

                AppendIDAT(bw, compressedImageData, i * chunkSize, count);
            }
        }

        private static byte[] CreateCompressedImage(BitMatrix matrix)
        {
            int rowBytes = matrix.Width / 8;

            if ((matrix.Width % 8) != 0)
            {
                rowBytes++;
            }

            rowBytes++; // the first byte of every row is the filtering type for the row

            using (Adler32 adler32 = new Adler32())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] zlibHeader = new byte[2];

                zlibHeader[0] = 0x78;   //deflate compression with 32k window size, 7 is window size, 8 is algorithm
                zlibHeader[1] = 0xDA;   //max compression, no dictionary, and associated checksum

                ms.Write(zlibHeader, 0, zlibHeader.Length);

                BitArray xor = new BitArray(matrix.Width);
                BitArray target = new BitArray(matrix.Width);

                for (int i = 0; i < matrix.Width; i++)
                {
                    xor.flip(i);
                }

                byte[] rowBuffer = new byte[rowBytes];

                using (DeflateStream deflateStream = new DeflateStream(ms, CompressionLevel.Optimal, true))
                {
                    for (int y = 0; y < matrix.Height; y++)
                    {
                        matrix.getRow(y, target);

                        target.xor(xor);

                        target.toBytes(0, rowBuffer, 1, rowBytes - 1);

                        //Updated the crc of the clear bytes
                        adler32.TransformBlock(rowBuffer, 0, rowBuffer.Length, null, 0);

                        // Write the row bytes to the deflate stream
                        deflateStream.Write(rowBuffer, 0, rowBuffer.Length);
                    }
                }

                adler32.TransformFinalBlock(new byte[0], 0, 0);

                byte[] adlerReversed = adler32.Hash.Reverse().ToArray();

                ms.Write(adlerReversed, 0, adlerReversed.Length);

                return ms.ToArray();
            }
        }

        private static void AppendIDAT(BinaryWriter writer, byte[] compressedImageBytes, int index, int count)
        {
            byte[] chunkName = new byte[] { (byte)'I', (byte)'D', (byte)'A', (byte)'T' };

            byte[] chunkSizeBytes = BitConverter.GetBytes(count).Reverse().ToArray();

            using (Crc32 crc32 = new Crc32())
            {
                crc32.TransformBlock(chunkName, 0, chunkName.Length, null, 0);
                crc32.TransformFinalBlock(compressedImageBytes, index, count);

                byte[] chunkCrc = crc32.Hash.Reverse().ToArray();

                writer.Write(chunkSizeBytes);
                writer.Write(chunkName);
                writer.Write(compressedImageBytes, index, count);
                writer.Write(chunkCrc);
            }
        }

        private static void AppendHeader(BinaryWriter writer)
        {
            // file png identifier
            byte[] identifier = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

            writer.Write(identifier);
        }

        private static void AppendIEND(BinaryWriter writer)
        {
            // iend is an empty chunk, so 0 length, the 4 byte chunk name, and the crc which is always the same
            writer.Write(new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 });
        }

        private static void AppendIHDR(BinaryWriter writer, int width, int height)
        {
            // The length of this header is always 13
            writer.Write(new byte[] { 0, 0, 0, 13 });

            byte[] widthBytes = BitConverter.GetBytes(width);
            byte[] heightBytes = BitConverter.GetBytes(height);

            // always 13 byte IHDR header length 
            byte[] chunk = new byte[] { 0x49, 0x48, 0x44, 0x52, widthBytes[3], widthBytes[2], widthBytes[1], widthBytes[0], heightBytes[3], heightBytes[2], heightBytes[1], heightBytes[0], 1, 0, 0, 0, 0 };

            using (Crc32 crc32 = new Crc32())
            {
                crc32.TransformFinalBlock(chunk, 0, chunk.Length);

                byte[] chunkCrc = crc32.Hash.Reverse().ToArray();

                writer.Write(chunk);
                writer.Write(chunkCrc);
            }
        }
    }
}
