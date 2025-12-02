using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace FileHostingTest.Service
{
    public interface IFileStorageService
    {
        Task<string> UploadFileAsync(IFormFile file);
        Task<List<StoredFileInfo>> GetAllFilesAsync();
        Task<List<StoredFileInfo>> GetFilesAsync(string prefix);
        Task<Stream> DownloadFileAsync(string fileName);
        Task<(Stream Stream, string ContentType)> GetObjectWithContentTypeAsync(string fileName);

        // Delete a single object by its object name/key
        Task DeleteObjectAsync(string objectName);

        // Delete all objects that start with the provided prefix (e.g. folder/)
        Task DeleteFolderAsync(string folderPrefix);

        // Upload directly from a stream (useful to avoid buffering large files in memory)
        Task<string> UploadStreamAsync(Stream stream, string contentType, string objectName, long objectSize);

        // Move (copy + delete) an object from source to destination. Used for soft-delete (trash) and restore.
        Task MoveObjectAsync(string sourceObjectName, string destinationObjectName);

        // Check if object exists
        Task<bool> ObjectExistsAsync(string objectName);
    }
}
