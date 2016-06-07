using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Babel;
using LZMA;
using System.IO;

namespace ResPacker
{
    public class ResPackerPlugin : BabelPluginBase
    {
        private MethodDef _unpackStream;

        public override string Description
        {
            get
            {
                return "Compress resource streams";
            }
        }

        public ResPackerPlugin()
            : base("ResPacker")
        {

        }

        public override void OnInit(IBabelServiceProvider services, IBabelLogger logger)
        {
            WriteLogo();
        }

        public override void OnBegin(AssemblyDef assembly)
        {
            var resources = assembly.Resources;
            if (resources.Count == 0)
                return;

            var entryPoints = new List<string>() {
                "System.Reflection.Assembly::GetManifestResourceStream(System.String):System.IO.Stream"
            };

            // Find all entry points
            var methods = entryPoints.ConvertAll(name => assembly.Find<MethodRef>(name)).Where(method => method != null).ToList();

            if (methods.Count == 0)
                return;

            // Get all resource entry points callers
            var query = from method in methods
                        from caller in assembly.CallersOf(method)
                        from resource in assembly.Resources
                        where caller.UserStrings.Contains(resource.Name)
                        group new { Caller = caller, Method = method } by resource into g
                        select new { Resource = g.Key, Groups = g };

            var resourceLoaders = query.ToList();
            if (resourceLoaders.Count == 0)
                return;

            // Merge decompress code
            MergeDecompress(assembly);

            foreach (var item in query)
            {
                var res = item.Resource;

                foreach (var group in item.Groups)
                {
                    MethodDef method = GetDecompressMethod(group.Method);
                    if (method != null)
                    {
                        group.Caller.ReplaceCall(group.Method, method);
                    }
                }

                var compressed = Compress(res);
                resources.Remove(res);
                resources.Add(compressed);

                double ratio = (((double)compressed.Data.Length) / res.Data.Length);
                Logger.Write("Resource {0} compress ratio {1:P}", res.Name, ratio);
            }
        }

        private void MergeDecompress(AssemblyDef target)
        {
            string lzma = Resources.LZMADecoderCode();
            string code = Resources.ResUnpakerCode();

            var unpacker = AssemblyDef.Compile(lzma, code);
            target.Merge(unpacker);

            _unpackStream = target.Find<MethodDef>("ResPacker.Code.ResUnpaker::UnpackResourceStream.*", true);
        }

        private MethodDef GetDecompressMethod(MethodRef method)
        {
            return _unpackStream;
        }

        private void WriteLogo()
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine();
            builder.AppendLine(@"8888888b.                   8888888b.     d8888          888                      ");
            builder.AppendLine(@"888   Y88b                  888   Y88b   d88888          888                      ");
            builder.AppendLine(@"888    888                  888    888  d88P888          888                      ");
            builder.AppendLine(@"888   d88P .d88b.  .d8888b  888   d88P d88P 888  .d8888b 888  888  .d88b.  888d888");
            builder.AppendLine(@"8888888P' d8P  Y8b 88K      8888888P' d88P  888 d88P'    888 .88P d8P  Y8b 888P'  ");
            builder.AppendLine(@"888 T88b  88888888 'Y8888b. 888      d88P   888 888      888888K  88888888 888    ");
            builder.AppendLine(@"888  T88b Y8b.          X88 888     d8888888888 Y88b.    888 '88b Y8b.     888    ");
            builder.AppendLine(@"888   T88b 'Y8888   88888P' 888    d88P     888  'Y8888P 888  888  'Y8888  888    ");

            Logger.Write(builder.ToString());
        }

        private ResourceDef Compress(ResourceDef resource)
        {
            CoderPropID[] propIDs = {
                CoderPropID.DictionarySize,
                CoderPropID.PosStateBits,
                CoderPropID.LitContextBits,
                CoderPropID.LitPosBits,
                CoderPropID.Algorithm,
                CoderPropID.NumFastBytes,
                CoderPropID.MatchFinder,
                CoderPropID.EndMarker
            };
            object[] properties = {
                (Int32)(1 << 23),
                (Int32)(2),
                (Int32)(3),
                (Int32)(0),
                (Int32)(2),
                (Int32)(128),
                "bt4",
                true
            };

            MemoryStream outStream = new MemoryStream();

            LZMAEncoder encoder = new LZMAEncoder();
            encoder.SetCoderProperties(propIDs, properties);
            encoder.WriteCoderProperties(outStream);

            MemoryStream inStream = new MemoryStream(resource.Data);
            encoder.Code(inStream, outStream, -1, -1);

            byte[] compressed = outStream.ToArray();
            return new ResourceDef(resource.Name, compressed);
        }
    }
}