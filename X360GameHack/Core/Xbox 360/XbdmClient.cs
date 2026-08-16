using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;

namespace X360GameHack.Core.Xbox360
{
    public class XbdmConnection : IDisposable
    {
        private const int XbdmPort = 730;
        private TcpClient _tcpClient;
        private NetworkStream _stream;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

        #region Connection Management

        public void Connect(string ipAddress)
        {
            if (IsConnected) Disconnect();

            try
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"Attempting connection to {ipAddress}:{XbdmPort}...");

                _tcpClient = new TcpClient { NoDelay = true, ReceiveTimeout = 5000, SendTimeout = 5000 };
                _tcpClient.Connect(ipAddress, XbdmPort);
                _stream = _tcpClient.GetStream();

                string banner = ReadResponseLine();
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"Successfully connected. Console banner: {banner}");
            }
            catch (Exception ex)
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"Connection failed to {ipAddress}: {ex.Message}");
                Disconnect();
                throw;
            }
        }

        public void Disconnect()
        {
            if (!IsConnected) return;

            X360GameHack.CurrentInstance.UpdateListboxForOutput("Disconnecting from console.");
            _stream?.Dispose();
            _tcpClient?.Close();
            _stream = null;
            _tcpClient = null;
        }

        public void Dispose()
        {
            Disconnect();
        }

        #endregion

        #region Base Communication

        /// <summary>
        /// Reads a single ASCII line from the stream byte-by-byte to prevent 
        /// buffering over binary data (critical for screenshots and files).
        /// </summary>
        private string ReadResponseLine()
        {
            List<byte> buffer = new List<byte>();
            int b;
            while ((b = _stream.ReadByte()) != -1)
            {
                if (b == '\n') break;
                if (b != '\r') buffer.Add((byte)b);
            }
            return Encoding.ASCII.GetString(buffer.ToArray());
        }

        /// <summary>
        /// Sends a raw text command to the XBDM server.
        /// </summary>
        public string SendCommand(string command)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected.");

            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Sending command: {command}");

            byte[] cmdBytes = Encoding.ASCII.GetBytes(command + "\r\n");
            _stream.Write(cmdBytes, 0, cmdBytes.Length);
            _stream.Flush();

            string response = ReadResponseLine();
            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Console response: {response}");

            return response;
        }

        #endregion

        #region Console Commands (DVD, Notify, Power)

        public void SendXNotify(string message)
        {
            // Note: Native XBDM requires specific plugins (like JRPC or custom DashLaunch plugins) 
            // to intercept and display XNotifies via TCP. 
            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Sending XNotify message: {message}");
            SendCommand($"notify=\"{message}\"");
        }

        public void DvdTray(bool open)
        {
            string state = open ? "open" : "close";
            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Sending DVD tray command: {state}");
            SendCommand($"dvdtray {state}");
        }

        public void Reboot(bool cold = false)
        {
            string bootType = cold ? "cold" : "warm";
            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Issuing {bootType} reboot command.");
            try
            {
                SendCommand($"magicboot {bootType}");
            }
            catch (IOException)
            {
                // Expected behavior: The console drops the TCP connection instantly upon reboot.
                X360GameHack.CurrentInstance.UpdateListboxForOutput("Connection dropped due to reboot (Expected).");
                Disconnect();
            }
        }

        #endregion

        #region File Transfer (Push/Pull)

        public void PushFile(string localFilePath, string remoteDestinationPath)
        {
            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Starting file push: {localFilePath} -> {remoteDestinationPath}");

            if (!File.Exists(localFilePath))
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"Push failed: Local file not found at {localFilePath}");
                throw new FileNotFoundException("Local file does not exist.", localFilePath);
            }

            FileInfo fileInfo = new FileInfo(localFilePath);
            string response = SendCommand($"sendfile name=\"{remoteDestinationPath}\" length=0x{fileInfo.Length:X}");

            if (response.StartsWith("20") || response.Contains("ready"))
            {
                using (FileStream fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[8192]; // 8KB buffer
                    int bytesRead;
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        _stream.Write(buffer, 0, bytesRead);
                    }
                }
                X360GameHack.CurrentInstance.UpdateListboxForOutput("File push completed successfully.");
            }
            else
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"File push rejected by console: {response}");
            }
        }

        public void PullFile(string remoteFilePath, string localDestinationPath)
        {
            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Starting file pull: {remoteFilePath} -> {localDestinationPath}");

            // XBDM file pulls often respond with the file size in hex
            string response = SendCommand($"getfile name=\"{remoteFilePath}\"");

            if (response.StartsWith("203") || response.Contains("binary"))
            {
                // Attempt to parse length if the console provided it in the response (e.g., "203- binary length=0x1A4")
                long expectedLength = -1;
                if (response.Contains("length=0x"))
                {
                    string hexStr = response.Substring(response.IndexOf("length=0x") + 9).Trim();
                    expectedLength = Convert.ToInt64(hexStr, 16);
                }

                using (FileStream fs = new FileStream(localDestinationPath, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    long totalRead = 0;

                    // Read until expected length is met, or the stream pauses (if length is unknown)
                    while ((expectedLength == -1 || totalRead < expectedLength) && _stream.DataAvailable)
                    {
                        bytesRead = _stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        fs.Write(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                    }
                }
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"File pull completed. Saved to {localDestinationPath}");
            }
            else
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"File pull rejected by console: {response}");
            }
        }

        #endregion

        #region Screenshot

        public void TakeScreenshot(string savePath)
        {
            X360GameHack.CurrentInstance.UpdateListboxForOutput($"Requesting console screenshot. Saving to: {savePath}");

            string response = SendCommand("screenshot");

            // XBDM typically replies with "203- binary" before dumping the BMP frame buffer
            if (response.StartsWith("203") || response.Contains("binary"))
            {
                using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                {
                    // A standard BMP header is 54 bytes. We read it to find the exact file size.
                    byte[] bmpHeader = new byte[54];
                    int headerRead = _stream.Read(bmpHeader, 0, 54);

                    if (headerRead == 54 && bmpHeader[0] == 'B' && bmpHeader[1] == 'M')
                    {
                        fs.Write(bmpHeader, 0, 54);

                        // Parse total file size from bytes 2-5 of the BMP header (Little Endian)
                        int fileSize = BitConverter.ToInt32(bmpHeader, 2);
                        int remainingBytes = fileSize - 54;

                        byte[] buffer = new byte[8192];
                        while (remainingBytes > 0)
                        {
                            int read = _stream.Read(buffer, 0, Math.Min(buffer.Length, remainingBytes));
                            if (read == 0) break;
                            fs.Write(buffer, 0, read);
                            remainingBytes -= read;
                        }
                        X360GameHack.CurrentInstance.UpdateListboxForOutput("Screenshot captured and saved successfully.");
                    }
                    else
                    {
                        X360GameHack.CurrentInstance.UpdateListboxForOutput("Screenshot failed: Invalid BMP header received.");
                    }
                }
            }
            else
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput($"Screenshot command rejected: {response}");
            }
        }

        #endregion
    }
}