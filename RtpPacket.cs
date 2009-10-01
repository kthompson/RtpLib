using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace RtpLib
{
    public class RtpPacket : UdpBuffer, IComparable<RtpPacket>
    {
        #region ctor 
        private RtpPacket()
        {
            _contributingSourceIds = new List<uint>();
        }

        public static RtpPacket FromUdpBuffer(UdpBuffer buffer)
        {
            var packet = new RtpPacket
                             {
                                 RemoteEndPoint = buffer.RemoteEndPoint,
                                 Size = buffer.Size,
                                 Data = buffer.Data
                             };
            packet.Parse();
            return packet;
        }
        #endregion

        #region public methods

        public int CopyPayloadTo(Stream buffer)
        {
            buffer.Write(this.Data, this.PayloadStartPosition, this.PayloadLength);
            return PayloadLength;
        }

        public int CopyPayloadTo(byte[] buffer, int startIndex)
        {
            Buffer.BlockCopy(this.Data, this.PayloadStartPosition, buffer, startIndex, PayloadLength);
            return PayloadLength;
        }
        #endregion
        private void Parse()
        {
            using(Stream stream = new MemoryStream(this.Data)){
            
                var flags = stream.ReadByte();
                
                Version = (byte)(flags >> 6);
                Padding = Convert.ToBoolean((flags >> 5) & 0x1);
                Extension = Convert.ToBoolean((flags >> 4) & 0x1);
                NumberOfCCs = (byte)(flags & 0x1111);
                
                Assert.That(Version == 2, () => new InvalidDataException("RTP Packet version must be 2."));
                Assert.That(Extension == false, () => new InvalidDataException("RTP Header extensions are not supported"));

                flags = stream.ReadByte();

                Marker = Convert.ToBoolean((flags >> 7) & 0x1);
                PayloadType = (byte)(flags & 0x7F);


                SequenceNumber = ReadInt16(stream);
                Timestamp = ReadInt32(stream);
                SyncSourceId = ReadInt32(stream);

                for (var i = 0; i < NumberOfCCs; i++)
                    _contributingSourceIds.Add(ReadInt32(stream));

                PayloadStartPosition = (int)stream.Position;
                PayloadLength = (int)(this.Size - stream.Position);
            }
        }

        #region helper methods
        private uint ReadInt32(Stream stream)
        {
            return (uint)((stream.ReadByte() << 24)
                          + (stream.ReadByte() << 16)
                          + (stream.ReadByte() << 8)
                          + (stream.ReadByte()));
        }

        private ushort ReadInt16(Stream stream)
        {
            return (ushort)((stream.ReadByte() << 8)
                            + (stream.ReadByte()));
        }
        #endregion

        #region properties

        public int PayloadStartPosition { get; private set; }

        public int PayloadLength { get; private set; }

        public byte Version { get; private set; }

        public bool Padding { get; private set; }

        public bool Extension { get; set; }

        public byte NumberOfCCs { get; private set; }

        public bool Marker { get; private set; }

        public byte PayloadType { get; private set; }

        public int SequenceNumber { get; private set; }

        public uint Timestamp { get; private set; }

        public uint SyncSourceId { get; private set; }

        private readonly List<uint> _contributingSourceIds;
        public List<uint> ContributingSourceIds
        {
            get { return _contributingSourceIds; }
        }
        #endregion

        #region IComparable<RtpPacket> Members

        public int CompareTo(RtpPacket other)
        {
            return this.SequenceNumber.CompareTo(other.SequenceNumber);
        }

        #endregion
    }
}