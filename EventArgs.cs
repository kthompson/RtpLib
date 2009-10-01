using System;
using System.Collections.Generic;
using System.Text;

namespace RtpLib
{
    public class EventArgs<T> : EventArgs
    {
        public EventArgs(T data)
        {
            Data = data;
        }

        public T Data { get; private set; }
    }
}