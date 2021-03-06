﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TLSHandler.Enums;
using TLS = TLSHandler.Internal.TLS;
using Records = TLSHandler.Internal.TLS.Records;
using Fragments = TLSHandler.Internal.TLS.Fragments;
using Handshakes = TLSHandler.Internal.TLS.Handshakes;
using Extensions = TLSHandler.Internal.TLS.Extensions;

namespace TLSHandler.Handler
{
    class Session12
    {
        public TLSSessionState State { get; protected set; }

        protected readonly NegotiationParams _params = null;
        protected readonly string _pubkeyfile = null;
        protected readonly string _prvkeyfile = null;
        protected ulong _receiveSeqNum = 0;
        protected ulong _sendSeqNum = 0;
        protected X509Certificate2[] _clientCertificates = null;
        protected List<Handshakes.Fragment> _handshakeMessages = new List<Handshakes.Fragment>();

        public Func<X509Certificate2[], bool> ClientCertificatesCallback { get; set; }

        public Session12(NegotiationParams para, string pub_crt_filepath, string prv_pfx_filepath)
        {
            _params = para;
            _pubkeyfile = pub_crt_filepath;
            _prvkeyfile = prv_pfx_filepath;
        }

        protected void AppendHandshakeMessages(Handshakes.Fragment fragment)
        {
            _handshakeMessages.Add(fragment);
        }

        protected byte[] GetHandshakeMessages(bool withoutLast = false)
        {
            var ret = new List<byte>();
            for (int i = 0; i < _handshakeMessages.Count; i++)
            {
                var fr = _handshakeMessages[i];
                if (!withoutLast || i < _handshakeMessages.Count - 1)
                    ret.AddRange(fr.Data);
            }
            return ret.ToArray();
        }

