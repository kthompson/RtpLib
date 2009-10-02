/*
 * Copyright (C) 2009, Kevin Thompson <mrunleaded@gmail.com>
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or
 * without modification, are permitted provided that the following
 * conditions are met:
 *
 * - Redistributions of source code must retain the above copyright
 *   notice, this list of conditions and the following disclaimer.
 *
 * - Redistributions in binary form must reproduce the above
 *   copyright notice, this list of conditions and the following
 *   disclaimer in the documentation and/or other materials provided
 *   with the distribution.
 *
 * - Neither the name of the Git Development Community nor the
 *   names of its contributors may be used to endorse or promote
 *   products derived from this software without specific prior
 *   written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

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

        public const int AutoFlushBufferMaximum = RtpListener.Constants.BufferSize * 15;

        private RtpListener _rtpListener;
        private readonly object _dataLock = new object();
        
        #region constructors

        public RtpStream(IPEndPoint localEp)
        {
            _rtpListener = new RtpListener(localEp);
            _rtpListener.PacketReceived += OnPacketReceived;
            this.AutoFlush = true;
        }

        public RtpStream(int port)
        {
            _rtpListener = new RtpListener(port);
            _rtpListener.PacketReceived += OnPacketReceived;
            this.AutoFlush = true;
        }
        

        #endregion


        void OnPacketReceived(object sender, EventArgs<RtpPacket> e)
        {
            lock (_dataLock)
            {
                //Let the EnsureBufferOf know that we got some more data
                Monitor.Pulse(_dataLock);
            }
        }


        #region static methods
        public static RtpStream Open(string uri)
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


            var client = new RtpStream(port);
            client._rtpListener.StartListening();
            client._rtpListener.JoinMulticastGroup(ip);
            return client;

        }
        #endregion

        #region properties

        public override long Length
        {
            get
            {
                throw new NotSupportedException("Length is not supported since the size of the Stream can change dynamically.");
            }
        }

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
                throw new NotSupportedException("Position is not supported since the size of the Stream can change dynamically.");
            }
            set
            {
                throw new NotSupportedException("Position is not supported since the size of the Stream can change dynamically.");
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

        public bool AutoFlush { get; set; }

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
                //basically this copies all unread data into a new buffer that does not include the Read data.
                var newBuffer = new byte[_data.Length - _bufferPosition];
                Buffer.BlockCopy(_data, _bufferPosition, newBuffer, 0, newBuffer.Length);
                _bufferPosition = 0;

                _data = newBuffer;
            }
        }

        #endregion

        #region private methods

        /// <summary>
        /// Internal method to run the auto flush if required.
        /// </summary>
        private void RunAutoFlush()
        {
            lock (_dataLock)
            {
                if (this.AutoFlush && _data.Length > AutoFlushBufferMaximum)
                    this.Flush();
            }
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
                    var payload = this._rtpListener.GetCombinedPayload();

                    if (payload != null)
                    {
                        _data = Concat(_data, payload);
                    }
                    else
                    {
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

        

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                this._rtpListener.PacketReceived -= OnPacketReceived;
                this._rtpListener.Dispose();
            }

            this._rtpListener = null;
           

            lock (_dataLock)
            {
                if(_data != null)
                    _data = null;
            }
        }
    }
}