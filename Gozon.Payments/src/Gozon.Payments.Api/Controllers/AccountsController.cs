using System.Threading.Tasks;
using Gozon.Payments.Api.Contracts;
using Gozon.Payments.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Gozon.Payments.Api.Controllers
{
    /// <summary>
    /// HTTP API для работы со счетами пользователей.
    /// </summary>
    [ApiController]
    [Route("api/accounts")]
    public class AccountsController : ControllerBase
    {
        private readonly PaymentsStore _store;

        /// <summary>
        /// Создает контроллер с доступом к хранилищу платежей.
        /// </summary>
        /// <param name="store">Хранилище платежей.</param>
        public AccountsController(PaymentsStore store)
        {
            _store = store;
        }

        /// <summary>
        /// Создает счет для пользователя, если он отсутствует.
        /// </summary>
        /// <returns>201 при создании или 409 при существующем счете.</returns>
        [HttpPost]
        public async Task<IActionResult> CreateAccount()
        {
            if (!UserIdResolver.TryResolveUserId(Request, out var userId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            var created = await _store.CreateAccountAsync(userId);
            if (!created)
            {
                return Conflict(new { error = "Account already exists" });
            }

            return Created("/api/accounts/balance", new { userId });
        }

        /// <summary>
        /// Пополняет счет пользователя.
        /// </summary>
        /// <param name="request">Запрос на пополнение.</param>
        /// <returns>200 при успехе или 404 при отсутствии счета.</returns>
        [HttpPost("topup")]
        public async Task<IActionResult> TopUp([FromBody] AccountTopUpRequest request)
        {
            if (!UserIdResolver.TryResolveUserId(Request, out var userId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            if (request.Amount <= 0)
            {
                return BadRequest(new { error = "Amount must be positive" });
            }

            var updated = await _store.TopUpAsync(userId, request.Amount);
            if (!updated)
            {
                return NotFound(new { error = "Account not found" });
            }

            return Ok(new { userId, request.Amount });
        }

        /// <summary>
        /// Возвращает баланс текущего пользователя.
        /// </summary>
        /// <returns>Баланс либо 404, если счет не найден.</returns>
        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            if (!UserIdResolver.TryResolveUserId(Request, out var userId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            var balance = await _store.GetBalanceAsync(userId);
            if (balance == null)
            {
                return NotFound(new { error = "Account not found" });
            }

            return Ok(new { userId, balance });
        }
    }
}
