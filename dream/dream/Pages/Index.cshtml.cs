using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace dream.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IWebHostEnvironment _env;

        private const string AdminUsername = "yangruikee";
        private const string AdminPassword = "ke0618";
        private const string LoginSessionKey = "IsAdminLoggedIn";

        public List<string> Images { get; set; } = new();
        public bool HasImages => Images.Count > 0;
        public bool IsLoggedIn { get; set; }

        public IndexModel(IWebHostEnvironment env)
        {
            _env = env;
        }

        public void OnGet()
        {
            HttpContext.Session.Remove(LoginSessionKey);
            LoadImages();
            IsLoggedIn = false;
        }

        public IActionResult OnPostLogin([FromBody] LoginRequest request)
        {
            if (request == null)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "登入資料無效"
                });
            }

            if (request.Username == AdminUsername && request.Password == AdminPassword)
            {
                HttpContext.Session.SetString(LoginSessionKey, "true");

                return new JsonResult(new
                {
                    success = true,
                    message = "登入成功"
                });
            }

            return new JsonResult(new
            {
                success = false,
                message = "帳號或密碼錯誤"
            });
        }

        public IActionResult OnPostLogout()
        {
            HttpContext.Session.Remove(LoginSessionKey);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> files)
        {
            var isLoggedIn = HttpContext.Session.GetString(LoginSessionKey) == "true";
            if (!isLoggedIn)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return new JsonResult(new
                {
                    success = false,
                    message = "未登入，無法上傳"
                });
            }

            var result = new List<string>();

            if (files == null || files.Count == 0)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "沒有收到檔案"
                });
            }

            var galleryPath = Path.Combine(_env.WebRootPath, "images", "gallery");
            Directory.CreateDirectory(galleryPath);

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            foreach (var file in files)
            {
                if (file == null || file.Length == 0)
                    continue;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                    continue;

                var newFileName = $"{Guid.NewGuid():N}{ext}";
                var savePath = Path.Combine(galleryPath, newFileName);

                using var stream = new FileStream(savePath, FileMode.Create);
                await file.CopyToAsync(stream);

                result.Add("/images/gallery/" + newFileName);
            }

            if (result.Count == 0)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "沒有可上傳的圖片格式"
                });
            }

            return new JsonResult(new
            {
                success = true,
                images = result
            });
        }

        private void LoadImages()
        {
            var galleryPath = Path.Combine(_env.WebRootPath, "images", "gallery");

            if (!Directory.Exists(galleryPath))
                return;

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            Images = Directory.GetFiles(galleryPath)
                .Where(f => allowed.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => "/images/gallery/" + Path.GetFileName(f))
                .ToList();
        }

        public class LoginRequest
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }
    }
}