        #region session info debug
        // for debug only
        protected Dictionary<string, Dictionary<string, string>> _sessionInfo = new Dictionary<string, Dictionary<string, string>>();
        public Dictionary<string, Dictionary<string, string>> GetSessionInfo()
        {
            return _sessionInfo;
        }
        protected void LogSessionInfo(Fragments.FragmentBody fragBody)
        {
#if DEBUG
            if (fragBody is Fragments.ClientHello ch)
            {
                _sessionInfo.Add("ClientHello", new Dictionary<string, string>());
                _sessionInfo["ClientHello"].Add("ProtocolVersion", ch.ProtocolVersion.ToString());
                _sessionInfo["ClientHello"].Add("CipherSuites", string.Join(", ", ch.CipherSuites.Ciphers.Select(c => c.ToString())));
                _sessionInfo["ClientHello"].Add("CompressionMethods", string.Join(", ", ch.CompressionMethods.Methods.Select(c => c.ToString())));
                if (ch.ServerName != null)
                    _sessionInfo["ClientHello"].Add("Extensions/ServerName", string.Join(", ", ch.ServerName));
                if (ch.SupportedVersions != null)
                    _sessionInfo["ClientHello"].Add("Extensions/SupportedVersions", string.Join(", ", ch.SupportedVersions.Select(c => c.ToString())));
                if (ch.KeyShare != null)
                {
                    for (int i = 0; i < ch.KeyShare.Length; i++)
                    {
                        _sessionInfo["ClientHello"].Add($"Extensions/KeyShare {i}", $"({ch.KeyShare[i].Group}){string.Join(", ", Utils.BytesString(ch.KeyShare[i].KeyExchange))}");
                    }
                }
                if (ch.PskKeyExchangeModes != null)
                    _sessionInfo["ClientHello"].Add("Extensions/PskKeyExchangeModes", string.Join(", ", ch.PskKeyExchangeModes.Select(c => c.ToString())));
                if (ch.SupportedGroups != null)
                    _sessionInfo["ClientHello"].Add("Extensions/SupportedGroups", string.Join(", ", ch.SupportedGroups.Select(c => c.ToString())));
                if (ch.SignatureAlgorithms != null)
                    _sessionInfo["ClientHello"].Add("Extensions/SignatureAlgorithms", string.Join(", ", ch.SignatureAlgorithms.Select(c => c.ToString())));
                if (ch.EcPointFormats != null)
                    _sessionInfo["ClientHello"].Add("Extensions/EcPointFormats", string.Join(", ", ch.EcPointFormats.Select(c => c.ToString())));
            }
            else if (fragBody is Fragments.ServerHello sh)
            {
                _sessionInfo.Add("ServerHello", new Dictionary<string, string>());
                _sessionInfo["ServerHello"].Add("ProtocolVersion", sh.ProtocolVersion.ToString() + (this is Session13 ? " (TLS 1.3 Payload)" : string.Empty));
                _sessionInfo["ServerHello"].Add("CipherSuite", sh.CipherSuite.ToString());
                _sessionInfo["ServerHello"].Add("CompressionMethod", sh.CompressionMethod.ToString());
                if (sh.Extensions != null)
                {
                    foreach (var ext in sh.Extensions)
                    {
                        if (ext is Extensions.KeyShare ks)
                        {
                            _sessionInfo["ServerHello"].Add("Extensions/KeyShare", $"({ks.Entries[0].Group}){Utils.BytesString(ks.Entries[0].KeyExchange)}");
                        }
                        else if (ext is Extensions.SupportedVersions sv)
                        {
                            _sessionInfo["ServerHello"].Add("Extensions/SupportedVersions", string.Join(", ", sv.Versions.Select(c => c.ToString())));
                        }
                    }
                }
            }
            else if (fragBody is Fragments.ClientKeyExchange cke)
            {
                _sessionInfo.Add("ClientKeyExchange", new Dictionary<string, string>());
                if (_params.Cipher.IsRsaKeyExchange)
                    _sessionInfo["ClientKeyExchange"].Add("RSAEncrypted_PreMasterSecret", Utils.BytesString(cke.RSA_PreMasterSecret));
                else
                    _sessionInfo["ClientKeyExchange"].Add("ECDH_PublicKey", Utils.BytesString(cke.ECDH_Pubkey));
            }
            else if (fragBody is Fragments.ServerKeyExchange ske)
            {
                _sessionInfo.Add("ServerKeyExchange", new Dictionary<string, string>());
                _sessionInfo["ServerKeyExchange"].Add("ECDH_PublicKey", Utils.BytesString(ske.Pubkey));
                _sessionInfo["ServerKeyExchange"].Add("CurveType", ske.CurveType.ToString());
                _sessionInfo["ServerKeyExchange"].Add("NamedCurve", ske.NamedCurve.ToString());
                _sessionInfo["ServerKeyExchange"].Add("SignatureAlgorithm", ske.SignatureAlgorithm.ToString());
                _sessionInfo["ServerKeyExchange"].Add("Signature", Utils.BytesString(ske.Signature));
            }
            else if (fragBody is Fragments.Certificate cer)
            {
                var key = "Server Certificate";
                if (_sessionInfo.ContainsKey(key))
                    key = "Client Certificate";
                _sessionInfo.Add(key, new Dictionary<string, string>());
                for (int i = 0; i < cer.Certs.Length; i++)
                    _sessionInfo[key].Add($"Certificate Chain {i}", $"Subject: {cer.Certs[i].Subject}, Issuer: {cer.Certs[i].Issuer}");
            }
            else if (fragBody is Fragments.CertificateRequest req)
            {
                _sessionInfo.Add("CertificateRequest", new Dictionary<string, string>());
                _sessionInfo["CertificateRequest"].Add("CertificateTypes", string.Join(", ", req.CertificateTypes.Select(c => c.ToString())));
                _sessionInfo["CertificateRequest"].Add("SignatureAlgorithms", string.Join(", ", req.SignatureAlgorithms.Select(c => c.ToString())));
            }
            else if (fragBody is Fragments.CertificateVerify cerv)
            {
                if(this is Session13)
                {
                    var key = "Server CertificateVerify";
                    if (_sessionInfo.ContainsKey(key))
                        key = "Client CertificateVerify";
                    _sessionInfo.Add(key, new Dictionary<string, string>());
                    _sessionInfo[key].Add("SignatureAlgorithm", cerv.SignatureAlgorithm.ToString());
                    _sessionInfo[key].Add("Signature", Utils.BytesString(cerv.Signature));
                }
                else
                {
                    var key = "Client CertificateVerify";
                    _sessionInfo.Add(key, new Dictionary<string, string>());
                    _sessionInfo[key].Add("SignatureAlgorithm", cerv.SignatureAlgorithm.ToString());
                    _sessionInfo[key].Add("Signature", Utils.BytesString(cerv.Signature));
                }
            }
#endif
        }
        #endregion

