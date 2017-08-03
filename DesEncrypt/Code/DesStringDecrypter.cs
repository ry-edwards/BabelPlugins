using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DesEncrypt.Code
{
    class DesStringDecrypter
    {
        class Decrypter
        {
            TripleDESCryptoServiceProvider algo;
            string[] strings;

            public Decrypter(byte[] data)
            {
                algo = new TripleDESCryptoServiceProvider();
                algo.Mode = CipherMode.ECB;
                algo.Padding = PaddingMode.PKCS7;
                var decryptor = algo.CreateDecryptor();

                // Read password
                MemoryStream stream = new MemoryStream(data);
                using (var reader = new BinaryReader(stream))
                {
                    algo.Key = reader.ReadBytes(24);

                    // Decrypt int values
                    int size = data.Length - 24;
                    if (size > 0)
                        strings = (string[])DecryptArray(reader.ReadBytes(size));
                }
            }

            public string GetString(string value)
            {
                int inx = 0;
                for (int i = 0; i < value.Length; i++)
                {
                    inx <<= 8;
                    inx |= (int)value[i];
                }

                return strings[inx];
            }

            public Array DecryptArray(byte[] array)
            {
                MemoryStream source = new MemoryStream(array);
                MemoryStream dest = new MemoryStream();
                using (var decryptor = algo.CreateDecryptor())
                {
                    using (CryptoStream cs = new CryptoStream(source, decryptor, CryptoStreamMode.Read))
                        cs.CopyTo(dest);
                }

                dest.Seek(0, SeekOrigin.Begin);
                var formatter = new BinaryFormatter();
                return (Array)formatter.Deserialize(dest);
            }
        }

        static byte[] encrypted;
        static Decrypter decrypter;

        static DesStringDecrypter()
        {
            decrypter = new Decrypter(encrypted);
        }

        public static string DecryptString(string value)
        {
            return decrypter.GetString(value);
        }
    }
}
