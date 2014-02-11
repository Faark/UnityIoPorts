//
// System.IO.Ports.SerialPortStream.cs
//
// Authors:
// Chris Toshok (toshok@ximian.com)
// Carlos Alberto Cortez (calberto.cortez@gmail.com)
//
// (c) Copyright 2006 Novell, Inc. (http://www.novell.com)
//
// Slightly modified by Konrad M. Kruczynski (added baud rate value checking)


using System;
using System.IO;
using System.Runtime.InteropServices;
using System.IO.Ports;

namespace UnityIoPorts
{
    class SerialPortStream : Stream, ISerialStream, IDisposable
    {
        int fd;
        int read_timeout;
        int write_timeout;
        bool disposed;


        static SerialPortStream()
        {
            open_serial = (open_serialDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(open_serialDelegate));
            read_serial = (read_serialDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(read_serialDelegate));
            poll_serial = (poll_serialDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(poll_serialDelegate));
            write_serial = (write_serialDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(write_serialDelegate)); 
            close_serial = (close_serialDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(close_serialDelegate));
            set_attributes  = (set_signalDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(set_attributesDelegate));
            get_bytes_in_buffer = (get_bytes_in_bufferDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(get_bytes_in_bufferDelegate));
            get_signals = (get_signalsDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(get_signalsDelegate));
            set_signal = (set_signalDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(set_signalDelegate));
            discard_buffer = (discard_bufferDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(discard_bufferDelegate));
            breakprop = (breakpropDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(breakpropDelegate));
            is_baud_rate_legal = (is_baud_rate_legalDelegate)Marshal.GetDelegateForFunctionPointer(/*insert ptr here*/, typeof(is_baud_rate_legalDelegate));
        }



        static open_serialDelegate open_serial;
        //[DllImport("MonoPosixHelper", SetLastError = true)]
        [UnmanagedFunctionPointer( System.Runtime.InteropServices.CallingConvention.Cdecl,SetLastError = true)]
        delegate int open_serialDelegate(string portName);

