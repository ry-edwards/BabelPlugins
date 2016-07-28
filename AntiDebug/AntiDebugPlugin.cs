using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Babel;

namespace AntiDebug
{
    public class AntiDebugPlugin : BabelPluginBase
    {
        public override string Description
        {
            get
            {
                return "Terminates the process when a debugged is being attached.";
            }
        }

        public AntiDebugPlugin()
            : base("AntiDebug")
        {

        }

        public override void OnBegin(AssemblyDef assembly)
        {
            MergeAntiDebug(assembly);
        }

        private void MergeAntiDebug(AssemblyDef target)
        {
            string code = Resources.AntiDebugNetCode();

            Compiler compiler = new Compiler();
            compiler.ReferencedAssemblies.Add("System.dll");

            var antiDebug = AssemblyDef.Compile(compiler, code);
            target.Merge(antiDebug);

            var method = target.Find<MethodDef>("AntiDebug.Code.AntiDebug::Start.*", true);

            // Call AntiDebug::Start at module initializer
            target.CallAtModuleInitializer(method);
        }
    }
}
