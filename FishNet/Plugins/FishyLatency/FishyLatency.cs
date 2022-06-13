using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Utility.Performance;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeardedMonkeys
{
    public class FishyLatency : Transport
    {
        #region Serialized
        /// <summary>
        /// Normal transport to use.
        /// </summary>
        [Tooltip("Normal transport to use.")]
        [SerializeField]
        private Transport _transport;

        [Header("Network Statistics")]
        /// <summary>
        /// Enable or disable the packet calculation
        /// </summary>
        [Tooltip("Enable or disable the packet calculation")]
        [SerializeField]
        private bool _showStatistics = true;

        [Header("Settings")]
        /// <summary>
        /// True to use latency simulation.
        /// </summary>
        [Tooltip("True to use latency simulation.")]
        [SerializeField]
        private bool _simulate = true;
        /// <summary>
        /// Milliseconds to add between packets. When acting as host this value will be doubled. Added latency will be a minimum of tick rate.
        /// </summary>
        [Tooltip("Milliseconds to add between packets. When acting as host this value will be doubled. Added latency will be a minimum of tick rate.")]
        [Range(0, 60000)]
        [SerializeField]
        private long _latency = 0;
        /// <summary>
        /// Percentage of packets which should drop.
        /// </summary>
        [Tooltip("Percentage of packets which should drop.")]
        [Range(0, 1)]
        [SerializeField]
        private double _packetloss = 0;

        [Header("Unreliable")]
        /// <summary>
        /// Percentage of unreliable packets which should arrive out of order.
        /// </summary>
        [Tooltip("Percentage of unreliable packets which should arrive out of order.")]
        [Range(0, 1)]
        [SerializeField]
        private double _outOfOrder = 0;
        #endregion

        #region Attributes
        // Sent Packets / bytes per second
        public int SentPacketsClient => _sentPackets[0];
        public int SentPacketsServer => _sentPackets[1];

        public string SentBytesClient => FormatBytes(_sentBytes[0]);
        public string SentBytesServer => FormatBytes(_sentBytes[1]);

        public int SentBytesRawClient => _sentBytes[0];
        public int SentBytesRawServer => _sentBytes[1];

        // Received Packets / bytes per second
        public int ReceivedPacketsClient => _receivedPackets[0];
        public int ReceivedPacketsServer => _receivedPackets[1];

        public string ReceivedBytesClient => FormatBytes(_receivedBytes[0]);
        public string ReceivedBytesServer => FormatBytes(_receivedBytes[1]);

        public int ReceivedBytesRawClient => _receivedBytes[0];
        public int ReceivedBytesRawServer => _receivedBytes[1];
        #endregion

        #region Private
        private float _longLatencyToFloat => (float)(_latency / 1000f);
        private List<Message> _toServerReliablePackets;
        private List<Message> _toServerUnreliablePackets;
        private List<Message> _toClientReliablePackets;
        private List<Message> _toClientUnreliablePackets;

        private struct Message
        {
            public readonly byte ChannelId;
            public readonly int ConnectionId;
            public readonly byte[] Data;
            public readonly int Length;
            public readonly float SendTime;

            public Message(byte channelId, int connectionId, ArraySegment<byte> segment, float latency)
            {
                this.ChannelId = channelId;
                this.ConnectionId = connectionId;
                this.SendTime = (Time.unscaledTime + latency);
                this.Length = segment.Count;
                this.Data = ByteArrayPool.Retrieve(this.Length);
                Buffer.BlockCopy(segment.Array, segment.Offset, this.Data, 0, this.Length);
            }

            public ArraySegment<byte> GetSegment()
            {
                return new ArraySegment<byte>(Data, 0, Length);
            }
        }

        private readonly System.Random _random = new System.Random();
        // Used to keep track of how many packets get sent / received per second
        // 0 = Client | 1 = Server
        private int[] _sentPackets, _receivedPackets, _sentBytes, _receivedBytes;
        private int[] _sentPacketsCount, _receivedPacketsCount, _sentBytesCount, _receivedBytesCount;
        private float _calculationTime = 0;
        #endregion

        #region Initialization and Unity
        public override void Initialize(NetworkManager networkManager, int transportIndex)
        {
            _transport.Initialize(networkManager, transportIndex);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _toServerReliablePackets = new List<Message>();
            _toServerUnreliablePackets = new List<Message>();
            _toClientReliablePackets = new List<Message>();
            _toClientUnreliablePackets = new List<Message>();
#endif
            _transport.OnClientConnectionState += HandleClientConnectionState;
            _transport.OnServerConnectionState += HandleServerConnectionState;
            _transport.OnRemoteConnectionState += HandleRemoteConnectionState;
            _transport.OnClientReceivedData += HandleClientReceivedDataArgs;
            _transport.OnServerReceivedData += HandleServerReceivedDataArgs;

            InitializeStatistics();
        }

        private void Update()
        {
            UpdateCalculation();
        }

        private void OnDestroy()
        {
            _transport.Shutdown();
            Shutdown();
        }
        #endregion        

        #region ConnectionStates
        /// <summary>
        /// Called when a connection state changes for the local client.
        /// </summary>
        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        /// <summary>
        /// Called when a connection state changes for the local server.
        /// </summary>
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        /// <summary>
        /// Called when a connection state changes for a remote client.
        /// </summary>
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        /// <summary>
        /// Gets the current local ConnectionState.
        /// </summary>
        /// <param name="server">True if getting ConnectionState for the server.</param>
        public override LocalConnectionState GetConnectionState(bool server)
        {
            return _transport.GetConnectionState(server);
        }
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return _transport.GetConnectionState(connectionId);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            OnClientConnectionState?.Invoke(connectionStateArgs);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local server.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for a remote client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {
            OnRemoteConnectionState?.Invoke(connectionStateArgs);
        }
        #endregion

        #region Iterating
        /// <summary>
        /// Processes data received by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateIncoming(bool server)
        {
            _transport.IterateIncoming(server);
        }

        /// <summary>
        /// Processes data to be sent by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateOutgoing(bool server)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Simulation(server);
#else
            _transport.IterateOutgoing(server);
#endif
        }
        #endregion

        #region ReceivedData
        /// <summary>
        /// Called when client receives data.
        /// </summary>
        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }
        /// <summary>
        /// Called when server receives data.
        /// </summary>
        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }
        #endregion

        #region Sending
        /// <summary>
        /// Sends to the server or all clients.
        /// </summary>
        /// <param name="channelId">Channel to use.</param>
        /// /// <param name="segment">Data to send.</param>
        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (_showStatistics)
                AddSendPacketToCalc(segment.Count, false);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_simulate)
                Add(channelId, segment);
            else
                _transport.SendToServer(channelId, segment);
