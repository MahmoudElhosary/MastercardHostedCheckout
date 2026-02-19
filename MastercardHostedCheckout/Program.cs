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
            builder.Services.AddHttpClient<MpgsPaymentService>();

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
    IHttpClientFactory factory,
    ILogger<Program> logger) =>
            {
                var opt = options.Value;

                // Generate short order id
                var orderId = $"ORD_{Guid.NewGuid():N}"[..10].ToUpper();

                var mId = opt.MerchantId;

                var url =
                    $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/session";

                var payload = new
                {
                    apiOperation = "CREATE_CHECKOUT_SESSION",
                    interaction = new
                    {
                        operation = "PURCHASE", // ✔ خصم مباشر
                        returnUrl = $"{opt.ReturnUrl}?orderId={orderId}"
                    },
                    order = new
                    {
                        id = orderId,
                        amount = "1.000", // ✔ KWD requires 3 decimals
                        currency = "KWD"
                    }
                };

                var client = factory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(60);

                // Basic Auth
                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}"));

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                HttpResponseMessage response;

                try
                {
                    response = await client.PostAsJsonAsync(url, payload);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "MPGS connection failed");
                    return Results.Problem("Payment gateway unreachable");
                }

                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError("MPGS Error: {Json}", json);
                    return Results.Problem($"Gateway Error: {json}");
                }

                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("session", out var sessionEl) ||
                    !sessionEl.TryGetProperty("id", out var idEl))
                {
                    logger.LogError("MPGS invalid response: {Json}", json);
                    return Results.Problem("Invalid gateway response");
                }

                var sessionId = idEl.GetString();

                return Results.Json(new
                {
                    success = true,
                    orderId,
                    sessionId
                });
            });


            // ==========================================
            // 2️⃣ VERIFY ORDER STATUS //endpoint
            // ==========================================
            app.MapGet("/api/mpgs/verify/{orderId}", async (
     string orderId,
     IOptions<MastercardConfig> options,
     IHttpClientFactory factory) =>
            {
                var opt = options.Value;
                var mId = opt.MerchantId;

                var url =
                    $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/order/{orderId}";

                var client = factory.CreateClient();

                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}"));

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                var response = await client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Results.Problem($"Gateway Error: {json}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                //  أهم تحقق
                var orderStatus = root
                    .GetProperty("order")
                    .GetProperty("status")
                    .GetString();

                var isCaptured = orderStatus == "CAPTURED";

                // optional details
                string? gatewayCode = null;
                string? amount = null;
                string? currency = null;

                if (root.TryGetProperty("transaction", out var txArray) &&
                    txArray.GetArrayLength() > 0)
                {
                    var tx = txArray[0];

                    if (tx.TryGetProperty("response", out var resp))
                    {
                        gatewayCode = resp.GetProperty("gatewayCode").GetString();
                    }

                    if (tx.TryGetProperty("amount", out var amt))
                    {
                        amount = amt.ToString();
                    }

                    if (tx.TryGetProperty("currency", out var cur))
                    {
                        currency = cur.GetString();
                    }
                }

                return Results.Json(new
                {
                    success = isCaptured,
                    status = orderStatus,
                    gatewayCode,
                    amount,
                    currency,
                    raw = root
                });
            });


            // ==========================================
            // 3️⃣ PAY AFTER 3DS AUTHENTICATION
            // ==========================================
            // ==========================================
            // 3️⃣ PAY AFTER 3DS AUTHENTICATION
            // ==========================================
            /*
            // ==========================================
            // 3️⃣ PAY AFTER 3DS AUTHENTICATION (MOVED TO PaymentController)
            // ==========================================
            app.MapPost("/api/mpgs/pay", async (
                PayRequest model,
                IOptions<MastercardConfig> options,
                IHttpClientFactory factory) =>
            {
                var opt = options.Value;
                var mId = string.IsNullOrEmpty(opt.MerchantIdKwd) ? opt.MerchantId : opt.MerchantIdKwd;

                var client = factory.CreateClient();
                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}")
                );

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authHeader);

                var url = $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/order/{model.OrderId}/transaction/1";

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
                        id = model.SessionId
                    }
                };

                var response = await client.PutAsJsonAsync(url, payload);
                var json = await response.Content.ReadAsStringAsync();

                return Results.Content(json, "application/json");
            });
            */

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
