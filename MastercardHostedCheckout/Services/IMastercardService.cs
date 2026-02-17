using MastercardHostedCheckout.Models;
using System.Text.Json;

namespace MastercardHostedCheckout.Services
{
    public interface IMastercardService
    {
        Task<(InitiateCheckoutResponse? Response, string? Errors)> InitiateCheckoutAsync(string merchantId, InitiateCheckoutRequest request);
        Task<bool> DeletePaymentLinkAsync(string merchantId, string linkId);
        Task<(JsonDocument? Response, string? Errors)> CompletePaymentAsync(string merchantId, string orderId, string payTransactionId, string authTransactionId, decimal amount, string currency);
        Task<(JsonDocument? Response, string? Errors)> RetrieveOrderAsync(string merchantId, string orderId);
    }
}
