using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RtpLib
{
    public class RtpListener : IDisposable
    {

        public class Constants
        {
            public const int PacketSize = 1400;
            public const int BufferSize = PacketSize * 1024;
            
        }

        private UdpListener _listener;
        private SortedList<RtpPacket, RtpPacket> _packets;



        private readonly object _packetLock = new object();

        public RtpListener()
            : this(new IPEndPoint(IPAddress.Any, 0))
        {

        }

        public RtpListener(int port)
            : this(new IPEndPoint(IPAddress.Any, port))
        {

        }

        public RtpListener(IPEndPoint localEp)
        {
            _packets = new SortedList<RtpPacket, RtpPacket>();
            _listener = new UdpListener(localEp);            
            _listener.BufferSize = Constants.PacketSize;
            _listener.ReceiveBuffer = Constants.BufferSize;
            _listener.ReceiveCallback = DataReceived;
        }


        private int _markerCount;
        public int MarkerCount
        {
            get
            {
                lock (_packetLock)
                {
                    return _markerCount;
                }
            }
        }
        /* TODO: only process once two markers are available to ensure all packets for the 
         * next marker are there
         * */
        public bool IsPayloadAvailable
        {
            get
            {
                lock (_packetLock)
                {
                    return _markerCount > 0;
                }
            }
        }


        #region static methods
        public static RtpListener Open(string uri)
        {
            var test = new Regex(@"(?<proto>[a-zA-Z]+)://@(?<ip>[\d\.]+)?(:(?<port>\d+))?");
            int port;
            IPAddress ip;

            Assert.That(test.IsMatch(uri), () => new ArgumentException("Please use a format of 'udp://@MCIP:PORT' where MCIP is a valid multicast IP address.", "uri"));

            var m = test.Match(uri);

            Assert.AreEqual(m.Groups["proto"].Value.ToLower(), "udp", "protocol");

            if (!IPAddress.TryParse(m.Groups["ip"].Value, out ip))
                ip = IPAddress.Any;

            if (!int.TryParse(m.Groups["port"].Value, out port))
                port = 1234;


            var client = new RtpListener(port);
            client._listener.StartListening();
            client._listener.JoinMulticastGroup(ip);
            return client;

        }
        #endregion


        public void StartListening()
        {
            Assert.IsNot(_listener.IsStarted, () => new InvalidOperationException("Already started"));
            this._listener.StartListening();
        }

        public void StopListening()
        {
            Assert.That(_listener.IsStarted, () => new InvalidOperationException("Already started"));
            this._listener.StopListening();
        }

        /// <summary>
        /// Method to handle incoming data from <c>_listener</c>.
        /// </summary>
        /// <param name="listener">The listener.</param>
        /// <param name="buffer">The buffer.</param>
        private void DataReceived(UdpListener listener, UdpBuffer buffer)
        {
            ThreadPool.QueueUserWorkItem(
                                            delegate
                                            {
                                                try
                                                {
                                                    var packet = RtpPacket.FromUdpBuffer(buffer);
                                                    lock (_packetLock)
                                                    {
                                                        this._packets.Add(packet, packet);

                                                        if (packet.Marker)
                                                            _markerCount++;
                                                    }

                                                    if (packet.Marker)
                                                        OnMarkerReceived(packet);
                                                    OnPacketReceived(packet);
                                                }
                                                catch (InvalidDataException ex)
                                                {
                                                    OnInvalidData(buffer);
                                                    Assert.Suppress(ex);
                                                }
                                            });
        }

        /// <summary>
        /// Gets the next payload from the packet list in the form of a byte array. This must only be called by <c>EnsureBufferOf</c> 
        /// </summary>
        /// <returns></returns>
        public byte[] GetNextPayload()
        {
            /* Get all packet payloads up to Marker
             *      make sure they are all in sequence
             *      return the payload as a byte array
             *      delete all packets from main packet array
             * Determine if there is another marker available already...
             *    if there is, then set payload available to true.
             * */
            long payloadSize = 0;
            List<RtpPacket> payloadPackets;

            lock (_packetLock)
            {
                if (!IsPayloadAvailable)
                    return null;

                payloadPackets = new List<RtpPacket>();

                //add all payload packets into a temporary list
                foreach (var kv in _packets)
                {
                    payloadPackets.Add(kv.Value);
                    payloadSize += kv.Value.PayloadLength;
                    if (kv.Value.Marker)
                    {
                        _markerCount--;
                        break;
                    }
                }

                // remove the payload packets to be returned from the main list
                foreach (var packet in payloadPackets)
                    _packets.Remove(packet);
            }

            // handle the payload now
            var payload = new byte[payloadSize];
            var payloadStartPosition = 0;
            //validate sequence numbers
            var seqNumber = payloadPackets[0].SequenceNumber;
            foreach (var packet in payloadPackets)
            {
                if (packet.SequenceNumber == seqNumber)
                {
                    // copy the packets payload into our payload array
                    payloadStartPosition += packet.CopyPayloadTo(payload, payloadStartPosition);
                    // increment seq number to check on next loop
                    seqNumber++;
                }
                else
                {
                    /* the sequence numbers are not in order or there are missing packets
                     * 
                     * TODO: we may have missed a marker packet so we should try to restart by updating 
                     * the seqNumber and payload size
                     * 
                     * */
                    OnPacketLoss();
                    return new byte[] { };
                }
            }
            return payload;
        }

        #region events
        /// <summary>
        /// Occurs when a packet is received that is not a valid rtp packet.
        /// </summary>
        public event EventHandler<EventArgs<UdpBuffer>> InvalidData;
        protected virtual void OnInvalidData(UdpBuffer buffer)
        {
            var handler = InvalidData;
            if (handler != null)
                handler(this, new EventArgs<UdpBuffer>(buffer));
        }

        /// <summary>
        /// Occurs when a packet is received.
        /// </summary>
        public event EventHandler<EventArgs<RtpPacket>> PacketReceived;
        protected virtual void OnPacketReceived(RtpPacket packet)
        {
            var handler = PacketReceived;

            if (handler != null)
                handler(this, new EventArgs<RtpPacket>(packet));
        }

        /// <summary>
        /// Occurs when a marker packet is received.
        /// </summary>
        public event EventHandler<EventArgs<RtpPacket>> MarkerReceived;
        protected virtual void OnMarkerReceived(RtpPacket packet)
        {
            var handler = MarkerReceived;
            if (handler != null)
                handler(this, new EventArgs<RtpPacket>(packet));
        }

        /// <summary>
        /// Occurs when a packet is loss or missed
        /// </summary>
        public event EventHandler PacketLoss;
        protected virtual void OnPacketLoss()
        {
            var handler = PacketLoss;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
        #endregion

        #region IDisposable Members

        private bool _disposed;
        public void Dispose()
        {
            Dispose(true);

            // Use SupressFinalize in case a subclass
            // of this type implements a finalizer.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // If you need thread safety, use a lock around these 
            // operations, as well as in your methods that use the resource.
            if (_disposed) return;

            if (disposing)
            {
                if (_listener.IsStarted)
                    _listener.StopListening();

                lock (_packetLock)
                {
                    _packets.Clear();
                }
            }

            // Indicate that the instance has been disposed.
            _listener = null;
            _packets = null;
            _disposed = true;
        }

        #endregion

        public void JoinMulticastGroup(IPAddress ip)
        {
            this._listener.JoinMulticastGroup(ip);
        }

        public void JoinMulticastGroup(IPAddress ip, int ttl)
        {
            this._listener.JoinMulticastGroup(ip, ttl);
        }

        public void DropMulticastGroup(IPAddress ip)
        {
            this._listener.DropMulticastGroup(ip);
        }
    }
}
