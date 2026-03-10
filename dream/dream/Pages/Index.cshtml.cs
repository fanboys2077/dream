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
        private string MetadataPath => Path.Combine(_env.WebRootPath, "data", "gallery-layout.json");

        public IndexModel(IWebHostEnvironment env)
        {
            _env = env;
        }

        public void OnGet()
        {
            LoadPhotos();
            IsLoggedIn = HttpContext.Session.GetString(LoginSessionKey) == "true";
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

        public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> files, string layoutJson)
        {
            var isLoggedIn = HttpContext.Session.GetString(LoginSessionKey) == "true";
            if (!isLoggedIn)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return new JsonResult(new { success = false, message = "未登入，無法上傳" });
            }

            if (files == null || files.Count == 0)
            {
                return new JsonResult(new { success = false, message = "沒有收到檔案" });
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
            Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            LoadPhotos();

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var layout = layouts[i];

                if (file == null || file.Length == 0)
                    continue;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                    continue;

                var newFileName = $"{Guid.NewGuid():N}{ext}";
                var savePath = Path.Combine(GalleryPath, newFileName);

                await using var stream = new FileStream(savePath, FileMode.Create);
                await file.CopyToAsync(stream);

                Photos.Add(new PhotoItem
                {
                    Url = "/images/gallery/" + newFileName,
                    Scale = layout.Scale <= 0 ? 1 : layout.Scale,
                    OffsetX = layout.OffsetX,
                    OffsetY = layout.OffsetY
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

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
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
                    Scale = 1,
                    OffsetX = 0,
                    OffsetY = 0
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
            public int RowIndex { get; set; }
            public int Order { get; set; }
            public double Scale { get; set; } = 1;
            public double OffsetX { get; set; } = 0;
            public double OffsetY { get; set; } = 0;
        }

        public class UploadLayoutItem
        {
            public double Scale { get; set; }
            public double OffsetX { get; set; }
            public double OffsetY { get; set; }
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