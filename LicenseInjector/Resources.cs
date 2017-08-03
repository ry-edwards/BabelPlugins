using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LicenseInjector
{
    class Resources
    {
        public static string LicenseFileCheckWinFormCode()
        {
            var assembly = typeof(Resources).Assembly;

            using (var stream = assembly.GetManifestResourceStream("LicenseInjector.Code.LicenseFileCheckWinForm.cs"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }
}
