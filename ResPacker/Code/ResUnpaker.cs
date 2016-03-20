using System;
using System.IO;
using System.Reflection;

namespace ResPacker.Code
{
    class ResUnpaker
    {
        public static Stream UnpackResourceStream(Assembly assembly, string name)
        {
            LZMA.LZMADecoder decoder = new LZMA.LZMADecoder();
            MemoryStream outStream = new MemoryStream();
            Stream inStream = assembly.GetManifestResourceStream(name);
            byte[] properties = new byte[5];
            inStream.Read(properties, 0, 5);
            decoder.SetDecoderProperties(properties);
            decoder.Code(inStream, outStream, -1, -1);
            outStream.Seek(0, SeekOrigin.Begin);
            return outStream;
        }
    }
}