        #region public
        public Result Process_Record(Records.TLSRecord rec)
        {
            if (rec is Records.Handshake hs)
            {
                return Record_Handshake(hs);
            }
            else if (rec is Records.ChangeCipherSpec ccs)
            {
                return Record_ChangeCipherSpec(ccs);
            }
            else if (rec is Records.ApplicationData ad)
            {
                return Record_ApplicationData(ad);
            }
            else if (rec is Records.Alert alt)
            {
                var msg = $">>>>>>>> Unhandled TLS Alert {alt.Description}";
                return new AlertResult(AlertDescription.unexpected_message, msg, alt.Level == AlertLevel.Fatal);
            }
            else
            {
                return Result.FatalAlert(AlertDescription.internal_error, $"Unhandled TLS RecordType {rec.Type}");
            }
        }

        public virtual Result GetEncryptedPacket(byte[] rawDataToSend)
        {
            if (State == TLSSessionState.Server_Finished)
            {
                var macSeed = Utils.GetMacSeed(_sendSeqNum, (byte)RecordType.ApplicationData, rawDataToSend);
                var myMac = _params.Cipher.ServerMessageAuthCode(macSeed);

                var serverIv = Utils.Random(16);
                var message = new List<byte>();
                message.AddRange(rawDataToSend);
                message.AddRange(myMac);
                var encryptedMsg = _params.Cipher.BulkEncrypt(message.ToArray(), serverIv);

                var appdata = new Records.ApplicationData(serverIv.Concat(encryptedMsg).ToArray());
                _sendSeqNum++;
                return new PacketResult(new[] { appdata });
            }
            else
            {
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on Server_ApplicationData create");
            }
        }
        #endregion

        #region record level
        protected virtual Result Record_Handshake(Records.Handshake rec)
        {
            var pkts = new List<Records.TLSRecord>();

            foreach (var frag in rec.Fragments)
            {
                if (frag is Handshakes.Fragment hf)
                {
                    AppendHandshakeMessages(hf);

                    var res = Fragment_Handshake(hf);
                    if (res != null)
                    {
                        if (res is AlertResult ar)
                            return ar;
                        else if (res is PacketResult hr && hr.Response != null)
                            pkts.AddRange(hr.Response);
                    }
                }
                else if (frag is Handshakes.EncryptedFragment ehf)
                {
                    var res = Fragment_EncryptedHandshake(ehf);
                    if (res != null)
                    {
                        if (res is AlertResult ar)
                            return ar;
                        else if (res is PacketResult hr && hr.Response != null)
                            pkts.AddRange(hr.Response);
                    }
                }
                else
                {
                    return Result.FatalAlert(AlertDescription.unexpected_message, $"Unhandled TLS Handshake.Fragment.Type {frag.GetType().Name}");
                }
            }
            if (pkts.Count > 0)
                return new PacketResult(pkts.ToArray());
            else
                return null;
        }

        protected virtual Result Record_ChangeCipherSpec(Records.ChangeCipherSpec rec)
        {
            if (State == TLSSessionState.Client_Key_Exchange)
            {
                State = TLSSessionState.Client_ChangeCipherSpec;
                return null;
            }
            else
            {
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on Client_ChangeCipherSpec message");
            }
        }

        protected virtual Result Record_ApplicationData(Records.ApplicationData rec)
        {
            if (State == TLSSessionState.Server_Finished)
            {
                var decrypt = _params.Cipher.BulkDecrypt(rec.EncryptedData, rec.IV);

                var body = decrypt.Take(decrypt.Length - _params.Cipher.GetMacLength()).ToArray();
                var mac = decrypt.Skip(body.Length).Take(_params.Cipher.GetMacLength()).ToArray();

                var macseed = Utils.GetMacSeed(_receiveSeqNum, (byte)RecordType.ApplicationData, body);
                var mymac = _params.Cipher.ClientMessageAuthCode(macseed);

                if (!Utils.BytesEqual(mac, mymac))
                {
                    return Result.FatalAlert(AlertDescription.bad_record_mac, $"unmatched MAC in Client_ApplicationData message");
                }
                _receiveSeqNum++;

                return new ApplicationResult(body);
            }
            else
            {
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on Client_ApplicationData message");
            }
        }
        #endregion

