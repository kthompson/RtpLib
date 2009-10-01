using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RtpLib
{
    /// <summary>
    /// Callback method for handling received data
    /// </summary>
    /// <param name="listener"></param>
    /// <param name="buffer">Data received</param>
    /// <returns></returns>
    public delegate void DataReceivedHandler(UdpListener listener, UdpBuffer buffer);

    public class UdpListener : IDisposable
    {

        #region Constructors 

        public UdpListener()
            : this(new IPEndPoint(IPAddress.Any, 0))
        {
        }

        public UdpListener(int port)
            : this(new IPEndPoint(IPAddress.Any, port))
        {
        }

        public UdpListener(IPEndPoint localEp)
        {
            Assert.IsNotNull(localEp, "localEp");

            this._rwLock = new ReaderWriterLock();
            this.BufferSize = UdpBuffer.DefaultBufferSize;
            this.LocalEp = new IPEndPoint(localEp.Address, localEp.Port);
            this.CreateSocket();
        }

        ~UdpListener()
        {
            this.Dispose(false);
        }

        #endregion

        #region IDisposable Members 

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }

            if (!this.IsDisposed)
            {
                this.StopListeningInternal();
                this._client = null;
            }
        }

        #endregion

        #region Public Methods 

        /// <summary>
        /// Start listening for traffic on the current IPEndPoint
        /// </summary>
        public void StartListening()
        {
            try
            {
                this._rwLock.AcquireWriterLock(Timeout.Infinite);
                if (!this.IsStarted)
                {
                    this._client.Bind(this.LocalEp);
                    this.IsStarted = true;
                    this.BeginReceive();
                }
            }
            finally
            {
                this._rwLock.ReleaseWriterLock();
            }
        }

        /// <summary>
        /// Stops listening for traffic
        /// </summary>
        public void StopListening()
        {
            this.StopListeningInternal();

            //recreate socket for reuse
            this.CreateSocket();
        }

        /// <summary>
        /// Sends buffered information to a specified IPEndPoint
        /// </summary>
        /// <param name="buffer"></param>
        public void Send(UdpBuffer buffer)
        {
            this.Send(buffer.Data, 0, buffer.Size, SocketFlags.None, buffer.RemoteEndPoint);
        }

        /// <summary>
        /// Sends buffered information to a specified IPEndPoint
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="remoteEp"></param>
        public void Send(byte[] buffer, EndPoint remoteEp)
        {
            this.Send(buffer, 0, buffer.Length, SocketFlags.None, remoteEp);
        }

        /// <summary>
        /// Sends buffered information to a specified IPEndPoint
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="socketFlags"></param>
        /// <param name="remoteEp"></param>
        public void Send(byte[] buffer, SocketFlags socketFlags, EndPoint remoteEp)
        {
            this.Send(buffer, 0, buffer.Length, socketFlags, remoteEp);
        }

        /// <summary>
        /// Sends buffered information to a specified IPEndPoint
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        /// <param name="socketFlags"></param>
        /// <param name="remoteEp"></param>
        public void Send(byte[] buffer, int offset, int size, SocketFlags socketFlags, EndPoint remoteEp)
        {
            try
            {
                this._rwLock.AcquireReaderLock(Timeout.Infinite);

                if (this.IsStarted)
                {
                    this._client.SendTo(buffer, offset, size, socketFlags, remoteEp);
                }
            }
            finally
            {
                this._rwLock.ReleaseReaderLock();
            }
        }

        #endregion 

        #region Multicast Joins 

        public void JoinMulticastGroup(IPAddress address, int ttl)
        {
            Assert.AreEqual(address.AddressFamily, this.LocalEp.AddressFamily, "address.AddressFamily");
            Assert.That(this.IsStarted, () => new InvalidOperationException("UdpListener must be started to join multicast group"));

            switch (this.LocalEp.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    var option1 = new MulticastOption(address);
                    this._client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, option1);
                    this._client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);
                    break;
                case AddressFamily.InterNetworkV6:
                    var option2 = new IPv6MulticastOption(address);
                    this._client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, option2);
                    this._client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, ttl);
                    break;
                default:
                    Assert.That(false, () => new NotSupportedException("UdpListener only supports IPv4 and IPv6."));
                    break;
            }
        }

        public void JoinMulticastGroup(IPAddress address)
        {
            Assert.That(this.IsStarted, () => new InvalidOperationException("UdpListener must be started to join multicast group"));
            Assert.AreEqual(address.AddressFamily, this.LocalEp.AddressFamily, "address.AddressFamily");

            switch (this.LocalEp.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    {
                        var option1 = new MulticastOption(address);
                        this._client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, option1);
                    }
                    break;
                case AddressFamily.InterNetworkV6:
                    {
                        var option2 = new IPv6MulticastOption(address);
                        this._client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, option2);
                    }
                    break;
                default:
                    Assert.That(false,() => new NotSupportedException("UdpListener only supports IPv4 and IPv6."));
                    break;
            }
        }

        public void DropMulticastGroup(IPAddress address)
        {
            Assert.AreEqual(address.AddressFamily, this.LocalEp.AddressFamily, "address.AddressFamily");

            if (this.LocalEp.AddressFamily == AddressFamily.InterNetwork)
            {
                var option1 = new MulticastOption(address);
                this._client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, option1);
            }
            else
            {
                var option2 = new IPv6MulticastOption(address);
                this._client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.DropMembership, option2);
            }
        }


        #endregion

        #region Private Helper Methods 

        private void CreateSocket()
        {
            this._client = new Socket(this.LocalEp.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            this.ReuseAddress = true;
        }

        private void StopListeningInternal()
        {
            try
            {
                this._rwLock.AcquireWriterLock(Timeout.Infinite);
                this.IsStarted = false;
                this._client.Close();
            }
            finally
            {
                this._rwLock.ReleaseWriterLock();
            }
        }

        protected void BeginReceive()
        {
            try
            {
                this._rwLock.AcquireReaderLock(Timeout.Infinite);

                if (this.IsStarted)
                {
                    // allocate a packet buffer
                    var buffer = new UdpBuffer(this.BufferSize);

                    // kick off an async read
                    this._client.BeginReceiveFrom(buffer.Data, 0, buffer.Data.Length, SocketFlags.None, ref buffer.RemoteEndPoint, EndReceive, buffer);
                }
            }
            finally
            {
                this._rwLock.ReleaseReaderLock();
            }
        }

        protected void EndReceive(IAsyncResult async)
        {
            try
            {
                this._rwLock.AcquireReaderLock(Timeout.Infinite);

                if (this.IsStarted)
                {
                    //immediately start the next receive
                    this.BeginReceive();

                    //get the buffer created for this receive
                    var buffer = (UdpBuffer)async.AsyncState;

                    //get the data length received
                    buffer.Size = this._client.EndReceiveFrom(async, ref buffer.RemoteEndPoint);

                    //fire the callback method
                    if (this.ReceiveCallback != null)
                        this.ReceiveCallback(this, buffer);
                }
            }
            catch (SocketException ex)
            {
                ex.ToString();
            }
            finally
            {
                this._rwLock.ReleaseReaderLock();
            }
        }

        #endregion

        #region SocketOption Accessors 

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue)
        {
            this._client.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue)
        {
            this._client.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, byte[] optionValue)
        {
            this._client.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public object GetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName)
        {
            return this._client.GetSocketOption(optionLevel, optionName);
        }

        public byte[] GetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionLength)
        {
            return this._client.GetSocketOption(optionLevel, optionName, optionLength);
        }

        public void GetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, byte[] optionValue)
        {
            this._client.GetSocketOption(optionLevel, optionName, optionValue);
        }

        #endregion

        #region Properties 

        private readonly ReaderWriterLock _rwLock;
        private Socket _client;

        public bool IsDisposed
        {
            get { return (this._client == null); }
        }

        public IPEndPoint LocalEp { get; private set; }

        public bool IsStarted { get; private set; }

        public int BufferSize { get; set; }

        public DataReceivedHandler ReceiveCallback { get; set; }

        public short Ttl
        {
            get
            {
                return (short)this._client.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive);
            }
            set
            {
                this._client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, value);
            }
        }

        public bool EnableBroadcast
        {
            get
            {
                return (((int)this._client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast)) != 0);
            }
            set
            {
                this._client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, value ? 1 : 0);
            }
        }

        public bool ReuseAddress
        {
            get
            {
                return ((int)this._client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) == 1);
            }
            set
            {
                this._client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, (value ? 1 : 0));
            }
        }

        public int ReceiveBuffer
        {
            get { return (int)this._client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer); }
            set { this._client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, value); }
        }

        #endregion

    }
}