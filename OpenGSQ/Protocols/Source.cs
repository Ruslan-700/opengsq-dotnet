﻿using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Checksum;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace OpenGSQ.Protocols
{
    public class Source : ProtocolBase
    {
        /// <summary>
        /// Source Engine Query Protocol<br />
        /// See: <see href="https://developer.valvesoftware.com/wiki/Server_queries">https://developer.valvesoftware.com/wiki/Server_queries</see>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="timeout"></param>
        public Source(string address, int port = 27015, int timeout = 5000) : base(address, port, timeout)
        {

        }

        /// <summary>
        /// Retrieves information about the server including, but not limited to: its name, the map currently being played, and the number of players.<br />
        /// See: <see href="https://developer.valvesoftware.com/wiki/Server_queries#A2S_INFO">https://developer.valvesoftware.com/wiki/Server_queries#A2S_INFO</see>
        /// </summary>
        /// <returns></returns>
        /// <exception cref="SocketException"></exception>
        public (object, Type) GetInfo()
        {
            using (var udpClient = new UdpClient())
            {
                var responseData = ConnectAndSendChallenge(udpClient, QueryRequest.A2S_INFO);

                using (var br = new BinaryReader(new MemoryStream(responseData), Encoding.UTF8))
                {
                    var header = br.ReadByte();

                    if (header != (byte)QueryResponse.S2A_INFO_SRC && header != (byte)QueryResponse.S2A_INFO_DETAILED)
                    {
                        throw new Exception($"Packet header mismatch. Received: {header}. Expected: {QueryResponse.S2A_INFO_SRC} or {QueryResponse.S2A_INFO_DETAILED}.");
                    }

                    if (header == (byte)QueryResponse.S2A_INFO_SRC)
                    {
                        var source = new Info.Source
                        {
                            Protocol = br.ReadByte(),
                            Name = br.ReadStringEx(),
                            Map = br.ReadStringEx(),
                            Folder = br.ReadStringEx(),
                            Game = br.ReadStringEx(),
                            ID = br.ReadInt16(),
                            Players = br.ReadByte(),
                            MaxPlayers = br.ReadByte(),
                            Bots = br.ReadByte(),
                            ServerType = (Info.ServerType)br.ReadByte(),
                            Environment = GetEnvironment(br.ReadByte()),
                            Visibility = (Info.Visibility)br.ReadByte(),
                            VAC = (Info.VAC)br.ReadByte()
                        };

                        if (source.ID == 2400)
                        {
                            source.Mode = br.ReadByte();
                            source.Witnesses = br.ReadByte();
                            source.Duration = br.ReadByte();
                        }

                        source.Version = br.ReadStringEx();

                        if (br.BaseStream.Position < br.BaseStream.Length)
                        {
                            source.EDF = (Info.ExtraDataFlag)br.ReadByte();

                            var edf = (Info.ExtraDataFlag)source.EDF;

                            if (edf.HasFlag(Info.ExtraDataFlag.Port))
                            {
                                source.Port = br.ReadInt16();
                            }

                            if (edf.HasFlag(Info.ExtraDataFlag.SteamID))
                            {
                                source.SteamID = br.ReadUInt64();
                            }

                            if (edf.HasFlag(Info.ExtraDataFlag.Spectator))
                            {
                                source.SpectatorPort = br.ReadInt16();
                                source.SpectatorName = br.ReadStringEx();
                            }

                            if (edf.HasFlag(Info.ExtraDataFlag.Keywords))
                            {
                                source.Keywords = br.ReadStringEx();
                            }

                            if (edf.HasFlag(Info.ExtraDataFlag.SteamID))
                            {
                                source.GameID = br.ReadUInt64();
                            }
                        }

                        return (source, source.GetType());
                    }
                    else
                    {
                        var goldSource = new Info.GoldSource
                        {
                            Address = br.ReadStringEx(),
                            Name = br.ReadStringEx(),
                            Map = br.ReadStringEx(),
                            Folder = br.ReadStringEx(),
                            Game = br.ReadStringEx(),
                            Players = br.ReadByte(),
                            MaxPlayers = br.ReadByte(),
                            Protocol = br.ReadByte(),
                            ServerType = (Info.ServerType)char.ToLower(Convert.ToChar(br.ReadByte())),
                            Environment = (Info.Environment)char.ToLower(Convert.ToChar(br.ReadByte())),
                            Visibility = (Info.Visibility)br.ReadByte(),
                            Mod = br.ReadByte()
                        };

                        if (goldSource.Mod == 1)
                        {
                            goldSource.Link = br.ReadStringEx();
                            goldSource.DownloadLink = br.ReadStringEx();
                            br.ReadByte();
                            goldSource.Version = br.ReadInt64();
                            goldSource.Size = br.ReadInt64();
                            goldSource.Type = br.ReadByte();
                            goldSource.DLL = br.ReadByte();
                        }

                        goldSource.VAC = (Info.VAC)br.ReadByte();
                        goldSource.Bots = br.ReadByte();

                        return (goldSource, goldSource.GetType());
                    }
                }
            }
        }

        /// <summary>
        /// This query retrieves information about the players currently on the server.<br />
        /// See: <see href="https://developer.valvesoftware.com/wiki/Server_queries#A2S_PLAYER">https://developer.valvesoftware.com/wiki/Server_queries#A2S_PLAYER</see>
        /// </summary>
        /// <returns></returns>
        /// <exception cref="SocketException"></exception>
        public List<Player> GetPlayers()
        {
            using (var udpClient = new UdpClient())
            {
                var responseData = ConnectAndSendChallenge(udpClient, QueryRequest.A2S_PLAYER);

                using (var br = new BinaryReader(new MemoryStream(responseData), Encoding.UTF8))
                {
                    var header = br.ReadByte();

                    if (header != (byte)QueryResponse.S2A_PLAYER)
                    {
                        throw new Exception($"Packet header mismatch. Received: {header}. Expected: {QueryResponse.S2A_PLAYER}.");
                    }

                    var playerCount = br.ReadByte();

                    var players = new List<Player>();

                    // Save the players
                    for (int i = 0; i < playerCount; i++)
                    {
                        br.ReadByte();

                        players.Add(new Player
                        {
                            Name = br.ReadStringEx(),
                            Score = br.ReadInt32(),
                            Duration = br.ReadSingle(),
                        });
                    }

                    if (br.BaseStream.Position < br.BaseStream.Length)
                    {
                        for (int i = 0; i < playerCount; i++)
                        {
                            players[i].Deaths = br.ReadInt32();
                            players[i].Money = br.ReadInt32();
                        }
                    }

                    return players;
                }
            }
        }

        /// <summary>
        /// Returns the server rules, or configuration variables in name/value pairs.<br />
        /// See: <see href="https://developer.valvesoftware.com/wiki/Server_queries#A2S_RULES">https://developer.valvesoftware.com/wiki/Server_queries#A2S_RULES</see>
        /// </summary>
        /// <returns></returns>
        /// <exception cref="SocketException"></exception>
        public Dictionary<string, string> GetRules()
        {
            using (var udpClient = new UdpClient())
            {
                var responseData = ConnectAndSendChallenge(udpClient, QueryRequest.A2S_RULES);

                using (var br = new BinaryReader(new MemoryStream(responseData), Encoding.UTF8))
                {
                    var header = br.ReadByte();

                    if (header != (byte)QueryResponse.S2A_RULES)
                    {
                        throw new Exception($"Packet header mismatch. Received: {header}. Expected: {QueryResponse.S2A_RULES}.");
                    }

                    var ruleCount = br.ReadUInt16();

                    var rules = new Dictionary<string, string>();

                    // Save the rules into dictionary
                    for (int i = 0; i < ruleCount; i++)
                    {
                        rules.Add(br.ReadStringEx(), br.ReadStringEx());
                    }

                    return rules;
                }
            }
        }

        private byte[] ConnectAndSendChallenge(UdpClient udpClient, QueryRequest queryRequest)
        {
            // Connect to remote host
            udpClient.Connect(_EndPoint);
            udpClient.Client.SendTimeout = _Timeout;
            udpClient.Client.ReceiveTimeout = _Timeout;

            // Set up request base
            var requestBase = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, (byte)queryRequest };

            if (queryRequest == QueryRequest.A2S_INFO)
            {
                requestBase = requestBase.Concat(Encoding.Default.GetBytes("Source Engine Query\0")).ToArray();
            }

            // Set up request data
            var requestData = requestBase;

            if (_Challenge.Length > 0)
            {
                requestData = requestData.Concat(_Challenge).ToArray();
            }
            else if (queryRequest != QueryRequest.A2S_INFO)
            {
                requestData = requestData.Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }).ToArray();
            }

            // Send and receive
            udpClient.Send(requestData, requestData.Length);
            var responseData = Receive(udpClient);

            // The server may reply with a challenge
            if (responseData[0] == (byte)QueryResponse.S2C_CHALLENGE)
            {
                _Challenge = responseData.Skip(1).ToArray();

                // Send the challenge and receive
                requestData = requestBase.Concat(_Challenge).ToArray();
                udpClient.Send(requestData, requestData.Length);
                responseData = Receive(udpClient);
            }

            return responseData;
        }

        private byte[] Receive(UdpClient udpClient)
        {
            bool isCompressed;
            int totalPackets = -1, crc32Sum = 0;
            var payloads = new SortedDictionary<int, byte[]>();
            var packets = new List<byte[]>();

            do
            {
                var responseData = udpClient.Receive(ref _EndPoint);
                packets.Add(responseData);

                using (var br = new BinaryReader(new MemoryStream(responseData), Encoding.UTF8))
                {
                    var header = br.ReadInt32();

                    // Simple Response Format
                    if (header == -1)
                    {
                        // Return the payload
                        return responseData.Skip((int)br.BaseStream.Position).ToArray();
                    }

                    // Packet id
                    int id = br.ReadInt32();
                    isCompressed = id < 0;

                    // Check is GoldSource multi-packet response format
                    if (IsGoldSourceSplit(responseData, (int)br.BaseStream.Position))
                    {
                        // Return the payload
                        return ParseGoldSourcePackets(udpClient, packets);
                    }

                    // The total number of packets
                    totalPackets = br.ReadByte();

                    // The number of the packet
                    int number = br.ReadByte();

                    // Packet size
                    br.ReadUInt16();

                    if (number == 0 && isCompressed)
                    {
                        // Decompressed size
                        br.ReadInt32();

                        // CRC32 sum
                        crc32Sum = br.ReadInt32();
                    }

                    payloads.Add(number, responseData.Skip((int)br.BaseStream.Position).ToArray());
                }
            } while (totalPackets == -1 || payloads.Count < totalPackets);

            // Combine the payloads
            var combinedPayload = payloads.Values.Aggregate((a, b) => a.Concat(b).ToArray());

            // Decompress the payload
            if (isCompressed)
            {
                using (var compressedData = new MemoryStream(combinedPayload))
                using (var uncompressedData = new MemoryStream())
                {
                    BZip2.Decompress(compressedData, uncompressedData, true);
                    combinedPayload = uncompressedData.ToArray();
                }

                // Check CRC32 sum
                var crc32 = new Crc32();
                crc32.Update(combinedPayload);

                if (crc32.Value != crc32Sum)
                {
                    throw new Exception("CRC32 checksum mismatch of uncompressed packet data.");
                }
            }

            return combinedPayload.Skip(4).ToArray();
        }

        private bool IsGoldSourceSplit(byte[] responseData, int streamPosition)
        {
            var data = responseData.Skip(streamPosition).ToArray();

            // Upper 4 bits represent the number of the current packet (starting at 0)
            int number = data[0] >> 4;

            // Check is it Gold Source packet split format
            return number == 0 && data[1] == 0xFF && data[2] == 0xFF && data[3] == 0xFF && data[4] == 0xFF;
        }

        private byte[] ParseGoldSourcePackets(UdpClient udpClient, List<byte[]> packets)
        {
            int totalPackets = -1;
            var payloads = new SortedDictionary<int, byte[]>();

            while (totalPackets == -1 || payloads.Count < totalPackets)
            {
                // Load the old received packets first, then receive the packets from udpClient
                var responseData = payloads.Count < packets.Count ? packets[payloads.Count] : udpClient.Receive(ref _EndPoint);

                using (var br = new BinaryReader(new MemoryStream(responseData), Encoding.UTF8))
                {
                    // Header
                    br.ReadInt32();

                    // Packet id
                    br.ReadInt32();

                    // The total number of packets
                    totalPackets = br.ReadByte();

                    // Upper 4 bits represent the number of the current packet (starting at 0)
                    int number = totalPackets >> 4;

                    // Bottom 4 bits represent the total number of packets (2 to 15)
                    totalPackets &= 0x0F;

                    payloads.Add(number, responseData.Skip((int)br.BaseStream.Position).ToArray());
                }
            }

            // Combine the payloads
            var combinedPayload = payloads.Values.Aggregate((a, b) => a.Concat(b).ToArray());

            return combinedPayload.Skip(4).ToArray();
        }

        private Info.Environment GetEnvironment(byte environmentByte)
        {
            switch (environmentByte)
            {
                case (byte)Info.Environment.Linux: 
                    return Info.Environment.Linux;
                case (byte)Info.Environment.Windows: 
                    return Info.Environment.Windows;
                default:
                    return Info.Environment.Mac;
            }
        }

        private enum QueryRequest : byte
        {
            A2S_INFO = 0x54,
            A2S_PLAYER = 0x55,
            A2S_RULES = 0x56,
        }

        private enum QueryResponse : byte
        {
            S2C_CHALLENGE = 0x41,
            S2A_INFO_SRC = 0x49,
            S2A_INFO_DETAILED = 0x6D,
            S2A_PLAYER = 0x44,
            S2A_RULES = 0x45,
        }

        public static class Info
        {
            public enum ServerType : byte
            {
                Dedicated = 0x64,
                Listen = 0x6C,
                Proxy = 0x70,
            }

            public enum Environment : byte
            {
                Linux = 0x6C,
                Windows = 0x77,
                Mac = 0x6D,
            }

            public enum Visibility : byte
            {
                Public,
                Private,
            }

            public enum VAC : byte
            {
                Unsecured,
                Secured,
            }

            [Flags]
            public enum ExtraDataFlag : byte
            {
                Port = 0x80,
                SteamID = 0x10,
                Spectator = 0x40,
                Keywords = 0x20,
                GameID = 0x01,
            }

            public class Source
            {
                public byte Protocol { get; set; }
                public string Name { get; set; }
                public string Map { get; set; }
                public string Folder { get; set; }
                public string Game { get; set; }
                public short ID { get; set; }
                public byte Players { get; set; }
                public byte MaxPlayers { get; set; }
                public byte Bots { get; set; }
                public ServerType ServerType { get; set; }
                public Environment Environment { get; set; }
                public Visibility Visibility { get; set; }
                public VAC VAC { get; set; }
                public byte? Mode { get; set; }
                public byte? Witnesses { get; set; }
                public byte? Duration { get; set; }
                public string Version { get; set; }
                public ExtraDataFlag? EDF { get; set; }
                public short? Port { get; set; }
                public ulong? SteamID { get; set; }
                public short? SpectatorPort { get; set; }
                public string SpectatorName { get; set; }
                public string Keywords { get; set; }
                public ulong? GameID { get; set; }
            }

            public class GoldSource
            {
                public string Address { get; set; }
                public string Name { get; set; }
                public string Map { get; set; }
                public string Folder { get; set; }
                public string Game { get; set; }
                public byte Players { get; set; }
                public byte MaxPlayers { get; set; }
                public byte Protocol { get; set; }
                public ServerType ServerType { get; set; }
                public Environment Environment { get; set; }
                public Visibility Visibility { get; set; }
                public byte Mod { get; set; }
                public string Link { get; set; }
                public string DownloadLink { get; set; }
                public long? Version { get; set; }
                public long? Size { get; set; }
                public byte Type { get; set; }
                public byte DLL { get; set; }
                public VAC VAC { get; set; }
                public byte Bots { get; set; }
            }
        }

        public class Player
        {
            public string Name { get; set; }
            public long Score { get; set; }
            public float Duration { get; set; }
            public long? Deaths { get; set; }
            public long? Money { get; set; }
        }

        public class RemoteConsole : ProtocolBase, IDisposable
        {
            private TcpClient _tcpClient;

            /// <summary>
            /// Source RCON Protocol
            /// </summary>
            /// <param name="address"></param>
            /// <param name="port"></param>
            /// <param name="timeout"></param>
            public RemoteConsole(string address, int port = 27015, int timeout = 5000) : base(address, port, timeout)
            {

            }

            public void Dispose()
            {
                _tcpClient?.Close();
            }

            /// <summary>
            /// Authenticate the connection
            /// </summary>
            /// <param name="password"></param>
            /// <exception cref="AuthenticationException"></exception>
            public void Authenticate(string password)
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new ArgumentException("Password is required");
                }

                // Connect
                _tcpClient = new TcpClient();
                _tcpClient.Connect(_EndPoint);
                _tcpClient.Client.SendTimeout = _Timeout;
                _tcpClient.Client.ReceiveTimeout = _Timeout;

                // Send password
                int id = new Random().Next(4096);
                _tcpClient.Client.Send(new Packet(id, PacketType.SERVERDATA_AUTH, password).GetBytes());

                // Receive and parse as Packet
                var buffer = new byte[4096];
                _tcpClient.Client.Receive(buffer);
                var packet = new Packet(buffer);

                // Sometimes it will return a PacketType.SERVERDATA_RESPONSE_VALUE, so receive again
                if (packet.Type != PacketType.SERVERDATA_AUTH_RESPONSE)
                {
                    _tcpClient.Client.Receive(buffer);
                    packet = new Packet(buffer);
                }

                // Throw exception if not PacketType.SERVERDATA_AUTH_RESPONSE
                if (packet.Type != PacketType.SERVERDATA_AUTH_RESPONSE)
                {
                    _tcpClient.Close();
                    throw new Exception($"Packet type mismatch. Received: {(int)packet.Type}. Expected: {(int)PacketType.SERVERDATA_AUTH_RESPONSE}.");
                }

                // Throw exception if authentication failed
                if (packet.Id == -1 || packet.Id != id)
                {
                    _tcpClient.Close();
                    throw new AuthenticationException("Authentication failed");
                }
            }

            /// <summary>
            /// Send command to the server
            /// </summary>
            /// <param name="command"></param>
            /// <returns>The server's response to the original command</returns>
            public string SendCommand(string command)
            {
                // Send the command and a empty command packet
                int id = new Random().Next(4096), dummyId = id + 1;
                _tcpClient.Client.Send(new Packet(id, PacketType.SERVERDATA_EXECCOMMAND, command).GetBytes());
                _tcpClient.Client.Send(new Packet(dummyId, PacketType.SERVERDATA_EXECCOMMAND, string.Empty).GetBytes());

                List<Packet> packets;
                var bytes = new byte[0];
                var buffer = new byte[4096];
                var response = new StringBuilder();

                while (true)
                {
                    // Receive
                    int bufferSize = _tcpClient.Client.Receive(buffer);

                    // Concat to last unused bytes
                    bytes = bytes.Concat(buffer.Take(bufferSize)).ToArray();

                    // Get the packets and get the unused bytes
                    (packets, bytes) = GetPackets(bytes);

                    // Loop all packets
                    foreach (var packet in packets)
                    {
                        // Return the full response until reaching the empty command packet
                        if (packet.Id == dummyId)
                        {
                            return response.ToString();
                        }

                        // Append the body data to response
                        response.Append(packet.Body);
                    }
                }
            }

            /// <summary>
            /// Handle Multiple-packet Responses
            /// </summary>
            /// <param name="bytes"></param>
            /// <returns></returns>
            private (List<Packet>, byte[]) GetPackets(byte[] bytes)
            {
                var packets = new List<Packet>();

                using (var br = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                {
                    // + 4 to ensure br.ReadInt32() is readable
                    while (br.BaseStream.Position + 4 < br.BaseStream.Length)
                    {
                        int size = br.ReadInt32();

                        // Return if we know not enough bytes to read
                        if (br.BaseStream.Position + size > br.BaseStream.Length)
                        {
                            return (packets, bytes.Skip((int)br.BaseStream.Position - 4).ToArray());
                        }

                        // Read packet and append to packets
                        var id = br.ReadInt32();
                        var type = (PacketType)br.ReadInt32();
                        var body = br.ReadStringEx();
                        br.ReadByte();

                        packets.Add(new Packet(id, type, body));
                    }

                    return (packets, new byte[0]);
                }
            }

            private enum PacketType : int
            {
                SERVERDATA_AUTH = 3,
                SERVERDATA_AUTH_RESPONSE = 2,
                SERVERDATA_EXECCOMMAND = 2,
                SERVERDATA_RESPONSE_VALUE = 0,
            }

            private class Packet
            {
                public int Id { get; private set; }
                public PacketType Type { get; private set; }
                public string Body { get; private set; }

                public Packet(int id, PacketType type, string body)
                {
                    (Id, Type, Body) = (id, type, body);
                }

                public byte[] GetBytes()
                {
                    var idBytes = BitConverter.GetBytes(Id);
                    var typeBytes = BitConverter.GetBytes((int)Type);
                    var bodyBytes = Encoding.UTF8.GetBytes(Body + "\0");
                    var size = idBytes.Length + typeBytes.Length + bodyBytes.Length;

                    return BitConverter.GetBytes(size).Concat(idBytes).Concat(typeBytes).Concat(bodyBytes).ToArray();
                }

                /// <summary>
                /// Single-packet Responses
                /// </summary>
                /// <param name="bytes"></param>
                public Packet(byte[] bytes)
                {
                    using (var br = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                    {
                        br.ReadInt32();
                        Id = br.ReadInt32();
                        Type = (PacketType)br.ReadInt32();
                        Body = br.ReadStringEx();
                    }
                }
            }
        }
    }
}
