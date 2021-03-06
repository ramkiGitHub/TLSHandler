﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketBase.Protocol;

namespace Https
{
    public class HttpServer : AppServer<TcpSession, Request.TLSRequest>
    {
        public readonly IPAddress IP;
        public readonly int Port;
        public readonly string PublicKeyFile;
        public readonly string PrivateKeyFile;
        public readonly bool ForceClientCertificate = false;
        public readonly bool ForceServerNameCheck = true;
        public readonly bool EnableTLS13 = true;

        public HttpServer()
        {
            var ip = ConfigurationManager.AppSettings["ServerIP"];
            var port = ConfigurationManager.AppSettings["ServerPort"];
            var pubkey = ConfigurationManager.AppSettings["ServerCertFilepath"];
            var pvtkey = ConfigurationManager.AppSettings["ServerPfxFilepath"];
            var fcc = ConfigurationManager.AppSettings["ForceClientCertificate"];
            var fsni = ConfigurationManager.AppSettings["ForceServerNameCheck"];
            var tls13 = ConfigurationManager.AppSettings["EnableTLS13"];

            if (!Path.IsPathRooted(pubkey))
                pubkey = GetFilePath(pubkey);
            if (!Path.IsPathRooted(pvtkey))
                pvtkey = GetFilePath(pvtkey);

            if (!IPAddress.TryParse(ip, out IP))
                throw new ArgumentException($"AppSetting [ServerIP] ({ip}) invalid");
            if (!int.TryParse(port, out Port))
                throw new ArgumentException($"AppSetting [ServerPort] ({port}) invalid");
            if (!File.Exists(pubkey))
                throw new ArgumentException($"AppSetting [ServerCertFilepath] ({pubkey}) does not exist");
            if (!File.Exists(pvtkey))
                throw new ArgumentException($"AppSetting [ServerPfxFilepath] ({pvtkey}) does not exist");

            if (bool.TryParse(fcc, out bool cc))
                this.ForceClientCertificate = cc;
            if (bool.TryParse(fsni, out bool sni))
                this.ForceServerNameCheck = sni;
            if (bool.TryParse(tls13, out bool use13))
                this.EnableTLS13 = use13;

            this.ReceiveFilterFactory = new DefaultReceiveFilterFactory<Filter.TLSPacketFilter, Request.TLSRequest>();
            this.PublicKeyFile = pubkey;
            this.PrivateKeyFile = pvtkey;
        }

        public bool Setup()
        {
            var cfg = new ServerConfig
            {
                Ip = IP.ToString(),
                Port = this.Port,
                MaxRequestLength = 100 * 1024,    // 100k
                ClearIdleSession = true,
                ClearIdleSessionInterval = 60,
                IdleSessionTimeOut = 30,
                DisableSessionSnapshot = true,
            };
            return base.Setup(cfg);
        }
    }
}
