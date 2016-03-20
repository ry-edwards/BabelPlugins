using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResPacker
{
    class Resources
    {
        public static string LZMADecoderCode()
        {
            var assembly = typeof(Resources).Assembly;

            using (var stream = assembly.GetManifestResourceStream("ResPacker.Code.LZMADecoder.cs"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        public static string ResUnpakerCode()
        {
            var assembly = typeof(Resources).Assembly;

            using (var stream = assembly.GetManifestResourceStream("ResPacker.Code.ResUnpaker.cs"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }
}
