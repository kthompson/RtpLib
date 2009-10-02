using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using RtpLib;
using Action=System.Action;

namespace TestApp
{
    public partial class Form1 : Form
    {
        private RtpListener _listener;
        private FileStream _file;
        public Form1()
        {
            InitializeComponent();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (File.Exists("temp.mpv"))
                File.Delete("temp.mpv");

            _file = new FileStream("temp.mpv", FileMode.CreateNew);

            _listener = RtpListener.Open(txtUri.Text);
            _listener.SequencedMarkerReceived += MarkerReceived;
            _listener.VerifyPayloadType = false;
        }

        void MarkerReceived(object sender, EventArgs<RtpPacket> e)
        {
            var data = _listener.GetCombinedPayload();
            _file.Write(data, 0, data.Length);
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (_listener == null) return;
            _listener.StopListening();
            _listener.Dispose();
            _listener = null;
            _file.Close();
            _file = null;
        }
    }
}
