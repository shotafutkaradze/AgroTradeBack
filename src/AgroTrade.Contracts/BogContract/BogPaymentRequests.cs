using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgroTrade.Contracts.BogContract
{
    public class BogPaymentRequests
    {
        public record BogStartInstallmentRequest(
            List<BogProductItem> Items
        );

        public record BogProductItem(
            string Id,
            string Title,
            int Quantity,
            decimal Price 
        );

        public record BogCheckoutResponse(
            string OrderId,
            string RedirectUrl
        );

        public record BogCallbackPayload(
            string OrderId,
            string Status, 
            decimal Amount,
            string Currency
        );
    }
}
