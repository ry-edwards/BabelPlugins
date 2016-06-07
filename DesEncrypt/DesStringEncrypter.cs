using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

using Babel;
using System.Runtime.Serialization.Formatters.Binary;

namespace DesEncrypt
{
    public class DesStringEncrypter : IBabelStringEncryptionService, IDisposable
    {
        #region Fields
        private MethodDef _decryptString;
        private FieldDef _encryptedData;
        private TripleDESCryptoServiceProvider _algo;
        private List<string> _strings;
        #endregion

        #region Constructors
        public DesStringEncrypter(string password)
        {
            if (password == null)
                throw new ArgumentNullException("password");

            _strings = new List<string>();
            _algo = new TripleDESCryptoServiceProvider();
            _algo.Key = Encoding.UTF8.GetBytes(password);
            _algo.Mode = CipherMode.ECB;
            _algo.Padding = PaddingMode.PKCS7;
        }

        #endregion

        #region Methods
        public void Dispose()
        {
            _algo.Dispose();
        }

        public MethodDef DecryptMethod
        {
            get
            {
                return _decryptString;
            }
        }

        public string Encrypt(string source)
        {
            _strings.Add(source);
            int index = _strings.Count - 1;

            // Encode value
            StringBuilder builder = new StringBuilder();
            while (index > 0)
            {
                int val = index & 0xFF;
                builder.Insert(0, (char)val);
                index >>= 8;
            }
            return builder.ToString();
        }

        public void OnBeginMethod(MethodDef method)
        {
        }

        public void OnEndMethod(MethodDef method)
        {
        }

        public void MergeDecryptionCode(AssemblyDef target)
        {
            // Merge decryption code
            string code = Resources.DesStringDecrypterCode();
            var decrypter = AssemblyDef.Compile(code);
            target.Merge(decrypter);

            // Add rule to avoid recursive calls to decryption code
            // and method dead core removal
            ObfuscationRule rule = new ObfuscationRule("des_strings");
            rule.Exclude(Features.StringEncryption, Features.DeadCode)
                .Targeting(Targets.Methods)
                .Matching(new Regex("DesStringDecrypter.*"));

            rule.ApplyTo(target);

            // Get just merged methods
            _decryptString = target.Find<MethodDef>("DesEncrypt.Code.DesStringDecrypter::DecryptString.*", true);

            // Get field where to store encrypted data
            _encryptedData = target.Find<FieldDef>("DesEncrypt.Code.DesStringDecrypter::encrypted.*", true);
        }

        public void Terminate()
        {
            if (_strings.Count == 0)
                return;

            MemoryStream stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(_algo.Key);

                byte[] data = EncryptArray(_strings.ToArray());
                writer.Write(data);
            }

            _encryptedData.SetInitalValue(stream.ToArray());
        }

        private byte[] EncryptArray(Array array)
        {
            var formatter = new BinaryFormatter();
            MemoryStream source = new MemoryStream();
            formatter.Serialize(source, array);

            source.Seek(0, SeekOrigin.Begin);

            MemoryStream dest = new MemoryStream();
            using (var encryptor = _algo.CreateEncryptor())
            {
                using (CryptoStream cs = new CryptoStream(dest, encryptor, CryptoStreamMode.Write))
                {
                    source.CopyTo(cs);
                }
            }

            return dest.ToArray();
        }
        #endregion
    }
}
