#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Renci.SshNet;
using Amazon.S3;
using Amazon.S3.Model;
using System.Collections.Generic;

namespace Concept.SmartTools.Editor
{

    /// <summary>
    /// SmartUpload handles file uploads to different targets (SFTP, AWS S3, etc.).
    /// It provides progress and status callbacks for integration with custom editor windows or runtime tools.
    /// </summary>
    public class SmartUploader
    {
        public enum UploadTarget { SFTP, AWSS3 }
        private UploadTarget m_uploadTarget;

        private string m_host;
        private int m_port;
        private string m_user;
        private string m_password;
        private IAmazonS3 m_s3Client;
        private string m_bucketName;

        /// <summary>
        /// Fired when the progress of the upload changes (0..1).
        /// </summary>
        public event Action<string> OnStatusChanged;
        /// <summary>
        /// Fired when the status message changes.
        /// </summary>
        public event Action<float> OnProgressChanged; // 0..1

        /// <summary>
        /// Creates a new SFTP SmartUpload instance with required configuration.
        /// </summary>
        /// <param name="host">Host or endpoint address.</param>
        /// <param name="port">Port (default 22 for SFTP).</param>
        /// <param name="user">Username for authentication.</param>
        /// <param name="password">Password for authentication.</param>
        public SmartUploader(string host, int port, string user, string password)
        {
            m_host = host;
            m_port = port;
            m_user = user;
            m_password = password;
            m_uploadTarget = UploadTarget.SFTP;
        }

        /// <summary>
        /// Creates a new AWSS3 SmartUpload instance with required configuration.
        /// </summary>
        /// <param name="amazonS3"></param>
        /// <param name="bucketName"></param>
        public SmartUploader(IAmazonS3 amazonS3, string bucketName)
        {
            m_bucketName = bucketName;
            m_s3Client = amazonS3;
            m_uploadTarget = UploadTarget.AWSS3;
        }

        /// <summary>
        /// Uploads multiple files asynchronously to the configured target.
        /// </summary>
        /// <param name="localPaths">Array of local file paths.</param>
        /// <param name="remoteDir">Remote directory path.</param>
        public async Task UploadFilesAsync(List<(string localPath, string remotePath)> files, string remoteDir, bool cleanUpCache)
        {
            int total = files.Count;

            if (m_uploadTarget == UploadTarget.SFTP)
            {
                using var client = new SftpClient(m_host, m_port, m_user, m_password);
                try
                {
                    client.Connect();
                    if (!client.IsConnected)
                    {
                        OnStatusChanged?.Invoke("[SmartUpload] Could not connect to SFTP server.");
                        return;
                    }

                    if (!client.Exists(remoteDir))
                        client.CreateDirectory(remoteDir);

                    if(cleanUpCache) await DeleteAsync(client, remoteDir);
                }
                finally
                {
                    client.Disconnect();
                }
            }

            for (int i = 0; i < total; i++)
            {
                var (localPath, remotePath) = files[i];
                OnStatusChanged?.Invoke($"[SmartUpload] Uploading {remotePath} ({i + 1}/{total})");

                if (m_uploadTarget == UploadTarget.SFTP)
                    await UploadSFTPAsync(localPath, Path.Combine(remoteDir, remotePath).Replace("\\", "/"));
                else if (m_uploadTarget == UploadTarget.AWSS3)
                    await UploadAWSAsync(localPath, Path.Combine(remoteDir, remotePath).Replace("\\", "/"));

                OnProgressChanged?.Invoke((i + 1f) / total);
            }

            OnStatusChanged?.Invoke("[SmartUpload] Upload finished!");
        }


        /// <summary>
        /// Uploads a single file via SFTP.
        /// </summary>
        /// <param name="localFilePath">Local path of the file to upload.</param>
        /// <param name="remoteFilePath">Remote directory path.</param>
        private async Task UploadSFTPAsync(string localFilePath, string remoteFilePath)
        {
            using var client = new SftpClient(m_host, m_port, m_user, m_password);

            try
            {
                client.Connect();
                if (!client.IsConnected)
                {
                    OnStatusChanged?.Invoke("[SmartUpload] Could not connect to SFTP server.");
                    return;
                }

                OnStatusChanged?.Invoke("[SmartUpload] Connected to SFTP server.");
                await SendFileAsync(client, localFilePath, remoteFilePath);
                OnStatusChanged?.Invoke("[SmartUpload] SFTP upload completed.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SmartUpload] Error in UploadSFTPAsync file '{localFilePath}': {ex.Message}");
            }
            finally
            {
                client.Disconnect();
            }
        }

