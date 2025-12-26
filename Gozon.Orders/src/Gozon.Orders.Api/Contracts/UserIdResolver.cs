using Microsoft.AspNetCore.Http;

namespace Gozon.Orders.Api.Contracts
{
    /// <summary>
    /// Извлекает идентификатор пользователя из HTTP-запроса.
    /// </summary>
    public static class UserIdResolver
    {
        /// <summary>
        /// Пытается найти user_id в заголовках или query string.
        /// </summary>
        /// <param name="request">HTTP-запрос.</param>
        /// <param name="userId">Результат поиска.</param>
        /// <returns>True, если найден непустой user_id.</returns>
        public static bool TryResolveUserId(HttpRequest request, out string userId)
        {
            if (request.Headers.TryGetValue("X-User-Id", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue))
            {
                userId = headerValue.ToString();
                return true;
            }

            if (request.Query.TryGetValue("user_id", out var queryValue) && !string.IsNullOrWhiteSpace(queryValue))
            {
                userId = queryValue.ToString();
                return true;
            }

            userId = string.Empty;
            return false;
        }
    }
}
