using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesEncrypt
{
    class Resources
    {
        public static string DesStringDecrypterCode()
        {
            var assembly = typeof(Resources).Assembly;

            using (var stream = assembly.GetManifestResourceStream("DesEncrypt.Code.DesStringDecrypter.cs"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        public static string DesValueDecrypterCode()
        {
            var assembly = typeof(Resources).Assembly;

            using (var stream = assembly.GetManifestResourceStream("DesEncrypt.Code.DesValueDecrypter.cs"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }
}