        #region fragment level

        protected Result Fragment_Handshake(Handshakes.Fragment frag)
        {
            LogSessionInfo(frag.Body);

            if (frag.Body is Fragments.ClientHello ch)
                return Fragment_ClientHello(ch);
            else if (frag.Body is Fragments.Certificate cert)
                return Fragment_ClientCertificate(cert);
            else if (frag.Body is Fragments.CertificateVerify certver)
                return Fragment_ClientCertificateVerify(certver);
            else if (frag.Body is Fragments.ClientKeyExchange cke)
                return Fragment_ClientKeyExchange(cke);
            else if (frag.Body is Fragments.Finished cf)
                return Fragment_ClientFinished(cf);
            else if (frag.Body is Fragments.KeyUpdate ku)
                return Fragment_ClientKeyUpdate(ku);
            else
                return Result.FatalAlert(AlertDescription.unexpected_message, $"Unhandled TLS HandshakeFragment.Body {frag.Body.GetType().Name}");
        }

        #region EncryptedHandshak

        Result Fragment_EncryptedHandshake(Handshakes.EncryptedFragment frag)
        {
            if (State == TLSSessionState.Client_ChangeCipherSpec)
            {
                var encrypt = frag.EncryptedData;
                var decryptedBytes = _params.Cipher.BulkDecrypt(encrypt, frag.IV);
                // 14 00 00 0C
                // 12-bytes-verify-data
                // 32-bytes-mac
                // 15-bytes-padding
                // 0x0F(padding-length)
                if (decryptedBytes[0] == 0x14 && Utils.ToUInt24(decryptedBytes, 1) == 12)
                {
                    var fragWithoutMac = new Handshakes.Fragment(decryptedBytes.Take(4 + _params.Cipher.VerifyDataLength).ToArray());
                    AppendHandshakeMessages(fragWithoutMac);

                    var fragWithMac = new Handshakes.Fragment(decryptedBytes);
                    return Fragment_Handshake(fragWithMac);
                }
                else
                {
                    return Result.FatalAlert(AlertDescription.decrypt_error, $"invalid Client_EncryptedHandshake message");
                }
            }
            else
            {
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on Client_EncryptedHandshake message");
            }
        }

        #endregion

        #region ClientHello
        protected virtual Result Fragment_ClientHello_ServerNameCheck(Fragments.ClientHello frag)
        {
            if (_params.ServerNameCheck)
            {
                if (frag.ServerName != null && frag.ServerName.Length > 0)
                {
                    var subject = new X509Certificate2(_pubkeyfile, "").Subject.Split(new[] { ',' });
                    var cn = subject.First(a => a.StartsWith("CN=")).Trim().Split(new[] { '=' })[1];
                    if (!frag.ServerName.Contains(cn))
                        return Result.FatalAlert(AlertDescription.unrecognized_name, "ServerName Extension in [ClientHello] NOT MATCH");
                }
                else
                    return Result.FatalAlert(AlertDescription.missing_extension, "ServerName Extension Missing in [ClientHello]");
            }
            return null;
        }

        protected virtual Result Fragment_ClientHello(Fragments.ClientHello frag)
        {
            var sncheck = Fragment_ClientHello_ServerNameCheck(frag);
            if (sncheck != null)
                return sncheck;

            State = TLSSessionState.Client_Hello;

            _params.ClientRandom = frag.Random;
            _params.ServerRandom = new TLS.ValueTypes.Random();            
            if (_params.EnableTls13)    // only when this is TLS13 server and client is TLS12, orelse ERR_TLS13_DOWNGRADE_DETECTED will be thrown from client
                _params.ServerRandom.UpdateLastBytesForTLS12Session();
            _params.Session = new TLS.ValueTypes.Session(Guid.NewGuid());

            var result = _params.Cipher.IsRsaKeyExchange
                            ? Fragment_ClientHello_RSA(frag)
                            : Fragment_ClientHello_ECDH(frag);

            State = TLSSessionState.Server_Hello_Done;

            return result;
        }

