using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace RtpLib
{
    /// <summary>
    /// Class is used to queue up and order RTP packets
    /// </summary>
    public class RtpStream : Stream
    {

        public class Constants
        {
            public const int PacketSize = 1400;
            public const int BufferSize = PacketSize * 1024;
            public const int AutoFlushBufferMaximum = BufferSize * 15;
        }

        private UdpListener _listener;
        private SortedList<RtpPacket, RtpPacket> _packets;

        private readonly object _packetLock = new object();
        private readonly object _dataLock = new object();
        
        #region constructors

        public RtpStream()
            : this(new IPEndPoint(IPAddress.Any, 0))
        {

        }

        public RtpStream(int port)
            : this(new IPEndPoint(IPAddress.Any, port))
        {

        }

        public RtpStream(IPEndPoint localEp)
        {
            _packets = new SortedList<RtpPacket, RtpPacket>();
            _listener = new UdpListener(localEp);            
            _listener.BufferSize = Constants.PacketSize;
            _listener.ReceiveBuffer = Constants.BufferSize;
            _listener.ReceiveCallback = DataReceived;
        }

        #endregion
        
        #region properties



        private long _length;
        public override long Length
        {
            get
            {
                lock (_dataLock)
                {
                    return _length;
                }
            }
        }

        private long _lastFlushPosition;
        private int _bufferPosition;
        /// <summary>
        /// When overridden in a derived class, gets or sets the position within the current stream.
        /// </summary>
        /// <value></value>
        /// <returns>The current position within the stream.</returns>
        /// <exception cref="T:System.ArgumentOutOfRangeException">the position is set to a location prior to last flush position or after the end of the stream. </exception>
        public override long Position
        {
            get
            {
                lock (_dataLock)
                {
                    return _lastFlushPosition + _bufferPosition;
                }
            }
            set
            {
                lock (_dataLock)
                {
                    Assert.IsGreaterThan(value, "position", _lastFlushPosition);
                    Assert.IsLessThan(value, "position", _data.Length);
                    _bufferPosition = (int)(value - _lastFlushPosition);
                }
            }
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
            get {
                lock (_packetLock)
                {
                    return _markerCount > 0;
                }
            }
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        private bool _autoFlush = true;
        public bool AutoFlush
        {
            get { return _autoFlush; }
            set { _autoFlush = value; }
        }

        #endregion

        #region static methods
        public static RtpStream Open(string uri)
        {
            var test = new Regex(@"(?<proto>[a-zA-Z]+)://@(?<ip>[\d\.]+)?(:(?<port>\d+))?");
            int port;
            IPAddress ip;

            Assert.That(test.IsMatch(uri), () => new ArgumentException("Please use a format of 'udp://@MCIP:PORT' where MCIP is a valid multicast IP address.","uri"));

            var m = test.Match(uri);

            Assert.AreEqual(m.Groups["proto"].Value.ToLower(), "udp", "protocol");
            
            if(!IPAddress.TryParse(m.Groups["ip"].Value, out ip))
                ip = IPAddress.Any;

            if(!int.TryParse(m.Groups["port"].Value, out port))
                port = 1234;


            var client = new RtpStream(port);
            client._listener.StartListening();
            client._listener.JoinMulticastGroup(ip);
            return client;
                
        }
        #endregion

        #region public methods


        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_dataLock)
            {
                RunAutoFlush();
                EnsureBufferOf(count);

                Buffer.BlockCopy(_data, _bufferPosition, buffer, offset, count);
                _bufferPosition += count;

                //Let the EnsureBufferOf know that we got some more data
                Monitor.Pulse(_dataLock);
                return count;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("The method or operation is not supported.");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("The method or operation is not supported.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The method or operation is not supported.");
        }

        /// <summary>
        /// Clears all buffers for this stream and updates position and length accordingly
        /// </summary>
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        public override void Flush()
        {
            lock (_dataLock)
            {
                _lastFlushPosition = this.Position;
                var newBuffer = new byte[_data.Length - _bufferPosition];
                Buffer.BlockCopy(_data, _bufferPosition, newBuffer, 0, newBuffer.Length);
                _bufferPosition = 0;

                _data = newBuffer;
            }
        }

        #endregion

        #region private methods


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

                                                    lock (_dataLock)
                                                    {
                                                        _length += packet.PayloadLength;
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
        /// Internal method to run the auto flush if required.
        /// </summary>
        private void RunAutoFlush()
        {
            lock (_dataLock)
            {
                if (_autoFlush && _data.Length > Constants.AutoFlushBufferMaximum)
                    this.Flush();
            }
        }



        /// <summary>
        /// Gets the next payload from the packet list in the form of a byte array. This must only be called by <c>EnsureBufferOf</c> 
        /// </summary>
        /// <returns></returns>
        private byte[] GetNextPayload()
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
                     * TODO: we may have missed a marker packet so we should try to restart by updated 
                     * the seqNumber and payload size
                     * 
                     * */
                    OnPacketLoss();
                    return new byte[] { };
                }
            }
            return payload;
        }

        private byte[] _data = new byte[] { };

        /// <summary>
        /// Ensures the buffer of a specified [size]. Throws a [TimeoutException] when it takes longer then 1000ms to fill the buffer.
        /// </summary>
        /// <param name="size">The size.</param>
        private void EnsureBufferOf(long size)
        {
            lock (_dataLock)
            {
                while (_data.Length - _bufferPosition < size)
                {
                    byte[] payload;

                    lock (_packetLock)
                    {
                        payload = this.GetNextPayload();
                    }

                    if (payload != null)
                    {
                        _data = Concat(_data, payload);
                    }
                    else
                    {
                        //TODO: We should really wait the full amount and use signaling to resume
                        //sleep doesn't release the lock so we need to use wait
                        //otherwise we'll get a ton of backed up jobs in the queue all waiting to update _length
                        //I could be missing an error somewhere that this is causing now, but it seems to have solved my problem
                        Monitor.Wait(_dataLock);
                    }
                }
            }
        }

        public static byte[] Concat(byte[] array1, byte[] array2)
        {
            var newData = new byte[array1.Length + array2.Length];
            Buffer.BlockCopy(array1, 0, newData, 0, array1.Length);
            Buffer.BlockCopy(array2, 0, newData, array1.Length, array2.Length);
            return newData;
        }

        #endregion

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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (this._listener != null)
            {
                this._listener.Dispose(disposing);
                this._listener = null;
            }

            lock(_packetLock)
            {
                if (_packets != null)
                    _packets = null;
            }

            lock (_dataLock)
            {
                if(_data != null)
                    _data = null;
            }
        }
    }
}