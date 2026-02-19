using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MastercardHostedCheckout.Models;
using Microsoft.Extensions.Options;
using System.Globalization;

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

            // مهم جداً
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        private async Task<(JsonDocument? Response, string? Errors)> SendRequestAsync(
            HttpMethod method,
            string url,
            string merchantId,
            object? body = null)
        {
            try
            {
                var request = new HttpRequestMessage(method, url);

                // ✅ Basic Auth
                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_config.ApiUsername}:{_config.ApiPassword}")
                );

                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body);
                    System.Diagnostics.Debug.WriteLine($"[API PAYLOAD] {json}");

                    request.Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json");
                }

                System.Diagnostics.Debug.WriteLine($"[API REQUEST] {method} {url}");

                var response = await _httpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine(
                    $"[API RESPONSE] Status: {(int)response.StatusCode}. Content: {responseJson}");

                if (!response.IsSuccessStatusCode)
                {
                    return (null,
                        $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n" +
                        $"Response: {responseJson}\nURL: {url}");
                }

                if (string.IsNullOrWhiteSpace(responseJson))
                    return (null, "Empty response from gateway.");

                if (responseJson.TrimStart().StartsWith("<"))
                    return (null, "Gateway returned HTML instead of JSON.");

                return (JsonDocument.Parse(responseJson), null);
            }
            catch (TaskCanceledException)
            {
                return (null, "Gateway timeout.");
            }
            catch (Exception ex)
            {
                return (null, "SendRequest Error: " + ex.Message);
            }
        }

        // ================================
        // INITIATE CHECKOUT
        // ================================
        public async Task<(InitiateCheckoutResponse? Response, string? Errors)>
            InitiateCheckoutAsync(string merchantId, InitiateCheckoutRequest request)
        {
            var url =
                $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/session";

            var (doc, errors) =
                await SendRequestAsync(HttpMethod.Post, url, merchantId, request);

            if (doc == null)
                return (null, errors);

            try
            {
                var json = doc.RootElement.GetRawText();
                return (JsonSerializer.Deserialize<InitiateCheckoutResponse>(json), null);
            }
            catch (Exception ex)
            {
                return (null, "Deserialization Error: " + ex.Message);
            }
        }

        // ================================
        // ✅ PAY (الأهم)
        // ================================
        public async Task<(JsonDocument? Response, string? Errors)> CompletePaymentAsync(
            string merchantId,
            string orderId,
            string payTransactionId,
            string authTransactionId,
            decimal amount,
            string currency)
        {
            try
            {
                var url =
                    $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/order/{orderId}/transaction/{payTransactionId}";

                var formattedAmount =
                    currency?.ToUpper() == "KWD"
                        ? amount.ToString("0.000")
                        : amount.ToString("0.000");

                // ✅ الهيكل الرسمي الصحيح لـ PAY
                var body = new
                {
                    apiOperation = "PAY",

                    order = new
                    {
                        amount = formattedAmount,
                        currency = currency?.ToUpper()
                    },

                    transaction = new
                    {
                        reference = payTransactionId
                    },

                    authentication = new
                    {
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

        // ================================
        // RETRIEVE ORDER
        // ================================
        public async Task<(JsonDocument? Response, string? Errors)>
            RetrieveOrderAsync(string merchantId, string orderId)
        {
            var url =
                $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/order/{orderId}";

            return await SendRequestAsync(HttpMethod.Get, url, merchantId);
        }

        // ================================
        // DELETE LINK
        // ================================
        public async Task<bool> DeletePaymentLinkAsync(string merchantId, string linkId)
        {
            var url =
                $"{_config.BaseUrl}/api/rest/version/{_config.ApiVersion}/merchant/{merchantId}/link/{linkId}";

            var (doc, _) =
                await SendRequestAsync(HttpMethod.Delete, url, merchantId);

            return doc != null;
        }
    }
}
