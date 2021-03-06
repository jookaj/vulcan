﻿namespace TcbInternetSolutions.Vulcan.Core.Implementation
{
    using EPiServer.Core;
    using EPiServer.ServiceLocation;
    using System;

    /// <summary>
    /// Converts media data to byte array
    /// </summary>
    [ServiceConfiguration(typeof(IVulcanMediaReader), Lifecycle = ServiceInstanceScope.Singleton)]
    public class VulcanMediaReader : IVulcanMediaReader
    {
        /// <summary>
        /// Reads complete media data to byte array
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        public virtual byte[] ReadToEnd(MediaData media)
        {
            if (media?.BinaryData == null) return null;
            byte[] bytes;

            using (var s = media.BinaryData.OpenRead())
            {
                bytes = ReadToEnd(s);
            }

            return bytes;
        }

        /// <summary>
        /// Reads full stream to byte array
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        protected static byte[] ReadToEnd(System.IO.Stream stream)
        {
            long originalPosition = 0;

            if (stream.CanSeek)
            {
                originalPosition = stream.Position;
                stream.Position = 0;
            }

            try
            {
                var readBuffer = new byte[4096];
                var totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = stream.Read(readBuffer, totalBytesRead, readBuffer.Length - totalBytesRead)) > 0)
                {
                    totalBytesRead += bytesRead;

                    if (totalBytesRead != readBuffer.Length) continue;

                    var nextByte = stream.ReadByte();
                    if (nextByte == -1) continue;


                    var temp = new byte[readBuffer.Length * 2];
                    Buffer.BlockCopy(readBuffer, 0, temp, 0, readBuffer.Length);
                    Buffer.SetByte(temp, totalBytesRead, (byte)nextByte);
                    readBuffer = temp;
                    totalBytesRead++;
                }

                var buffer = readBuffer;

                if (readBuffer.Length == totalBytesRead) return buffer;

                buffer = new byte[totalBytesRead];
                Buffer.BlockCopy(readBuffer, 0, buffer, 0, totalBytesRead);

                return buffer;
            }
            finally
            {
                if (stream.CanSeek)
                {
                    stream.Position = originalPosition;
                }
            }
        }
    }
}
