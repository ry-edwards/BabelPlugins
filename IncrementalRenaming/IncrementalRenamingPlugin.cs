using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Babel;
using Babel.Xml;

namespace IncrementalRenaming
{
    public class IncrementalRenamingPlugin : BabelPluginBase
    {
        private IncrementalRenamingListner renaming;

        public string XmlMapFilePath { get; set; }

        public IncrementalRenamingPlugin()
            : base("IncrementalRenaming")
        {

        }

        public override void OnInit(IBabelServiceProvider services, IBabelLogger logger)
        {
            var config = services.GetService<IBabelConfigurationService>();
            ParseArguments(config.Arguments);

            renaming = new IncrementalRenamingListner();
            services.AddService(renaming);

            WriteLogo();
        }

        public override void OnBegin(AssemblyDef assembly)
        {
            XmlMapFile mapFile = XmlMapFile.Load(XmlMapFilePath);
            var asmMap = mapFile.Assemblies.FirstOrDefault(item => item.FullName == assembly.FullName);

            var attrMvid = asmMap.Attribute("mvid");
            if (attrMvid != null)
            {
                // Check MVID of the original assembly
                Guid mvid;
                if (Guid.TryParse(attrMvid.Value, out mvid))
                {
                    if (assembly.Mvid != mvid)
                    {
                        Logger.Warning("The assembly {0} is not aligned with mapping file provided.", assembly.Name);
                    }
                }
            }

            renaming.AssemblyElement = asmMap;
        }

        public override void OnEnd(AssemblyDef assembly)
        {
            Logger.Warning("{0} of symbol(s) not found in mapping file", renaming.SymbolsNotFound.Count);
        }

        private void ParseArguments(IDictionary<string, string> arguments)
        {
            string mapFile;
            if (arguments.TryGetValue("mapfile", out mapFile))
                XmlMapFilePath = mapFile;
        }

        private void WriteLogo()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine(@"  __  __   __  ______  ______  ______  __    __  ______  __   __  ______  ______  __       ");
            builder.AppendLine(@" /\ \/\ '-.\ \/\  ___\/\  == \/\  ___\/\ '-./  \/\  ___\/\ '-.\ \/\__  _\/\  __ \/\ \      ");
            builder.AppendLine(@" \ \ \ \ \-.  \ \ \___\ \  __<\ \  __\\ \ \-./\ \ \  __\\ \ \-.  \/_/\ \/\ \  __ \ \ \____ ");
            builder.AppendLine(@"  \ \_\ \_\\'\_\ \_____\ \_\ \_\ \_____\ \_\ \ \_\ \_____\ \_\\'\_\ \ \_\ \ \_\ \_\ \_____\");
            builder.AppendLine(@"   \/_/\/_/ \/_/\/_____/\/_/ /_/\/_____/\/_/  \/_/\/_____/\/_/ \/_/  \/_/  \/_/\/_/\/_____/");
            builder.AppendLine(@"                                                                                           ");
            builder.AppendFormat("Map File: {0}", XmlMapFilePath);

            Logger.Write(builder.ToString());
        }
    }
}
