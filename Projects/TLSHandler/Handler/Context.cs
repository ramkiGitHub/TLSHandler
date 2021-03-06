﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using TLSHandler.Enums;
using Ciphers = TLSHandler.Internal.Ciphers;
using Records = TLSHandler.Internal.TLS.Records;
using TLS = TLSHandler.Internal.TLS;

namespace TLSHandler.Handler
{
    public class Context
    {
        public TLSSessionState State { get { return _session != null ? _session.State : TLSSessionState.None; } }
        public Func<X509Certificate2[], bool> ClientCertificatesCallback { get; set; }

        NegotiationParams _params = null;
        Session12 _session = null;
        readonly string _pubkeyfile = null;
        readonly string _prvkeyfile = null;
        readonly string _serverCertType = null;

        public Context(string pub_crt_filepath, string prv_pfx_filepath, bool force_ClientCert = false, bool force_ServerNameCheck = false, bool enable_tls13 = true)
        {
            _pubkeyfile = pub_crt_filepath;
            _prvkeyfile = prv_pfx_filepath;
            _serverCertType = new X509Certificate2(_pubkeyfile).PublicKey.Oid.FriendlyName;
            _params = new NegotiationParams(force_ClientCert, force_ServerNameCheck, enable_tls13);
        }

        public Result Initialize(TLS.Records.Handshake clientHello)
        {
            if (_session != null)
                throw new InvalidOperationException("Initialize should be called only once");

            if (clientHello.Fragments.Length > 0)
            {
                if (clientHello.Fragments[0] is TLS.Handshakes.Fragment hf && hf.Body is TLS.Fragments.ClientHello ch)
                {
                    var err = Parameters_Negotiation(ch, out bool tls13Session);
                    if (err != null)
                        return err;

                    _session = tls13Session ? new Session13(_params, _pubkeyfile, _prvkeyfile) : new Session12(_params, _pubkeyfile, _prvkeyfile);
                    _session.ClientCertificatesCallback = this.ClientCertificatesCallback;
                    return _session.Process_Record(clientHello);
                }
                else
                    return Result.FatalAlert(AlertDescription.unexpected_message, $"unexpected fragment in this record, ClientHello expected, got {clientHello.Fragments[0].GetType().Name}");
            }
            else
                return Result.FatalAlert(AlertDescription.unexpected_message, $"invalid tls record, empty fragment");
        }

        public Result Process_Record(Records.TLSRecord record)
        {
            return _session.Process_Record(record);
        }

        public Result GetEncryptedPacket(byte[] rawDataToSend)
        {
            return _session.GetEncryptedPacket(rawDataToSend);
        }

        public Dictionary<string, Dictionary<string, string>> GetSessionInfo()
        {
            return _session.GetSessionInfo();
        }

        #region parameters negotiation
        Result Parameters_Negotiation(TLS.Fragments.ClientHello ch, out bool is_tls13Session)
        {
            if (_params.EnableTls13 && ClientSupportTls13(ch))
            {
                _params.Cipher = Select_CipherSuite(ch, true);
                _params.KeyExchangeCurve = Select_EllipticCurve(ch.SupportedGroups).Value;
                _params.SignatureAlgorithm = Select_SignatureAlgorithm(ch.SignatureAlgorithms).Value;                
                _params.KeyShare = Select_KeyShareEntry(ch.KeyShare);
                is_tls13Session = true;
                return null;
            }
            else
            {
                _params.Cipher = Select_CipherSuite(ch, false);
                var kec = Select_EllipticCurve(ch.SupportedGroups);
                var sa = Select_SignatureAlgorithm(ch.SignatureAlgorithms);
                if (kec.HasValue)
                    _params.KeyExchangeCurve = kec.Value;
                if (sa.HasValue)
                    _params.SignatureAlgorithm = sa.Value;
                is_tls13Session = false;
                if (_params.Cipher != null)
                    return null;
                else
                    return Result.FatalAlert(AlertDescription.handshake_failure, "CipherSuite not supported");
            }
        }

        bool ClientSupportTls13(TLS.Fragments.ClientHello ch)
        {
            var sv = ch.SupportedVersions;
            var ks = ch.KeyShare;
            if (sv != null && sv.Contains(ProtocolVersion.TLSv1_3) && ks != null)
            {
                var keyshareEntry = Select_KeyShareEntry(ks);
                if (keyshareEntry != null)
                {
                    var cipherSupport = Select_CipherSuite(ch, true);
                    return cipherSupport != null;
                }
            }
            return false;
        }

