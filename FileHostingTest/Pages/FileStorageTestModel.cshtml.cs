// File: FileStorageTestModel.cshtml.cs
// What this file is:
// - Razor PageModel that backs the Index page used for uploading, listing and downloading files.
// - Talks to an abstraction `IFileStorageService` which handles the actual storage (MinIO in your setup).
// How it works (high level):
// - OnGetAsync: loads the list of stored files and folders to render on the page for the current path.
// - OnPostUploadAsync / OnPostUploadAjaxAsync: accept posted files and forward them to the storage service.
// - OnPostCreateFolderAsync: creates a placeholder object to represent a folder in object storage.
// - OnGetDownloadAsync: fetches a file stream from the storage service and returns it to the browser.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using FileHostingTest.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace FileHostingTest.Pages
{
    public class FileStorageTestModel : PageModel
    {
        private readonly IFileStorageService _fileStorageService;
        private readonly IDataProtector _protector;

        // Constructor: receives the file storage service via dependency injection
        public FileStorageTestModel(IFileStorageService fileStorageService, IDataProtectionProvider dataProtectionProvider)
        {
            _fileStorageService = fileStorageService;
            _protector = dataProtectionProvider.CreateProtector("FileHostingTest.ShareToken");
        }

        // BindProperty for the form file input named "Upload"
        // This is populated automatically when the upload form posts.
        [BindProperty]
        public IFormFile Upload { get; set; }

        // Current path (folder) the user is viewing. Examples: "", "folderA/", "folderA/sub/
        // SupportsGet so ?path=folderA/ will bind on GET requests.
        [BindProperty(SupportsGet = true)]
        public string Path { get; set; } = string.Empty;

        // Files directly under the current Path. Populated by OnGetAsync.
        public List<StoredFileInfo> Files { get; set; } = new();

        // Immediate subfolders under the current Path (not recursive). Populated by OnGetAsync.
        public List<string> Folders { get; set; } = new();

        /*
         ##### OnGetAsync - Load files and folders for current path #####
         # Purpose:
         # - Fetches all files from the storage service and derives the folders for the current path.
         # How it works:
         # - Calls _fileStorageService.GetAllFilesAsync() which returns a flat list of StoredFileInfo objects
         #   representing all objects in the bucket (server/service should map MinIO objects to this model).
         # - We filter by the current path prefix and split remaining keys by '/' to determine immediate folders
         #   (common approach when emulating a folder view on top of object storage).
         */
        public async Task OnGetAsync()
        {
            // Normalize path: ensure empty or ends with '/'
            if (!string.IsNullOrEmpty(Path) && !Path.EndsWith('/'))
            {
                Path += '/';
            }

            // Use prefix listing to fetch only objects for the current path
            var all = string.IsNullOrEmpty(Path) ? await _fileStorageService.GetAllFilesAsync() : await _fileStorageService.GetFilesAsync(Path);

            var files = new List<StoredFileInfo>();
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in all)
            {
                var name = f.Name.Replace("\\", "/"); // normalize
                if (!string.IsNullOrEmpty(Path))
                {
                    if (!name.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
                    {
                        // not in this folder
                        continue;
                    }
                    var remainder = name.Substring(Path.Length);
                    if (string.IsNullOrEmpty(remainder))
                    {
                        // this is the folder marker or exact match
                        continue;
                    }

                    // Hide placeholder marker files named ".folder" so they don't show as regular files
                    if (string.Equals(remainder, ".folder", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // skip the placeholder file
                    }

                    var idx = remainder.IndexOf('/');
                    if (idx >= 0)
                    {
                        // there's a subfolder
                        var folderName = remainder.Substring(0, idx + 1); // include trailing slash
                        folders.Add(Path + folderName);
                    }
                    else
                    {
                        // file directly in this folder
                        files.Add(f);
                    }
                }
                else
                {
                    // root
                    var remainder = name;
                    var idx = remainder.IndexOf('/');
                    if (idx >= 0)
                    {
                        var folderName = remainder.Substring(0, idx + 1);
                        folders.Add(folderName);
                    }
                    else
                    {
                        files.Add(f);
                    }
                }
            }

            Files = files.OrderByDescending(x => x.LastModified).ToList();
            Folders = folders.OrderBy(x => x).ToList();
        }

        /*
         ##### Upload Function #####
         # Handles upload functionality by receiving the posted IFormFile and handing it to the storage service.
         # Important addition for folders:
         # - If Path is set (non-empty), the uploaded file should be stored inside that path, so the object key
         #   we send to storage must include the path as a prefix: e.g. "folderA/myfile.txt".
         # How we do that here (beginner-friendly):
         # - We cannot change the incoming IFormFile.FileName directly, so the code creates a new FormFile that
         #   wraps the same file data but uses a prefixed filename (Path + original name).
         # - To create a new FormFile we copy the data into a MemoryStream. Note: copying to memory is simple but
         #   uses RAM proportional to file size. For production with large files you should stream directly to MinIO
         #   from the incoming request (implement an UploadFileAsync that accepts a Stream + object key) to avoid
         #   buffering the whole file server-side.
         */
        public async Task<IActionResult> OnPostUploadAsync()
        {
            if (Upload == null || Upload.Length == 0)
            {
                ModelState.AddModelError("Upload", "Please select a file");

                // Re-populate files and folders so page can render with validation errors
                await OnGetAsync();
                return Page();
            }

            // Determine target object name. Prefix with Path if provided.
            var prefix = string.IsNullOrEmpty(Path) ? string.Empty : Path.TrimStart('/');
            if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith('/')) prefix += '/';

            var targetName = (string.IsNullOrEmpty(prefix) ? "" : prefix) + PathTrimUploadFileName(Upload.FileName);

            // Create a new IFormFile with the desired target name by copying into memory.
            using var ms = new MemoryStream();
            await Upload.CopyToAsync(ms);
            ms.Position = 0;

            await _fileStorageService.UploadStreamAsync(ms, Upload.ContentType ?? "application/octet-stream", targetName, ms.Length);

            // Redirect back to current path so the user remains in that folder after upload
            return RedirectToPage(new { path = Path });
        }

        // AJAX upload endpoint with progress from client
        public async Task<JsonResult> OnPostUploadAjaxAsync()
        {
            // Use Request.Form.Files to access multipart upload
            var file = Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                return new JsonResult(new { success = false, message = "No file uploaded" });
            }

            // Determine target object name including path prefix
            var prefix = string.IsNullOrEmpty(Path) ? string.Empty : Path.TrimStart('/');
            if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith('/')) prefix += '/';
            var targetName = (string.IsNullOrEmpty(prefix) ? "" : prefix) + PathTrimUploadFileName(file.FileName);

            try
            {
                using var stream = file.OpenReadStream();
                await _fileStorageService.UploadStreamAsync(stream, file.ContentType ?? "application/octet-stream", targetName, file.Length);
                return new JsonResult(new { success = true, name = targetName });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // Helper to sanitize / trim the uploaded filename (remove any path segments provided by client)
        private static string PathTrimUploadFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return fileName;
            fileName = fileName.Replace("\\", "/");
            var idx = fileName.LastIndexOf('/');
            if (idx >= 0) return fileName.Substring(idx + 1);
            return fileName;
        }

        /*
         ##### Create Folder Function #####
         # Purpose:
         # - Create a new folder inside the current path.
         # How it works with MinIO / object storage:
         # - Object storage does not have real folders. We emulate folders by creating a placeholder object
         #   inside the folder (for example a zero-byte object named "folderName/.folder") or relying on prefixes.
         # - Here we create a small placeholder file named "<path><folderName>/.folder" so the folder appears when listing.
         # - For simplicity we build a tiny in-memory FormFile and call the same UploadFileAsync.
         */
        public async Task<IActionResult> OnPostCreateFolderAsync()
        {
            var form = Request.Form;
            var folderName = form["newFolderName"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(folderName))
            {
                if (IsAjaxRequest()) return new JsonResult(new { success = false, message = "Folder name is required" });
                ModelState.AddModelError("newFolderName", "Folder name is required");
                await OnGetAsync();
                return Page();
            }

            // sanitize folder name
            folderName = folderName.Trim().Replace("/", "").Replace("\\", "");

            var prefix = string.IsNullOrEmpty(Path) ? string.Empty : Path.TrimStart('/');
            if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith('/')) prefix += '/';

            var placeholderName = (string.IsNullOrEmpty(prefix) ? "" : prefix) + folderName + "/.folder";

            // Upload a zero-byte object using streaming API to avoid FormFile headers issue
            using var ms = new MemoryStream();
            await _fileStorageService.UploadStreamAsync(ms, "application/octet-stream", placeholderName, 0);

            if (IsAjaxRequest()) return new JsonResult(new { success = true });
            return RedirectToPage(new { path = Path });
        }

        /*
         ##### Delete Folder (AJAX) #####
         # Purpose:
         # - Delete all objects that belong to the provided folder prefix. This is used when the user wants to
         #   remove a folder and its contents.
         # How it works:
         # - Calls _fileStorageService.DeleteFolderAsync which will iterate object keys with the provided prefix
         #   and remove them from the bucket.
         */
        public async Task<JsonResult> OnPostDeleteFolderAsync([FromForm] string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return new JsonResult(new { success = false, message = "Folder is required" });
            await _fileStorageService.DeleteFolderAsync(folder);
            return new JsonResult(new { success = true });
        }

        /*
         ##### Delete Object (AJAX) #####
         # Purpose:
         # - Delete a single object by name.
         */
        public async Task<JsonResult> OnPostDeleteObjectAsync([FromForm] string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return new JsonResult(new { success = false, message = "Object is required" });
            await _fileStorageService.DeleteObjectAsync(objectName);
            return new JsonResult(new { success = true });
        }

        /*
         ##### Download Function #####
         # Purpose:
         # - Given a file name, ask the storage service for a readable stream and return it as a FileResult.
         # How it works with MinIO:
         # - The storage service will call MinIO's API to get an object's data stream.
         # - We return that stream to the browser with a content type of application/octet-stream so the
         #   browser triggers a download.
         */
        public async Task<IActionResult> OnGetDownloadAsync(string fileName)
        {
            var stream = await _fileStorageService.DownloadFileAsync(fileName);
            return File(stream, "application/octet-stream", fileName);
        }

        // Generate presigned URL for sharing (AJAX)
        public async Task<JsonResult> OnPostPresignAsync([FromForm] string objectName, [FromForm] int expires)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return new JsonResult(new { success = false, message = "Object required" });
            if (expires <= 0) expires = 60 * 60 * 12; // default 12 hours

            // Validate object exists
            var exists = await _fileStorageService.ObjectExistsAsync(objectName);
            if (!exists) return new JsonResult(new { success = false, message = "Object not found" });

            // Create a time-limited protected token that encodes object name and expiry
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expires).ToUnixTimeSeconds();
            var payload = objectName + "|" + expiresAt.ToString();
            var token = _protector.Protect(payload);

            // Build an absolute URL to the server-proxied download handler. This link will force download via the server and honor expiry.
            var url = Url.Page(null, null, new { handler = "SharedDownload", token }, Request.Scheme, Request.Host.Value);

            return new JsonResult(new { success = true, url });
        }

        // Server-proxied shared download handler. Accepts a protected token, validates expiry, and streams the file with Content-Disposition attachment.
        public async Task<IActionResult> OnGetSharedDownloadAsync([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token)) return NotFound();

            try
            {
                var payload = _protector.Unprotect(token);
                var parts = payload.Split('|', 2);
                if (parts.Length != 2) return NotFound();

                var objectName = parts[0];
                if (!long.TryParse(parts[1], out var expiresAt)) return NotFound();

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now > expiresAt) return NotFound();

                // Stream object from storage and return as file with attachment disposition
                var (stream, contentType) = await _fileStorageService.GetObjectWithContentTypeAsync(objectName);
                stream.Position = 0;
                var fileName = System.IO.Path.GetFileName(objectName) ?? objectName;
                return File(stream, contentType ?? "application/octet-stream", fileName);
            }
            catch
            {
                return NotFound();
            }
        }

        private bool IsAjaxRequest()
        {
            return Request.Headers.TryGetValue("X-Requested-With", out var val) && val == "XMLHttpRequest";
        }
    }
}
