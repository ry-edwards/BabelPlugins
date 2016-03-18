using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Babel;

namespace DesEncrypt
{
    public class DesEncryptPlugin : BabelPluginBase
    {
        public DesStringEncrypter StringEncrypter { get; set; }

        public DesValueEncrypter ValueEncrypter { get; set; }
        
        public string Password { get; set; }

        public DesEncryptPlugin()
            : base("DES crypter")
        {
        }

        public override string Description
        {
            get
            {
                return "Encrypt string and vvalues using Triple DES";
            }
        }
        public override void OnInit(IBabelServiceProvider services, IBabelLogger logger)
        {
            var config = services.GetService<IBabelConfigurationService>();
            ParseArguments(config.Arguments);

            if (config.UseCustomStringEncryption)
            {
                StringEncrypter = new DesStringEncrypter(Password);
                services.AddService<IBabelStringEncryptionService>(StringEncrypter);
            }

            if (config.UseCustomValueEncryption)
            {
                ValueEncrypter = new DesValueEncrypter(Password);
                services.AddService<IBabelValueEncryptionService>(ValueEncrypter);
            }

            WriteLogo();
        }

        private void ParseArguments(IDictionary<string, string> arguments)
        {
            string password;
            if (!arguments.TryGetValue("despassword", out password))
                password = Guid.NewGuid().ToString("N").ToLower();

            // Must be at least 24 chars
            Password = password.PadRight(24, '#').Substring(0, 24);
        }

        public override void OnBegin(AssemblyDef assembly)
        {
            Logger.Write("DES crypter {0}", assembly.Framework);

            if (StringEncrypter != null)
                StringEncrypter.MergeDecryptionCode(assembly);

            if (ValueEncrypter != null)
                ValueEncrypter.MergeDecryptionCode(assembly);
        }

        public override void OnEnd(AssemblyDef assembly)
        {
            if (StringEncrypter != null)
            {
                StringEncrypter.Terminate();
                StringEncrypter.Dispose();
            }

            if (ValueEncrypter != null)
            {
                ValueEncrypter.Terminate();
                ValueEncrypter.Dispose();
            }
        }

        private void WriteLogo()
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine();
            builder.AppendLine(@" _____            ______                             _   ");
            builder.AppendLine(@"|  __ \          |  ____|                           | |  ");
            builder.AppendLine(@"| |  | | ___  ___| |__   _ __   ___ _ __ _   _ _ __ | |_ ");
            builder.AppendLine(@"| |  | |/ _ \/ __|  __| | '_ \ / __| '__| | | | '_ \| __|");
            builder.AppendLine(@"| |__| |  __/\__ \ |____| | | | (__| |  | |_| | |_) | |_ ");
            builder.AppendLine(@"|_____/ \___||___/______|_| |_|\___|_|   \__, | .__/ \__|");
            builder.AppendLine(@"                                          __/ | |        ");
            builder.AppendLine(@"                                         |___/|_|        ");
            builder.AppendLine();
            builder.AppendFormat("DES Password: {0}", Password);
            builder.AppendLine();
            builder.AppendFormat("DES String Encryption: {0}", StringEncrypter != null);
            builder.AppendLine();
            builder.AppendFormat("DES Value Encryption: {0}", ValueEncrypter != null);
            builder.AppendLine();

            Logger.Write(builder.ToString());
        }
    }
}
