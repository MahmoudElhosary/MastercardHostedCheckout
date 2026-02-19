using MastercardHostedCheckout.Models;
using MastercardHostedCheckout.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;

namespace MastercardHostedCheckout
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllersWithViews();
            builder.Services.AddRazorPages();
            builder.Services.Configure<MastercardConfig>(
            builder.Configuration.GetSection("Mastercard"));
            builder.Services.AddHttpClient();
            builder.Services.AddHttpClient<IMastercardService, MastercardService>();

            var app = builder.Build();

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            // ==========================================
            // 1️⃣ CREATE CHECKOUT SESSION (Hosted Checkout)
            // ==========================================
            app.MapPost("/api/mpgs/session", async (
                IOptions<MastercardConfig> options,
                IHttpClientFactory factory) =>
            {
                var opt = options.Value;
                var orderId = $"ORD_{Guid.NewGuid():N}".Substring(0, 10).ToUpper();
                var mId = opt.MerchantId;

                var url =
                    $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/session";

                var payload = new
                {
                    apiOperation = "CREATE_CHECKOUT_SESSION",
                    interaction = new
                    {
                        operation = "PURCHASE",
                        returnUrl = $"{opt.ReturnUrl}?orderId={orderId}"
                    },
                    order = new
                    {
                        id = orderId,
                        amount = "1.000",
                        currency = "KWD"
                    }
                };

                var client = factory.CreateClient();
                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}"));

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                var response = await client.PostAsJsonAsync(url, payload);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Results.Problem($"Gateway Error: {json}");

                using var doc = JsonDocument.Parse(json);
                var sessionId =
                    doc.RootElement.GetProperty("session").GetProperty("id").GetString();

                return Results.Json(new { orderId, sessionId });
            });

            // ==========================================
            // 2️⃣ VERIFY ORDER STATUS
            // ==========================================
            app.MapGet("/api/mpgs/verify/{orderId}", async (
                string orderId,
                IOptions<MastercardConfig> options,
                IHttpClientFactory factory) =>
            {
                var opt = options.Value;
                var mId = opt.MerchantId;

                var client = factory.CreateClient();
                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}"));

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                var url =
                    $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/order/{orderId}";

                var response = await client.GetStringAsync(url);

                return Results.Content(response, "application/json");
            });

            // ==========================================
            // 3️⃣ PAY AFTER 3DS AUTHENTICATION
            // ==========================================
            app.MapPost("/api/mpgs/pay/{orderId}", async (
                string orderId,
                IOptions<MastercardConfig> options,
                IHttpClientFactory factory) =>
            {
                var opt = options.Value;
                var mId = opt.MerchantId;

                var client = factory.CreateClient();
                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}"));

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                // أولاً: نجيب بيانات الطلب عشان نستخرج authenticationToken
                var orderUrl =
                    $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/order/{orderId}";

                var orderResponse = await client.GetStringAsync(orderUrl);
                using var orderDoc = JsonDocument.Parse(orderResponse);

                var authToken = orderDoc.RootElement
                    .GetProperty("authentication")
                    .GetProperty("3ds")
                    .GetProperty("authenticationToken")
                    .GetString();

                var threeDsTransactionId = orderDoc.RootElement
                    .GetProperty("authentication")
                    .GetProperty("3ds")
                    .GetProperty("transactionId")
                    .GetString();

                var transactionId =
                    $"pay-{Guid.NewGuid():N}".Substring(0, 15);

                var payUrl =
                    $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/order/{orderId}/transaction/{transactionId}";

                var payload = new
                {
                    apiOperation = "PAY",
                    order = new
                    {
                        amount = "1.000",
                        currency = "KWD"
                    },
                    session = new
                    {
                        id = sessionId
                    }
                };


                var payResponse = await client.PutAsJsonAsync(payUrl, payload);
                var resultJson = await payResponse.Content.ReadAsStringAsync();

                return Results.Content(resultJson, "application/json");
            });

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
