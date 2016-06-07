using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Babel;

namespace UnreadableNames
{
    public class UnreadableNamesPlugin : BabelPluginBase
    {
        private UnredableNamesService _renaming;

        public override string Description
        {
            get
            {
                return "This renaming algorithm generates unique names varing randomly the characted case of a fixed-lenght name.";
            }
        }

        public UnreadableNamesPlugin()
            : base("UnreadableNames")
        {
            
        }

        public override void OnInit(IBabelServiceProvider services, IBabelLogger logger)
        {
            var random = services.GetService<IBabelRandomGeneratorService>();
            _renaming = new UnredableNamesService(random);

            var config = services.GetService<IBabelConfigurationService>();
            ParseArguments(config.Arguments);

            _renaming.MakeSeed();

            services.AddService(_renaming);

            WriteLogo(); 
        }

        public override void OnEnd(AssemblyDef assembly)
        {
            Logger.Write("Number of names generated: {0}", _renaming.GeneratedNamesCount);
        }

        private void ParseArguments(IDictionary<string, string> arguments)
        {
            string alphabet;
            if (arguments.TryGetValue("alphabet", out alphabet))
                _renaming.Alphabet = alphabet;

            string nameLength;
            if (arguments.TryGetValue("namelength", out nameLength))
                _renaming.NameLength = int.Parse(nameLength);

            string prefixLength;
            if (arguments.TryGetValue("prefixlength", out prefixLength))
                _renaming.PrefixLength = int.Parse(prefixLength);
        }

        private void WriteLogo()
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine();
            builder.AppendLine(@" _   _      ______ _____     ______  ___  _     _          _   _      ___  ___ _____    ");
            builder.AppendLine(@"| | | |     | ___ \  ___|    |  _  \/ _ \| |   | |        | \ | |     |  \/  ||  ___|   ");
            builder.AppendLine(@"| | | |_ __ | |_/ / |__  __ _| | | / /_\ \ |__ | |     ___|  \| | __ _| .  . || |__ ___ ");
            builder.AppendLine(@"| | | | '_ \|    /|  __|/ _` | | | |  _  | '_ \| |    / _ \ . ` |/ _` | |\/| ||  __/ __|");
            builder.AppendLine(@"| |_| | | | | |\ \| |__| (_| | |/ /| | | | |_) | |___|  __/ |\  | (_| | |  | || |__\__ \");
            builder.AppendLine(@" \___/|_| |_\_| \_\____/\__,_|___/ \_| |_/_.__/\_____/\___\_| \_/\__,_\_|  |_/\____/___/");

            Logger.Write(builder.ToString());
        }
    }
}
