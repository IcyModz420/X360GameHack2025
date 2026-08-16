// =============================================================================
// XDCKIT.XboxFile.cs - File system helper (xbdm dirlist / mkdir / sendfile / etc.)
// =============================================================================
// Use as `console.File.DirList(@"HDD:\")`, `console.File.SendFile(local, remote)`,
// `console.File.GetFile(remote, local)`, `console.File.Delete(...)`, etc.
//
// Each method speaks the documented xbdm command.  The fast bulk-transfer
// commands (sendfile/getfile) use the binary 203 path: the text command is
// followed by a length-prefixed binary stream over the same socket.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;

    public sealed class XboxFile
    {
        private readonly XboxConsole _console;

        internal XboxFile(XboxConsole console) => _console = console;

        private static string Q(string s) => XboxExtensions.QuoteXbdm(s);

        #region DirList

        /// <summary>
        /// Enumerate the contents of a remote folder (e.g. <c>HDD:\</c>).
        /// Wraps xbdm <c>DIRLIST NAME="%s"</c> (see xbdm.dll HrGetDirList).
        /// Each row: <c>name="..." sizehi=0xX sizelo=0xX createhi=0xX
        /// createlo=0xX changehi=0xX changelo=0xX [directory] [readonly] [hidden]</c>.
        /// </summary>
        public List<XboxDirEntry> DirList(string remotePath)
        {
            var list = new List<XboxDirEntry>();
            if (string.IsNullOrEmpty(remotePath)) return list;

            var resp = _console.SendTextCommand("dirlist name=" + Q(remotePath));
            if ((int)resp.Status != 202) return list;

            foreach (var line in XboxConsole.SplitMultilineBody(resp.Body))
            {
                var entry = new XboxDirEntry
                {
                    Name = XboxConsole.ParseKvLine(line, "name") ?? string.Empty,
                };

                uint sizeHi = XboxConsole.ParseUIntKvHex(line, "sizehi");
                uint sizeLo = XboxConsole.ParseUIntKvHex(line, "sizelo");
                entry.Size = XboxConsole.CombineHiLo(sizeHi, sizeLo);

                uint cthi = XboxConsole.ParseUIntKvHex(line, "createhi");
                uint ctlo = XboxConsole.ParseUIntKvHex(line, "createlo");
                long ct = (long)XboxConsole.CombineHiLo(cthi, ctlo);
                if (ct > 0) try { entry.CreateTime = DateTime.FromFileTime(ct); } catch { /* ignore */ }

                uint chhi = XboxConsole.ParseUIntKvHex(line, "changehi");
                uint chlo = XboxConsole.ParseUIntKvHex(line, "changelo");
                long ch = (long)XboxConsole.CombineHiLo(chhi, chlo);
                if (ch > 0) try { entry.ChangeTime = DateTime.FromFileTime(ch); } catch { /* ignore */ }

                entry.IsDirectory = line.IndexOf("directory", StringComparison.OrdinalIgnoreCase) >= 0;
                list.Add(entry);
            }
            return list;
        }

        #endregion

        #region Create / delete / rename

        /// <summary>DmMakeDir — wraps SDK <c>MKDIR NAME="%s"</c>.</summary>
        public void MakeDirectory(string remotePath)
            => _console.SendTextCommand("mkdir name=" + Q(remotePath));

        /// <summary>
        /// DmDeleteFile — wraps SDK <c>DELETE NAME="%s" [DIR]</c>.  Pass
        /// <paramref name="isDirectory"/>=true to delete an (empty) folder.
        /// </summary>
        public void Delete(string remotePath, bool isDirectory = false)
        {
            string cmd = "delete name=" + Q(remotePath) + (isDirectory ? " dir" : string.Empty);
            _console.SendTextCommand(cmd);
        }

        /// <summary>DmRenameFile — wraps SDK <c>RENAME NAME="%s" NEWNAME="%s"</c>.</summary>
        public void Rename(string oldPath, string newPath)
            => _console.SendTextCommand("rename name=" + Q(oldPath) + " newname=" + Q(newPath));

        /// <summary>
        /// DmFileEof — wraps SDK <c>FILEEOF NAME="%s" SIZE=%lu [CANCREATE | MUSTCREATE]</c>.
        /// Resize / truncate / extend a remote file.
        /// </summary>
        public void SetFileSize(string remotePath, ulong size, bool canCreate = false, bool mustCreate = false)
        {
            string suffix = mustCreate ? " mustcreate"
                          : canCreate  ? " cancreate"
                          : string.Empty;
            _console.SendTextCommand("fileeof name=" + Q(remotePath) + " size=" + size + suffix);
        }

        /// <summary>
        /// Create an empty file at <paramref name="remotePath"/>.  Maps to
        /// <c>fileeof size=0 mustcreate</c> (DmFileEof).
        /// </summary>
        public void CreateEmptyFile(string remotePath)
            => SetFileSize(remotePath, 0, canCreate: false, mustCreate: true);

        #endregion

        #region SendFile / GetFile (bulk binary transfer)

        /// <summary>
        /// Upload a local file to the console.  Throws when xbdm reports any
        /// status other than 200 OK after the binary payload is sent.
        /// </summary>
        public void SendFile(string localPath, string remotePath)
        {
            if (!System.IO.File.Exists(localPath))
                throw new FileNotFoundException("Local file not found.", localPath);

            long total = new FileInfo(localPath).Length;

            using (var fs = System.IO.File.OpenRead(localPath))
            {
                var resp = _console.SendTextCommand("sendfile name=" + Q(remotePath) + " length=" + total);
                if ((int)resp.Status != 204)
                    throw new InvalidOperationException($"sendfile failed: {(int)resp.Status} {resp.StatusMessage}");

                var buf = new byte[XboxClient.FileTransferBufferSize];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    _console.Client.SendRaw(buf, 0, n);
            }

            // xbdm sends a full status reply after the payload (sometimes 202
            // multi-line for verification info, usually a single 200 line).
            // Failures show up here; do NOT swallow them.
            var post = _console.Client.ReceiveStatusResponse();
            if (!post.IsSuccess)
                throw new InvalidOperationException($"sendfile completion failed: {(int)post.Status} {post.StatusMessage}");
        }

        /// <summary>Download a remote file to a local path.</summary>
        public void GetFile(string remotePath, string localPath)
        {
            var resp = _console.SendTextCommand("getfile name=" + Q(remotePath));
            if ((int)resp.Status != 203)
                throw new InvalidOperationException($"getfile failed: {(int)resp.Status} {resp.StatusMessage}");

            // xbdm sends the size big-endian as a 32-bit UNSIGNED integer (PPC byte
            // order).  Reading it as a host-LE signed int blew up at 2 GiB and
            // produced negative remaining-byte counts when the high bit was set.
            var lenBytes = _console.Client.ReadExact(4);
            uint len = XboxExtensions.ReadUInt32BE(lenBytes);

            using (var fs = System.IO.File.Create(localPath))
            {
                var buf = new byte[XboxClient.FileTransferBufferSize];
                long remaining = len;
                while (remaining > 0)
                {
                    int chunk = (int)Math.Min(buf.Length, remaining);
                    _console.Client.ReadExact(buf, 0, chunk);
                    fs.Write(buf, 0, chunk);
                    remaining -= chunk;
                }
            }
        }

        #endregion

        #region Drives (proxy to XboxConsole)

        public List<XboxDrive> GetDrives() => _console.GetDrives();
        public DriveSpaceInfo GetDriveFreeSpace(string driveName) => _console.GetDriveFreeSpace(driveName);

        #endregion

        #region SDK aliases (DmReceiveFileA / DmReceiveFileW / DmTransmitFile / DmSendVolumeFile)

        /// <summary>DmReceiveFileA / DmReceiveFileW alias for <see cref="GetFile"/>.</summary>
        public void ReceiveFile(string remotePath, string localPath) => GetFile(remotePath, localPath);

        /// <summary>
        /// DmTransmitFile alias.  In genuine xbdm.dll this took raw socket
        /// handles for high-throughput uploads; here it routes back to the
        /// regular <see cref="SendFile"/> path which is already streaming.
        /// </summary>
        public void TransmitFile(string localPath, string remotePath) => SendFile(localPath, remotePath);

        /// <summary>
        /// DmSendVolumeFile - wraps SDK <c>SENDVFILE COUNT=%d</c> followed by
        /// per-file <c>FILE NAME="..." SIZE=N</c> headers and the binary
        /// payloads.  Used for atomic batch uploads (e.g. an XEX + the
        /// adjacent .pdb) to a write-locked volume.
        /// </summary>
        /// <param name="files">
        /// (localPath, remotePath) pairs.  All local files must exist.
        /// </param>
        public void SendVolumeFile(IEnumerable<(string LocalPath, string RemotePath)> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var arr = new List<(string Local, string Remote, long Size)>();
            foreach (var (local, remote) in files)
            {
                if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(remote))
                    throw new ArgumentException("Each entry needs a local and remote path.");
                if (!System.IO.File.Exists(local))
                    throw new FileNotFoundException("Local file not found.", local);
                arr.Add((local, remote, new FileInfo(local).Length));
            }
            if (arr.Count == 0) return;

            var resp = _console.SendTextCommand($"SENDVFILE COUNT={arr.Count}");
            if ((int)resp.Status != 204 && (int)resp.Status != 202 && (int)resp.Status != 200)
                throw new InvalidOperationException($"sendvfile failed: {(int)resp.Status} {resp.StatusMessage}");

            foreach (var entry in arr)
            {
                _console.Client.SendRawAscii("FILE NAME=" + Q(entry.Remote) + " SIZE=" + entry.Size + "\r\n");
                using (var fs = System.IO.File.OpenRead(entry.Local))
                {
                    var buf = new byte[XboxClient.FileTransferBufferSize];
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                        _console.Client.SendRaw(buf, 0, n);
                }
            }

            // Read the trailing status; bubble up failures.
            var post = _console.Client.ReceiveStatusResponse();
            if (!post.IsSuccess)
                throw new InvalidOperationException($"sendvfile completion failed: {(int)post.Status} {post.StatusMessage}");
        }

        #endregion
    }
