namespace MastercardHostedCheckout.Models
{
    public class MastercardConfig
    {
        public string MerchantId { get; set; } = string.Empty;
        public string MerchantIdKwd { get; set; } = string.Empty;
        public string MerchantIdUsd { get; set; } = string.Empty;
        public string MerchantIdEur { get; set; } = string.Empty;
        public string ApiUsername { get; set; } = string.Empty;
        public string ApiPassword { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiVersion { get; set; } = "63";
        public string ReturnUrl { get; set; } = string.Empty; 
    }
}