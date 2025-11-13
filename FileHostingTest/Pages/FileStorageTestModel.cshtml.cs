using FileHostingTest.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FileHostingTest.Pages
{
    public class FileStorageTestModel : PageModel
    {
        private readonly IFileStorageService _fileStorageService;

        public FileStorageTestModel(IFileStorageService fileStorageService)
        {
            _fileStorageService = fileStorageService;
        }

        [BindProperty]
        public IFormFile Upload { get; set; }

        public List<StoredFileInfo> Files { get; set; } = new();

        public async Task OnGetAsync()
        {
            Files = await _fileStorageService.GetAllFilesAsync();
        }

        public async Task<IActionResult> OnPostUploadAsync()
        {
            if (Upload == null || Upload.Length == 0)
            {
                ModelState.AddModelError("Upload", "Please select a file");
                return Page();
            }

            await _fileStorageService.UploadFileAsync(Upload);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnGetDownloadAsync(string fileName)
        {
            var stream = await _fileStorageService.DownloadFileAsync(fileName);
            return File(stream, "application/octet-stream", fileName);
        }
    }
}
