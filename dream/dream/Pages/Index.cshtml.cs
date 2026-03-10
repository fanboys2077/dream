using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace dream.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IWebHostEnvironment _env;

        private const string AdminUsername = "yangruikee";
        private const string AdminPassword = "ke0618";
        private const string LoginSessionKey = "IsAdminLoggedIn";

        public List<PhotoItem> Photos { get; set; } = new();
        public bool HasImages => Photos.Count > 0;
        public bool IsLoggedIn { get; set; }

        private string GalleryPath => Path.Combine(_env.WebRootPath, "images", "gallery");
        private string PreviewPath => Path.Combine(_env.WebRootPath, "images", "gallery", "previews");
        private string MetadataPath => Path.Combine(_env.WebRootPath, "data", "gallery-layout.json");

        public IndexModel(IWebHostEnvironment env)
        {
            _env = env;
        }

        public void OnGet()
        {
            HttpContext.Session.Remove(LoginSessionKey);
            LoadPhotos();
            IsLoggedIn = false;
        }

        public IActionResult OnPostLogin([FromBody] LoginRequest request)
        {
            if (request == null)
            {
                return new JsonResult(new { success = false, message = "登入資料無效" });
            }

            if (request.Username == AdminUsername && request.Password == AdminPassword)
            {
                HttpContext.Session.SetString(LoginSessionKey, "true");
                return new JsonResult(new { success = true, message = "登入成功" });
            }

            return new JsonResult(new { success = false, message = "帳號或密碼錯誤" });
        }

        public IActionResult OnPostLogout()
        {
            HttpContext.Session.Remove(LoginSessionKey);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> files, List<IFormFile> previewFiles, string layoutJson)
        {
            var isLoggedIn = HttpContext.Session.GetString(LoginSessionKey) == "true";
            if (!isLoggedIn)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return new JsonResult(new { success = false, message = "未登入，無法上傳" });
            }

            if (files == null || files.Count == 0)
            {
                return new JsonResult(new { success = false, message = "沒有收到原圖檔案" });
            }

            if (previewFiles == null || previewFiles.Count != files.Count)
            {
                return new JsonResult(new { success = false, message = "預覽圖數量與原圖不一致" });
            }

            List<UploadLayoutItem>? layouts;
            try
            {
                layouts = JsonSerializer.Deserialize<List<UploadLayoutItem>>(layoutJson ?? "[]");
            }
            catch
            {
                return new JsonResult(new { success = false, message = "顯示區域資料格式錯誤" });
            }

            if (layouts == null || layouts.Count != files.Count)
            {
                return new JsonResult(new { success = false, message = "圖片數量與顯示區域資料不一致" });
            }

            Directory.CreateDirectory(GalleryPath);
            Directory.CreateDirectory(PreviewPath);
            Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            LoadPhotos();

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var previewFile = previewFiles[i];
                var layout = layouts[i];

                if (file == null || file.Length == 0)
                    continue;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                    continue;

                var originalFileName = $"{Guid.NewGuid():N}{ext}";
                var originalSavePath = Path.Combine(GalleryPath, originalFileName);

                await using (var stream = new FileStream(originalSavePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var previewFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}_preview.jpg";
                var previewSavePath = Path.Combine(PreviewPath, previewFileName);

                await using (var stream = new FileStream(previewSavePath, FileMode.Create))
                {
                    await previewFile.CopyToAsync(stream);
                }

                Photos.Add(new PhotoItem
                {
                    Url = "/images/gallery/" + originalFileName,
                    PreviewUrl = "/images/gallery/previews/" + previewFileName,
                    CropX = layout.CropX,
                    CropY = layout.CropY,
                    CropWidth = layout.CropWidth,
                    CropHeight = layout.CropHeight,
                    OriginalWidth = layout.OriginalWidth,
                    OriginalHeight = layout.OriginalHeight,
                    RenderWidth = layout.RenderWidth > 0 ? layout.RenderWidth : 260,
                    RenderHeight = layout.RenderHeight > 0 ? layout.RenderHeight : 160
                });
            }

            RebalancePhotos(Photos);
            SavePhotos(Photos);

            return new JsonResult(new
            {
                success = true,
                photos = Photos
            });
        }

        public IActionResult OnPostDelete([FromBody] DeleteRequest request)
        {
            var isLoggedIn = HttpContext.Session.GetString(LoginSessionKey) == "true";
            if (!isLoggedIn)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return new JsonResult(new { success = false, message = "未登入，無法刪除" });
            }

            if (request == null || request.Images == null || request.Images.Count == 0)
            {
                return new JsonResult(new
                {
                    success = true,
                    deletedImages = new List<string>(),
                    message = "本次刪除了 0 張照片",
                    photos = Photos
                });
            }

            LoadPhotos();

            var deletedImages = new List<string>();

            foreach (var imageUrl in request.Images)
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                    continue;

                var fileName = Path.GetFileName(imageUrl);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                var filePath = Path.Combine(GalleryPath, fileName);
                var previewPath = Path.Combine(PreviewPath, $"{Path.GetFileNameWithoutExtension(fileName)}_preview.jpg");

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                if (System.IO.File.Exists(previewPath))
                {
                    System.IO.File.Delete(previewPath);
                }

                var removed = Photos.RemoveAll(p => p.Url == imageUrl);
                if (removed > 0)
                {
                    deletedImages.Add(imageUrl);
                }
            }

            RebalancePhotos(Photos);
            SavePhotos(Photos);

            return new JsonResult(new
            {
                success = true,
                deletedImages = deletedImages,
                message = $"本次刪除了 {deletedImages.Count} 張照片",
                photos = Photos
            });
        }

        private void LoadPhotos()
        {
            Directory.CreateDirectory(GalleryPath);
            Directory.CreateDirectory(PreviewPath);
            Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);

            if (System.IO.File.Exists(MetadataPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(MetadataPath);
                    var saved = JsonSerializer.Deserialize<List<PhotoItem>>(json);

                    if (saved != null)
                    {
                        Photos = saved
                            .Where(p => !string.IsNullOrWhiteSpace(p.Url))
                            .Where(p =>
                            {
                                var fileName = Path.GetFileName(p.Url);
                                return System.IO.File.Exists(Path.Combine(GalleryPath, fileName));
                            })
                            .ToList();

                        foreach (var photo in Photos)
                        {
                            if (string.IsNullOrWhiteSpace(photo.PreviewUrl))
                            {
                                photo.PreviewUrl = photo.Url;
                            }
                        }

                        RebalancePhotos(Photos);
                        SavePhotos(Photos);
                        return;
                    }
                }
                catch
                {
                }
            }

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            Photos = Directory.GetFiles(GalleryPath)
                .Where(f => allowed.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => new PhotoItem
                {
                    Url = "/images/gallery/" + Path.GetFileName(f),
                    PreviewUrl = "/images/gallery/" + Path.GetFileName(f),
                    CropX = 0,
                    CropY = 0,
                    CropWidth = 0,
                    CropHeight = 0,
                    OriginalWidth = 0,
                    OriginalHeight = 0,
                    RenderWidth = 260,
                    RenderHeight = 160
                })
                .ToList();

            RebalancePhotos(Photos);
            SavePhotos(Photos);
        }

        private void SavePhotos(List<PhotoItem> photos)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);

            var json = JsonSerializer.Serialize(photos, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            System.IO.File.WriteAllText(MetadataPath, json);
        }

        private static void RebalancePhotos(List<PhotoItem> photos)
        {
            for (int i = 0; i < photos.Count; i++)
            {
                photos[i].RowIndex = i % 3;
                photos[i].Order = i / 3;
            }
        }

        public class PhotoItem
        {
            public string Url { get; set; } = "";
            public string PreviewUrl { get; set; } = "";
            public int RowIndex { get; set; }
            public int Order { get; set; }

            public double CropX { get; set; }
            public double CropY { get; set; }
            public double CropWidth { get; set; }
            public double CropHeight { get; set; }
            public double OriginalWidth { get; set; }
            public double OriginalHeight { get; set; }

            public double RenderWidth { get; set; } = 260;
            public double RenderHeight { get; set; } = 160;
        }

        public class UploadLayoutItem
        {
            public double CropX { get; set; }
            public double CropY { get; set; }
            public double CropWidth { get; set; }
            public double CropHeight { get; set; }
            public double OriginalWidth { get; set; }
            public double OriginalHeight { get; set; }
            public double RenderWidth { get; set; }
            public double RenderHeight { get; set; }
        }

        public class LoginRequest
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }

        public class DeleteRequest
        {
            public List<string> Images { get; set; } = new();
        }
    }
}