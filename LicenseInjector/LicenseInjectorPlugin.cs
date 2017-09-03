using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Babel;

namespace LicenseInjector
{
    public class LicenseInjectorPlugin : BabelPluginBase
    {
        public LicenseInjectorPlugin()
            : base("LicenseInjector")
        {
        }

        public override void OnBeginPhase(AssemblyDef assembly, BabelPhase phase)
        {
            if (phase.IsMerge) {
                Logger.Write("Adding license check");
                MergeLicensingCode(assembly);
            }
        }

        private void MergeLicensingCode(AssemblyDef target)
        {
            // Merge decryption code
            string code = Resources.LicenseFileCheckWinFormCode();

            Compiler compiler = new Compiler();
            compiler.ReferencedAssemblies.Add("System.dll");
            compiler.ReferencedAssemblies.Add("System.Windows.Forms.dll");
            compiler.ReferencedAssemblies.Add(@".\Babel.Licensing.dll");

            var licensing = AssemblyDef.Compile(compiler, code);
            target.Merge(licensing, true);

            var method = target.Find<MethodDef>(".*::ValidateLicense.*", true);

            // Call license validation at module initializer
            target.CallAtModuleInitializer(method);
        }
    }
}
