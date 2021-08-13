using System;
using System.Globalization;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace atomex_frontend.atomex_data_structures
{
    public class Transaction : IBlockchainTransaction
    {
        public Transaction(
            CurrencyConfig currencyConfig,
            string id,
            BlockchainTransactionState state,
            BlockchainTransactionType type,
            DateTime? creationTime,
            bool isConfirmed,
            decimal amount,
            decimal fee = 0,
            string from = null,
            string to = null,
            decimal gasPrice = 0,
            decimal gasLimit = 0,
            decimal gasUsed = 0,
            bool isInternal = false,
            string alias = null,
            bool isRewardTx = false
        )
        {
            if (creationTime != null && creationTime.Value.Year == 1970)
            {
                creationTime = DateTime.Now;
            }

            CurrencyConfig = currencyConfig;
            Id = id;
            State = state;
            Type = type;
            CreationTime = TimeZoneInfo.ConvertTime(creationTime ?? DateTime.Now, TimeZoneInfo.Local);
            IsConfirmed = isConfirmed;
            Amount = amount;
            Fee = fee;
            From = from;
            To = to;
            GasPrice = gasPrice;
            GasLimit = gasLimit;
            GasUsed = gasUsed;
            IsInternal = isInternal;
            Alias = alias;
            IsRewardTx = isRewardTx;

            CanBeRemoved = state == BlockchainTransactionState.Unknown ||
                           state == BlockchainTransactionState.Failed ||
                           state == BlockchainTransactionState.Pending ||
                           state == BlockchainTransactionState.Unconfirmed;

            var netAmount = amount + fee;

            Description = GetDescription(
                type: Type,
                amount: Amount,
                netAmount: netAmount,
                amountDigits: currencyConfig.Digits,
                currencyCode: currencyConfig.Name);
        }

        public CurrencyConfig CurrencyConfig { get; set; }

        public string Currency
        {
            get => CurrencyConfig.Name;
            set { }
        }
        public string Id { get; set; }
        public BlockchainTransactionState State { get; set; }
        public BlockchainTransactionType Type { get; set; }
        public DateTime? CreationTime { get; set; }
        public bool IsConfirmed { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
        public decimal Fee { get; set; }

        public string From { get; set; }

        public string To { get; set; }

        public decimal GasPrice { get; set; }

        public decimal GasLimit { get; set; }

        public decimal GasUsed { get; set; }

        public bool IsInternal { get; set; }

        public string Alias { get; set; }

        public bool IsRewardTx { get; set; }

        public bool CanBeRemoved { get; set; }

        public static string GetDescription(
            BlockchainTransactionType type,
            decimal amount,
            decimal netAmount,
            int amountDigits,
            string currencyCode)
        {
            if (type.HasFlag(BlockchainTransactionType.SwapPayment))
            {
                return
                    $"Swap payment {Math.Abs(amount).ToString("0." + new string('#', amountDigits), CultureInfo.InvariantCulture)} {currencyCode}";
            }
            else if (type.HasFlag(BlockchainTransactionType.SwapRefund))
            {
                return
                    $"Swap refund {Math.Abs(netAmount).ToString("0." + new string('#', amountDigits), CultureInfo.InvariantCulture)} {currencyCode}";
            }
            else if (type.HasFlag(BlockchainTransactionType.SwapRedeem))
            {
                return
                    $"Swap redeem {Math.Abs(netAmount).ToString("0." + new string('#', amountDigits), CultureInfo.InvariantCulture)} {currencyCode}";
            }
            else if (type.HasFlag(BlockchainTransactionType.TokenApprove))
            {
                return $"Token approve";
            }
            else if (type.HasFlag(BlockchainTransactionType.TokenCall))
            {
                return $"Token call";
            }
            else if (type.HasFlag(BlockchainTransactionType.SwapCall))
            {
                return $"Token swap call";
            }
            else if (amount <= 0)
            {
                return
                    $"Sent {Math.Abs(netAmount).ToString("0." + new string('#', amountDigits), CultureInfo.InvariantCulture)} {currencyCode}";
            }
            else if (amount > 0)
            {
                return
                    $"Received {Math.Abs(netAmount).ToString("0." + new string('#', amountDigits), CultureInfo.InvariantCulture)} {currencyCode}";
            }

            return "Unknown transaction";
        }

        public BlockInfo BlockInfo => throw new NotImplementedException();
    }
}