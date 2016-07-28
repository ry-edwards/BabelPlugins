using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntiDebug
{
    class Resources
    {
        public static string AntiDebugNetCode()
        {
            var assembly = typeof(Resources).Assembly;

            using (var stream = assembly.GetManifestResourceStream("AntiDebug.Code.AntiDebug.cs"))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }
}
