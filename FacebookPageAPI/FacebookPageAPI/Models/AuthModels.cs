using System.ComponentModel.DataAnnotations;

namespace FacebookPageAPI.Models
{
    // ===== ENTITY =====
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "User"; // "Admin" | "User"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ===== REQUEST DTOs =====
    public class RegisterRequest
    {
        [Required(ErrorMessage = "Username là bắt buộc")]
        [MinLength(3, ErrorMessage = "Username tối thiểu 3 ký tự")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email là bắt buộc")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password là bắt buộc")]
        [MinLength(6, ErrorMessage = "Password tối thiểu 6 ký tự")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Xác nhận password là bắt buộc")]
        [Compare("Password", ErrorMessage = "Password xác nhận không khớp")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        [Required(ErrorMessage = "Email là bắt buộc")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password là bắt buộc")]
        public string Password { get; set; } = string.Empty;
    }

    // ===== RESPONSE DTOs =====
    public class AuthResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Token { get; set; }
        public UserInfo? User { get; set; }
    }

    public class UserInfo
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
