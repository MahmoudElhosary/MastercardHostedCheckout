using MastercardHostedCheckout.Models;
using MastercardHostedCheckout.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace MastercardHostedCheckout.Controllers
{
    public class CheckoutController : Controller
    {
        private readonly IMastercardService _mastercardService;
        private readonly MastercardConfig _config;

        public CheckoutController(
            IMastercardService mastercardService,
            IOptions<MastercardConfig> config)
        {
            _mastercardService = mastercardService;
            _config = config.Value;
        }

        public IActionResult Index()
        {
            return View();
        }

        // =========================
        // INITIATE CHECKOUT
        // =========================
        [HttpPost]
        public async Task<IActionResult> InitiateCheckout(decimal amount, string currency)
        {
            currency = currency.ToUpper();

            // ✅ تنسيق المبلغ حسب العملة
            string formattedAmount = FormatAmount(amount, currency);

            var orderId = "ORDER_" + Guid.NewGuid().ToString("N")[..8];
            var merchantId = GetMerchantId(currency);

            var request = new InitiateCheckoutRequest
            {
                Order = new Order
                {
                    Id = orderId,
                    Amount = formattedAmount,
                    Currency = currency,
                    Description = $"Order for {formattedAmount} {currency}"
                },
                Interaction = new Interaction
                {
                    Operation = "PURCHASE",
                    ReturnUrl = Url.Action(
                        "Success",
                        "Checkout",
                        new { orderId, merchantId },
                        Request.Scheme),
                    CancelUrl = Url.Action(
                        "Index",
                        "Checkout",
                        null,
                        Request.Scheme),
                    Merchant = new Merchant
                    {
                        Name = "ADAAWER MPGS"
                    },
                    Action = new InteractionAction
                    {
                        ThreeDSecure = "MANDATORY"
                    },
                    DisplayControl = new DisplayControl
                    {
                        BillingAddress = "HIDE",
                        CardSecurityCode = "MANDATORY"
                    }
                }
            };

            var (response, error) =
                await _mastercardService.InitiateCheckoutAsync(merchantId, request);

            if (response != null && response.Result == "SUCCESS")
            {
                ViewBag.SessionId = response.Session?.Id;
                ViewBag.OrderId = orderId;
                ViewBag.MerchantId = merchantId;
                ViewBag.Amount = formattedAmount;
                ViewBag.Currency = currency;
                return View("HostedPage");
            }

            ViewBag.ErrorMessage = error ?? "Failed to initiate session.";
            return View("Error");
        }

        // =========================
        // SUCCESS CALLBACK
        // =========================
        [HttpGet]
        public async Task<IActionResult> Success(string orderId, string merchantId)
        {
            if (string.IsNullOrEmpty(orderId))
                return Error("OrderId missing.");

            merchantId ??= _config.MerchantId;

            var (orderResponse, orderError) =
                await _mastercardService.RetrieveOrderAsync(merchantId, orderId);

            if (orderResponse == null)
                return Error(orderError ?? "Retrieve order failed.");

            var root = orderResponse.RootElement;

            decimal amount = 0;
            string currency = "KWD";
            string? authTransactionId = null;

            // ===== Extract amount & currency =====
            if (root.TryGetProperty("order", out var order))
            {
                if (order.TryGetProperty("amount", out var amt))
                {
                    amount = amt.ValueKind == JsonValueKind.Number
                        ? amt.GetDecimal()
                        : decimal.Parse(amt.GetString() ?? "0", CultureInfo.InvariantCulture);
                }

                if (order.TryGetProperty("currency", out var cur))
                    currency = cur.GetString() ?? "KWD";
            }

            // ===== Extract 3DS Authentication ID =====
            authTransactionId = FindAuthTransactionId(root);

            // ===== Format again for display =====
            string formattedAmount = FormatAmount(amount, currency);

            ViewBag.OrderId = orderId;
            ViewBag.MerchantId = merchantId;
            ViewBag.AuthTransactionId = authTransactionId;
            ViewBag.Amount = formattedAmount;
            ViewBag.Currency = currency;

            return View();
        }

        // =========================
        // HELPERS
        // =========================

        private string GetMerchantId(string currency)
        {
            return currency switch
            {
                "USD" when !string.IsNullOrEmpty(_config.MerchantIdUsd) => _config.MerchantIdUsd,
                "EUR" when !string.IsNullOrEmpty(_config.MerchantIdEur) => _config.MerchantIdEur,
                "KWD" when !string.IsNullOrEmpty(_config.MerchantIdKwd) => _config.MerchantIdKwd,
                _ => _config.MerchantId
            };
        }

        // ✅ أهم جزء: تنسيق حسب العملة
        private string FormatAmount(decimal amount, string currency)
        {
            int decimals = currency switch
            {
                "KWD" => 3,
                "BHD" => 3,
                "JOD" => 3,
                _ => 2
            };

            return Math.Round(amount, decimals)
                       .ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }

        private string? FindAuthTransactionId(JsonElement root)
        {
            if (!root.TryGetProperty("transaction", out var txArray) ||
                txArray.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var tx in txArray.EnumerateArray())
            {
                if (!tx.TryGetProperty("result", out var res) ||
                    res.GetString() != "SUCCESS")
                    continue;

                if (tx.TryGetProperty("authentication", out var auth))
                {
                    if (auth.TryGetProperty("3ds", out var threeDs) &&
                        threeDs.TryGetProperty("transactionId", out var id))
                    {
                        return id.GetString();
                    }
                }
            }

            return null;
        }

        public IActionResult Error(string message)
        {
            ViewBag.ErrorMessage = message;
            return View("Error");
        }
    }
}
