namespace Gozon.Payments.Api.Contracts
{
    /// <summary>
    /// Запрос на пополнение счета.
    /// </summary>
    public class AccountTopUpRequest
    {
        /// <summary>Сумма пополнения в целых единицах валюты.</summary>
        public long Amount { get; set; }
    }
}
