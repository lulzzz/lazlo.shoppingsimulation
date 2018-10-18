using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace Lazlo.ShoppingSimulation.ConsumerSimulationActor
{
    public class Mp4TagExtractor
    {
        public static Task<List<KeyValuePair<string, string>>> ParseTags(Stream mp4Stream)
        {
            using (BinaryReader reader = new BinaryReader(mp4Stream))
            {
                List<KeyValuePair<string, string>> meta = null;

                while (true)
                {
                    byte[] lengthBuffer = reader.ReadBytes(4);

                    lengthBuffer = lengthBuffer.Reverse().ToArray();

                    int length = BitConverter.ToInt32(lengthBuffer, 0);
                    byte[] tagName = reader.ReadBytes(4);

                    string tName = System.Text.Encoding.UTF8.GetString(tagName);

                    byte[] contentBuffer = reader.ReadBytes(length - 8);

                    if (tName == "moov")
                    {
                        meta = ParseMoov(contentBuffer);
                    }

                    if (mp4Stream.Length == mp4Stream.Position)
                    {
                        break;
                    }
                }

                if (meta == null)
                {
                    meta = new List<System.Collections.Generic.KeyValuePair<string, string>>();
                }

                return Task.FromResult(meta);
            }
        }

        private static List<KeyValuePair<string, string>> ParseMoov(byte[] moovContent)
        {
            MemoryStream ms = new MemoryStream(moovContent);

            BinaryReader reader = new BinaryReader(ms);

            while (true)
            {
                byte[] lengthBuffer = reader.ReadBytes(4);

                lengthBuffer = lengthBuffer.Reverse().ToArray();

                int length = BitConverter.ToInt32(lengthBuffer, 0);
                byte[] tagName = reader.ReadBytes(4);

                string tName = System.Text.Encoding.UTF8.GetString(tagName);

                byte[] contentBuffer = reader.ReadBytes(length - 8);

                if (tName == "udta")
                {
                    return ParseUdta(contentBuffer);
                }

                if (ms.Length == ms.Position)
                {
                    break;
                }
            }

            return new List<KeyValuePair<string, string>>();
        }

        private static List<KeyValuePair<string, string>> ParseUdta(byte[] udtaContent)
        {
            MemoryStream ms = new MemoryStream(udtaContent);

            BinaryReader reader = new BinaryReader(ms);

            while (true)
            {
                byte[] lengthBuffer = reader.ReadBytes(4);

                lengthBuffer = lengthBuffer.Reverse().ToArray();

                int length = BitConverter.ToInt32(lengthBuffer, 0);
                byte[] tagName = reader.ReadBytes(4);

                string tName = System.Text.Encoding.UTF8.GetString(tagName);

                byte[] contentBuffer = reader.ReadBytes(length - 8);

                if (tName == "meta")
                {
                    return ParseMeta(contentBuffer);
                }

                if (ms.Length == ms.Position)
                {
                    break;
                }
            }

            return new List<KeyValuePair<string, string>>();
        }

        private static List<KeyValuePair<string, string>> ParseMeta(byte[] metaContent)
        {
            MemoryStream ms = new MemoryStream(metaContent);

            BinaryReader reader = new BinaryReader(ms);

            // Searching some open source code, the four leading bytes here may be the version and flags for the meta box
            byte[] mystery = reader.ReadBytes(4);           // Didn't find any explanation for this anywhere, and it really bugs me
            int mysteryNumber = BitConverter.ToInt32(mystery, 0);

            while (true)
            {
                byte[] lengthBuffer = reader.ReadBytes(4);

                lengthBuffer = lengthBuffer.Reverse().ToArray();

                int length = BitConverter.ToInt32(lengthBuffer, 0);
                byte[] tagName = reader.ReadBytes(4);

                string tName = System.Text.Encoding.UTF8.GetString(tagName);

                byte[] contentBuffer = reader.ReadBytes(length - 8);

                if (tName == "ilst")
                {
                    return ParseIlst(contentBuffer);
                }

                if (ms.Length == ms.Position)
                {
                    break;
                }
            }

            return new List<KeyValuePair<string, string>>();
        }

        private static List<KeyValuePair<string, string>> ParseIlst(byte[] metaContent)
        {
            List<KeyValuePair<string, string>> result = new List<System.Collections.Generic.KeyValuePair<string, string>>();

            if (metaContent.Length == 0)
            {
                return result;
            }

            MemoryStream ms = new MemoryStream(metaContent);

            BinaryReader reader = new BinaryReader(ms);

            while (true)
            {
                byte[] lengthBuffer = reader.ReadBytes(4);

                lengthBuffer = lengthBuffer.Reverse().ToArray();

                int length = BitConverter.ToInt32(lengthBuffer, 0);
                byte[] tagName = reader.ReadBytes(4);

                string tName = System.Text.Encoding.UTF8.GetString(tagName);

                byte[] contentBuffer = reader.ReadBytes(length - 8);

                int subContentLength = BitConverter.ToInt32(contentBuffer.Take(4).Reverse().ToArray(), 0);

                string subContentName = System.Text.Encoding.UTF8.GetString(contentBuffer, 4, 4);

                var wtf = contentBuffer.Skip(16).ToArray();

                string blah = System.Text.Encoding.UTF8.GetString(wtf);

                result.Add(new KeyValuePair<string, string>(tName, blah));

                if (ms.Length == ms.Position)
                {
                    break;
                }
            }

            return result;
        }
    }
}
