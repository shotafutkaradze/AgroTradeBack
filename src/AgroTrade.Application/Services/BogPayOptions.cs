using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgroTrade.Application.Services
{
    public class BogPayOptions
    {
        public string ClientId { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api.bog.ge"; 
        public string RedirectUrl { get; set; } = "https://yourdomain.com/api/payments/bog/return";
        public string CallbackUrl { get; set; } = "https://yourdomain.com/api/payments/bog/callback";
        public string FrontendReturnUrl { get; set; } = "http://localhost:5173/AgroTradefrontend/";
    }
}
