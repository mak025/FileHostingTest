using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using FileHostingTest.Models;

namespace FileHostingTest.Service
{
    // Repository: contains MinIO-specific logic for interacting with the object store.
    public class MinioFileStorageRepository
    {
        private readonly IMinioClient _minioClient;
        private readonly string _bucketName;

        public MinioFileStorageRepository(IOptions<MinioSettings> settings)
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

        public async Task PutObjectAsync(Stream data, long size, string objectName, string contentType)
        {
            var putArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithStreamData(data)
                .WithObjectSize(size)
                .WithContentType(contentType ?? "application/octet-stream");

            await _minioClient.PutObjectAsync(putArgs);
        }

        public async Task<List<StoredFileInfo>> ListObjectsAsync(string prefix = null)
        {
            var files = new List<StoredFileInfo>();
            var listArgs = new ListObjectsArgs().WithBucket(_bucketName).WithRecursive(true);
            if (!string.IsNullOrEmpty(prefix)) listArgs.WithPrefix(prefix).WithRecursive(true);

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

        public async Task<List<string>> ListKeysWithPrefixAsync(string prefix)
        {
            var keys = new List<string>();
            var listArgs = new ListObjectsArgs().WithBucket(_bucketName).WithPrefix(prefix).WithRecursive(true);
            await foreach (var item in _minioClient.ListObjectsEnumAsync(listArgs))
            {
                keys.Add(item.Key);
            }
            return keys;
        }

        public async Task<Stream> GetObjectAsync(string objectName)
        {
            var memoryStream = new MemoryStream();
            var getArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithCallbackStream((stream) => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getArgs);
            memoryStream.Position = 0;
            return memoryStream;
        }

        public async Task<(Stream Stream, string ContentType)> GetObjectWithContentTypeAsync(string objectName)
        {
            var memoryStream = new MemoryStream();
            // Get object with callback to copy stream
            var getArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithCallbackStream((stream) => stream.CopyTo(memoryStream));

            // Retrieve object's metadata for content type
            var statArgs = new StatObjectArgs().WithBucket(_bucketName).WithObject(objectName);
            var stat = await _minioClient.StatObjectAsync(statArgs);

            await _minioClient.GetObjectAsync(getArgs);
            memoryStream.Position = 0;
            return (memoryStream, stat.ContentType);
        }

        public async Task<bool> ObjectExistsAsync(string objectName)
        {
            try
            {
                var statArgs = new StatObjectArgs().WithBucket(_bucketName).WithObject(objectName);
                var stat = await _minioClient.StatObjectAsync(statArgs);
                return stat != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task RemoveObjectAsync(string objectName)
        {
            var args = new RemoveObjectArgs().WithBucket(_bucketName).WithObject(objectName);
            await _minioClient.RemoveObjectAsync(args);
        }

        public async Task CopyObjectAsync(string sourceObjectName, string destinationObjectName)
        {
            // Simple copy by streaming the object through memory. For very large objects consider using server-side copy if SDK supports it.
            using var ms = new MemoryStream();
            var getArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(sourceObjectName)
                .WithCallbackStream((stream) => stream.CopyTo(ms));

            await _minioClient.GetObjectAsync(getArgs);
            ms.Position = 0;
            await PutObjectAsync(ms, ms.Length, destinationObjectName, null);
        }

        public async Task<string> GetPresignedUrlAsync(string objectName, int expiresSeconds)
        {
            // Some MinIO SDK versions expose different presign APIs. Create a presigned GET URL without custom response headers.
            var presignArgs = new PresignedGetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithExpiry(expiresSeconds);

            var url = await _minioClient.PresignedGetObjectAsync(presignArgs);
            return url;
        }
    }
}
