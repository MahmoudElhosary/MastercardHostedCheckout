using MastercardHostedCheckout.Models;
using MastercardHostedCheckout.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MastercardHostedCheckout.Controllers
{
    [ApiController]
    [Route("api/payment")]
    public class PaymentController : ControllerBase
    {
        private readonly IMastercardService _service;
        private readonly MastercardConfig _config;

        public PaymentController(IMastercardService service, IOptions<MastercardConfig> config)
        {
            _service = service;
            _config = config.Value;
        }

        [HttpPost("pay")]
        public async Task<IActionResult> Pay([FromBody] PayRequest request)
        {
            if (string.IsNullOrEmpty(request.OrderId) || string.IsNullOrEmpty(request.SessionId))
                return BadRequest("Missing orderId or sessionId.");

            var merchantId = string.IsNullOrEmpty(_config.MerchantIdKwd) ? _config.MerchantId : _config.MerchantIdKwd;

            System.Diagnostics.Debug.WriteLine($"[PAY API] OrderId: {request.OrderId}, AuthTransactionId: {request.AuthTransactionId}");

            var (response, error) = await _service.CompletePaymentAsync(
                merchantId,
                request.OrderId,
                "1", // transactionId دائمًا 1
                !string.IsNullOrEmpty(request.AuthTransactionId) ? request.AuthTransactionId : request.SessionId,
                decimal.Parse(request.Amount),
                request.Currency
            );

            if (response != null)
                return Ok(response);

            return BadRequest(new { error });
        }
    }
}
