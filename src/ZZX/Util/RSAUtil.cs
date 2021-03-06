﻿
//using Jayrock.Json.Conversion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ZZX.Util
{
    public class RSAUtil
    {
        public static string Encrypt(string data, string publickey, string encoding = "UTF-8")
        {
            byte[] dataInBytes = Encoding.GetEncoding(encoding).GetBytes(data);
            byte[] btEncryptedSecret = Encrypt(dataInBytes, publickey);
            return Convert.ToBase64String(btEncryptedSecret);
        }

        public static string Decrypt(string data, string privateKey, string encoding = "UTF-8")
        {
            byte[] dataInBytes = Convert.FromBase64String(data);
            byte[] de = Decrypt(dataInBytes, privateKey);
            return Encoding.GetEncoding(encoding).GetString(de);
        }

        public static string Sign(string data, string privateKey, string encoding = "UTF-8")
        {
            privateKey = ParseRSAPrivateKey(privateKey);
            RSACryptoServiceProvider rsaCsp = new RSACryptoServiceProvider();
            rsaCsp.FromXmlString(privateKey);
            byte[] dataBytes = null;
            dataBytes = Encoding.GetEncoding(encoding).GetBytes(data);
            byte[] signatureBytes = rsaCsp.SignData(dataBytes, "SHA256");
            return Convert.ToBase64String(signatureBytes);
        }

        public static bool Verify(string content, string signedString, string publicKey, string encoding = "UTF-8")
        {
            publicKey = ParseRSAPublicKey(publicKey);
            RSACryptoServiceProvider rsaPub = new RSACryptoServiceProvider();
            rsaPub.PersistKeyInCsp = false;
            rsaPub.FromXmlString(publicKey);
            byte[] data = Encoding.GetEncoding(encoding).GetBytes(content);
            byte[] signedData = Convert.FromBase64String(signedString);
            bool result = rsaPub.VerifyData(data, "SHA256", signedData);
            return result;
        }

        private static byte[] Encrypt(byte[] dataInBytes, string publickey)
        {
            publickey = ParseRSAPublicKey(publickey);
            RSACryptoServiceProvider rsaSender = new RSACryptoServiceProvider();
            rsaSender.FromXmlString(publickey);


            int keySize = 0;
            int blockSize = 0;
            int lastblockSize = 0;
            int counter = 0;
            int iterations = 0;
            int index = 0;

            byte[] btPlaintextToken;
            byte[] btEncryptedToken;
            byte[] btEncryptedSecret;

            keySize = rsaSender.KeySize / 8;
            blockSize = keySize - 11;

            if ((dataInBytes.Length % blockSize) != 0)
            {
                iterations = dataInBytes.Length / blockSize + 1;
            }
            else
            {
                iterations = dataInBytes.Length / blockSize;
            }

            btPlaintextToken = new byte[blockSize];			//See REMARK 2 below.
            btEncryptedSecret = new byte[iterations * keySize];	//See REMARK 2 below.
            for (index = 0, counter = 0; counter < iterations; counter++, index += blockSize)
            {
                if (counter == (iterations - 1)) //last block
                {
                    //Size of last block to be encrypted.
                    lastblockSize = dataInBytes.Length % blockSize;
                    if (lastblockSize == 0)
                    {
                        lastblockSize = blockSize;
                    }

                    //REMARK 1: Caution! The last block of buffer may be of different size!
                    //		    Don't copy into a block which is larger than what's actually there in the last block. 
                    //		    - you won't get exception, but last block will not be decrypted properly.
                    btPlaintextToken = new byte[lastblockSize];
                    Array.Copy(dataInBytes, index, btPlaintextToken, 0, lastblockSize);
                }
                else
                {
                    Array.Copy(dataInBytes, index, btPlaintextToken, 0, blockSize);
                }

                //REMARK 2: Okay, here's the TRICK! (Again, you won't find this on MSDN!)
                //(a) Size of blocks going in: blockSize (btPlaintextToken going in)
                //(b) Size of blocks going out: keySize (btEncryptedToken coming out)
                //If you don't do this carefully, you get cryptographic exception: "Bad Data"
                btEncryptedToken = rsaSender.Encrypt(btPlaintextToken, false); //set fOAEP to true only if you have the high encryption pack.
                Array.Copy(btEncryptedToken, 0, btEncryptedSecret, counter * keySize, keySize);
            }

            return btEncryptedSecret;
        }

        public static byte[] Decrypt(byte[] dataInBytes, string privateKey)
        {
            privateKey = ParseRSAPrivateKey(privateKey);
            int keySize = 0;
            int blockSize = 0;
            int counter = 0;
            int iterations = 0;
            int index = 0;
            int byteCount = 0;

            byte[] btPlaintextToken;
            byte[] btEncryptedToken;
            byte[] btDecryptedSecret;

            RSACryptoServiceProvider rsaReceiver = new RSACryptoServiceProvider();
            rsaReceiver.FromXmlString(privateKey);
            keySize = rsaReceiver.KeySize / 8;
            blockSize = keySize - 11;

            //REMARK 3: This should always be true: "btEncryptedSecret.Length % keySize == 0"
            if ((dataInBytes.Length % keySize) != 0)
            {
                //Error condition.
                return null;
            }

            iterations = dataInBytes.Length / keySize;

            btEncryptedToken = new byte[keySize];	//See REMARK 4 below.
            Queue tokenQueue = new Queue();
            for (index = 0, counter = 0; counter < iterations; index += blockSize, counter++)
            {

                Array.Copy(dataInBytes, counter * keySize, btEncryptedToken, 0, keySize);

                //REMARK 4: Okay, here's another trick:
                //(a) Encrypted blocks going in: btEncryptedToken / size: keySize
                //(b) Decrypted plain text coming out: btPlaintextToken / size: blockSize
                btPlaintextToken = rsaReceiver.Decrypt(btEncryptedToken, false);
                tokenQueue.Enqueue(btPlaintextToken);
            }

            byteCount = 0;
            foreach (byte[] PlaintextToken in tokenQueue)
            {
                //REMARK 5: Size of decrypted buffer depends on size of last block.
                byteCount += PlaintextToken.Length;
            }

            counter = 0;
            btDecryptedSecret = new byte[byteCount]; //REMARK 5 (see foreach loop above)
            foreach (byte[] PlaintextToken in tokenQueue)
            {
                if (counter == (iterations - 1))
                {
                    Array.Copy(PlaintextToken, 0, btDecryptedSecret, btDecryptedSecret.Length - PlaintextToken.Length, PlaintextToken.Length);
                }
                else
                {
                    Array.Copy(PlaintextToken, 0, btDecryptedSecret, counter * blockSize, blockSize);
                }

                counter++;
            }

            return btDecryptedSecret;
        }

        public static string ParseRSAPrivateKey(string privateKey)
        {
            RsaPrivateCrtKeyParameters privateKeyParam = (RsaPrivateCrtKeyParameters)PrivateKeyFactory.CreateKey(Convert.FromBase64String(privateKey));

            return string.Format("<RSAKeyValue><Modulus>{0}</Modulus><Exponent>{1}</Exponent><P>{2}</P><Q>{3}</Q><DP>{4}</DP><DQ>{5}</DQ><InverseQ>{6}</InverseQ><D>{7}</D></RSAKeyValue>",
                Convert.ToBase64String(privateKeyParam.Modulus.ToByteArrayUnsigned()),
                Convert.ToBase64String(privateKeyParam.PublicExponent.ToByteArrayUnsigned()),
                Convert.ToBase64String(privateKeyParam.P.ToByteArrayUnsigned()),
                Convert.ToBase64String(privateKeyParam.Q.ToByteArrayUnsigned()),
                Convert.ToBase64String(privateKeyParam.DP.ToByteArrayUnsigned()),
                Convert.ToBase64String(privateKeyParam.DQ.ToByteArrayUnsigned()),
                Convert.ToBase64String(privateKeyParam.QInv.ToByteArrayUnsigned()),
                Convert.ToBase64String(privateKeyParam.Exponent.ToByteArrayUnsigned()));
        }

        public static string ParseRSAPublicKey(string publicKey)
        {
            RsaKeyParameters publicKeyParam = (RsaKeyParameters)PublicKeyFactory.CreateKey(Convert.FromBase64String(publicKey));
            return string.Format("<RSAKeyValue><Modulus>{0}</Modulus><Exponent>{1}</Exponent></RSAKeyValue>",
                Convert.ToBase64String(publicKeyParam.Modulus.ToByteArrayUnsigned()),
                Convert.ToBase64String(publicKeyParam.Exponent.ToByteArrayUnsigned()));
        }

        //public static string ParseBizResponse(string fullResponse, string privateKey, string charset)
        //{
        //    IDictionary json = JsonConvert.Import(fullResponse) as IDictionary;
        //    if (json != null)
        //    {
        //        string encryptedNode = json[ZZXConstants.ENCRYPTED].ToString();

        //        bool encrypted = false;
        //        if (encryptedNode != null)
        //        {
        //            encrypted = bool.Parse(encryptedNode);
        //        }

        //        string bizResponse = null;
        //        foreach (object key in json.Keys)
        //        {
        //            string keyStr = key as string;
        //            if (keyStr.EndsWith(ZZXConstants.RESPONSE_SUFFIX))
        //            {
        //                bizResponse = json[key] as string;
        //                if (encrypted)
        //                {
        //                    bizResponse = Decrypt(bizResponse, privateKey, charset);
        //                }
        //                break;
        //            }
        //        }

        //        return bizResponse;
        //    }

        //    return null;
        //}

        //public static void VerifySign(string fullResponse, string decryptedBizRsp, string publicKey, string charset)
        //{
            //IDictionary json = JsonConvert.Import(fullResponse) as IDictionary;
            //if (json != null)
            //{
            //    string bizResponseSign = json[ZZXConstants.BIZ_RESPONSE_SIGN] as string;
            //    if (bizResponseSign != null)
            //    {
            //        bool verifyResult = Verify(decryptedBizRsp, bizResponseSign, publicKey, charset);
            //        if (verifyResult == false)
            //        {
            //            throw new ZZXException("check sign failed: " + json.ToString());
            //        }

            //    }
            //}
        //}

        public static void VerifySign(string unsignString, string signString, string publicKey, string charset)
        {
            ////IDictionary<string,object> dic = JsonConvert.DeserializeObject<IDictionary<string,object>>(fullResponse);
            //IDictionary<string, object> dic = new Dictionary<string, object>();
            //JObject jObject = JsonConvert.DeserializeObject(fullResponse) as JObject;
            //if (jObject != null)
            //{
            //    //去掉 statuscode errmsg  sign 三个键值对 排序组合成待签名字符串
            //    dic.Add("method", jObject["method"].ToString());
            //    dic.Remove("errMsg");
            //    var signobj = dic["sign"];
            //    if (object.Equals(signobj, null) || object.Equals(signobj, DBNull.Value))
            //    {
            //        throw new ZZXException("check sign failed: " + dic.ToString());
            //    }
            //    var sign = signobj.ToString().Trim();
            //    //移除sign
            //    dic.Remove("sign");
            //    var d = dic.OrderBy(p => p.Key).ToDictionary(p => p.Key, o => o.Value);
            //    var s = WebUtils.BuildQuery(d, false, charset);
            //    bool verifyResult = Verify(s, sign, publicKey,charset);
            //    if (verifyResult == false)
            //    {
            //        throw new ZZXException("check sign failed: " + dic.ToString());
            //    }
            //}
            if (signString != null)
            {
                bool verifyResult = Verify(unsignString, signString, publicKey, charset);
                if (verifyResult == false)
                {
                    throw new ZZXException("check sign failed: " + unsignString);
                }

            }
        }
    }

}
