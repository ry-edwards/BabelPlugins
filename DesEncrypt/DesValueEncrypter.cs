using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Runtime.Serialization.Formatters.Binary;

using Babel;

namespace DesEncrypt
{
    public class DesValueEncrypter : IBabelValueEncryptionService, IDisposable
    {
        #region Fields
        private MethodDef _decryptArray;
        private MethodDef _decryptInt32;
        private FieldDef _encryptedData;

        private TripleDESCryptoServiceProvider _algo;
        private List<int> _values;
        #endregion

        #region Properties
        public MethodDef DecryptArrayMethod
        {
            get
            {
                return _decryptArray;
            }
        }

        public MethodDef DecryptInt32Method
        {
            get
            {
                return _decryptInt32;
            }
        } 
        #endregion

        #region Not Implemented
        public MethodDef DecryptDoubleMethod
        {
            get
            {
                return null;
            }
        }

        public MethodDef DecryptInt64Method
        {
            get
            {
                return null;
            }
        }

        public MethodDef DecryptSingleMethod
        {
            get
            {
                return null;
            }
        }

        public int? EncryptDouble(double source)
        {
            throw new NotImplementedException();
        }

        public int? EncryptInt64(long source)
        {
            throw new NotImplementedException();
        }

        public int? EncryptSingle(float source)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region Constructors
        public DesValueEncrypter(string password)
        {
            if (password == null)
                throw new ArgumentNullException("password");

            _values = new List<int>();
            _algo = new TripleDESCryptoServiceProvider();
            _algo.Key = Encoding.UTF8.GetBytes(password);
            _algo.Mode = CipherMode.ECB;
            _algo.Padding = PaddingMode.PKCS7;
        }

        #endregion

        #region Methods
        public void Dispose()
        {
            if (_algo != null)
                _algo.Dispose();
        }

        public void OnBeginMethod(MethodDef method)
        {
        }

        public void OnEndMethod(MethodDef method)
        {
        }

        public byte[] EncryptArray(Array array)
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

        public int? EncryptInt32(int source)
        {
            _values.Add(source);
            return _values.Count - 1;
        }

        public void MergeDecryptionCode(AssemblyDef target)
        {
            // Merge decryption code
            string code = Resources.DesValueDecrypterCode();
            var decrypter = AssemblyDef.Compile(code);
            target.Merge(decrypter);

            // Add rule to avoid recursive calls to decryption code
            // and method dead core removal
            ObfuscationRule rule = new ObfuscationRule("des_values");
            rule.Exclude(Features.ValueEncryption, Features.DeadCode)
                .Targeting(Targets.Methods)
                .Matching(new Regex("DesValueDecrypter.*"));

            rule.ApplyTo(target);

            // Get merged methods
            _decryptArray = target.Find<MethodDef>("DesEncrypt.Code.DesValueDecrypter::DecryptArray.*", true);
            _decryptInt32 = target.Find<MethodDef>("DesEncrypt.Code.DesValueDecrypter::DecryptInt32.*", true);

            // Store to encrypted data
            _encryptedData = target.Find<FieldDef>("DesEncrypt.Code.DesValueDecrypter::encrypted.*", true);
        }

        public void Terminate()
        {
            MemoryStream stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(_algo.Key);

                if (_values.Count > 0)
                {
                    byte[] data = EncryptArray(_values.ToArray());
                    writer.Write(data);
                }
            }

            _encryptedData.SetInitalValue(stream.ToArray());
        } 
        #endregion
    }
}