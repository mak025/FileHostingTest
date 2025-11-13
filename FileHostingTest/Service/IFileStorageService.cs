using FileHostingTest.Models;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace FileHostingTest.Service
{
    public interface IFileStorageService
    {
        Task<string> UploadFileAsync(IFormFile file);
        Task<List<StoredFileInfo>> GetAllFilesAsync();
        Task<Stream> DownloadFileAsync(string fileName);
    }

    public class MinioFileStorageService : IFileStorageService
    {
        private readonly IMinioClient _minioClient;
        private readonly string _bucketName;

        public MinioFileStorageService(IOptions<MinioSettings> settings)
        {
            var config = settings.Value;
            _bucketName = config.BucketName;

            _minioClient = new MinioClient()
                .WithEndpoint(config.Endpoint)
                .WithCredentials(config.AccessKey, config.SecretKey)
                .Build();

            EnsureBucketExists().Wait();
        }

        private async Task EnsureBucketExists()
        {
            var beArgs = new BucketExistsArgs().WithBucket(_bucketName);
            bool found = await _minioClient.BucketExistsAsync(beArgs);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _minioClient.MakeBucketAsync(mbArgs);
            }
        }

        public async Task<string> UploadFileAsync(IFormFile file)
        {
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";

            using var stream = file.OpenReadStream();
            var putArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(file.ContentType);

            await _minioClient.PutObjectAsync(putArgs);
            return fileName;
        }

        public async Task<List<StoredFileInfo>> GetAllFilesAsync()
        {
            var files = new List<StoredFileInfo>();
            var listArgs = new ListObjectsArgs().WithBucket(_bucketName);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listArgs))
            {
                files.Add(new StoredFileInfo
                {
                    Name = item.Key,
                    Size = (long)item.Size,
                    LastModified = item.LastModifiedDateTime ?? DateTime.MinValue
                });
            }

            return files;
        }

        public async Task<Stream> DownloadFileAsync(string fileName)
        {
            var memoryStream = new MemoryStream();
            var getArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCallbackStream((stream) => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getArgs);
            memoryStream.Position = 0;
            return memoryStream;
        }
    }

    public class StoredFileInfo
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }
}