        /// <summary>
        /// Deletes all files and subdirectories recursively inside a remote directory (SFTP).
        /// </summary>
        /// <param name="client">Connected SFTP client.</param>
        /// <param name="remoteDir">Remote directory path.</param>
        private async Task DeleteAsync(SftpClient client, string remoteDir)
        {
            await Task.Run(() =>
            {
                foreach (var file in client.ListDirectory(remoteDir))
                {
                    if (file.Name == "." || file.Name == "..") continue;

                    if (file.IsDirectory)
                    {
                        DeleteAsync(client, remoteDir + "/" + file.Name).GetAwaiter().GetResult();
                        client.DeleteDirectory(remoteDir + "/" + file.Name);
                    }
                    else
                    {
                        client.DeleteFile(remoteDir + "/" + file.Name);
                    }
                }
            });
        }

        /// <summary>
        /// Sends a single file with upload progress tracking via SFTP.
        /// </summary>
        /// <param name="client">Connected SFTP client.</param>
        /// <param name="localFilePath">Path of the local file.</param>
        /// <param name="remoteFilePath">Destination directory on the server.</param>
        private Task SendFileAsync(SftpClient client, string localFilePath, string remoteFilePath)
        {
            if (!File.Exists(localFilePath))
                throw new FileNotFoundException($"File not found: {localFilePath}");

            remoteFilePath = remoteFilePath.Replace("\\", "/");

            return Task.Run(() =>
            {
                // Cria subpastas se n�o existirem
                string dir = Path.GetDirectoryName(remoteFilePath).Replace("\\", "/");
                if (!string.IsNullOrEmpty(dir))
                {
                    string[] folders = dir.Split('/');
                    string current = "";
                    foreach (var folder in folders)
                    {
                        if (string.IsNullOrEmpty(folder)) continue;
                        current += folder +"/";
                        if (!client.Exists(current))
                            client.CreateDirectory(current);
                    }
                }

                using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
                client.UploadFile(fs, remoteFilePath, uploadedBytes =>
                {
                    float progress = (float)uploadedBytes / fs.Length;
                    OnProgressChanged?.Invoke(progress);
                });
            });
        }








        /// <summary>
        /// Uploads a single file to AWS S3.
        /// </summary>
        /// <param name="localFilePath">Local path of the file to upload.</param>
        /// <param name="remoteFilePath">Remote directory or S3 bucket path.</param>
        private async Task UploadAWSAsync(string localFilePath, string remoteFilePath)
        {
            if (m_s3Client == null || string.IsNullOrEmpty(m_bucketName))
            {
                Debug.LogError("[SmartUpload] AWS S3 client or bucket not configured.");
                return;
            }

            try
            {
                using FileStream fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);

                var request = new PutObjectRequest
                {
                    BucketName = m_bucketName,
                    Key = remoteFilePath.Replace("\\", "/"), // garante o separador correto
                    InputStream = fs,
                    ContentType = GetContentType(Path.GetFileName(localFilePath))
                };

                if (localFilePath.EndsWith(".br"))
                    request.Headers["Content-Encoding"] = "br";

                await m_s3Client.PutObjectAsync(request);
                OnStatusChanged?.Invoke($"[SmartUpload] AWS upload completed: {remoteFilePath}");
            }
            catch (AmazonS3Exception e)
            {
                Debug.LogError($"[SmartUpload] AWS S3 upload error {localFilePath}: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SmartUpload] Unknown error uploading {localFilePath}: {e.Message}");
            }
        }


        /// <summary>
        /// Resolves the correct MIME Content-Type for a given file extension.
        /// </summary>
        /// <param name="fileName">File name to check.</param>
        /// <returns>Content-Type string.</returns>
        private static string GetContentType(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLower();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".json" => "application/json",
                ".txt" => "text/plain",
                ".js" => "application/javascript",
                ".html" => "text/html",
                ".css" => "text/css",
                ".br" => "application/octet-stream",
                _ => "application/octet-stream",
            };
        }
    }
}
#endif