        Result Fragment_ClientHello_RSA(Fragments.ClientHello frag)
        {
            var serverhelloBody = new Fragments.ServerHello(ProtocolVersion.TLSv1_2, _params.ServerRandom, _params.Session, _params.Cipher.CipherSuite);
            var certificateBody = new Fragments.Certificate(new[] { new X509Certificate2(_pubkeyfile) }, false);
            var serverhellodoneBody = new Fragments.ServerHelloDone();

            var responseFragments = _params.ClientCertificateRequire
                ? new[]
                {
                    new Handshakes.Fragment(HandshakeType.Server_Hello, serverhelloBody),
                    new Handshakes.Fragment(HandshakeType.Certificate, certificateBody),
                    new Handshakes.Fragment(HandshakeType.Certificate_Request, new Fragments.CertificateRequest()),
                    new Handshakes.Fragment(HandshakeType.Server_Hello_Done, serverhellodoneBody)
                }
                : new[]
                {
                    new Handshakes.Fragment(HandshakeType.Server_Hello, serverhelloBody),
                    new Handshakes.Fragment(HandshakeType.Certificate, certificateBody),                    
                    new Handshakes.Fragment(HandshakeType.Server_Hello_Done, serverhellodoneBody)
                };
            foreach (var f in responseFragments)
            {
                AppendHandshakeMessages(f);

                LogSessionInfo(f.Body);
            }

            return new PacketResult(new[] { new Records.Handshake(responseFragments) });
        }

        Result Fragment_ClientHello_ECDH(Fragments.ClientHello frag)
        {
            var ecdhpub = GeneratePubKey();
            var signdata = _params.ClientRandom.Data.Concat(_params.ServerRandom.Data).Concat(Fragments.ServerKeyExchange.ServerECDHParams(_params.KeyExchangeCurve, ecdhpub)).ToArray();
            var signature = MakeSignatureWithCertificate(signdata);

            var serverhelloBody = new Fragments.ServerHello(ProtocolVersion.TLSv1_2, _params.ServerRandom, _params.Session, _params.Cipher.CipherSuite);
            var certificateBody = new Fragments.Certificate(new[] { new X509Certificate2(_pubkeyfile) }, false);
            var serverkeyexBody = new Fragments.ServerKeyExchange(_params.KeyExchangeCurve, ecdhpub, _params.SignatureAlgorithm, signature);
            var serverhellodoneBody = new Fragments.ServerHelloDone();

            var responseFragments = _params.ClientCertificateRequire
                ? new[]
                {
                    new Handshakes.Fragment(HandshakeType.Server_Hello, serverhelloBody),
                    new Handshakes.Fragment(HandshakeType.Certificate, certificateBody),
                    new Handshakes.Fragment(HandshakeType.Server_Key_Exchange, serverkeyexBody),
                    new Handshakes.Fragment(HandshakeType.Certificate_Request, new Fragments.CertificateRequest()),
                    new Handshakes.Fragment(HandshakeType.Server_Hello_Done, serverhellodoneBody)
                }
                : new[]
                {
                    new Handshakes.Fragment(HandshakeType.Server_Hello, serverhelloBody),
                    new Handshakes.Fragment(HandshakeType.Certificate, certificateBody),
                    new Handshakes.Fragment(HandshakeType.Server_Key_Exchange, serverkeyexBody),
                    new Handshakes.Fragment(HandshakeType.Server_Hello_Done, serverhellodoneBody)
                };
            foreach (var f in responseFragments)
            {
                AppendHandshakeMessages(f);

                LogSessionInfo(f.Body);
            }

            return new PacketResult(new[] { new Records.Handshake(responseFragments) });
        }

