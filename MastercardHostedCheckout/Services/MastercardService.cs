using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MastercardHostedCheckout.Models;
using Microsoft.Extensions.Options;

namespace MastercardHostedCheckout.Services
{
    public class MastercardService : IMastercardService
    {
        private readonly HttpClient _httpClient;
        private readonly MastercardConfig _config;

        public MastercardService(HttpClient httpClient, IOptions<MastercardConfig> config)
        {
            _httpClient = httpClient;
            _config = config.Value;
        }

        private async Task<(JsonDocument? Response, string? Errors)> SendRequestAsync(HttpMethod method, string url, string merchantId, object? body = null)
        {
            var request = new HttpRequestMessage(method, url);

            // تم استخدام Encoding.UTF8 لضمان التوافق مع كلمات السر المعقدة
            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"merchant.{merchantId}:{_config.ApiPassword}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body);
                System.Diagnostics.Debug.WriteLine($"[API PAYLOAD] {json}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            System.Diagnostics.Debug.WriteLine($"[API REQUEST] {method} {url}");
            var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[API RESPONSE] Status: {(int)response.StatusCode}. Content: {responseJson}");

            if (response.IsSuccessStatusCode)
            {
                if (responseJson.TrimStart().StartsWith("<"))
                {
                    return (null, "Response is HTML, expected JSON. Response: " + responseJson.Substring(0, Math.Min(100, responseJson.Length)));
                }
                return (JsonDocument.Parse(responseJson), null);
            }

            var errorMsg = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\nResponse: {responseJson}\nURL: {url}";
            return (null, errorMsg);
        }

        /// <summary>
        /// Initiates a checkout session with Mastercard (API v63).
        /// </summary>
        public async Task<(InitiateCheckoutResponse? Response, string? Errors)> InitiateCheckoutAsync(string merchantId, InitiateCheckoutRequest request)
        {
            var url = $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/session";

            // ملاحظة: الـ InitiateCheckoutRequest يجب أن يحتوي على كائن merchant.name حسب v63
            var (doc, errors) = await SendRequestAsync(HttpMethod.Post, url, merchantId, request);

            if (doc != null)
            {
                try
                {
                    var json = doc.RootElement.GetRawText();
                    return (JsonSerializer.Deserialize<InitiateCheckoutResponse>(json), null);
                }
                catch (JsonException ex)
                {
                    return (null, "Deserialization Error: " + ex.Message);
                }
            }
            return (null, errors);
        }

        /// <summary>
        /// Completes the payment (PAY operation) using the correct 3DS Transaction ID.
        /// </summary>
        public async Task<(JsonDocument? Response, string? Errors)> CompletePaymentAsync(
            string merchantId,
            string orderId,
            string payTransactionId,
            string authTransactionId, // هذا يجب أن يكون الـ GUID المستخرج من 3ds.transactionId
            decimal amount,
            string currency)
        {
            try
            {
                var url = $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/order/{orderId}/transaction/{payTransactionId}";

                // تنسيق المبلغ (3 خانات عشرية للكويت)
                var formattedAmount = currency?.ToUpper() == "KWD" ? amount.ToString("0.000") : amount.ToString("0.00");

                // الهيكل الصحيح لعملية PAY في الإصدارات الحديثة
                var body = new
                {
                    apiOperation = "PAY",
                    order = new // في v63 نستخدم كائن order لتحديد المبلغ والعملة
                    {
                        amount = formattedAmount,
                        currency = currency?.ToUpper()
                    },
                    authentication = new
                    {
                        // ربط العملية بنجاح الـ 3DS Challenge
                        transactionId = authTransactionId
                    }
                };

                return await SendRequestAsync(HttpMethod.Put, url, merchantId, body);
            }
            catch (Exception ex)
            {
                return (null, "CompletePayment Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Retrieves order details to extract the authentication transaction ID.
        /// </summary>
        public async Task<(JsonDocument? Response, string? Errors)> RetrieveOrderAsync(string merchantId, string orderId)
        {
            var url = $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/order/{orderId}";
            return await SendRequestAsync(HttpMethod.Get, url, merchantId);
        }

        public async Task<bool> DeletePaymentLinkAsync(string merchantId, string linkId)
        {
            var url = $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/link/{linkId}";
            var (doc, _) = await SendRequestAsync(HttpMethod.Delete, url, merchantId);
            return doc != null;
        }
    }
}