#else
            _transport.SendToServer(channelId, segment);
#endif            
        }
        /// <summary>
        /// Sends data to a client.
        /// </summary>
        /// <param name="channelId"></param>
        /// <param name="segment"></param>
        /// <param name="connectionId"></param>
        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (_showStatistics)
                AddSendPacketToCalc(segment.Count, true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_simulate)
                Add(channelId, segment, true, connectionId);
            else
                _transport.SendToClient(channelId, segment, connectionId);
#else
            _transport.SendToClient(channelId, segment, connectionId);
#endif            
        }

        #endregion

        #region Configuration
        /// <summary>
        /// Gets the IP address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public override string GetConnectionAddress(int connectionId)
        {
            return _transport.GetConnectionAddress(connectionId);
        }
        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server. If the transport does not support this method the value -1 is returned.
        /// </summary>
        /// <returns></returns>
        public override int GetMaximumClients()
        {
            return _transport.GetMaximumClients();
        }
        /// <summary>
        /// Sets maximum number of clients allowed to connect to the server. If applied at runtime and clients exceed this value existing clients will stay connected but new clients may not connect.
        /// </summary>
        /// <param name="value"></param>
        public override void SetMaximumClients(int value)
        {
            _transport.SetMaximumClients(value);
        }
        /// <summary>
        /// Sets which address the client will connect to.
        /// </summary>
        /// <param name="address"></param>
        public override void SetClientAddress(string address)
        {
            _transport.SetClientAddress(address);
        }
        /// <summary>
        /// Sets which address the server will bind to.
        /// </summary>
        /// <param name="address"></param>
        public override void SetServerBindAddress(string address, IPAddressType addressType)
        {
            _transport.SetServerBindAddress(address, addressType);
        }
        /// <summary>
        /// Gets which address the server will bind to.
        /// </summary>
        /// <param name="address"></param>
        public override string GetServerBindAddress(IPAddressType addressType)
        {
            return _transport.GetServerBindAddress(addressType);
        }
        /// <summary>
        /// Sets which port to use.
        /// </summary>
        /// <param name="port"></param>
        public override void SetPort(ushort port)
        {
            _transport.SetPort(port);
        }
        /// <summary>
        /// Gets which port to use.
        /// </summary>
        /// <param name="port"></param>
        public override ushort GetPort()
        {
            return _transport.GetPort();
        }
        /// <summary>
        /// Returns the adjusted timeout as float
        /// </summary>
        /// <param name="asServer"></param>
        public override float GetTimeout(bool asServer)
        {
            return _transport.GetTimeout(asServer);
        }
        #endregion

        #region Start and Stop
        /// <summary>
        /// Starts the local server or client using configured settings.
        /// </summary>
        /// <param name="server">True to start server.</param>
        public override bool StartConnection(bool server)
        {
            return _transport.StartConnection(server);
        }

        /// <summary>
        /// Stops the local server or client.
        /// </summary>
        /// <param name="server">True to stop server.</param>
        public override bool StopConnection(bool server)
        {
            return _transport.StopConnection(server);
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        /// <param name="immediately">True to abrutly stp the client socket without waiting socket thread.</param>
        public override bool StopConnection(int connectionId, bool immediately)
        {
            return _transport.StopConnection(connectionId, immediately);
        }

        /// <summary>
        /// Stops both client and server.
        /// </summary>
        public override void Shutdown()
        {
            _transport.OnClientConnectionState -= HandleClientConnectionState;
            _transport.OnServerConnectionState -= HandleServerConnectionState;
            _transport.OnRemoteConnectionState -= HandleRemoteConnectionState;
            _transport.OnClientReceivedData -= HandleClientReceivedDataArgs;
            _transport.OnServerReceivedData -= HandleServerReceivedDataArgs;

            DeinitializeStatistics();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _toServerReliablePackets.Clear();
            _toServerUnreliablePackets.Clear();
            _toClientReliablePackets.Clear();
            _toClientUnreliablePackets.Clear();
#endif

            //Stops client then server connections.
            StopConnection(false);
            StopConnection(true);
        }
        #endregion

        #region Channels
        /// <summary>
        /// Gets the MTU for a channel. This should take header size into consideration.
        /// For example, if MTU is 1200 and a packet header for this channel is 10 in size, this method should return 1190.
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public override int GetMTU(byte channel)
        {
            return _transport.GetMTU(channel);
        }
        #endregion

        #region Simulation
        private void Add(byte channelId, ArraySegment<byte> segment, bool server = false, int connectionId = 0)
        {
            Channel c = (Channel)channelId;
            List<Message> collection;

            if (server)
                collection = (c == Channel.Reliable) ? _toServerReliablePackets : _toServerUnreliablePackets;
            else
                collection = (c == Channel.Reliable) ? _toClientReliablePackets : _toClientUnreliablePackets;

            float latency = _longLatencyToFloat; ;
            //If dropping check to add extra latency if reliable, or discard if not.
            if (CheckPacketLoss())
            {
                if (c == Channel.Reliable)
                {
                    latency += _longLatencyToFloat; //add extra for resend.
                }
                //If not reliable then return the segment array to pool.
                else
                {
                    return;
                }
            }

            Message msg = new Message(channelId, connectionId, segment, latency);
            int cCount = collection.Count;
            if (c == Channel.Unreliable && cCount > 0 && CheckOutOfOrder())
                collection.Insert(cCount - 1, msg);
            else
                collection.Add(msg);
        }

        private void Simulation(bool server)
        {
            if (server)
            {
                IterateCollection(_toServerReliablePackets, server);
                IterateCollection(_toServerUnreliablePackets, server);
            }
            else
            {
                IterateCollection(_toClientReliablePackets, server);
                IterateCollection(_toClientUnreliablePackets, server);
            }

            _transport.IterateOutgoing(server);
        }

        private void IterateCollection(List<Message> collection, bool server)
        {
            float unscaledTime = Time.unscaledTime;
            int count = collection.Count;
            int iterations = 0;
            for (int i = 0; i < count; i++)
            {
                Message msg = collection[0];
                //Not enough time has passed.
                if (unscaledTime < msg.SendTime)
                    break;

                //Enough time has passed.
                if (server)
                    _transport.SendToClient(msg.ChannelId, msg.GetSegment(), msg.ConnectionId);
                else
                    _transport.SendToServer(msg.ChannelId, msg.GetSegment());

                iterations++;
            }

            if (iterations > 0)
                collection.RemoveRange(0, iterations);
        }

        private bool CheckPacketLoss()
        {
            return _packetloss > 0 && _random.NextDouble() < _packetloss;
        }

        private bool CheckOutOfOrder()
        {
            return _outOfOrder > 0 && _random.NextDouble() < _outOfOrder;
        }
        #endregion

        #region Packet Calculation
        private void InitializeStatistics()
        {
            _transport.OnClientReceivedData += OnClientReceivedDataCalc;
            _transport.OnServerReceivedData += OnServerReceivedDataCalc;

            _sentPackets = new int[2];
            _receivedPackets = new int[2];
            _sentBytes = new int[2];
            _receivedBytes = new int[2];

            _sentPacketsCount = new int[2];
            _receivedPacketsCount = new int[2];
            _sentBytesCount = new int[2];
            _receivedBytesCount = new int[2];
        }

        private void DeinitializeStatistics()
        {
            _transport.OnClientReceivedData -= OnClientReceivedDataCalc;
            _transport.OnServerReceivedData -= OnServerReceivedDataCalc;

            _sentPackets = new int[2];
            _receivedPackets = new int[2];
            _sentBytes = new int[2];
            _receivedBytes = new int[2];

            _sentPacketsCount = new int[2];
            _receivedPacketsCount = new int[2];
            _sentBytesCount = new int[2];
            _receivedBytesCount = new int[2];
        }

        private void UpdateCalculation()
        {
            if (_showStatistics)
            {
                _calculationTime += Time.deltaTime;

                if ((_calculationTime % 60) >= 1)
                {
                    _calculationTime = 0;

                    _sentPackets[0] = _sentPacketsCount[0];
                    _sentBytes[0] = _sentBytesCount[0];
                    _receivedPackets[0] = _receivedPacketsCount[0];
                    _receivedBytes[0] = _receivedBytesCount[0];

                    _sentPackets[1] = _sentPacketsCount[1];
                    _sentBytes[1] = _sentBytesCount[1];
                    _receivedPackets[1] = _receivedPacketsCount[1];
                    _receivedBytes[1] = _receivedBytesCount[1];

                    _sentPacketsCount = new int[2];
                    _receivedPacketsCount = new int[2];
                    _sentBytesCount = new int[2];
                    _receivedBytesCount = new int[2];
                }
            }
        }

        private void AddSendPacketToCalc(int length, bool asServer)
        {
            _sentPacketsCount[asServer ? 1 : 0]++;
            _sentBytesCount[asServer ? 1 : 0] += length;
        }

        private void AddReceivePacketToCalc(int length, bool asServer)
        {
            _receivedPacketsCount[asServer ? 1 : 0]++;
            _receivedBytesCount[asServer ? 1 : 0] += length;
        }

        private void OnClientReceivedDataCalc(ClientReceivedDataArgs receivedDataArgs)
        {
            if (_showStatistics)
                AddReceivePacketToCalc(receivedDataArgs.Data.Count, false);
        }

        private void OnServerReceivedDataCalc(ServerReceivedDataArgs receivedDataArgs)
        {
            if (_showStatistics)
                AddReceivePacketToCalc(receivedDataArgs.Data.Count, true);
        }

        string FormatBytes(long byteCount)
        {
            string[] suf = { "b", "kB", "mb", "GB", "TB", "PB", "EB" };

            if (byteCount == 0)
                return "0 " + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + " " + suf[place];
        }
        #endregion
    }
}
