using System.Threading.Tasks;
using Gozon.Orders.Api.Contracts;
using Gozon.Orders.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Gozon.Orders.Api.Controllers
{
    /// <summary>
    /// HTTP API для создания заказов и получения их статусов.
    /// </summary>
    [ApiController]
    [Route("api/orders")]
    public class OrdersController : ControllerBase
    {
        private readonly OrdersStore _store;

        /// <summary>
        /// Создает контроллер, связанный с хранилищем заказов.
        /// </summary>
        /// <param name="store">Хранилище заказов.</param>
        public OrdersController(OrdersStore store)
        {
            _store = store;
        }

        /// <summary>
        /// Создает заказ и запускает асинхронную оплату.
        /// </summary>
        /// <param name="request">Данные для создания заказа.</param>
        /// <returns>202 с идентификатором заказа и статусом NEW.</returns>
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            if (!UserIdResolver.TryResolveUserId(Request, out var userId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            if (request.Amount <= 0)
            {
                return BadRequest(new { error = "Amount must be positive" });
            }

            var description = request.Description?.Trim() ?? string.Empty;
            var order = await _store.CreateOrderAsync(userId, request.Amount, description);
            return Accepted(new { orderId = order.Id, status = order.Status.ToString() });
        }

        /// <summary>
        /// Возвращает все заказы текущего пользователя.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            if (!UserIdResolver.TryResolveUserId(Request, out var userId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            var orders = await _store.GetOrdersByUserAsync(userId);
            return Ok(orders);
        }

        /// <summary>
        /// Возвращает один заказ по id для текущего пользователя.
        /// </summary>
        /// <param name="orderId">Идентификатор заказа.</param>
        [HttpGet("{orderId}")]
        public async Task<IActionResult> GetOrder(string orderId)
        {
            if (!UserIdResolver.TryResolveUserId(Request, out var userId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            var order = await _store.GetOrderAsync(orderId);
            if (order == null || order.UserId != userId)
            {
                return NotFound();
            }

            return Ok(order);
        }
    }
}
