using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DesEncrypt.Code
{
    class DesValueDecrypter
    {
        class Decrypter
        {
            TripleDESCryptoServiceProvider algo;
            int[] values;

            public Decrypter(byte[] data)
            {
                algo = new TripleDESCryptoServiceProvider();
                algo.Mode = CipherMode.ECB;
                algo.Padding = PaddingMode.PKCS7;

                // Read password
                MemoryStream stream = new MemoryStream(data);
                using (var reader = new BinaryReader(stream))
                {
                    algo.Key = reader.ReadBytes(24);

                    // Decrypt int values
                    int size = data.Length - 24;
                    if (size > 0)
                        values = (int[])DecryptArray(reader.ReadBytes(size));
                }
            }

            public int GetInt(int value)
            {
                return values[value];
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

        static DesValueDecrypter()
        {
            decrypter = new Decrypter(encrypted);
        }

        public static int DecryptInt32(int value)
        {
            return decrypter.GetInt(value);
        }

        public static Array DecryptArray(byte[] array)
        {
            return decrypter.DecryptArray(array);
        }
    }
}