        Ciphers.CipherSuiteBase Select_CipherSuite(TLS.Fragments.ClientHello ch, bool tls13)
        {
            var client_ciphers = ch.CipherSuites.Ciphers;
            if (client_ciphers == null)
                return null;
            var ec = Select_EllipticCurve(ch.SupportedGroups);          // check EllipticCurves Extension            
            var sa = Select_SignatureAlgorithm(ch.SignatureAlgorithms); // check SignatureAlgorithm Extension

            if (tls13)
            {
                if (!ec.HasValue || !sa.HasValue)
                    return null;
                if (client_ciphers.Contains(CipherSuite.TLS_AES_256_GCM_SHA384))
                    return new Ciphers.TLS_AES_256_GCM_SHA384();
                else if (client_ciphers.Contains(CipherSuite.TLS_CHACHA20_POLY1305_SHA256))    // mobile client use
                    return new Ciphers.TLS_CHACHA20_POLY1305_SHA256();
                else if (client_ciphers.Contains(CipherSuite.TLS_AES_128_GCM_SHA256))          // TLS1.3 Mandatory Cipher Suite
                    return new Ciphers.TLS_AES_128_GCM_SHA256();                
                return null;
            }
            else
            {
                if (_serverCertType == "RSA")
                {
                    if (client_ciphers.Contains(CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256) && ec.HasValue && sa.HasValue) // prefer ecdhe
                        return new Ciphers.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256();
                    else if (client_ciphers.Contains(CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA) && ec.HasValue && sa.HasValue) // prefer ecdhe
                        return new Ciphers.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA();
                    else if (client_ciphers.Contains(CipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA256))  // ECDHE_RSA KeyExchange fail, fallback to RSA KeyExchange
                        return new Ciphers.TLS_RSA_WITH_AES_128_CBC_SHA256();
                    else if (client_ciphers.Contains(CipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA))     // fallback to TLS1.2 Mandatory Cipher Suite
                        return new Ciphers.TLS_RSA_WITH_AES_128_CBC_SHA();
                }
                //else if (_serverCertType == "ECC")
                //{
                //    if (client_ciphers.Contains(CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256) && ec.HasValue && sa.HasValue)
                //        return new Ciphers.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256();
                //    else if (client_ciphers.Contains(CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA) && ec.HasValue && sa.HasValue)
                //        return new Ciphers.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA();
                //}
                return null;
            }
        }

        TLS.Extensions.KeyShareEntry Select_KeyShareEntry(TLS.Extensions.KeyShareEntry[] client_shares)
        {
            if (client_shares != null)
            {
                var entry = client_shares.FirstOrDefault(a => a.Group == NamedGroup.x25519);
                if (entry != null)
                    return entry;
                entry = client_shares.FirstOrDefault(a => a.Group == NamedGroup.x448);
                if (entry != null)
                    return entry;
                entry = client_shares.FirstOrDefault(a => a.Group == NamedGroup.secp521r1);
                if (entry != null)
                    return entry;
                entry = client_shares.FirstOrDefault(a => a.Group == NamedGroup.secp384r1);
                if (entry != null)
                    return entry;
                entry = client_shares.FirstOrDefault(a => a.Group == NamedGroup.secp256r1);
                if (entry != null)
                    return entry;
            }
            return null;
        }

        NamedGroup? Select_EllipticCurve(NamedGroup[] client_groups)
        {
            if (client_groups != null)
            {
                if (client_groups.Contains(NamedGroup.x25519))
                    return NamedGroup.x25519;
                else if (client_groups.Contains(NamedGroup.x448))
                    return NamedGroup.x448;
                else if (client_groups.Contains(NamedGroup.secp521r1))
                    return NamedGroup.secp521r1;
                else if (client_groups.Contains(NamedGroup.secp384r1))
                    return NamedGroup.secp384r1;
                else if (client_groups.Contains(NamedGroup.secp256r1))
                    return NamedGroup.secp256r1;
            }
            return null;
        }

        SignatureAlgorithm? Select_SignatureAlgorithm(SignatureAlgorithm[] client_algorithms)
        {
            if (client_algorithms != null)
            {
                if (_serverCertType == "RSA")
                {
                    if (client_algorithms.Contains(SignatureAlgorithm.rsa_pss_rsae_sha256))
                        return SignatureAlgorithm.rsa_pss_rsae_sha256;
                    else if (client_algorithms.Contains(SignatureAlgorithm.rsa_pss_rsae_sha384))
                        return SignatureAlgorithm.rsa_pss_rsae_sha384;
                    else if (client_algorithms.Contains(SignatureAlgorithm.rsa_pss_rsae_sha512))
                        return SignatureAlgorithm.rsa_pss_rsae_sha512;
                    else if (client_algorithms.Contains(SignatureAlgorithm.rsa_pkcs1_sha512))
                        return SignatureAlgorithm.rsa_pkcs1_sha512;
                    else if (client_algorithms.Contains(SignatureAlgorithm.rsa_pkcs1_sha384))
                        return SignatureAlgorithm.rsa_pkcs1_sha384;
                    else if (client_algorithms.Contains(SignatureAlgorithm.rsa_pkcs1_sha256))
                        return SignatureAlgorithm.rsa_pkcs1_sha256;
                }
                //else if (_serverCertType == "ECC")
                //{
                //    if (client_algorithms.Contains(SignatureAlgorithm.ecdsa_secp256r1_sha256))
                //        return SignatureAlgorithm.ecdsa_secp256r1_sha256;
                //    else if (client_algorithms.Contains(SignatureAlgorithm.ecdsa_secp384r1_sha384))
                //        return SignatureAlgorithm.ecdsa_secp384r1_sha384;
                //    else if (client_algorithms.Contains(SignatureAlgorithm.ecdsa_secp521r1_sha512))
                //        return SignatureAlgorithm.ecdsa_secp521r1_sha512;
                //}
            }
            return null;
        }

        #endregion

    }
}
