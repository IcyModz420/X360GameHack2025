using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace X360GameHack
{
    internal class FTPClient
    {
        public string UserName;
        public string Password;
        public string IP;
        public string Port;
        public string SendCurDirToGS;
        public string FTPIP = "";
        public string FilePathToFileToUpload = "";

        public void UploadFile(string filePath, string FTPDirectory, string IP, string Port, string FTPUserName, string FTPPassword)
        {
            try
            {
                // Validate file existence
                if (!File.Exists(filePath))
                {
                    MessageBox.Show("File not found: " + filePath);
                    return;
                }

                // Build the FTP URL including the directory
                string url = "ftp://" + IP + ":" + Port + "/" + FTPDirectory + Path.GetFileName(filePath);

                // Create FtpWebRequest for upload
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.Credentials = new NetworkCredential(FTPUserName,
             FTPPassword);
                request.UseBinary
             = true; // Set for binary file transfer

                // Open the file stream for reading
                using (FileStream fileStream = File.OpenRead(filePath))
                {
                    // Get the request stream for writing to the server
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        // Copy file contents to the request stream
                        fileStream.CopyTo(requestStream);
                    }
                }

                // Get the response from the server (optional for checking success)
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    MessageBox.Show("Upload successful! Status: {0}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error sending file: {0}", ex.Message);
            }
        }

        public void DownloadFile(string fileName, string FTPDirectory, string IP, string Port, string FTPUserName, string FTPPassword)
        {
            try
            {
                // Build the local file path (adjust as needed)
                string localFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

                // Build the FTP URL including the directory and filename
                string url = "ftp://" + IP + ":" + Port + "/" + FTPDirectory + "/" + fileName;

                // Create FtpWebRequest for download
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
                request.Method = WebRequestMethods.Ftp.DownloadFile;
                request.Credentials = new NetworkCredential(FTPUserName, FTPPassword);
                request.UseBinary = true; // Set for binary file transfer

                // Open a file stream for writing the downloaded file
                using (FileStream fileStream = File.Create(localFilePath))
                {
                    // Get the response stream from the server
                    using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                    {
                        // Get the response stream for reading the downloaded file
                        using (Stream responseStream = response.GetResponseStream())
                        {
                            // Copy the file contents from the server to the local file
                            responseStream.CopyTo(fileStream);
                            MessageBox.Show("File downloaded to " + fileStream, "File Downloaded!");
                        }
                    }
                }

                // Show success message
                MessageBox.Show("Download successful! File saved to: " + localFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error downloading file: {0}", ex.Message);
            }
        }

        public void DeleteFile(string fileName, string FTPDirectory, string IP, string Port, string FTPUserName, string FTPPassword)
        {
            try
            {
                // Build the FTP URL including the directory and filename
                string url = "ftp://" + IP + ":" + Port + "/" + FTPDirectory + "/" + fileName;

                // Create FtpWebRequest for deletion
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
                request.Method = WebRequestMethods.Ftp.DeleteFile;
                request.Credentials = new NetworkCredential(FTPUserName,
             FTPPassword);

                // Get the response from the server (optional for checking success)
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    // Check the response status code
                    if (response.StatusCode == FtpStatusCode.FileActionOK)
                    {
                        MessageBox.Show("File deleted successfully!");
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete file. Status code: " + response.StatusCode);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error deleting file: " + ex.Message);
            }
        }

        public void RenameFile(string oldFileName, string newFileName, string currentftpdirectory, string ip, string Port, string ftpUsername, string ftpPassword)
        {
            try
            {
                // Build the FTP URL for the old and new file names
                string oldFilePath = "ftp://" + ip + ":" + Port + "/" + currentftpdirectory + "/" + oldFileName;
                string newFilePath = "ftp://" + ip + ":" + Port + "/" + currentftpdirectory + "/" + newFileName;

                // Create FtpWebRequest for renaming
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(oldFilePath);
                request.Method = WebRequestMethods.Ftp.Rename;
                request.Credentials = new NetworkCredential(ftpUsername, ftpPassword);

                // Send the rename command
                request.GetResponse();

                // Update the file path to the new name
                oldFilePath = newFilePath;

                // Show success message
                MessageBox.Show("File renamed successfully on FTP server.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error renaming file on FTP server: " + ex.Message);
            }
        }

        public long GetFileSize(string filename, string currentftpdirectory, string ip, int ftpPort, string ftpUsername, string ftpPassword)
        {
            try
            {
                // Build the FTP URL for the file
                string url = "ftp://" + ip + ":" + ftpPort + "/" + currentftpdirectory + "/" + filename;

                // Create FtpWebRequest for retrieving file information
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
                request.Method = WebRequestMethods.Ftp.GetFileSize;
                request.Credentials = new NetworkCredential(ftpUsername, ftpPassword);

                // Get the response and extract the file size
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    MessageBox.Show(filename + " is " + response.ContentLength.ToString(), "File Size Grabbed!");
                    return response.ContentLength;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error getting file size: " + ex.Message);
                return -1; // Indicate an error
            }
        }

        public void CreateFtpDirectory(string currentftpdirectory, string directorytomakePath, string ip, string ftpPort, string ftpUsername, string ftpPassword)
        {
            try
            {
                // Build the FTP URL for the directory
                string url = "ftp://" + ip + ":" + ftpPort + "/" + currentftpdirectory + "/" + directorytomakePath;

                // Create FtpWebRequest for creating directory
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
                request.Method = WebRequestMethods.Ftp.MakeDirectory;
                request.Credentials = new NetworkCredential(ftpUsername, ftpPassword);

                // Send the request and get the response
                FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                response.Close();

                MessageBox.Show("Directory created successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error creating directory: " + ex.Message);
            }
        }

        public void RefreshFTPListBox(ListBox listBox, string ftpip, int ftpPort, string ftpUsername, string ftpPassword)
        {
            
        }
    }
}
