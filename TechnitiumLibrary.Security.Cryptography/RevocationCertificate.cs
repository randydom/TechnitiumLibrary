﻿/*
Technitium Library
Copyright (C) 2015  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.IO;
using System.Text;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;

namespace TechnitiumLibrary.Security.Cryptography
{
    public sealed class RevocationCertificate : WriteStream
    {
        #region variables

        string _serialNumber;
        DateTime _revokedOnUTC;
        byte[] _signature;
        string _hashAlgo;

        #endregion

        #region constructor

        public RevocationCertificate(Certificate certToRevoke, string hashAlgo, AsymmetricCryptoKey privateKey)
        {
            _serialNumber = certToRevoke.SerialNumber;
            _revokedOnUTC = DateTime.UtcNow;
            _signature = privateKey.Sign(GetHash(hashAlgo, _serialNumber, _revokedOnUTC), hashAlgo);
            _hashAlgo = hashAlgo;
        }

        public RevocationCertificate(Stream s)
        {
            ReadFrom(new BinaryReader(s));
        }

        public RevocationCertificate(BinaryReader bR)
        {
            ReadFrom(bR);
        }

        #endregion

        #region static

        public static void WriteNotFoundServerResponseTo(Stream s)
        {
            s.WriteByte(0); //not found
        }

        public static bool IsRevoked(Certificate certToCheck, out RevocationCertificate revokeCert)
        {
            if (certToCheck.RevocationURL == null)
                throw new CryptoException("Certificate does not support revocation.");

            using (WebClientEx client = new WebClientEx())
            {
                client.Timeout = 10000; //request timeout of 10 sec
                byte[] buffer = client.DownloadData(certToCheck.RevocationURL.AbsoluteUri + "?sn=" + certToCheck.SerialNumber);

                using (BinaryReader bR = new BinaryReader(new MemoryStream(buffer)))
                {
                    switch (bR.ReadByte())
                    {
                        case 0: //not found
                            revokeCert = null;
                            return false;

                        case 1:
                            revokeCert = new RevocationCertificate(bR);
                            break;

                        default:
                            throw new CryptoException("RevokedCertificate version not supported.");
                    }
                }

                return revokeCert.IsValid(certToCheck);
            }
        }

        private static bool IsValid(Certificate certToCheck, DateTime revokedOnUTC, byte[] signature, string hashAlgo)
        {
            Certificate signingCert;

            if (certToCheck.Type == CertificateType.RootCA)
                signingCert = certToCheck;
            else
                signingCert = certToCheck.IssuerSignature.SigningCertificate;

            return AsymmetricCryptoKey.Verify(GetHash(hashAlgo, certToCheck.SerialNumber, revokedOnUTC), signature, hashAlgo, signingCert);
        }

        private static byte[] GetHash(string hashAlgorithm, string serialNumber, DateTime revokedOnUTC)
        {
            using (MemoryStream mS = new MemoryStream())
            {
                BinaryWriter bW = new BinaryWriter(mS);

                //serial number
                bW.Write(Convert.ToByte(serialNumber.Length));
                bW.Write(Encoding.ASCII.GetBytes(serialNumber));

                //revoked on
                bW.Write(revokedOnUTC.ToBinary());

                bW.Flush();

                //reset
                mS.Position = 0;

                return System.Security.Cryptography.HashAlgorithm.Create(hashAlgorithm).ComputeHash(mS);
            }
        }

        #endregion

        #region private

        private void ReadFrom(BinaryReader bR)
        {
            if (Encoding.ASCII.GetString(bR.ReadBytes(2)) != "RC")
                throw new InvalidCertificateException("Invalid RevocationCertificate format.");

            switch (bR.ReadByte()) //version
            {
                case 1:
                    _serialNumber = Encoding.ASCII.GetString(bR.ReadBytes(bR.ReadByte()));
                    _revokedOnUTC = DateTime.FromBinary(bR.ReadInt64());
                    _signature = bR.ReadBytes(bR.ReadUInt16());
                    _hashAlgo = Encoding.ASCII.GetString(bR.ReadBytes(bR.ReadByte()));
                    break;

                default:
                    throw new InvalidCertificateException("RevocationCertificate format version not supported.");
            }
        }

        #endregion

        #region public

        public bool IsValid(Certificate certToCheck)
        {
            if (certToCheck.SerialNumber != _serialNumber)
                throw new CryptoException("Certificate serial number does not match with the revocation certificate.");

            return IsValid(certToCheck, _revokedOnUTC, _signature, _hashAlgo);
        }

        public void WriteFoundServerResponseTo(Stream s)
        {
            BinaryWriter bW = new BinaryWriter(s);
            bW.Write((byte)1); //found
            WriteTo(bW);
            bW.Flush();
        }

        public override void WriteTo(BinaryWriter bW)
        {
            //format
            bW.Write(Encoding.ASCII.GetBytes("RC"));

            //version
            bW.Write((byte)1);

            //serial number
            bW.Write(Convert.ToByte(_serialNumber.Length));
            bW.Write(Encoding.ASCII.GetBytes(_serialNumber));

            //revoked on
            bW.Write(_revokedOnUTC.ToBinary());

            //signature
            bW.Write(Convert.ToUInt16(_signature.Length));
            bW.Write(_signature);

            //hash algo
            bW.Write(Convert.ToByte(_hashAlgo.Length));
            bW.Write(Encoding.ASCII.GetBytes(_hashAlgo));
        }

        #endregion

        #region properties

        public string SerialNumber
        { get { return _serialNumber; } }

        public DateTime RevokedOnUTC
        { get { return _revokedOnUTC; } }

        public byte[] Signature
        { get { return _signature; } }

        public string HashAlgorithm
        { get { return _hashAlgo; } }

        #endregion
    }
}