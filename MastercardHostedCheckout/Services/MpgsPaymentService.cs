using MastercardHostedCheckout.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MastercardHostedCheckout.Services
{
    public class MpgsPaymentService
    {
        private readonly HttpClient _http;
        private readonly MastercardConfig _config;

        public MpgsPaymentService(HttpClient http, IOptions<MastercardConfig> config)
        {
            _http = http;
            _config = config.Value;

            // Use KWD Merchant ID if available, otherwise fallback to default MerchantId
            var merchantId = string.IsNullOrEmpty(_config.MerchantIdKwd) ? _config.MerchantId : _config.MerchantIdKwd;

            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"merchant.{merchantId}:{_config.ApiPassword}")
            );

            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", auth);
        }

        public async Task<string> PayAsync(string orderId, string sessionId, string amount, string currency)
        {
            // Use KWD Merchant ID if available, otherwise fallback to default MerchantId
            var merchantId = string.IsNullOrEmpty(_config.MerchantIdKwd) ? _config.MerchantId : _config.MerchantIdKwd;
            
            var url =
                $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/order/{orderId}/transaction/1";

            var payload = new
            {
                apiOperation = "PAY",
                order = new
                {
                    amount = amount,
                    currency = currency
                },
                authentication = new
                {
                    transactionId = sessionId // في هذا السياق، المتغير المسمى sessionId قد يحتوي على الـ 3ds transactionId
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PutAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();

            return body;
        }
    }
}
