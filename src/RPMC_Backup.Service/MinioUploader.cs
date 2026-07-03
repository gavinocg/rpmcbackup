using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Minio;
using Minio.DataModel.Args;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Service;

public class MinioUploader
{
    private const string S3Service = "s3";
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";
    private const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly string _endpoint;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;
    private readonly bool _useSsl;
    private readonly SemaphoreSlim _semaphore = new(5, 5);
    private static readonly HttpClient _http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    public MinioUploader(AppConfig config)
    {
        _bucket = config.BucketName;
        _endpoint = config.MinioEndpoint;
        _accessKey = config.MinioAccessKey;
        _secretKey = config.MinioSecretKey;
        _useSsl = config.MinioUseSsl;
        _region = string.IsNullOrEmpty(config.S3Region) ? "us-east-1" : config.S3Region;

        _client = new MinioClient()
            .WithEndpoint(config.MinioEndpoint)
            .WithCredentials(config.MinioAccessKey, config.MinioSecretKey);
        if (!config.MinioUseSsl)
            _client.WithSSL(false);
        _client.Build();
    }

    public async Task UploadAsync(string objectName, string filePath, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var bucketExists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
            if (!bucketExists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
            }

            var contentType = GetContentType(filePath);
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length == 0) return;

            var encodedParts = objectName.Split('/').Select(Uri.EscapeDataString);
            var encodedKey = string.Join("/", encodedParts);
            var protocol = _useSsl ? "https" : "http";
            var url = $"{protocol}://{_endpoint}/{_bucket}/{encodedKey}";
            var host = new Uri(url).Authority;

            var now = DateTime.UtcNow;
            var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            var canonicalUri = $"/{_bucket}/{encodedKey}";
            var canonicalQuery = "";
            var signedHeaders = "host;x-amz-content-sha256;x-amz-date";
            var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{UnsignedPayload}\nx-amz-date:{amzDate}\n";

            var canonicalRequest = $"PUT\n{canonicalUri}\n{canonicalQuery}\n{canonicalHeaders}\n{signedHeaders}\n{UnsignedPayload}";

            var credentialScope = $"{dateStamp}/{_region}/{S3Service}/aws4_request";
            var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n{Hex(SHA256(Encoding.UTF8.GetBytes(canonicalRequest)))}";

            var signingKey = GetSigningKey(_secretKey, dateStamp, _region, S3Service);
            var signature = Hex(HMAC(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

            var authorization = $"AWS4-HMAC-SHA256 Credential={_accessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

            var content = new StreamContent(fs);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var msg = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
            msg.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
            msg.Headers.TryAddWithoutValidation("x-amz-content-sha256", UnsignedPayload);
            msg.Headers.TryAddWithoutValidation("Authorization", authorization);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromHours(2));
            var response = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Status: {(int)response.StatusCode}, Body: {errorBody}");
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task TestConnectionAsync()
    {
        var bucketExists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket));
        if (!bucketExists)
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket));
    }

    public async Task<Dictionary<string, DateTime>> ListExistingObjectsAsync(string prefix, CancellationToken ct)
    {
        var result = new Dictionary<string, DateTime>();
        try
        {
            var args = new ListObjectsArgs()
                .WithBucket(_bucket)
                .WithPrefix(prefix)
                .WithRecursive(true);
            var items = _client.ListObjectsEnumAsync(args, ct);
            await foreach (var item in items)
            {
                var ts = item.LastModifiedDateTime;
                if (ts.HasValue)
                    result[item.Key] = ts.Value;
            }
        }
        catch (Exception ex)
        {
            throw new HttpRequestException($"ListObjects failed: {ex.Message}", ex);
        }
        return result;
    }

    private static byte[] GetSigningKey(string secretKey, string dateStamp, string region, string service)
    {
        var kDate = HMAC(Encoding.UTF8.GetBytes("AWS4" + secretKey), Encoding.UTF8.GetBytes(dateStamp));
        var kRegion = HMAC(kDate, Encoding.UTF8.GetBytes(region));
        var kService = HMAC(kRegion, Encoding.UTF8.GetBytes(service));
        return HMAC(kService, Encoding.UTF8.GetBytes("aws4_request"));
    }

    private static byte[] HMAC(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    private static byte[] SHA256(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(data);
    }

    private static string Hex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    private static string GetContentType(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".doc" or ".docx" => "application/msword",
            ".xls" or ".xlsx" => "application/vnd.ms-excel",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".gz" => "application/gzip",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".csv" => "text/csv",
            _ => "application/octet-stream"
        };
    }
}
