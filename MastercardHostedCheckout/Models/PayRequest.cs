namespace MastercardHostedCheckout.Models
{
    public class PayRequest
    {
        public required string OrderId { get; set; }
        public required string SessionId { get; set; }
        public required string Amount { get; set; }
        public required string Currency { get; set; }
        public string? AuthTransactionId { get; set; }
        public string? MerchantId { get; set; }
    }
}
