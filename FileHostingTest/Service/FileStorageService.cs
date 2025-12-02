using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FileHostingTest.Service
{
    // Service layer: uses the repository and exposes higher-level operations to the rest of the app.
    public class FileStorageService : IFileStorageService
    {
        private readonly MinioFileStorageRepository _repo;

        public FileStorageService(MinioFileStorageRepository repo)
        {
            _repo = repo;
        }

        public async Task<string> UploadFileAsync(IFormFile file)
        {
            var objectName = file.FileName;
            using var stream = file.OpenReadStream();
            await _repo.PutObjectAsync(stream, file.Length, objectName, file.ContentType ?? "application/octet-stream");
            return objectName;
        }

        public async Task<string> UploadStreamAsync(Stream stream, string contentType, string objectName, long objectSize)
        {
            await _repo.PutObjectAsync(stream, objectSize, objectName, contentType);
            return objectName;
        }

        public async Task<List<StoredFileInfo>> GetAllFilesAsync()
        {
            return await _repo.ListObjectsAsync();
        }

        public async Task<List<StoredFileInfo>> GetFilesAsync(string prefix)
        {
            return await _repo.ListObjectsAsync(prefix);
        }

        public async Task<Stream> DownloadFileAsync(string fileName)
        {
            return await _repo.GetObjectAsync(fileName);
        }

        public async Task<(Stream Stream, string ContentType)> GetObjectWithContentTypeAsync(string fileName)
        {
            return await _repo.GetObjectWithContentTypeAsync(fileName);
        }

        public async Task DeleteObjectAsync(string objectName)
        {
            await _repo.RemoveObjectAsync(objectName);
        }

        public async Task DeleteFolderAsync(string folderPrefix)
        {
            if (!string.IsNullOrEmpty(folderPrefix) && !folderPrefix.EndsWith('/')) folderPrefix += '/';
            var keys = await _repo.ListKeysWithPrefixAsync(folderPrefix);
            foreach (var k in keys)
            {
                try { await _repo.RemoveObjectAsync(k); } catch { }
            }
        }

        public async Task MoveObjectAsync(string sourceObjectName, string destinationObjectName)
        {
            await _repo.CopyObjectAsync(sourceObjectName, destinationObjectName);
            await _repo.RemoveObjectAsync(sourceObjectName);
        }

        public async Task<bool> ObjectExistsAsync(string objectName)
        {
            return await _repo.ObjectExistsAsync(objectName);
        }
    }
}