        protected byte[] GeneratePubKey()
        {
            if (_params.KeyExchangeCurve == NamedGroup.x25519)
            {
                var sKeygenPara = new Org.BouncyCastle.Crypto.Parameters.X25519KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom());
                var skeygen = new Org.BouncyCastle.Crypto.Generators.X25519KeyPairGenerator();
                skeygen.Init(sKeygenPara);
                _params.ServerKey = skeygen.GenerateKeyPair();
                var serverPub = (Org.BouncyCastle.Crypto.Parameters.X25519PublicKeyParameters)_params.ServerKey.Public;
                return serverPub.GetEncoded();
            }
            else if (_params.KeyExchangeCurve == NamedGroup.x448)
            {
                var sKeygenPara = new Org.BouncyCastle.Crypto.Parameters.X448KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom());
                var skeygen = new Org.BouncyCastle.Crypto.Generators.X448KeyPairGenerator();
                skeygen.Init(sKeygenPara);
                _params.ServerKey = skeygen.GenerateKeyPair();
                var serverPub = (Org.BouncyCastle.Crypto.Parameters.X448PublicKeyParameters)_params.ServerKey.Public;
                return serverPub.GetEncoded();
            }
            else
            {
                var ecDomainParam = Org.BouncyCastle.Crypto.Tls.TlsEccUtilities.GetParametersForNamedCurve((int)_params.KeyExchangeCurve);
                _params.ServerKey = Org.BouncyCastle.Crypto.Tls.TlsEccUtilities.GenerateECKeyPair(new Org.BouncyCastle.Security.SecureRandom(), ecDomainParam);
                var serverPub = (Org.BouncyCastle.Crypto.Parameters.ECPublicKeyParameters)_params.ServerKey.Public;
                return serverPub.Q.XCoord.GetEncoded().Concat(serverPub.Q.YCoord.GetEncoded()).ToArray();
            }
        }

        protected byte[] MakeSignatureWithCertificate(byte[] signdata)
        {
            AsymmetricAlgorithm asymmetric = null;
            using (var cert = new X509Certificate2(_prvkeyfile))
            {
                if (cert.PublicKey.Oid.FriendlyName == "RSA")
                    asymmetric = cert.GetRSAPrivateKey();
                else if (cert.PublicKey.Oid.FriendlyName == "ECC")
                    asymmetric = cert.GetECDsaPrivateKey();
                else
                    throw new NotSupportedException($"Server Certificate Type {cert.PublicKey.Oid.FriendlyName} NotSupported");
            }
            var signature = _params.Cipher.Signature(signdata, _params.SignatureAlgorithm, asymmetric);
            asymmetric.Dispose();
            return signature;
        }

        protected bool VerifyWithClientCertificate(byte[] data, byte[] signature, SignatureAlgorithm signatureAlgorithm)
        {
            AsymmetricAlgorithm asymmetric = null;
            var cert = _clientCertificates.First();
            if (cert.PublicKey.Oid.FriendlyName == "RSA")
                asymmetric = cert.GetRSAPublicKey();
            else if (cert.PublicKey.Oid.FriendlyName == "ECC")
                asymmetric = cert.GetECDsaPublicKey();
            else
                throw new NotSupportedException($"Client Certificate Type {cert.PublicKey.Oid.FriendlyName} NotSupported");
            var verified = _params.Cipher.SignatureVerify(data, signature, signatureAlgorithm, asymmetric);
            asymmetric.Dispose();
            return verified;
        }
        #endregion

        #region ClientKeyExchange
        protected virtual Result Fragment_ClientKeyExchange(Fragments.ClientKeyExchange frag)
        {
            if (_params.Cipher.IsRsaKeyExchange)
            {
                var prvParams = ((RSACryptoServiceProvider)(new X509Certificate2(_prvkeyfile, "", X509KeyStorageFlags.Exportable)).PrivateKey).ExportParameters(true);
                _params.Cipher.KeyExchange(frag.RSA_PreMasterSecret, _params.ClientRandom.Data, _params.ServerRandom.Data, prvParams);
            }
            else
            {
                _params.Cipher.KeyExchange(frag.ECDH_Pubkey, _params.ClientRandom.Data, _params.ServerRandom.Data, _params.ServerKey.Private);
                _params.ServerKey = null;
            }

            State = TLSSessionState.Client_Key_Exchange;

            return null;
        }

        #endregion

        #region Certificate
        protected virtual Result Fragment_ClientCertificate(Fragments.Certificate frag)
        {
            if(State == TLSSessionState.Server_Hello_Done)
            {
                if (ClientCertificatesCallback != null)
                {
                    var valid = ClientCertificatesCallback(frag.Certs);
                    if (!valid)
                        return Result.FatalAlert(AlertDescription.bad_certificate, "Client Certificate invalid");
                    _clientCertificates = frag.Certs.ToArray();
                }
                else
                {
                    throw new ArgumentNullException("NegotiationParams Configed ClientCertificateRequire = true, but ClientCertificateVerifyCallback is null");
                }
                return null;
            }
            else
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on Client_Certificate message");
        }

        protected virtual Result Fragment_ClientCertificateVerify(Fragments.CertificateVerify frag)
        {
            if (State == TLSSessionState.Client_Key_Exchange)
            {
                if (_clientCertificates != null && _clientCertificates.Length > 0)
                {
                    var clientHello_clientKeyEx = GetHandshakeMessages(true); // without CertificateVerify message itself
                    var valid = VerifyWithClientCertificate(clientHello_clientKeyEx, frag.Signature, frag.SignatureAlgorithm);
                    if (!valid)
                        return Result.FatalAlert(AlertDescription.bad_certificate, "Client Certificate Signature Verify failure");
                    return null;
                }
                else
                    return Result.FatalAlert(AlertDescription.unexpected_message, "Client Certificate Empty but sent CertificateVerify message");
            }
            else
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on Client_CertificateVerify message");
        }
        #endregion

        #region ClientFinished
        protected virtual Result Fragment_ClientFinished(Fragments.Finished frag)
        {
            if (State == TLSSessionState.Client_ChangeCipherSpec)
            {
                var clienthello_changecipher = GetHandshakeMessages(true); // without this finished itself
                var myVerify = _params.Cipher.GetVerifyData("client finished", clienthello_changecipher);
                if (!Utils.BytesEqual(myVerify, frag.VerifyData))
                {
                    return Result.FatalAlert(AlertDescription.bad_record_mac, $"unmatched VerifyData in Client_EncryptedHandshake message");
                }

                var clientfinishedMessage = new byte[] { 0x14, 0x00, 0x00, 0x0C }.Concat(frag.VerifyData).ToArray();
                var macseed = Utils.GetMacSeed(_receiveSeqNum, (byte)RecordType.Handshake, clientfinishedMessage);
                var myMac = _params.Cipher.ClientMessageAuthCode(macseed);
                if (!Utils.BytesEqual(myMac, frag.Mac))
                {
                    return Result.FatalAlert(AlertDescription.bad_record_mac, $"unmatched MAC in Client_EncryptedHandshake message");
                }
                // received Encrypted messages seq num
                _receiveSeqNum++;
                State = TLSSessionState.Client_Finished;
                // changeCipherSpec, server encryptedhandshake
                return ChangeCipherSpecAndFinished();
            }
            else
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on Client_ApplicationData message");
        }

        Result ChangeCipherSpecAndFinished()
        {
            if (State == TLSSessionState.Client_Finished)
            {
                var clienthello_clientfinish = GetHandshakeMessages();
                var finishedVerify = _params.Cipher.GetVerifyData("server finished", clienthello_clientfinish);

                var finishedMessage = new byte[] { 0x14, 0x00, 0x00, 0x0C }.Concat(finishedVerify).ToArray();
                var macSeed = Utils.GetMacSeed(_sendSeqNum, (byte)RecordType.Handshake, finishedMessage);
                var myMac = _params.Cipher.ServerMessageAuthCode(macSeed);

                var finished = new List<byte>();
                finished.AddRange(finishedMessage);
                finished.AddRange(myMac);

                var serverIv = Utils.Random(16);
                var encryptedFinished = _params.Cipher.BulkEncrypt(finished.ToArray(), serverIv);

                var serverFinishFragment = new Handshakes.EncryptedFragment(serverIv.Concat(encryptedFinished).ToArray());

                var changeCipherRecord = new Records.ChangeCipherSpec();
                var serverFinishRecord = new Records.Handshake(new[] { serverFinishFragment });

                _sendSeqNum++;
                State = TLSSessionState.Server_Finished;

                return new PacketResult(new Records.TLSRecord[] { changeCipherRecord, serverFinishRecord });
            }
            else
            {
                return Result.FatalAlert(AlertDescription.unexpected_message, $"State [{State}] check failed on ChangeCipherSpecAndFinished");
            }
        }

        #endregion

        protected virtual Result Fragment_ClientKeyUpdate(Fragments.KeyUpdate frag)
        {
            throw new InvalidOperationException("Not In TLS 1.2");
        }

        #endregion
    }
}
