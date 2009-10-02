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
        private List<RtpPacket> _receivedPackets;
        private List<RtpPacket> _sequencedPackets;

        private Thread _sequencingThread;

        private readonly object _receivingLock = new object();
        private readonly object _sequencingLock = new object();

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
            _sequencedPackets = new List<RtpPacket>();
            _receivedPackets = new List<RtpPacket>();
            _listener = new UdpListener(localEp)
                            {
                                BufferSize = Constants.PacketSize,
                                ReceiveBuffer = Constants.BufferSize,
                                ReceiveCallback = DataReceived
                            };
            this.MaximumBufferedPackets = 25;
            this.VerifyPayloadType = true;
        }


        private int _markerCount;
        public int MarkerCount
        {
            get
            {
                lock (_sequencingLock)
                {
                    return _markerCount;
                }
            }
        }

        public bool IsPayloadAvailable
        {
            get
            {
                lock (_sequencingLock)
                {
                    return this._sequencedPackets.Count > 0;
                }
            }
        }

        public bool IsMarkerPayloadAvailable
        {
            get { return this.MarkerCount > 0; }
        }

        public int MaximumBufferedPackets { get; set; }
        public bool VerifyPayloadType { get; set; }
        public bool IsRunning { get; private set; }


        #region static methods
        public static RtpListener Open(string uri)
        {
            var test = new Regex(@"(?<proto>[a-zA-Z]+)://(?<ip>[\d\.]+)?@(?<joinip>[\d\.]+)?(:(?<port>\d+))?");
            int port;
            IPAddress ip;
            IPAddress joinip;

            Assert.That(test.IsMatch(uri), () => new ArgumentException("Please use a format of 'udp://@MCIP:PORT' where MCIP is a valid multicast IP address.", "uri"));

            var m = test.Match(uri);

            Assert.AreEqual(m.Groups["proto"].Value.ToLower(), "udp", "protocol");

            if (!IPAddress.TryParse(m.Groups["ip"].Value, out ip))
                ip = IPAddress.Any;

            if (!IPAddress.TryParse(m.Groups["joinip"].Value, out joinip))
                joinip = IPAddress.Any;

            if (!int.TryParse(m.Groups["port"].Value, out port))
                port = 1234;


            var client = new RtpListener(new IPEndPoint(ip, port));
            client.StartListening();
            //check if its MC
            if((joinip.GetAddressBytes()[0] & 224) == 224)
                client._listener.JoinMulticastGroup(joinip);
            return client;

        }
        #endregion

        
        public void StartListening()
        {
            Assert.IsNot(_listener.IsStarted, () => new InvalidOperationException("Already started"));
            this._listener.StartListening();
            this.IsRunning = true;
            this._sequencingThread = new Thread(SequencingThread);
            this._sequencingThread.Start();
        }

        public void StopListening()
        {
            Assert.That(_listener.IsStarted, () => new InvalidOperationException("Already started"));
            this._listener.StopListening();
            this.IsRunning = false;
            //make sure if our thread was in a Wait then it exits properly.
            this._sequencingThread.Interrupt();
        }

        private void SequencingThread()
        {
            var seqNumber = 0;
            var payloadType = 0;
            try
            {
                //wait until we get at least one packet and setup our unknowns. ie PT, and seqNumber.
                if (this.IsRunning)
                {
                    lock (_receivingLock)
                    {
                        //get some packets
                        while (_receivedPackets.Count == 0)
                            Monitor.Wait(_receivingLock);

                        //set our sequence number and payload type
                        payloadType = _receivedPackets[0].PayloadType;
                        seqNumber = _receivedPackets[0].SequenceNumber;
                    }
                }

                while (this.IsRunning)
                {
                    RtpPacket nextPacket = null;

                    lock (_receivingLock)
                    {
                        //get some packets
                        while (_receivedPackets.Count == 0)
                            Monitor.Wait(_receivingLock);

                        while (true)
                        {
                            //see if we can find the sequence number
                            for (var i = 0; i < _receivedPackets.Count; i++)
                            {
                                if (_receivedPackets[i].SequenceNumber != seqNumber) continue;
                                nextPacket = _receivedPackets[i];
                                _receivedPackets.RemoveAt(i);
                                break;
                            }

                            //found one so lets get out of here
                            if (nextPacket != null)
                                break;

                            //too many packets queued so give up
                            if (_receivedPackets.Count >= this.MaximumBufferedPackets)
                                break;

                            //nothing yet so lets release the lock and wait for more packets
                            Monitor.Wait(_receivingLock);
                        }
                    }
                    //now we can start looking for the next packet
                    seqNumber++;

                    //we went through all packets but could not find the next in the sequence so skip it...
                    if (nextPacket == null)
                    {
                        OnPacketLoss(seqNumber - 1);
                        continue;
                    }

                    //all packets should have the same payload type but endura is dumb so we may need to ignore it
                    if (this.VerifyPayloadType && nextPacket.PayloadType != payloadType)
                    {
                        OnInvalidPacket(nextPacket);
                        continue;
                    }

                    lock (_sequencingLock)
                    {
                        if (nextPacket.Marker)
                            _markerCount++;

                        this._sequencedPackets.Add(nextPacket);

                        //if we are worried about order use these...
                        if (nextPacket.Marker)
                            OnSequencedMarkerReceived(nextPacket);
                        OnSequencedPacketReceived(nextPacket);
                    }

                    //if not use these...
                    if (nextPacket.Marker)
                        OnMarkerReceived(nextPacket);
                    OnPacketReceived(nextPacket);
                }
            }
            catch (ThreadInterruptedException)
            {
                // we got interrupted so we will exit.
                Assert.IsNot(this.IsRunning, () => new InvalidOperationException("This thread should not get interrupted without being stopped."));
            }
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
                                                    lock (_receivingLock)
                                                    {
                                                        this._receivedPackets.Add(packet);

                                                        //signal that we got a packets
                                                        Monitor.Pulse(_receivingLock);
                                                    }
                                                }
                                                catch (InvalidDataException ex)
                                                {
                                                    OnInvalidData(buffer);
                                                    Assert.Suppress(ex);
                                                }
                                            });
        }

        /// <summary>
        /// Get the Next Payload in the sequence
        /// </summary>
        /// <returns></returns>
        public byte[] GetNextPayload()
        {
            RtpPacket packet;

            lock (_sequencingLock)
            {
                if(this._sequencedPackets.Count == 0)
                    return null;

                packet = _sequencedPackets[0];
                _sequencedPackets.RemoveAt(0);
            }

            return packet.GetPayload();
        }

        /// <summary>
        /// Gets the combined payload of the oldest marker and the payloads from each previous packet
        /// </summary>
        /// <returns></returns>
        public byte[] GetCombinedPayload()
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

            lock (_sequencingLock)
            {
                if (!IsMarkerPayloadAvailable)
                    return null;

                payloadPackets = new List<RtpPacket>();

                //add all payload packets into a temporary list
                foreach (var packet in _sequencedPackets)
                {
                    payloadPackets.Add(packet);
                    payloadSize += packet.PayloadLength;
                    if (packet.Marker)
                    {
                        _markerCount--;
                        break;
                    }
                }

                // remove the payload packets to be returned from the main list
                foreach (var packet in payloadPackets)
                    _sequencedPackets.Remove(packet);
            }

            var payload = new MemoryStream((int)payloadSize);
            foreach (var packet in payloadPackets)
            {
                // copy the packets payload into our payload array
                packet.WriteTo(payload);
            }

            return payload.GetBuffer();
        }

        #region events
        /// <summary>
        /// Occurs when a packet with a different payload type is recieved in the stream
        /// </summary>
        public event EventHandler<EventArgs<RtpPacket>> InvalidPacket;
        protected virtual void OnInvalidPacket(RtpPacket packet)
        {
            var handler = this.InvalidPacket;
            if (handler != null)
                handler(this, new EventArgs<RtpPacket>(packet));
        }

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
        /// Occurs when a packet is received.
        /// </summary>
        public event EventHandler<EventArgs<RtpPacket>> SequencedPacketReceived;
        protected virtual void OnSequencedPacketReceived(RtpPacket packet)
        {
            var handler = SequencedPacketReceived;

            if (handler != null)
                handler(this, new EventArgs<RtpPacket>(packet));
        }

        /// <summary>
        /// Occurs when a marker packet is received.
        /// </summary>
        public event EventHandler<EventArgs<RtpPacket>> SequencedMarkerReceived;
        protected virtual void OnSequencedMarkerReceived(RtpPacket packet)
        {
            var handler = SequencedMarkerReceived;
            if (handler != null)
                handler(this, new EventArgs<RtpPacket>(packet));
        }

        /// <summary>
        /// Occurs when a packet is loss or missed, provides sequence Number that was expected
        /// </summary>
        public event EventHandler<EventArgs<int>> PacketLoss;
        protected virtual void OnPacketLoss(int sequenceNumber)
        {
            var handler = PacketLoss;
            if (handler != null)
                handler(this, new EventArgs<int>(sequenceNumber));
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

                lock (_sequencingLock)
                {
                    _sequencedPackets.Clear();
                }

                lock (_receivingLock)
                {
                    _receivedPackets.Clear();
                }
            }

            // Indicate that the instance has been disposed.
            _listener = null;
            _receivedPackets = null;
            _sequencedPackets = null;
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