        public SerialPortStream(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits,
        bool dtrEnable, bool rtsEnable, Handshake handshake, int readTimeout, int writeTimeout,
        int readBufferSize, int writeBufferSize)
        {
            fd = open_serial(portName);
            if (fd == -1)
                ThrowIOException();

            TryBaudRate(baudRate);

            if (!set_attributes(fd, baudRate, parity, dataBits, stopBits, handshake))
                ThrowIOException(); // Probably Win32Exc for compatibility

            read_timeout = readTimeout;
            write_timeout = writeTimeout;

            SetSignal(SerialSignal.Dtr, dtrEnable);

            if (handshake != Handshake.RequestToSend &&
            handshake != Handshake.RequestToSendXOnXOff)
                SetSignal(SerialSignal.Rts, rtsEnable);
        }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return true;
            }
        }

        public override bool CanTimeout
        {
            get
            {
                return true;
            }
        }

        public override int ReadTimeout
        {
            get
            {
                return read_timeout;
            }
            set
            {
                if (value < 0 && value != SerialPort.InfiniteTimeout)
                    throw new ArgumentOutOfRangeException("value");

                read_timeout = value;
            }
        }

        public override int WriteTimeout
        {
            get
            {
                return write_timeout;
            }
            set
            {
                if (value < 0 && value != SerialPort.InfiniteTimeout)
                    throw new ArgumentOutOfRangeException("value");

                write_timeout = value;
            }
        }

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        public override void Flush()
        {
            // If used, this _could_ flush the serial port
            // buffer (not the SerialPort class buffer)
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static read_serialDelegate read_serial;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate int read_serialDelegate(int fd, byte[] buffer, int offset, int count);

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static poll_serialDelegate poll_serial;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate bool poll_serialDelegate(int fd, out int error, int timeout);

        public override int Read([In, Out] byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0)
                throw new ArgumentOutOfRangeException("offset or count less than zero.");

            if (buffer.Length - offset < count)
                throw new ArgumentException("offset+count",
                "The size of the buffer is less than offset + count.");

            int error;
            bool poll_result = poll_serial(fd, out error, read_timeout);
            if (error == -1)
                ThrowIOException();

            if (!poll_result)
            {
                // see bug 79735 http://bugzilla.ximian.com/show_bug.cgi?id=79735
                // should the next line read: return -1;
                throw new TimeoutException();
            }

            int result = read_serial(fd, buffer, offset, count);
            if (result == -1)
                ThrowIOException();
            return result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static write_serialDelegate write_serial;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate int write_serialDelegate(int fd, byte[] buffer, int offset, int count, int timeout);

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (offset < 0 || count < 0)
                throw new ArgumentOutOfRangeException();

            if (buffer.Length - offset < count)
                throw new ArgumentException("offset+count",
                "The size of the buffer is less than offset + count.");

            // FIXME: this reports every write error as timeout
            if (write_serial(fd, buffer, offset, count, write_timeout) < 0)
                throw new TimeoutException("The operation has timed-out");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed)
                return;

            disposed = true;
            if (close_serial(fd) != 0)
                ThrowIOException();
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static close_serialDelegate close_serial;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate int close_serialDelegate(int fd);

        public override void Close()
        {
            ((IDisposable)this).Dispose();
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SerialPortStream()
        {
            Dispose(false);
        }

        void CheckDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static set_attributesDelegate set_attributes;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate bool set_attributesDelegate(int fd, int baudRate, Parity parity, int dataBits, StopBits stopBits, Handshake handshake);

        public void SetAttributes(int baud_rate, Parity parity, int data_bits, StopBits sb, Handshake hs)
        {
            if (!set_attributes(fd, baud_rate, parity, data_bits, sb, hs))
                ThrowIOException();
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static get_bytes_in_bufferDelegate get_bytes_in_buffer;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate int get_bytes_in_bufferDelegate(int fd, int input);

        public int BytesToRead
        {
            get
            {
                int result = get_bytes_in_buffer(fd, 1);
                if (result == -1)
                    ThrowIOException();
                return result;
            }
        }

        public int BytesToWrite
        {
            get
            {
                int result = get_bytes_in_buffer(fd, 0);
                if (result == -1)
                    ThrowIOException();
                return result;
            }
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static discard_bufferDelegate discard_buffer;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate int discard_bufferDelegate(int fd, bool inputBuffer);

        public void DiscardInBuffer()
        {
            if (discard_buffer(fd, true) != 0)
                ThrowIOException();
        }

        public void DiscardOutBuffer()
        {
            if (discard_buffer(fd, false) != 0)
                ThrowIOException();
        }
        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static get_signalsDelegate get_signals;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate SerialSignal get_signalsDelegate(int fd, out int error);

        public SerialSignal GetSignals()
        {
            int error;
            SerialSignal signals = get_signals(fd, out error);
            if (error == -1)
                ThrowIOException();

            return signals;
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static set_signalDelegate set_signal;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate int set_signalDelegate(int fd, SerialSignal signal, bool value);

        public void SetSignal(SerialSignal signal, bool value)
        {
            if (signal < SerialSignal.Cd || signal > SerialSignal.Rts ||
            signal == SerialSignal.Cd ||
            signal == SerialSignal.Cts ||
            signal == SerialSignal.Dsr)
                throw new Exception("Invalid internal value");

            if (set_signal(fd, signal, value) == -1)
                ThrowIOException();
        }

        //[DllImport("MonoPosixHelper", SetLastError = true)]
        static breakpropDelegate breakprop ;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate int breakpropDelegate(int fd);

        public void SetBreakState(bool value)
        {
            if (value)
                if (breakprop(fd) == -1)
                    ThrowIOException();
        }

        [DllImport("libc")]
        static extern IntPtr strerror(int errnum);

        static void ThrowIOException()
        {
            int errnum = Marshal.GetLastWin32Error();
            string error_message = Marshal.PtrToStringAnsi(strerror(errnum));

            throw new IOException(error_message);
        }
        //[DllImport("MonoPosixHelper")]
        static is_baud_rate_legalDelegate is_baud_rate_legal;
        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError = true)]
        delegate bool is_baud_rate_legalDelegate(int baud_rate);

        private void TryBaudRate(int baudRate)
        {
            if (!is_baud_rate_legal(baudRate))
            {
                // this kind of exception to be compatible with MSDN API
                throw new ArgumentOutOfRangeException("baudRate",
                "Given baud rate is not supported on this platform.");
            }
        }
    }
}

