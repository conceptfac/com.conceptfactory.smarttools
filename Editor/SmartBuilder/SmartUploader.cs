#if UNITY_EDITOR
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Renci.SshNet; // Added namespace for SftpClient
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace Concept.SmartTools.Editor
{

    /// <summary>
    /// SmartUpload handles file uploads to different targets (SFTP, AWS S3, etc.).
    /// It provides progress and status callbacks for integration with custom editor windows or runtime tools.
    /// </summary>
    public class SmartUploader
    {
        private static readonly Regex RemoteDirectorySegmentRegex = new Regex(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);
        private const float PreparationProgressWeight = 0.15f;
        public static SmartUploaderSettings Settings => SmartBuilderConfig.instance?.m_uploadSettings;


        public enum UploadTarget { SFTP, AWSS3 }
        private UploadTarget m_uploadTarget;

        private string m_host;
        private int m_port;
        private string m_user;
        private string m_password;
        private string m_privateKeyPath;
        private string m_privateKeyPassphrase;
        private bool m_usePrivateKey;
        private IAmazonS3 m_s3Client;
        private string m_bucketName;
        private CancellationTokenSource m_cancellationTokenSource;
        private SftpClient m_activeSftpClient;

        /// <summary>
        /// Fired when the progress of the upload changes (0..1).
        /// </summary>
        public event Action<string> OnStatusChanged;
        /// <summary>
        /// Fired when the status message changes.
        /// </summary>
        public event Action<float> OnProgressChanged; // 0..1
        public event Action<float> OnStepProgressChanged; // 0..1
        public bool IsCancellationRequested => m_cancellationTokenSource != null && m_cancellationTokenSource.IsCancellationRequested;

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

        public SmartUploader(string host, int port, string user, string privateKeyPath, string privateKeyPassphrase, bool usePrivateKey)
        {
            m_host = host;
            m_port = port;
            m_user = user;
            m_privateKeyPath = privateKeyPath;
            m_privateKeyPassphrase = privateKeyPassphrase;
            m_usePrivateKey = usePrivateKey;
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
        public SmartUploader(string accessKey, string secretKey, string bucketName, RegionEndpoint region = default)
        {

            if (region == null || region == default) region = RegionEndpoint.USEast1;
                region = RegionEndpoint.USEast1;
            m_bucketName = bucketName;

            var config = new AmazonS3Config
            {
                RegionEndpoint = region
            };

            m_s3Client = new AmazonS3Client(accessKey, secretKey, config);

            m_uploadTarget = UploadTarget.AWSS3;
        }


        public async Task DeleteS3FolderAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
        {
            // Normaliza para garantir que termina com '/'
            if (!prefix.EndsWith("/"))
                prefix += "/";

            var request = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix
            };


            ListObjectsV2Response response;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                response = await m_s3Client.ListObjectsV2Async(request, cancellationToken);

                if (response.S3Objects.Count > 0)
                {
                    var deleteRequest = new DeleteObjectsRequest
                    {
                        BucketName = bucketName,
                        Objects = response.S3Objects
                            .Select(o => new KeyVersion { Key = o.Key })
                            .ToList()
                    };

                    await m_s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken);
                }

                request.ContinuationToken = response.NextContinuationToken;

            } while (response.IsTruncated);
        }



        /// <summary>
        /// Uploads multiple files asynchronously to the configured target.
        /// </summary>
        /// <param name="localPaths">Array of local file paths.</param>
        /// <param name="remoteDir">Remote directory path.</param>
        public async Task UploadFilesAsync(string rootPath, string remoteDir, bool cleanUpCache = false)
        {
            await UploadFilesAsync(GetAllFilesRecursively(rootPath), remoteDir, cleanUpCache);
        }

        public async Task UploadFilesAsync(List<(string localPath, string remotePath)> files, string remoteDir, bool cleanUpCache)
        {
            BeginCancellationScope();
            CancellationToken cancellationToken = m_cancellationTokenSource.Token;
            remoteDir = NormalizeRemotePath(remoteDir, treatEmptyAsRoot: true);
            int totalFiles = files.Count;
            ReportStatus("[SmartUpload] Preparing upload...", 0f);
            ReportStepProgress(0f);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_uploadTarget == UploadTarget.AWSS3 && cleanUpCache)
                {
                    ReportStatus("[SmartUpload] Cleaning remote bucket path...", 0.03f);
                    ReportStepProgress(0f);
                    await DeleteS3FolderAsync(m_bucketName, remoteDir, cancellationToken);
                    ReportStepProgress(1f);
                }

                if (m_uploadTarget == UploadTarget.SFTP)
                {
                    await Task.Run(async () =>
                    {
                        using var client = CreateSftpClient();
                        SetActiveSftpClient(client);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            ReportStatus("[SmartUpload] Connecting to SFTP server...", 0.03f);
                            ReportStepProgress(0f);
                            client.Connect();
                            cancellationToken.ThrowIfCancellationRequested();
                            ReportStepProgress(1f);
                            if (!client.IsConnected)
                            {
                                OnStatusChanged?.Invoke("[SmartUpload] Could not connect to SFTP server.");
                                return;
                            }

                            ReportStatus("[SmartUpload] Connected. Validating remote directory...", 0.07f);
                            EnsureRemoteDirectoryExists(client, remoteDir, cancellationToken);
                            ReportStepProgress(1f);

                            if (cleanUpCache)
                            {
                                ReportStatus("[SmartUpload] Cleaning remote directory...", 0.11f);
                                ReportStepProgress(0f);
                                await DeleteAsync(client, remoteDir, cancellationToken);
                                ReportStepProgress(1f);
                            }
                        }
                        finally
                        {
                            ClearActiveSftpClient(client);
                            client.Disconnect();
                        }
                    }, cancellationToken);
                }
                else
                {
                    ReportStatus("[SmartUpload] Preparing remote destination...", cleanUpCache ? 0.12f : 0.08f);
                    ReportStepProgress(1f);
                }
            }
            catch (SocketException ex)
            {
                ReportStatus($"[SmartUpload] Connection failed: {ex.Message}", 0f);
                EndCancellationScope();
                throw new InvalidOperationException($"Could not connect to '{m_host}:{m_port}'. Check the host, port, firewall, and network access.", ex);
            }
            catch (OperationCanceledException)
            {
                ReportStatus("[SmartUpload] Upload cancelled.", 0f);
                Debug.Log("[SmartUpload] Upload cancelled.");
                EndCancellationScope();
                throw;
            }
            catch (Exception ex)
            {
                if (IsCancellationRequested)
                {
                    ReportStatus("[SmartUpload] Upload cancelled.", 0f);
                    Debug.Log("[SmartUpload] Upload cancelled.");
                    EndCancellationScope();
                    throw new OperationCanceledException("Upload cancelled.", ex, cancellationToken);
                }

                ReportStatus($"[SmartUpload] Upload preparation failed: {ex.Message}", 0f);
                EndCancellationScope();
                throw;
            }

            if (totalFiles == 0)
            {
                ReportStatus("[SmartUpload] No files found to upload.", 1f);
                EndCancellationScope();
                return;
            }

            for (int i = 0; i < totalFiles; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (localPath, remotePath) = files[i];

                ReportStatus($"[SmartUpload] Uploading file {i + 1}/{totalFiles}: {remotePath}", CalculateFileStageProgress(i, totalFiles));
                ReportStepProgress(0f);

                if (m_uploadTarget == UploadTarget.SFTP)
                    await UploadSFTPAsync(localPath, CombineRemotePath(remoteDir, remotePath), cancellationToken);
                else if (m_uploadTarget == UploadTarget.AWSS3)
                    await UploadAWSAsync(localPath, CombineRemotePath(remoteDir, remotePath), cancellationToken);

                ReportStepProgress(1f);
                // Progresso de 0 a 1 baseado na quantidade de arquivos
                float progress = CalculateFileStageProgress(i + 1, totalFiles);
                OnProgressChanged?.Invoke(progress);
            }

            ReportStatus("[SmartUpload] Upload finished!", 1f);
            Debug.Log("[SmartUpload] Upload finished!");
            EndCancellationScope();
        }


        public async Task UploadFilesAsyncGetBytes(List<(string localPath, string remotePath)> files, string remoteDir, bool cleanUpCache)
        {
            BeginCancellationScope();
            CancellationToken cancellationToken = m_cancellationTokenSource.Token;
            remoteDir = NormalizeRemotePath(remoteDir, treatEmptyAsRoot: true);
            int totalFiles = files.Count;
            ReportStatus("[SmartUpload] Preparing upload...", 0f);
            byte[][] allFilesBytes = await GetAllFilesBytesAsync(files);

            // Soma total de bytes de todos os arquivos
            long totalBytes = allFilesBytes.Sum(b => (long)b.Length);
            long bytesUploaded = 0;

            try
            {
                if (m_uploadTarget == UploadTarget.SFTP)
                {
                    await Task.Run(async () =>
                    {
                        using var client = CreateSftpClient();
                        SetActiveSftpClient(client);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            ReportStatus("[SmartUpload] Connecting to SFTP server...", 0.03f);
                            client.Connect();
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!client.IsConnected)
                            {
                                OnStatusChanged?.Invoke("[SmartUpload] Could not connect to SFTP server.");
                                return;
                            }

                            ReportStatus("[SmartUpload] Connected. Validating remote directory...", 0.07f);
                            EnsureRemoteDirectoryExists(client, remoteDir, cancellationToken);

                            if (cleanUpCache)
                            {
                                ReportStatus("[SmartUpload] Cleaning remote directory...", 0.11f);
                                await DeleteAsync(client, remoteDir, cancellationToken);
                            }
                        }
                        finally
                        {
                            ClearActiveSftpClient(client);
                            client.Disconnect();
                        }
                    }, cancellationToken);
                }
                else
                {
                    if (cleanUpCache)
                    {
                        ReportStatus("[SmartUpload] Cleaning remote bucket path...", 0.03f);
                        await DeleteS3FolderAsync(m_bucketName, remoteDir, cancellationToken);
                    }

                    ReportStatus("[SmartUpload] Preparing remote destination...", cleanUpCache ? 0.12f : 0.08f);

                    if (totalFiles == 0)
                    {
                        ReportStatus("[SmartUpload] No files found to upload.", 1f);
                        EndCancellationScope();
                        return;
                    }

                    for (int i = 0; i < totalFiles; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var (localPath, remotePath) = files[i];
                        byte[] fileBytes = allFilesBytes[i];

                        ReportStatus($"[SmartUpload] Uploading file {i + 1}/{totalFiles}: {remotePath}", CalculateByteStageProgress(bytesUploaded, totalBytes));

                        if (m_uploadTarget == UploadTarget.SFTP)
                            await UploadSFTPAsync(localPath, CombineRemotePath(remoteDir, remotePath), cancellationToken);
                        else if (m_uploadTarget == UploadTarget.AWSS3)
                            await UploadAWSAsync(localPath, CombineRemotePath(remoteDir, remotePath), cancellationToken);

                        bytesUploaded += fileBytes.Length;
                        float progress = CalculateByteStageProgress(bytesUploaded, totalBytes);
                        OnProgressChanged?.Invoke(progress);
                    }
                }
            }
            catch (SocketException ex)
            {
                ReportStatus($"[SmartUpload] Connection failed: {ex.Message}", 0f);
                EndCancellationScope();
                throw new InvalidOperationException($"Could not connect to '{m_host}:{m_port}'. Check the host, port, firewall, and network access.", ex);
            }
            catch (OperationCanceledException)
            {
                ReportStatus("[SmartUpload] Upload cancelled.", 0f);
                Debug.Log("[SmartUpload] Upload cancelled.");
                EndCancellationScope();
                throw;
            }
            catch (Exception ex)
            {
                if (IsCancellationRequested)
                {
                    ReportStatus("[SmartUpload] Upload cancelled.", 0f);
                    Debug.Log("[SmartUpload] Upload cancelled.");
                    EndCancellationScope();
                    throw new OperationCanceledException("Upload cancelled.", ex, cancellationToken);
                }

                ReportStatus($"[SmartUpload] Upload preparation failed: {ex.Message}", 0f);
                EndCancellationScope();
                throw;
            }

            ReportStatus("[SmartUpload] Upload finished!", 1f);
            Debug.Log("[SmartUpload] Upload finished!");
            EndCancellationScope();
        }



        /// <summary>
        /// Uploads a single file via SFTP.
        /// </summary>
        /// <param name="localFilePath">Local path of the file to upload.</param>
        /// <param name="remoteFilePath">Remote directory path.</param>
        private async Task UploadSFTPAsync(string localFilePath, string remoteFilePath, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Run(async () =>
                {
                    using var client = CreateSftpClient();
                    SetActiveSftpClient(client);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        OnStatusChanged?.Invoke("[SmartUpload] Connecting to SFTP server...");
                        client.Connect();
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!client.IsConnected)
                        {
                            OnStatusChanged?.Invoke("[SmartUpload] Could not connect to SFTP server.");
                            return;
                        }

                        OnStatusChanged?.Invoke("Uploading file...");
                        await SendFileAsync(client, localFilePath, remoteFilePath, cancellationToken);
                    }
                    finally
                    {
                        ClearActiveSftpClient(client);
                        client.Disconnect();
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (IsCancellationRequested)
                    throw new OperationCanceledException("Upload cancelled.", ex, cancellationToken);

                Debug.LogError($"[SmartUpload] Error in UploadSFTPAsync file '{localFilePath}': {ex.Message}");
                throw;
            }
        }

        private SftpClient CreateSftpClient()
        {
            if (!m_usePrivateKey)
                return new SftpClient(m_host, m_port, m_user, m_password);

            PrivateKeyFile keyFile = string.IsNullOrWhiteSpace(m_privateKeyPassphrase)
                ? new PrivateKeyFile(m_privateKeyPath)
                : new PrivateKeyFile(m_privateKeyPath, m_privateKeyPassphrase);

            var authMethod = new PrivateKeyAuthenticationMethod(m_user, keyFile);
            var connectionInfo = new ConnectionInfo(m_host, m_port, m_user, authMethod);
            return new SftpClient(connectionInfo);
        }

        /// <summary>
        /// Deletes all files and subdirectories recursively inside a remote directory (SFTP).
        /// </summary>
        /// <param name="client">Connected SFTP client.</param>
        /// <param name="remoteDir">Remote directory path.</param>
        private async Task DeleteAsync(SftpClient client, string remoteDir, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in client.ListDirectory(remoteDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (file.Name == "." || file.Name == "..") continue;

                    if (file.IsDirectory)
                    {
                        DeleteAsync(client, remoteDir + "/" + file.Name, cancellationToken).GetAwaiter().GetResult();
                        cancellationToken.ThrowIfCancellationRequested();
                        client.DeleteDirectory(remoteDir + "/" + file.Name);
                    }
                    else
                    {
                        client.DeleteFile(remoteDir + "/" + file.Name);
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Sends a single file with upload progress tracking via SFTP.
        /// </summary>
        /// <param name="client">Connected SFTP client.</param>
        /// <param name="localFilePath">Path of the local file.</param>
        /// <param name="remoteFilePath">Destination directory on the server.</param>
        private Task SendFileAsync(SftpClient client, string localFilePath, string remoteFilePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(localFilePath))
                throw new FileNotFoundException($"File not found: {localFilePath}");

            remoteFilePath = NormalizeRemotePath(remoteFilePath, treatEmptyAsRoot: false);

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string dir = GetDirectoryName(remoteFilePath);
                if (!string.IsNullOrEmpty(dir))
                    EnsureRemoteDirectoryExists(client, dir, cancellationToken);

                using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
                client.UploadFile(fs, remoteFilePath, uploadedBytes =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            client.Disconnect();
                        }
                        catch
                        {
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    float progress = fs.Length <= 0 ? 1f : (float)uploadedBytes / fs.Length;
                    ReportStepProgress(progress);
                });
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);
        }








        /// <summary>
        /// Uploads a single file to AWS S3.
        /// </summary>
        /// <param name="localFilePath">Local path of the file to upload.</param>
        /// <param name="remoteFilePath">Remote directory or S3 bucket path.</param>
        private async Task UploadAWSAsync(string localFilePath, string remoteFilePath, CancellationToken cancellationToken)
        {
            if (m_s3Client == null || string.IsNullOrEmpty(m_bucketName))
            {
                Debug.LogError("[SmartUpload] AWS S3 client or bucket not configured.");
                return;
            }

            try
            {
                OnStatusChanged?.Invoke("[SmartUpload] Sending file to AWS S3...");
                ReportStepProgress(0f);
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

                await m_s3Client.PutObjectAsync(request, cancellationToken);
                ReportStepProgress(1f);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AmazonS3Exception e)
            {
                if (IsCancellationRequested)
                    throw new OperationCanceledException("Upload cancelled.", e, cancellationToken);

                Debug.LogError($"[SmartUpload] AWS S3 upload error {localFilePath}: {e.Message}");
                throw;
            }
            catch (Exception e)
            {
                if (IsCancellationRequested)
                    throw new OperationCanceledException("Upload cancelled.", e, cancellationToken);

                Debug.LogError($"[SmartUpload] Unknown error uploading {localFilePath}: {e.Message}");
                throw;
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
                ".symbols.json" => "application/json",
                ".txt" => "text/plain",
                ".js" => "application/javascript",
                ".html" => "text/html",
                ".css" => "text/css",
                ".uxml" => "text/xml",
                ".uss" => "text/css",
                ".wasm" => "application/wasm",
                ".data" => "application/octet-stream",
                ".mem" => "application/octet-stream",
                ".br" => "application/octet-stream",
                "" => "application/octet-stream", // files without extension
                _ => "application/octet-stream",
            };
        }


        private List<(string localPath, string remotePath)> GetAllFilesRecursively(string rootPath)
        {
            var files = new List<(string, string)>();

            foreach (string file in Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(rootPath, file); // caminho relativo corretamente
                string remotePath = relativePath.Replace("\\", "/");       // remotePath sempre com /

                files.Add((file, remotePath));
            }

            return files;
        }



        private List<(string localPath, string remotePath)> GetAllFilesRecursively2(string rootPath)
        {
            var files = new List<(string, string)>();
            int rootLength = rootPath.Length + 1; // +1 pra remover a barra final

            foreach (string file in Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(rootLength); // caminho relativo à pasta root
                files.Add((file, relativePath.Replace("\\", "/"))); // remotePath sempre com / 
            }

            return files;
        }


        public static async Task<byte[][]> GetAllFilesBytesAsync(List<(string localPath, string remotePath)> files)
        {
            byte[][] allBytes = new byte[files.Count][];

            for (int i = 0; i < files.Count; i++)
            {
                var (localPath, remotePath) = files[i];

                if (File.Exists(localPath))
                {
                    allBytes[i] = await File.ReadAllBytesAsync(localPath);
                }
                else
                {
                    Debug.LogWarning($"File not found: {localPath}");
                    allBytes[i] = Array.Empty<byte>(); // ou null, depende do que tu quer
                }
            }

            return allBytes;
        }

        public static bool TryNormalizeRemoteSubdirectory(string rawValue, out string normalizedPath, out string errorMessage)
        {
            normalizedPath = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(rawValue) || string.Equals(rawValue.Trim(), SmartUploaderSettings.RootSubdirectoryToken, StringComparison.OrdinalIgnoreCase))
                return true;

            string candidate = rawValue.Trim();
            if (candidate.Contains("\\"))
            {
                errorMessage = "Use only forward slashes ('/') in the remote subdirectory.";
                return false;
            }

            if (candidate.StartsWith("/") || candidate.EndsWith("/"))
            {
                errorMessage = "The remote subdirectory must be a relative path without leading or trailing '/'.";
                return false;
            }

            string[] segments = candidate.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment))
                {
                    errorMessage = "The remote subdirectory cannot contain empty path segments.";
                    return false;
                }

                if (segment == "." || segment == "..")
                {
                    errorMessage = "The remote subdirectory cannot contain '.' or '..'.";
                    return false;
                }

                if (!RemoteDirectorySegmentRegex.IsMatch(segment))
                {
                    errorMessage = "Each folder name may use only letters, numbers, '.', '_' or '-'.";
                    return false;
                }
            }

            normalizedPath = string.Join("/", segments);
            return true;
        }

        public static string NormalizeRemotePath(string path, bool treatEmptyAsRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return treatEmptyAsRoot ? "/" : string.Empty;

            string normalized = path.Trim().Replace("\\", "/");
            bool isAbsolute = normalized.StartsWith("/");
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
                return treatEmptyAsRoot ? "/" : string.Empty;

            string joined = string.Join("/", segments);
            return isAbsolute ? "/" + joined : joined;
        }

        public static string CombineRemotePath(string basePath, string childPath)
        {
            string normalizedBase = NormalizeRemotePath(basePath, treatEmptyAsRoot: true);
            string normalizedChild = NormalizeRemotePath(childPath, treatEmptyAsRoot: false);

            if (string.IsNullOrEmpty(normalizedChild))
                return normalizedBase;

            if (normalizedBase == "/")
                return "/" + normalizedChild;

            return normalizedBase.TrimEnd('/') + "/" + normalizedChild;
        }

        private static string GetDirectoryName(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
                return string.Empty;

            string normalized = NormalizeRemotePath(remotePath, treatEmptyAsRoot: false);
            int lastSlash = normalized.LastIndexOf('/');
            if (lastSlash < 0)
                return string.Empty;

            if (lastSlash == 0)
                return "/";

            return normalized.Substring(0, lastSlash);
        }

        private static void EnsureRemoteDirectoryExists(SftpClient client, string remoteDir, CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            string normalizedDir = NormalizeRemotePath(remoteDir, treatEmptyAsRoot: true);
            if (normalizedDir == "/")
                return;

            bool isAbsolute = normalizedDir.StartsWith("/");
            string[] segments = normalizedDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string current = isAbsolute ? "/" : string.Empty;

            for (int i = 0; i < segments.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string segment = segments[i];
                current = isAbsolute
                    ? $"{current.TrimEnd('/')}/{segment}"
                    : string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";

                if (!client.Exists(current))
                    client.CreateDirectory(current);
            }
        }

        public void Cancel()
        {
            if (m_cancellationTokenSource == null || m_cancellationTokenSource.IsCancellationRequested)
                return;

            Debug.Log("[SmartUpload] Cancelling upload...");
            ReportStatus("[SmartUpload] Cancelling upload...", 0f);
            m_cancellationTokenSource.Cancel();
            DisconnectActiveSftpClient();
        }

        private void BeginCancellationScope()
        {
            EndCancellationScope();
            m_cancellationTokenSource = new CancellationTokenSource();
        }

        private void EndCancellationScope()
        {
            m_cancellationTokenSource?.Dispose();
            m_cancellationTokenSource = null;
            m_activeSftpClient = null;
        }

        private void SetActiveSftpClient(SftpClient client)
        {
            m_activeSftpClient = client;
        }

        private void ClearActiveSftpClient(SftpClient client)
        {
            if (ReferenceEquals(m_activeSftpClient, client))
                m_activeSftpClient = null;
        }

        private void DisconnectActiveSftpClient()
        {
            try
            {
                m_activeSftpClient?.Disconnect();
            }
            catch
            {
            }
        }

        private void ReportStatus(string status, float progress)
        {
            OnStatusChanged?.Invoke(status);
            OnProgressChanged?.Invoke(Mathf.Clamp01(progress));
        }

        private void ReportStepProgress(float progress)
        {
            OnStepProgressChanged?.Invoke(Mathf.Clamp01(progress));
        }

        private static float CalculateFileStageProgress(int completedFiles, int totalFiles)
        {
            if (totalFiles <= 0)
                return 1f;

            float uploadRatio = Mathf.Clamp01((float)completedFiles / totalFiles);
            return PreparationProgressWeight + ((1f - PreparationProgressWeight) * uploadRatio);
        }

        private static float CalculateByteStageProgress(long uploadedBytes, long totalBytes)
        {
            if (totalBytes <= 0)
                return 1f;

            float uploadRatio = Mathf.Clamp01((float)uploadedBytes / totalBytes);
            return PreparationProgressWeight + ((1f - PreparationProgressWeight) * uploadRatio);
        }

    }
}
#endif

