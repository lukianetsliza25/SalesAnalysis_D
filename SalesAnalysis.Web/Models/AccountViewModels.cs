// SalesAnalysis.Web/Models/RegisterViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace SalesAnalysis.Web.Models
{
    // Окрема модель для реєстрації
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Ім'я обов'язкове")]
        public string FirstName { get; set; } // Нове поле

        [Required(ErrorMessage = "Електронна адреса обов'язкова")]
        [EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Пароль обов'язковий")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }

    // Окрема модель для входу
    public class LoginViewModel
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // Об'єднана модель для спільної сторінки (Auth.cshtml)
    public class AuthViewModel
    {
        public LoginViewModel Login { get; set; } = new LoginViewModel();
        public RegisterViewModel Register { get; set; } = new RegisterViewModel();
    }
}