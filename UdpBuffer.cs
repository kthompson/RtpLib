using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace RtpLib
{
    /// <summary>
    /// Buffer containing information received or sent using the UdpListener
    /// </summary>
    public class UdpBuffer
    {
        #region Constants 

        public const int DefaultBufferSize = 4096;

        #endregion

        #region Constructors 

        public UdpBuffer() : this(DefaultBufferSize)
        {
        }

        public UdpBuffer(int bufferSize)
        {
            this.Data = new byte[bufferSize];
            RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }

        #endregion

        #region Properties

        public byte[] Data;
        public int Size;
        public EndPoint RemoteEndPoint;

        #endregion

    }
}